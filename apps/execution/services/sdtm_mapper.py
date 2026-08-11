"""
CDASH-to-SDTM Domain Mapping Engine.

Provides automated rule-based transformations including ISO 8601 standardizing,
sequence number auto-incrementing, study day derivations, and controlled terminology
lookup.

Requirements: PRD-SYS-001
"""

import re
from datetime import date, datetime
from typing import Any

from sqlalchemy import select

from apps.execution.domain.sdtm.sdtm_models import (
    SDTMRecordAE,
    SDTMRecordCM,
    SDTMRecordDM,
    SDTMRecordDS,
    SDTMRecordLB,
    SDTMRecordMH,
    SDTMRecordSV,
    SDTMRecordVS,
)

# NCI C-code controlled terminology lookups
CONTROLLED_TERMINOLOGY = {
    "SEVERITY": {
        "MILD": "C49487",
        "MODERATE": "C49488",
        "SEVERE": "C49489",
    },
    "SERIOUSNESS": {
        "Y": "C48450",
        "YES": "C48450",
        "N": "C48451",
        "NO": "C48451",
    },
    "SEX": {
        "M": "C16576",
        "MALE": "C16576",
        "F": "C16575",
        "FEMALE": "C16575",
        "U": "C17998",
        "UNKNOWN": "C17998",
    },
    "RACE": {
        "WHITE": "C41261",
        "BLACK OR AFRICAN AMERICAN": "C16352",
        "ASIAN": "C41260",
        "AMERICAN INDIAN OR ALASKA NATIVE": "C41259",
        "NATIVE HAWAIIAN OR OTHER PACIFIC ISLANDER": "C41219",
        "MULTIPLE": "C17998",
    },
}


def normalize_to_nci_code(category: str, val: Any) -> str:
    """
    Maps an eCRF value to an NCI C-code if available, otherwise returns upper case string.
    """
    if val is None:
        return ""
    val_str = str(val).strip().upper()
    cat_map = CONTROLLED_TERMINOLOGY.get(category.upper())
    if cat_map and val_str in cat_map:
        return cat_map[val_str]
    return val_str


def parse_date(val: Any) -> date | None:
    """
    Safely parses various date formats into a datetime.date object.
    """
    if not val:
        return None
    if isinstance(val, (datetime, date)):
        if isinstance(val, datetime):
            return val.date()
        return val
    if isinstance(val, str):
        val_clean = val.strip().replace("/", "-")
        # YYYY-MM-DD
        match = re.match(r"^(\d{4})-(\d{2})-(\d{2})", val_clean)
        if match:
            try:
                return date(
                    int(match.group(1)), int(match.group(2)), int(match.group(3))
                )
            except ValueError:
                pass
    return None


def standardize_iso_datetime(val: Any) -> str | None:
    """
    Converts standard dates, datetimes, or validated strings into CDISC DTC ISO 8601 format (YYYY-MM-DDThh:mm:ss).
    If partial, standardizes to the matching level of precision.
    """
    if val is None:
        return None
    if isinstance(val, (datetime, date)):
        if isinstance(val, datetime):
            # Formats to YYYY-MM-DDThh:mm:ss
            return val.strftime("%Y-%m-%dT%H:%M:%S")
        return val.strftime("%Y-%m-%d")
    if isinstance(val, str):
        val_clean = val.strip().replace("/", "-")
        if not val_clean:
            return None
        # Check if already in standard ISO 8601 format with/without timezone
        if re.match(r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}", val_clean):
            return val_clean[:19]  # truncate timezone / milliseconds for simplicity
        # Simple date format YYYY-MM-DD
        if re.match(r"^\d{4}-\d{2}-\d{2}$", val_clean):
            return val_clean
        # Try converting "02-Aug-2026" or similar
        for fmt in ("%d-%b-%Y", "%d %b %Y", "%d/%m/%Y", "%Y-%m-%d %H:%M:%S"):
            try:
                dt = datetime.strptime(val_clean, fmt)
                if ":" in val_clean:
                    return dt.strftime("%Y-%m-%dT%H:%M:%S")
                return dt.strftime("%Y-%m-%d")
            except ValueError:
                continue
        return val_clean
    return str(val)


def calculate_study_day(
    event_date_str: str | None, rfstdtc_str: str | None
) -> int | None:
    """
    Calculates the study day (AEDY, VSDY, LBDY, etc.) relative to RFSTDTC.
    CDISC Study Day logic:
    - If Event Date is on or after RFSTDTC: Study Day = (Event Date - RFSTDTC) + 1
    - If Event Date is before RFSTDTC: Study Day = Event Date - RFSTDTC
    """
    if not event_date_str or not rfstdtc_str:
        return None
    event_date = parse_date(event_date_str)
    rfstdtc = parse_date(rfstdtc_str)
    if not event_date or not rfstdtc:
        return None

    delta = (event_date - rfstdtc).days
    if delta >= 0:
        return delta + 1
    return delta


class CDASHToSDTMMapper:
    """
    Automated CDASH-to-SDTM domain mapping transformation engine.
    """

    def map_adverse_events(
        self, study_id: str, subject_id: str, raw_ae_forms: list[dict[str, Any]]
    ) -> list[dict[str, Any]]:
        """Transform raw eCRF Adverse Event observations into standard SDTM AE domain records.

        Requirements: PRD-SYS-001
        """
        sdtm_ae_records = []
        for idx, form in enumerate(raw_ae_forms, start=1):
            severity = form.get("ae_severity") or form.get("AESEV") or ""
            serious = form.get("ae_serious") or form.get("AESER") or "N"
            if isinstance(serious, bool):
                serious = "Y" if serious else "N"

            # Parse and standardize dates
            raw_start = form.get("ae_start_date") or form.get("AESTDTC")
            raw_end = form.get("ae_end_date") or form.get("AEENDTC")
            aestdtc = standardize_iso_datetime(raw_start)
            aeendtc = standardize_iso_datetime(raw_end)

            sdtm_ae_records.append(
                {
                    "STUDYID": study_id,
                    "DOMAIN": "AE",
                    "USUBJID": f"{study_id}-{subject_id}",
                    "AESEQ": idx,
                    "AETERM": str(
                        form.get("ae_term") or form.get("AETERM") or ""
                    ).upper(),
                    "AEDECOD": str(
                        form.get("ae_meddra_pt")
                        or form.get("AEDECOD")
                        or form.get("ae_term")
                        or form.get("AETERM")
                        or ""
                    ).upper(),
                    "AESEV": normalize_to_nci_code("SEVERITY", severity),
                    "AESER": normalize_to_nci_code("SERIOUSNESS", serious),
                    "AESTDTC": aestdtc,
                    "AEENDTC": aeendtc,
                }
            )
        return sdtm_ae_records

    def map_demographics(
        self, study_id: str, subject_id: str, raw_dm: dict[str, Any]
    ) -> dict[str, Any]:
        """
        Transform raw demographics data into SDTM Record DM.
        """
        sex = raw_dm.get("gender") or raw_dm.get("sex") or raw_dm.get("SEX") or "U"
        race = raw_dm.get("race") or raw_dm.get("RACE") or "OTHER"
        rfstdtc = standardize_iso_datetime(
            raw_dm.get("rfstdtc") or raw_dm.get("RFSTDTC")
        )
        brthdtc = standardize_iso_datetime(
            raw_dm.get("birthdate")
            or raw_dm.get("BRTHDTC")
            or raw_dm.get("birth_date")
            or raw_dm.get("date_of_birth")
            or raw_dm.get("dob")
        )

        # Calculate age
        age = raw_dm.get("age") or raw_dm.get("AGE")
        if age is None and rfstdtc and brthdtc:
            from apps.execution.sdtm_mapper import compute_age

            age = compute_age(rfstdtc, brthdtc)

        return {
            "STUDYID": study_id,
            "DOMAIN": "DM",
            "USUBJID": f"{study_id}-{subject_id}",
            "SUBJID": subject_id,
            "RFSTDTC": rfstdtc,
            "BRTHDTC": brthdtc,
            "AGE": age,
            "AGEU": "YEARS",
            "SEX": normalize_to_nci_code("SEX", sex),
            "RACE": normalize_to_nci_code("RACE", race),
            "ETHNIC": raw_dm.get("ethnic") or raw_dm.get("ETHNIC"),
        }

    def map_vital_signs(
        self,
        study_id: str,
        subject_id: str,
        raw_vs_forms: list[dict[str, Any]],
        rfstdtc: str | None = None,
    ) -> list[dict[str, Any]]:
        """
        Transform raw Vital Signs data into SDTM Record VS with unit conversion support and Study Days.
        """
        sdtm_vs_records = []
        for idx, form in enumerate(raw_vs_forms, start=1):
            test_code = form.get("test_code") or form.get("VSTESTCD") or ""
            test_name = form.get("test_name") or form.get("VSTEST") or ""
            val = form.get("value") or form.get("VSORRES")
            unit = form.get("unit") or form.get("VSORRESU") or ""

            # Standard unit conversions
            # LB -> KG
            # IN -> CM
            std_val = val
            std_unit = unit
            if isinstance(val, (int, float)):
                if unit.upper() in ("LB", "[LBF]"):
                    std_val = round(val * 0.45359237, 2)
                    std_unit = "KG"
                elif unit.upper() in ("IN", "[IN_I]"):
                    std_val = round(val * 2.54, 2)
                    std_unit = "CM"

            raw_date = form.get("observation_date") or form.get("VSDTC")
            vsdtc = standardize_iso_datetime(raw_date)

            sdtm_vs_records.append(
                {
                    "STUDYID": study_id,
                    "DOMAIN": "VS",
                    "USUBJID": f"{study_id}-{subject_id}",
                    "VSSEQ": idx,
                    "VSTESTCD": test_code,
                    "VSTEST": test_name,
                    "VSORRES": val,
                    "VSORRESU": unit,
                    "VSSTRESC": str(std_val) if std_val is not None else None,
                    "VSSTRESN": float(std_val) if std_val is not None else None,
                    "VSSTRESU": std_unit,
                    "VSDTC": vsdtc,
                    "VSDY": calculate_study_day(vsdtc, rfstdtc),
                }
            )
        return sdtm_vs_records

    def map_laboratory(
        self,
        study_id: str,
        subject_id: str,
        raw_lb_forms: list[dict[str, Any]],
        rfstdtc: str | None = None,
    ) -> list[dict[str, Any]]:
        """
        Transform raw Lab findings data into SDTM Record LB with Study Days.
        """
        sdtm_lb_records = []
        for idx, form in enumerate(raw_lb_forms, start=1):
            raw_date = form.get("observation_date") or form.get("LBDTC")
            lbdtc = standardize_iso_datetime(raw_date)

            sdtm_lb_records.append(
                {
                    "STUDYID": study_id,
                    "DOMAIN": "LB",
                    "USUBJID": f"{study_id}-{subject_id}",
                    "LBSEQ": idx,
                    "LBTESTCD": form.get("test_code") or form.get("LBTESTCD") or "",
                    "LBTEST": form.get("test_name") or form.get("LBTEST") or "",
                    "LBORRES": str(form.get("value") or form.get("LBORRES") or ""),
                    "LBORRESU": form.get("unit") or form.get("LBORRESU"),
                    "LBSTRESC": str(
                        form.get("normalized_value")
                        or form.get("LBSTRESC")
                        or form.get("value")
                        or ""
                    ),
                    "LBSTRESN": float(form.get("normalized_value"))
                    if form.get("normalized_value") is not None
                    else None,
                    "LBSTRESU": form.get("normalized_unit") or form.get("LBSTRESU"),
                    "LBDTC": lbdtc,
                    "LBDY": calculate_study_day(lbdtc, rfstdtc),
                }
            )
        return sdtm_lb_records

    def map_subject_visits(
        self,
        study_id: str,
        subject_id: str,
        raw_sv_forms: list[dict[str, Any]],
        rfstdtc: str | None = None,
    ) -> list[dict[str, Any]]:
        """
        Transform raw Subject Visit data into SDTM Record SV with Study Days.
        """
        sdtm_sv_records = []
        for idx, form in enumerate(raw_sv_forms, start=1):
            raw_start = form.get("visit_date") or form.get("SVSTDTC")
            raw_end = form.get("visit_end_date") or form.get("SVENDTC")
            svstdtc = standardize_iso_datetime(raw_start)
            svendtc = standardize_iso_datetime(raw_end)

            sdtm_sv_records.append(
                {
                    "STUDYID": study_id,
                    "DOMAIN": "SV",
                    "USUBJID": f"{study_id}-{subject_id}",
                    "SVSEQ": idx,
                    "VISIT": form.get("visit_name") or form.get("VISIT") or "",
                    "SVSTDTC": svstdtc,
                    "SVENDTC": svendtc,
                    "SVDY": calculate_study_day(svstdtc, rfstdtc),
                }
            )
        return sdtm_sv_records

    def map_concomitant_medications(
        self, study_id: str, subject_id: str, raw_cm_forms: list[dict[str, Any]]
    ) -> list[dict[str, Any]]:
        """
        Transform raw Concomitant Medications data into SDTM Record CM.
        """
        sdtm_cm_records = []
        for idx, form in enumerate(raw_cm_forms, start=1):
            sdtm_cm_records.append(
                {
                    "STUDYID": study_id,
                    "DOMAIN": "CM",
                    "USUBJID": f"{study_id}-{subject_id}",
                    "CMSEQ": idx,
                    "CMTRT": str(form.get("cmtrt") or form.get("CMTRT") or "").upper(),
                    "CMDECOD": str(
                        form.get("cmdecod") or form.get("CMDECOD") or ""
                    ).upper(),
                    "CMSTDTC": standardize_iso_datetime(
                        form.get("cmstdtc") or form.get("CMSTDTC")
                    ),
                    "CMENDTC": standardize_iso_datetime(
                        form.get("cmendtc") or form.get("CMENDTC")
                    ),
                }
            )
        return sdtm_cm_records

    def map_dispositions(
        self, study_id: str, subject_id: str, raw_ds_forms: list[dict[str, Any]]
    ) -> list[dict[str, Any]]:
        """
        Transform raw Dispositions data into SDTM Record DS.
        """
        sdtm_ds_records = []
        for idx, form in enumerate(raw_ds_forms, start=1):
            sdtm_ds_records.append(
                {
                    "STUDYID": study_id,
                    "DOMAIN": "DS",
                    "USUBJID": f"{study_id}-{subject_id}",
                    "DSSEQ": idx,
                    "DSTERM": str(
                        form.get("dsterm") or form.get("DSTERM") or ""
                    ).upper(),
                    "DSDECOD": str(
                        form.get("dsdecod") or form.get("DSDECOD") or ""
                    ).upper(),
                    "DSCAT": str(form.get("dscat") or form.get("DSCAT") or "").upper(),
                    "DSSTDTC": standardize_iso_datetime(
                        form.get("dsstdtc") or form.get("DSSTDTC")
                    ),
                }
            )
        return sdtm_ds_records

    def map_medical_history(
        self, study_id: str, subject_id: str, raw_mh_forms: list[dict[str, Any]]
    ) -> list[dict[str, Any]]:
        """
        Transform raw Medical History data into SDTM Record MH.
        """
        sdtm_mh_records = []
        for idx, form in enumerate(raw_mh_forms, start=1):
            sdtm_mh_records.append(
                {
                    "STUDYID": study_id,
                    "DOMAIN": "MH",
                    "USUBJID": f"{study_id}-{subject_id}",
                    "MHSEQ": idx,
                    "MHTERM": str(
                        form.get("mhterm") or form.get("MHTERM") or ""
                    ).upper(),
                    "MHDECOD": str(
                        form.get("mhdecod") or form.get("MHDECOD") or ""
                    ).upper(),
                    "MHCAT": str(form.get("mhcat") or form.get("MHCAT") or "").upper(),
                    "MHDTC": standardize_iso_datetime(
                        form.get("mhdtc") or form.get("MHDTC")
                    ),
                }
            )
        return sdtm_mh_records


def map_cdash_to_sdtm(domain_code: str, ecrf_data: list[dict]) -> list[dict]:
    """
    Executes domain-specific transformation logic.

    Generates derived study days (AEDY, VSDY, LBDY, etc.) relative to RFSTDTC.
    Supports domains: DM, AE, LB, VS, SV, CM, DS, MH.
    """
    mapper = CDASHToSDTMMapper()
    domain = domain_code.upper()

    # Determine default/fallback references
    study_id = "STUDY-USDM"
    subject_id = "SUBJ"
    rfstdtc = None

    # First pass: try to resolve STUDYID, USUBJID, RFSTDTC if available in the dataset
    for item in ecrf_data:
        if item.get("study_id"):
            study_id = item["study_id"]
        elif item.get("STUDYID"):
            study_id = item["STUDYID"]

        if item.get("subject_id"):
            subject_id = item["subject_id"]
        elif item.get("SUBJID"):
            subject_id = item["SUBJID"]

        if item.get("rfstdtc"):
            rfstdtc = standardize_iso_datetime(item["rfstdtc"])
        elif item.get("RFSTDTC"):
            rfstdtc = standardize_iso_datetime(item["RFSTDTC"])

    results = []
    if domain == "DM":
        # Group raw inputs and map them, excluding any AE records
        for item in ecrf_data:
            item_domain = str(item.get("domain") or item.get("DOMAIN") or "").upper()
            if item_domain == "AE" or "AETERM" in item or "ae_term" in item:
                continue
            sid = item.get("subject_id") or item.get("SUBJID") or subject_id
            st_id = item.get("study_id") or item.get("STUDYID") or study_id
            mapped = mapper.map_demographics(st_id, sid, item)
            results.append(mapped)

    elif domain == "AE":
        # Group raw AE entries by subject
        subjects_ae: dict[str, list[dict]] = {}
        for item in ecrf_data:
            sid = item.get("subject_id") or item.get("SUBJID") or subject_id
            subjects_ae.setdefault(sid, []).append(item)

        for sid, forms in subjects_ae.items():
            results.extend(mapper.map_adverse_events(study_id, sid, forms))

    elif domain == "VS":
        subjects_vs: dict[str, list[dict]] = {}
        for item in ecrf_data:
            sid = item.get("subject_id") or item.get("SUBJID") or subject_id
            subjects_vs.setdefault(sid, []).append(item)

        for sid, forms in subjects_vs.items():
            results.extend(mapper.map_vital_signs(study_id, sid, forms, rfstdtc))

    elif domain == "LB":
        subjects_lb: dict[str, list[dict]] = {}
        for item in ecrf_data:
            sid = item.get("subject_id") or item.get("SUBJID") or subject_id
            subjects_lb.setdefault(sid, []).append(item)

        for sid, forms in subjects_lb.items():
            results.extend(mapper.map_laboratory(study_id, sid, forms, rfstdtc))

    elif domain == "SV":
        subjects_sv: dict[str, list[dict]] = {}
        for item in ecrf_data:
            sid = item.get("subject_id") or item.get("SUBJID") or subject_id
            subjects_sv.setdefault(sid, []).append(item)

        for sid, forms in subjects_sv.items():
            results.extend(mapper.map_subject_visits(study_id, sid, forms, rfstdtc))

    elif domain == "CM":
        subjects_cm: dict[str, list[dict]] = {}
        for item in ecrf_data:
            sid = item.get("subject_id") or item.get("SUBJID") or subject_id
            subjects_cm.setdefault(sid, []).append(item)

        for sid, forms in subjects_cm.items():
            results.extend(mapper.map_concomitant_medications(study_id, sid, forms))

    elif domain == "DS":
        subjects_ds: dict[str, list[dict]] = {}
        for item in ecrf_data:
            sid = item.get("subject_id") or item.get("SUBJID") or subject_id
            subjects_ds.setdefault(sid, []).append(item)

        for sid, forms in subjects_ds.items():
            results.extend(mapper.map_dispositions(study_id, sid, forms))

    elif domain == "MH":
        subjects_mh: dict[str, list[dict]] = {}
        for item in ecrf_data:
            sid = item.get("subject_id") or item.get("SUBJID") or subject_id
            subjects_mh.setdefault(sid, []).append(item)

        for sid, forms in subjects_mh.items():
            results.extend(mapper.map_medical_history(study_id, sid, forms))

    else:
        raise ValueError(f"Domain mapping code '{domain_code}' not supported.")

    return results


async def persist_sdtm_records(
    session: Any,
    study_id: str,
    domain_code: str,
    created_by: str = "system",
    reason_for_change: str = "Automated GxP CDASH-to-SDTM mapping",
) -> list[Any]:
    """
    Read eCRF form submission answers (ClinicalObservation and ClinicalSubject) from the
    database, transform them into standard SDTM records, validate against Pydantic schemas,
    and persist into the sdtm_domain_records table.
    """
    from apps.execution.database.models import (
        ClinicalObservation,
        ClinicalSubject,
        SDTMDomainRecord,
    )

    domain = domain_code.upper()

    # Load subjects
    stmt_subj = select(ClinicalSubject).where(
        ClinicalSubject.study_id == study_id,
        ClinicalSubject.is_deleted.is_(False),
    )
    res_subj = await session.execute(stmt_subj)
    subjects = res_subj.scalars().all()

    # Load observations for this domain
    # If compiling AE, we also pull any AE observations that were misclassified as DM
    # If compiling DM, we exclude any AE observations that were misclassified as DM
    ae_test_codes = ["AETERM", "AESEV", "AESER", "AESTDTC", "AEENDTC", "AEDECOD", "AELOC", "AELDTC", "AEREL", "AEOUT"]
    if domain == "AE":
        stmt_obs = select(ClinicalObservation).where(
            ClinicalObservation.study_id == study_id,
            (ClinicalObservation.domain == "AE") | (
                (ClinicalObservation.domain == "DM") &
                (ClinicalObservation.test_code.in_(ae_test_codes))
            ),
            ClinicalObservation.is_deleted.is_(False),
        )
    elif domain == "DM":
        stmt_obs = select(ClinicalObservation).where(
            ClinicalObservation.study_id == study_id,
            ClinicalObservation.domain == "DM",
            ~ClinicalObservation.test_code.in_(ae_test_codes),
            ClinicalObservation.is_deleted.is_(False),
        )
    else:
        stmt_obs = select(ClinicalObservation).where(
            ClinicalObservation.study_id == study_id,
            ClinicalObservation.domain == domain,
            ClinicalObservation.is_deleted.is_(False),
        )
    res_obs = await session.execute(stmt_obs)
    observations = res_obs.scalars().all()

    # We can reconstruct raw CDASH records from the database observations.
    mapper = CDASHToSDTMMapper()

    # If domain is DM, we map from demographics
    raw_records = []
    if domain == "DM":
        for subj in subjects:
            # Reconstruct demographics dictionary
            from apps.execution.sdtm_mapper import get_demographics

            demo_dict = get_demographics(subj)
            # Add subject_id, study_id, etc.
            demo_dict["subject_id"] = subj.subject_id
            demo_dict["study_id"] = subj.study_id
            # Resolve exposure (RFSTDTC) and disposition (RFENDTC) from observations to match DM rules
            stmt_ex = select(ClinicalObservation).where(
                ClinicalObservation.subject_id == subj.subject_id,
                ClinicalObservation.study_id == study_id,
                ClinicalObservation.domain == "EX",
                ClinicalObservation.is_deleted.is_(False),
            )
            res_ex = await session.execute(stmt_ex)
            ex_obs = res_ex.scalars().all()
            if ex_obs:
                dates = [o.value_string for o in ex_obs if o.value_string]
                if dates:
                    demo_dict["rfstdtc"] = min(dates)

            mapped = mapper.map_demographics(study_id, subj.subject_id, demo_dict)
            raw_records.append(mapped)

    elif domain == "AE":
        # Reconstruct raw AE forms
        from collections import defaultdict

        ae_by_page = defaultdict(dict)
        for obs in observations:
            page_key = obs.page_id or f"raw_{obs.id}"
            ae_by_page[page_key]["subject_id"] = obs.subject_id
            if obs.test_code == "AETERM":
                ae_by_page[page_key]["ae_term"] = obs.value_string
            elif obs.test_code == "AESEV":
                ae_by_page[page_key]["ae_severity"] = obs.value_string
            elif obs.test_code == "AESER":
                ae_by_page[page_key]["ae_serious"] = obs.value_string
            elif obs.test_code == "AESTDTC":
                ae_by_page[page_key]["ae_start_date"] = obs.value_string
            elif obs.test_code == "AEENDTC":
                ae_by_page[page_key]["ae_end_date"] = obs.value_string

        # Map each subject's AEs
        subj_aes = defaultdict(list)
        for page_key, ae_dict in ae_by_page.items():
            sid = ae_dict.get("subject_id")
            if sid:
                subj_aes[sid].append(ae_dict)

        for sid, ae_list in subj_aes.items():
            mapped_list = mapper.map_adverse_events(study_id, sid, ae_list)
            raw_records.extend(mapped_list)

    elif domain == "VS":
        from collections import defaultdict

        vs_by_page = defaultdict(dict)
        for obs in observations:
            page_key = obs.page_id or f"raw_{obs.id}"
            vs_by_page[page_key]["subject_id"] = obs.subject_id
            vs_by_page[page_key]["test_code"] = obs.test_code
            vs_by_page[page_key]["test_name"] = obs.test_name
            vs_by_page[page_key]["value"] = obs.value
            vs_by_page[page_key]["unit"] = obs.unit
            vs_by_page[page_key]["observation_date"] = obs.observation_date

        subj_vs = defaultdict(list)
        for page_key, vs_dict in vs_by_page.items():
            sid = vs_dict.get("subject_id")
            if sid:
                subj_vs[sid].append(vs_dict)

        for sid, vs_list in subj_vs.items():
            mapped_list = mapper.map_vital_signs(study_id, sid, vs_list, rfstdtc=None)
            raw_records.extend(mapped_list)

    elif domain == "LB":
        from collections import defaultdict

        lb_by_page = defaultdict(dict)
        for obs in observations:
            page_key = obs.page_id or f"raw_{obs.id}"
            lb_by_page[page_key]["subject_id"] = obs.subject_id
            lb_by_page[page_key]["test_code"] = obs.test_code
            lb_by_page[page_key]["test_name"] = obs.test_name
            lb_by_page[page_key]["value"] = obs.value
            lb_by_page[page_key]["unit"] = obs.unit
            lb_by_page[page_key]["observation_date"] = obs.observation_date
            lb_by_page[page_key]["normalized_value"] = obs.normalized_value
            lb_by_page[page_key]["normalized_unit"] = obs.normalized_unit

        subj_lb = defaultdict(list)
        for page_key, lb_dict in lb_by_page.items():
            sid = lb_dict.get("subject_id")
            if sid:
                subj_lb[sid].append(lb_dict)

        for sid, lb_list in subj_lb.items():
            mapped_list = mapper.map_laboratory(study_id, sid, lb_list, rfstdtc=None)
            raw_records.extend(mapped_list)

    elif domain == "SV":
        from collections import defaultdict

        sv_by_page = defaultdict(dict)
        for obs in observations:
            page_key = obs.page_id or f"raw_{obs.id}"
            sv_by_page[page_key]["subject_id"] = obs.subject_id
            sv_by_page[page_key]["visit_name"] = obs.test_name or obs.test_code
            sv_by_page[page_key]["visit_date"] = obs.observation_date

        subj_sv = defaultdict(list)
        for page_key, sv_dict in sv_by_page.items():
            sid = sv_dict.get("subject_id")
            if sid:
                subj_sv[sid].append(sv_dict)

        for sid, sv_list in subj_sv.items():
            mapped_list = mapper.map_subject_visits(
                study_id, sid, sv_list, rfstdtc=None
            )
            raw_records.extend(mapped_list)

    elif domain == "CM":
        from collections import defaultdict

        cm_by_page = defaultdict(dict)
        for obs in observations:
            page_key = obs.page_id or f"raw_{obs.id}"
            cm_by_page[page_key]["subject_id"] = obs.subject_id
            
            tc = (obs.test_code or "").upper()
            if tc == "CMTRT":
                cm_by_page[page_key]["cmtrt"] = obs.value_string
            elif tc == "CMDECOD":
                cm_by_page[page_key]["cmdecod"] = obs.value_string
            elif tc in ("CMSTDTC", "CM_START_DATE"):
                cm_by_page[page_key]["cmstdtc"] = obs.value_string
            elif tc in ("CMENDTC", "CM_END_DATE"):
                cm_by_page[page_key]["cmendtc"] = obs.value_string

        subj_cm = defaultdict(list)
        for page_key, cm_dict in cm_by_page.items():
            sid = cm_dict.get("subject_id")
            if sid:
                subj_cm[sid].append(cm_dict)

        for sid, cm_list in subj_cm.items():
            mapped_list = mapper.map_concomitant_medications(study_id, sid, cm_list)
            raw_records.extend(mapped_list)

    elif domain == "DS":
        from collections import defaultdict

        ds_by_page = defaultdict(dict)
        for obs in observations:
            page_key = obs.page_id or f"raw_{obs.id}"
            ds_by_page[page_key]["subject_id"] = obs.subject_id
            
            tc = (obs.test_code or "").upper()
            if tc == "DSTERM":
                ds_by_page[page_key]["dsterm"] = obs.value_string
            elif tc == "DSDECOD":
                ds_by_page[page_key]["dsdecod"] = obs.value_string
            elif tc == "DSCAT":
                ds_by_page[page_key]["dscat"] = obs.value_string
            elif tc in ("DSSTDTC", "DS_START_DATE"):
                ds_by_page[page_key]["dsstdtc"] = obs.value_string

        subj_ds = defaultdict(list)
        for page_key, ds_dict in ds_by_page.items():
            sid = ds_dict.get("subject_id")
            if sid:
                subj_ds[sid].append(ds_dict)

        for sid, ds_list in subj_ds.items():
            mapped_list = mapper.map_dispositions(study_id, sid, ds_list)
            raw_records.extend(mapped_list)

    elif domain == "MH":
        from collections import defaultdict

        mh_by_page = defaultdict(dict)
        for obs in observations:
            page_key = obs.page_id or f"raw_{obs.id}"
            mh_by_page[page_key]["subject_id"] = obs.subject_id
            
            tc = (obs.test_code or "").upper()
            if tc == "MHTERM":
                mh_by_page[page_key]["mhterm"] = obs.value_string
            elif tc == "MHDECOD":
                mh_by_page[page_key]["mhdecod"] = obs.value_string
            elif tc == "MHCAT":
                mh_by_page[page_key]["mhcat"] = obs.value_string
            elif tc in ("MHDTC", "MH_DATE"):
                mh_by_page[page_key]["mhdtc"] = obs.value_string

        subj_mh = defaultdict(list)
        for page_key, mh_dict in mh_by_page.items():
            sid = mh_dict.get("subject_id")
            if sid:
                subj_mh[sid].append(mh_dict)

        for sid, mh_list in subj_mh.items():
            mapped_list = mapper.map_medical_history(study_id, sid, mh_list)
            raw_records.extend(mapped_list)

    # For any mapped raw record, instantiate/validate using Pydantic Record schemas
    schema_map = {
        "DM": SDTMRecordDM,
        "AE": SDTMRecordAE,
        "VS": SDTMRecordVS,
        "LB": SDTMRecordLB,
        "SV": SDTMRecordSV,
        "CM": SDTMRecordCM,
        "DS": SDTMRecordDS,
        "MH": SDTMRecordMH,
    }

    schema_cls = schema_map.get(domain)
    persisted_records = []

    for item in raw_records:
        if schema_cls:
            # Ensure mandatory AuditableModel fields are provided
            auditable_data = {
                **item,
                "created_by": created_by,
                "reason_for_change": reason_for_change,
                "version_index": 1,
            }
            # Validate using Pydantic model
            pydantic_obj = schema_cls(**auditable_data)
            dumped_data = pydantic_obj.model_dump(mode="json")
        else:
            dumped_data = item

        usubjid_val = item.get("USUBJID") or f"{study_id}-unknown"

        db_rec = SDTMDomainRecord(
            study_id=study_id,
            domain=domain,
            usubjid=usubjid_val,
            record_data=dumped_data,
        )
        session.add(db_rec)
        persisted_records.append(db_rec)

    await session.flush()
    return persisted_records
