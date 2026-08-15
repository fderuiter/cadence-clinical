import logging
import re
import threading
import time
from typing import Any

from sqlalchemy.ext.asyncio import AsyncSession

from apps.safety.adapters import SafetyDatabaseAdapter
from apps.safety.adapters.execution_client import ExecutionClient
from apps.safety.adapters.models import SAEDiscrepancy, SAEReconciliationRun
from apps.safety.domain.sae_icsr import (
    IndividualCaseSafetyReport,
    MedDRACoding,
    SeriousAdverseEvent,
)

logger = logging.getLogger("safety-reconciliation")


class TerminologyCache:
    """Thread-safe in-memory cache for MedDRA term resolutions."""

    def __init__(self, max_size: int = 1000, ttl: float | None = None) -> None:
        """Initializes the thread-safe terminology cache.

        Args:
            max_size: The maximum size of the cache. Defaults to 1000.
            ttl: The cache expiration TTL. Defaults to None (loaded from env).
        """
        self.max_size = max_size
        self._cache: dict[tuple[str, str], tuple[dict[str, Any], float]] = {}
        self._lock = threading.Lock()

        if ttl is not None:
            self.ttl = float(ttl)
        else:
            import os

            env_ttl = os.getenv("TERMINOLOGY_CACHE_TTL") or os.getenv("CACHE_TTL")
            if env_ttl is not None:
                try:
                    self.ttl = float(env_ttl)
                except ValueError:
                    self.ttl = 3600.0
            else:
                self.ttl = 3600.0

    def get(self, term: str, version: str) -> dict[str, Any] | None:
        """Retrieves a cached term resolution if still valid.

        Args:
            term: The verbatim term to resolve.
            version: The MedDRA dictionary version.

        Returns:
            The cached dictionary match payload, or None if expired/not found.
        """
        now = time.time()
        key = (term, version)
        with self._lock:
            if key in self._cache:
                data, timestamp = self._cache[key]
                if now - timestamp < self.ttl:
                    return data
        return None

    def set(self, term: str, version: str, data: dict[str, Any]) -> None:
        """Stores a resolved term resolution in the cache.

        Args:
            term: The verbatim term that was resolved.
            version: The MedDRA dictionary version used.
            data: The resolved dictionary match payload.
        """
        key = (term, version)
        with self._lock:
            if len(self._cache) >= self.max_size:
                # Basic eviction policy
                self._cache.pop(next(iter(self._cache)))
            self._cache[key] = (data, time.time())

    def clear(self) -> None:
        """Clears all entries in the cache."""
        with self._lock:
            self._cache.clear()

    def get_status(self) -> dict[str, int]:
        """Returns the current cache usage status.

        Returns:
            A dictionary containing cache current size and max size.
        """
        with self._lock:
            return {"size": len(self._cache), "max_size": self.max_size}


terminology_cache = TerminologyCache()


def generate_stable_event_key(subject_key: str, sae: SeriousAdverseEvent) -> str:
    """Generates a stable, unique, and PII-free key for an adverse event.

    Aligns to safety case worldwide_unique_case_id / subject key.
    Uses AESEQ only as an event-level component when present, never as the sole key.

    Args:
        subject_key: The subject identifier.
        sae: The SeriousAdverseEvent object representing the adverse event.

    Returns:
        A formatted string key.
    """
    # Standardize subject key
    subj = str(subject_key).strip().upper()
    if sae.AESEQ is not None:
        return f"{subj}:SEQ-{sae.AESEQ}"
    # Normalize verbatim term (strip excess spaces, uppercase)
    normalized_term = re.sub(r"\s+", " ", sae.AETERM.strip().upper())
    start_date = (sae.AESTDTC or "").strip()
    return f"{subj}:TERM-{normalized_term}:{start_date}"


def normalize_edc_ae_to_sae(
    ae_dict: dict[str, Any], meddra_coding: MedDRACoding | None = None
) -> SeriousAdverseEvent:
    """Normalizes an EDC Adverse Event dict into a SeriousAdverseEvent model.

    Args:
        ae_dict: Dictionary representing the adverse event from EDC.
        meddra_coding: Optional pre-resolved MedDRA coding hierarchy.

    Returns:
        A standardized SeriousAdverseEvent model.
    """
    # Aligns keys
    subject_key = ae_dict.get("USUBJID") or ae_dict.get("subject_key") or "UNKNOWN"
    aeterm = ae_dict.get("AETERM") or ae_dict.get("reaction_term") or "UNKNOWN"
    aestdtc = (
        ae_dict.get("AESTDTC")
        or ae_dict.get("start_date")
        or "2026-07-28"  # deid-ignore
    )  # safe fallback
    aeendtc = ae_dict.get("AEENDTC") or ae_dict.get("end_date") or None

    aesev = ae_dict.get("AESEV") or "MILD"
    aeser = (
        ae_dict.get("AESER") or "Y"
    )  # If reconciling, expect serious or default serious
    aerel = ae_dict.get("AEREL") or None
    aeout = ae_dict.get("AEOUT") or None
    aeseq = ae_dict.get("AESEQ") or None

    return SeriousAdverseEvent(
        subject_key=subject_key,
        AETERM=aeterm,
        AESTDTC=aestdtc,
        AEENDTC=aeendtc,
        AESEV=aesev,
        AESER=aeser,
        AEREL=aerel,
        AEOUT=aeout,
        AESEQ=aeseq or 1,
        meddra_coding=meddra_coding,
        version_index=1,
        reason_for_change="Normalized observation",
    )


def normalize_external_icsr_to_saes(
    icsr_dict: dict[str, Any],
) -> list[SeriousAdverseEvent]:
    """Normalizes reaction events inside external safety case/ICSR payload dict to SAEs.

    Args:
        icsr_dict: Parsed JSON/dict of an IndividualCaseSafetyReport.

    Returns:
        A list of standardized SeriousAdverseEvent models.
    """
    try:
        icsr = IndividualCaseSafetyReport(**icsr_dict)
    except Exception as e:
        logger.error(
            "Failed to parse safety case dict into IndividualCaseSafetyReport: %s", e
        )
        # Attempt loose key extraction if pydantic validation fails due to minor E2B mismatch
        patient_id = icsr_dict.get("patient", {}).get("patient_id") or "UNKNOWN"
        reactions = icsr_dict.get("reactions") or []
        saes = []
        for r in reactions:
            term = r.get("reaction_term") or "UNKNOWN"
            is_serious = (
                "Y"
                if any(
                    r.get(f) == "Y"
                    for f in (
                        "seriousness_death",
                        "seriousness_life_threatening",
                        "seriousness_hospitalization",
                    )
                )
                else "N"
            )
            saes.append(
                SeriousAdverseEvent(
                    subject_key=patient_id,
                    AETERM=term,
                    AESTDTC=str(r.get("start_date") or "2026-07-28"),  # deid-ignore
                    AEENDTC=r.get("end_date"),
                    AESEV="SEVERE" if is_serious == "Y" else "MILD",
                    AESER=is_serious,
                    AEREL=r.get("causality"),
                    AEOUT=r.get("outcome"),
                    AESEQ=r.get("sequence_number") or 1,
                    meddra_coding=r.get("meddra_coding"),
                    version_index=1,
                    reason_for_change="Initial ingestion",
                )
            )
        return saes

    patient_id = icsr.patient.patient_id
    saes = []
    for reaction in icsr.reactions:
        # Determine seriousness based on flags
        seriousness_flags = [
            reaction.seriousness_death,
            reaction.seriousness_life_threatening,
            reaction.seriousness_hospitalization,
            reaction.seriousness_disability,
            reaction.seriousness_congenital_anomaly,
            reaction.seriousness_other_medically_important,
        ]
        is_serious = "Y" if any(flag == "Y" for flag in seriousness_flags) else "N"
        aesev = "SEVERE" if is_serious == "Y" else "MILD"

        saes.append(
            SeriousAdverseEvent(
                subject_key=patient_id,
                AETERM=reaction.reaction_term,
                AESTDTC=str(reaction.start_date or "2026-07-28"),  # deid-ignore
                AEENDTC=reaction.end_date,
                AESEV=aesev,
                AESER=is_serious,
                AEREL=None,
                AEOUT=reaction.outcome,
                AESEQ=1,
                meddra_coding=reaction.meddra_coding,
                version_index=1,
                reason_for_change="Initial ingestion",
            )
        )
    return saes


def compare_sae_records(
    edc_saes: list[SeriousAdverseEvent],
    safety_saes: list[SeriousAdverseEvent],
    meddra_version: str = "26.0",
) -> list[dict[str, Any]]:
    """Compares normalized EDC and external Safety SAE representations.

    Compares AESER, AESTDTC, AEENDTC, AESEV, AEREL, AEOUT, and MedDRA coding.
    Produces deterministic field-level discrepancy records sorted by key then field.

    Args:
        edc_saes: Standardized adverse events sourced from the EDC system.
        safety_saes: Standardized adverse events sourced from the safety system.
        meddra_version: Version of the MedDRA dictionary used. Defaults to "26.0".

    Returns:
        A list of discrepancy dictionaries containing mismatch details.
    """
    edc_map = {generate_stable_event_key(s.subject_key, s): s for s in edc_saes}
    safety_map = {generate_stable_event_key(s.subject_key, s): s for s in safety_saes}

    discrepancies: list[dict[str, Any]] = []
    all_keys = sorted(list(set(edc_map.keys()) | set(safety_map.keys())))

    for key in all_keys:
        edc_sae = edc_map.get(key)
        safety_sae = safety_map.get(key)

        # 1. Handle missing on either side
        if edc_sae is None:
            discrepancies.append(
                {
                    "source": "EDC",
                    "case_event_key": key,
                    "field_name": "event_presence",
                    "expected_value": "MISSING",
                    "actual_value": "PRESENT",
                    "meddra_version": meddra_version,
                }
            )
            continue

        if safety_sae is None:
            discrepancies.append(
                {
                    "source": "SAFETY",
                    "case_event_key": key,
                    "field_name": "event_presence",
                    "expected_value": "PRESENT",
                    "actual_value": "MISSING",
                    "meddra_version": meddra_version,
                }
            )
            continue

        # 2. Field-level comparisons
        # standard fields
        standard_fields = ["AESER", "AESTDTC", "AEENDTC", "AESEV", "AEREL", "AEOUT"]
        for f in standard_fields:
            v_edc = getattr(edc_sae, f, None)
            v_safety = getattr(safety_sae, f, None)

            # Stringify and strip for comparison
            s_edc = str(v_edc).strip() if v_edc is not None else None
            s_safety = str(v_safety).strip() if v_safety is not None else None

            if s_edc != s_safety:
                discrepancies.append(
                    {
                        "source": "RECONCILIATION",
                        "case_event_key": key,
                        "field_name": f,
                        "expected_value": s_edc,
                        "actual_value": s_safety,
                        "meddra_version": meddra_version,
                    }
                )

        # MedDRA coding comparison
        edc_coding = edc_sae.meddra_coding
        safety_coding = safety_sae.meddra_coding

        if (edc_coding is None) != (safety_coding is None):
            discrepancies.append(
                {
                    "source": "RECONCILIATION",
                    "case_event_key": key,
                    "field_name": "meddra_coding",
                    "expected_value": f"LLT_CODE:{edc_coding.llt_code}"
                    if edc_coding
                    else "None",
                    "actual_value": f"LLT_CODE:{safety_coding.llt_code}"
                    if safety_coding
                    else "None",
                    "meddra_version": meddra_version,
                }
            )
        elif edc_coding is not None and safety_coding is not None:
            # Compare resolved codes and hierarchies
            meddra_mismatch = False
            if edc_coding.llt_code != safety_coding.llt_code:
                meddra_mismatch = True
            if edc_coding.pt_code != safety_coding.pt_code:
                meddra_mismatch = True
            if edc_coding.soc_code != safety_coding.soc_code:
                meddra_mismatch = True
            if edc_coding.primary_soc_flag != safety_coding.primary_soc_flag:
                meddra_mismatch = True

            if meddra_mismatch:
                discrepancies.append(
                    {
                        "source": "RECONCILIATION",
                        "case_event_key": key,
                        "field_name": "meddra_coding",
                        "expected_value": f"LLT:{edc_coding.llt_code}, PT:{edc_coding.pt_code}, SOC:{edc_coding.soc_code}, primary_soc_flag:{edc_coding.primary_soc_flag}",
                        "actual_value": f"LLT:{safety_coding.llt_code}, PT:{safety_coding.pt_code}, SOC:{safety_coding.soc_code}, primary_soc_flag:{safety_coding.primary_soc_flag}",
                        "meddra_version": meddra_version,
                    }
                )

    # Sort deterministically by case_event_key then field_name
    discrepancies.sort(key=lambda x: (x["case_event_key"], x["field_name"]))
    return discrepancies


async def run_reconciliation(
    study_id: str,
    session: AsyncSession,
    created_by: str,
    reason_for_change: str,
    client: Any | None = None,
    meddra_version: str = "26.0",
) -> dict[str, Any]:
    """Orchestrates safety reconciliation by comparing EDC and safety-system data.

    Processes EDC adverse event records, queries the external clinical dictionaries
    to retrieve and normalize MedDRA coding, matches them with safety case records,
    detects material mismatches, and persists the reconciliation outcome.

    Args:
        study_id: Unique trial/study identifier.
        session: Database session used to persist records.
        created_by: User or service initiating the run.
        reason_for_change: GxP audit change reason explanation.
        client: Optional HTTPX async client used for service interaction.
        meddra_version: Target MedDRA version to use. Defaults to "26.0".

    Returns:
        A dictionary containing the generated "run" and "discrepancies" lists.
    """
    exec_client = ExecutionClient()
    adapter = SafetyDatabaseAdapter(client=client)

    # 1. Fetch EDC AE data
    edc_data = await exec_client.fetch_ae_data(study_id=study_id, client=client)
    ae_records = edc_data.get("AE", [])

    # 2. Resolve MedDRA codes for each EDC AE record
    normalized_edc_saes = []
    for ae_rec in ae_records:
        aeterm = ae_rec.get("AETERM")
        meddra_coding = None

        if aeterm:
            key = (aeterm, meddra_version)
            res = None
            expired_data = None

            # Retrieve from cache under lock
            with terminology_cache._lock:
                if key in terminology_cache._cache:
                    data, timestamp = terminology_cache._cache[key]
                    if time.time() - timestamp < terminology_cache.ttl:
                        res = data
                    else:
                        expired_data = data

            if res is None:
                try:
                    res = await exec_client.resolve_meddra_code(
                        term=aeterm, version=meddra_version, client=client
                    )
                    terminology_cache.set(aeterm, meddra_version, res)
                except Exception as e:
                    if expired_data is not None:
                        logger.warning(
                            "Failed to resolve MedDRA code for term '%s', falling back to expired cache: %s",
                            aeterm,
                            e,
                        )
                        res = expired_data
                    else:
                        logger.warning(
                            "Failed to resolve MedDRA code for term '%s': %s", aeterm, e
                        )

            if res:
                matches = res.get("matches") or []
                if matches:
                    top_match = matches[0]
                    meddra_coding = MedDRACoding(
                        llt_code=top_match.get("llt_code") or "",
                        llt_name=top_match.get("llt_name") or "",
                        pt_code=top_match.get("pt_code") or "",
                        pt_name=top_match.get("pt_name") or "",
                        hlt_code=top_match.get("hlt_code") or "",
                        hlt_name=top_match.get("hlt_name") or "",
                        hlgt_code=top_match.get("hlgt_code") or "",
                        hlgt_name=top_match.get("hlgt_name") or "",
                        soc_code=top_match.get("soc_code") or "",
                        soc_name=top_match.get("soc_name") or "",
                        primary_soc_flag=top_match.get("primary_soc_flag"),
                        score=top_match.get("score", 1.0),
                    )

        normalized_edc_saes.append(normalize_edc_ae_to_sae(ae_rec, meddra_coding))

    # 3. Fetch safety cases from SafetyDatabaseAdapter
    try:
        cases_payload = await adapter.fetch_cases()
    except Exception as e:
        logger.error("Failed to fetch external safety cases: %s", e)
        cases_payload = []

    if isinstance(cases_payload, dict):
        cases_payload = [cases_payload]

    # 4. Normalize external safety cases to serious adverse events
    normalized_safety_saes = []
    for case_dict in cases_payload:
        normalized_safety_saes.extend(normalize_external_icsr_to_saes(case_dict))

    # 5. Invoke the pure comparison function
    raw_discrepancies = compare_sae_records(
        edc_saes=normalized_edc_saes,
        safety_saes=normalized_safety_saes,
        meddra_version=meddra_version,
    )

    # 6. Persist results inside transaction
    async with session.begin_nested():
        from sqlalchemy import func, select

        # Query max run version index
        stmt_run_max = select(func.max(SAEReconciliationRun.version_index)).where(
            SAEReconciliationRun.study_id == study_id
        )
        res_run_max = await session.execute(stmt_run_max)
        max_run_idx = res_run_max.scalar() or 0
        next_run_version = max_run_idx + 1

        run = SAEReconciliationRun(
            study_id=study_id,
            created_by=created_by,
            reason_for_change=reason_for_change,
            version_index=next_run_version,
        )
        session.add(run)
        await session.flush()

        persisted_discrepancies = []
        for d in raw_discrepancies:
            # Query max discrepancy version index
            stmt_disc_max = select(func.max(SAEDiscrepancy.version_index)).where(
                SAEDiscrepancy.case_event_key == d["case_event_key"],
                SAEDiscrepancy.field_name == d["field_name"],
            )
            res_disc_max = await session.execute(stmt_disc_max)
            max_disc_idx = res_disc_max.scalar() or 0
            next_disc_version = max_disc_idx + 1

            disc = SAEDiscrepancy(
                run_id=run.id,
                source=d["source"],
                case_event_key=d["case_event_key"],
                field_name=d["field_name"],
                expected_value=d["expected_value"],
                actual_value=d["actual_value"],
                meddra_version=d["meddra_version"],
                created_by=created_by,
                reason_for_change=reason_for_change,
                version_index=next_disc_version,
            )
            session.add(disc)
            persisted_discrepancies.append(disc)

        await session.flush()

    return {
        "run": run,
        "discrepancies": persisted_discrepancies,
    }
