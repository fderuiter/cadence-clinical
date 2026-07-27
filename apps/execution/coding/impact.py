import logging
from datetime import datetime

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.coding.matcher import _get_meddra_hierarchy, _get_whodrug_context
from apps.execution.database.models import (
    ClinicalCodingAssignment,
    ClinicalCodingLedger,
    CodingState,
    DictionaryType,
    MedDRATerm,
    RecodingState,
    WHODrugRecord,
)

logger = logging.getLogger(__name__)


async def run_impact_analysis(
    session: AsyncSession,
    dictionary_type: str,
    new_version: str,
    actor: str = "system",
) -> dict:
    """
    Performs up-versioning impact analysis on existing coded assignments for the imported dictionary.
    Classifies coded assignments into three groups:
    1. Unchanged: promoted automatically.
    2. Deprecated/Missing: changed to RECODING_REQUIRED, recoding_status=PENDING.
    3. Hierarchically reclassified: changed to RECODING_REQUIRED, recoding_status=PENDING.

    Returns summary metrics.
    """
    logger.info(f"Starting impact analysis for {dictionary_type} version {new_version}")

    # Convert to standard DictionaryType enum
    try:
        dict_type_enum = DictionaryType[dictionary_type.upper()]
    except KeyError:
        raise ValueError(f"Unsupported dictionary type: {dictionary_type}")

    # Query all active, coded assignments for this dictionary type that have an older version
    stmt = select(ClinicalCodingAssignment).where(
        ClinicalCodingAssignment.dictionary_type == dict_type_enum,
        ClinicalCodingAssignment.dictionary_version != new_version,
        ClinicalCodingAssignment.status.in_(
            [CodingState.CODED, CodingState.AUTO_CODED]
        ),
        ClinicalCodingAssignment.is_deleted.is_(False),
    )
    res = await session.execute(stmt)
    assignments = list(res.scalars().all())

    unchanged_count = 0
    deprecated_count = 0
    reclassified_count = 0
    skipped_count = 0

    for a in assignments:
        # Idempotency check: verify if a ledger entry already exists for this assignment and new version
        stmt_ledger = select(ClinicalCodingLedger).where(
            ClinicalCodingLedger.assignment_id == a.id,
            ClinicalCodingLedger.new_dictionary_version == new_version,
        )
        res_ledger = await session.execute(stmt_ledger)
        if res_ledger.scalars().first():
            logger.info(
                f"Ledger entry already exists for assignment {a.id} on version {new_version}, skipping."
            )
            skipped_count += 1
            continue

        code = a.coded_code
        term = a.coded_term
        old_version = a.dictionary_version
        old_hierarchy = a.hierarchy or {}

        # 1. Check if the code exists in the new dictionary version
        code_exists = False
        new_hierarchy = {}

        if dict_type_enum == DictionaryType.MEDDRA:
            stmt_term = select(MedDRATerm).where(
                MedDRATerm.dictionary_version == new_version,
                MedDRATerm.code == code,
            )
            res_term = await session.execute(stmt_term)
            term_obj = res_term.scalars().first()
            if term_obj:
                code_exists = True
                new_hierarchy_list = await _get_meddra_hierarchy(
                    session, term_obj, new_version
                )
                new_hierarchy = (
                    {"hierarchies": new_hierarchy_list} if new_hierarchy_list else {}
                )

        elif dict_type_enum == DictionaryType.WHODRUG:
            stmt_rec = select(WHODrugRecord).where(
                WHODrugRecord.dictionary_version == new_version,
                WHODrugRecord.drug_code == code,
            )
            res_rec = await session.execute(stmt_rec)
            rec_obj = res_rec.scalars().first()
            if rec_obj:
                code_exists = True
                atc_context, ingredients = await _get_whodrug_context(
                    session, rec_obj, new_version
                )
                new_hierarchy = {"atc_context": atc_context, "ingredients": ingredients}

        if not code_exists:
            # Deprecated / Missing
            a.status = CodingState.RECODING_REQUIRED
            a.recoding_status = RecodingState.PENDING
            session.add(a)

            # Record in Ledger
            ledger = ClinicalCodingLedger(
                assignment_id=a.id,
                verbatim_text=a.verbatim_text,
                observation_id=a.observation_id,
                dictionary_type=dict_type_enum,
                old_dictionary_version=old_version,
                old_coded_code=code,
                old_coded_term=term,
                new_dictionary_version=new_version,
                new_coded_code=code,
                new_coded_term=term,
                recoding_reason=f"Code {code} is deprecated/missing in new dictionary version {new_version}.",
                decision_by=actor,
                decision_at=datetime.utcnow(),
                old_hierarchy=old_hierarchy,
                new_hierarchy={},
                recoding_status=RecodingState.PENDING,
            )
            session.add(ledger)
            deprecated_count += 1
            logger.info(f"Assignment {a.id} classified as DEPRECATED (Code {code})")

        else:
            # Compare hierarchy representation
            hierarchies_equal = False
            if dict_type_enum == DictionaryType.MEDDRA:
                old_h_list = old_hierarchy.get("hierarchies", [])
                new_h_list = new_hierarchy.get("hierarchies", [])

                # Normalize keys and values for comparison
                def normalize_meddra_h(h):
                    return {
                        "llt_code": str(h.get("llt_code", "") or ""),
                        "pt_code": str(h.get("pt_code", "") or ""),
                        "hlt_code": str(h.get("hlt_code", "") or ""),
                        "hlgt_code": str(h.get("hlgt_code", "") or ""),
                        "soc_code": str(h.get("soc_code", "") or ""),
                        "primary_soc_flag": str(h.get("primary_soc_flag", "") or ""),
                    }

                old_norm = sorted(
                    [normalize_meddra_h(h) for h in old_h_list],
                    key=lambda x: (
                        x["llt_code"],
                        x["pt_code"],
                        x["hlt_code"],
                        x["hlgt_code"],
                        x["soc_code"],
                    ),
                )
                new_norm = sorted(
                    [normalize_meddra_h(h) for h in new_h_list],
                    key=lambda x: (
                        x["llt_code"],
                        x["pt_code"],
                        x["hlt_code"],
                        x["hlgt_code"],
                        x["soc_code"],
                    ),
                )
                hierarchies_equal = old_norm == new_norm

            elif dict_type_enum == DictionaryType.WHODRUG:
                old_atc = old_hierarchy.get("atc_context", [])
                new_atc = new_hierarchy.get("atc_context", [])
                old_ing = old_hierarchy.get("ingredients", [])
                new_ing = new_hierarchy.get("ingredients", [])

                old_atc_norm = sorted(
                    [str(item.get("atc_code") or "") for item in old_atc]
                )
                new_atc_norm = sorted(
                    [str(item.get("atc_code") or "") for item in new_atc]
                )

                old_ing_norm = sorted(
                    [str(item.get("ingredient_code") or "") for item in old_ing]
                )
                new_ing_norm = sorted(
                    [str(item.get("ingredient_code") or "") for item in new_ing]
                )

                hierarchies_equal = (old_atc_norm == new_atc_norm) and (
                    old_ing_norm == new_ing_norm
                )

            if hierarchies_equal:
                # Unchanged - Auto Promotion
                # In order to preserve enrollment-time coding semantics (e.g., historical codes remain queryable),
                # we preserve the historical ledger entry representing the original assignment.
                # Here we promote the current active state of the assignment.
                a.dictionary_version = new_version
                a.hierarchy = new_hierarchy
                session.add(a)

                ledger = ClinicalCodingLedger(
                    assignment_id=a.id,
                    verbatim_text=a.verbatim_text,
                    observation_id=a.observation_id,
                    dictionary_type=dict_type_enum,
                    old_dictionary_version=old_version,
                    old_coded_code=code,
                    old_coded_term=term,
                    new_dictionary_version=new_version,
                    new_coded_code=code,
                    new_coded_term=term,
                    recoding_reason=f"Auto-promoted unchanged code {code} to dictionary version {new_version}.",
                    decision_by=actor,
                    decision_at=datetime.utcnow(),
                    old_hierarchy=old_hierarchy,
                    new_hierarchy=new_hierarchy,
                    recoding_status=RecodingState.NONE,
                )
                session.add(ledger)
                unchanged_count += 1
                logger.info(f"Assignment {a.id} classified as UNCHANGED and PROMOTED")

            else:
                # Hierarchically Reclassified - Lock pending sign-off
                # Preserve enrollment-time coding semantics (keep existing code/term intact, but flag for recoding)
                a.status = CodingState.RECODING_REQUIRED
                a.recoding_status = RecodingState.PENDING
                session.add(a)

                ledger = ClinicalCodingLedger(
                    assignment_id=a.id,
                    verbatim_text=a.verbatim_text,
                    observation_id=a.observation_id,
                    dictionary_type=dict_type_enum,
                    old_dictionary_version=old_version,
                    old_coded_code=code,
                    old_coded_term=term,
                    new_dictionary_version=new_version,
                    new_coded_code=code,
                    new_coded_term=term,
                    recoding_reason=f"Hierarchy of code {code} changed in dictionary version {new_version}.",
                    decision_by=actor,
                    decision_at=datetime.utcnow(),
                    old_hierarchy=old_hierarchy,
                    new_hierarchy=new_hierarchy,
                    recoding_status=RecodingState.PENDING,
                )
                session.add(ledger)
                reclassified_count += 1
                logger.info(
                    f"Assignment {a.id} classified as RECLASSIFIED (Code {code})"
                )

    await session.flush()

    return {
        "unchanged": unchanged_count,
        "deprecated": deprecated_count,
        "reclassified": reclassified_count,
        "skipped": skipped_count,
    }
