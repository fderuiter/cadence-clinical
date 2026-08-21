import contextlib
import logging
import os
import zipfile
from collections.abc import Callable
from datetime import UTC, datetime

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.coding.parsers import MedDRAParser, WHODrugParser
from apps.execution.database.context import audit_context, current_session
from apps.execution.database.models import (
    DictionaryImportJob,
    ImportState,
    MedDRAHierarchy,
    MedDRATerm,
    WHODrugATC,
    WHODrugDrugATC,
    WHODrugDrugIngredient,
    WHODrugIngredient,
    WHODrugRecord,
)

logger = logging.getLogger(__name__)


async def update_job_progress(
    job_id: str,
    status: ImportState,
    progress: int,
    records_count: int,
    errors_encountered: int,
    error_details: str | None,
    session_maker: Callable[[], AsyncSession],
) -> None:
    """Updates the import job progress/status in a separate transactional session."""
    async with session_maker() as session, session.begin():
        stmt = select(DictionaryImportJob).where(DictionaryImportJob.id == job_id)
        res = await session.execute(stmt)
        job = res.scalar_one_or_none()
        if job:
            job.status = status
            job.progress_percentage = progress
            job.records_imported = records_count
            job.errors_encountered = errors_encountered
            if error_details is not None:
                job.error_details = error_details[:1000]
            if status in (ImportState.COMPLETED, ImportState.FAILED):
                job.completed_at = datetime.now(UTC).replace(tzinfo=None)


async def process_dictionary_import(
    job_id: str,
    dictionary_type: str,
    version: str,
    temp_zip_path: str,
    session_maker: Callable[[], AsyncSession],
    user_id: str | None = None,
    change_reason: str | None = None,
) -> None:
    """Background tasks worker that parses a dictionary archive and persists records transactionally.

    Ensures that any parser/persistence failures roll back the entire transaction so that
    no partially committed batches are left inconsistent.
    """
    with audit_context(user_id, change_reason):
        # 1. Transition job to PROCESSING (RUNNING)
        await update_job_progress(
            job_id=job_id,
            status=ImportState.PROCESSING,
            progress=0,
            records_count=0,
            errors_encountered=0,
            error_details=None,
            session_maker=session_maker,
        )

        records_imported = 0
        try:
            async with session_maker() as session:
                token = current_session.set(session)
                try:
                    async with session.begin():
                        with zipfile.ZipFile(temp_zip_path) as z:
                            if dictionary_type == "MEDDRA":
                                parser = MedDRAParser(dictionary_version=version)

                                # Pre-fetch existing terms and hierarchies for this version for idempotency
                                existing_terms = set(
                                    (r[0], r[1])
                                    for r in (
                                        await session.execute(
                                            select(
                                                MedDRATerm.code, MedDRATerm.level
                                            ).where(
                                                MedDRATerm.dictionary_version == version
                                            )
                                        )
                                    ).all()
                                )
                                existing_hiers = set(
                                    (r[0], r[1], r[2], r[3], r[4], r[5])
                                    for r in (
                                        await session.execute(
                                            select(
                                                MedDRAHierarchy.llt_code,
                                                MedDRAHierarchy.pt_code,
                                                MedDRAHierarchy.hlt_code,
                                                MedDRAHierarchy.hlgt_code,
                                                MedDRAHierarchy.soc_code,
                                                MedDRAHierarchy.primary_soc_flag,
                                            ).where(
                                                MedDRAHierarchy.dictionary_version
                                                == version
                                            )
                                        )
                                    ).all()
                                )

                                asc_files = [
                                    name
                                    for name in z.namelist()
                                    if name.lower().endswith(".asc")
                                ]

                                total_files = len(asc_files)
                                for idx, file_name in enumerate(asc_files, start=1):
                                    try:
                                        file_type = parser.detect_file_type(file_name)
                                    except ValueError:
                                        # Skip files that don't match any known MedDRA types (like readme, etc.)
                                        continue

                                    with z.open(file_name) as f:
                                        # Read lines and decode using utf-8 (ignoring errors gracefully)
                                        lines = [
                                            line.decode("utf-8", errors="replace")
                                            for line in f
                                        ]

                                    # Parse and add to DB in batches
                                    for batch in parser.parse_in_batches(
                                        lines,
                                        file_type=file_type,
                                        file_name=file_name,
                                        batch_size=1000,
                                    ):
                                        for record in batch:
                                            if record["type"] == "term":
                                                term_key = (
                                                    record["data"]["code"],
                                                    record["data"]["level"],
                                                )
                                                if term_key in existing_terms:
                                                    continue
                                                existing_terms.add(term_key)
                                                term_obj = MedDRATerm(
                                                    dictionary_version=version,
                                                    code=record["data"]["code"],
                                                    term_name=record["data"][
                                                        "term_name"
                                                    ],
                                                    level=record["data"]["level"],
                                                )
                                                session.add(term_obj)
                                                records_imported += 1
                                            elif record["type"] == "hierarchy":
                                                hier_key = (
                                                    record["data"]["llt_code"],
                                                    record["data"]["pt_code"],
                                                    record["data"]["hlt_code"],
                                                    record["data"]["hlgt_code"],
                                                    record["data"]["soc_code"],
                                                    record["data"]["primary_soc_flag"],
                                                )
                                                if hier_key in existing_hiers:
                                                    continue
                                                existing_hiers.add(hier_key)
                                                hier_obj = MedDRAHierarchy(
                                                    dictionary_version=version,
                                                    llt_code=record["data"]["llt_code"],
                                                    pt_code=record["data"]["pt_code"],
                                                    hlt_code=record["data"]["hlt_code"],
                                                    hlgt_code=record["data"][
                                                        "hlgt_code"
                                                    ],
                                                    soc_code=record["data"]["soc_code"],
                                                    primary_soc_flag=record["data"][
                                                        "primary_soc_flag"
                                                    ],
                                                )
                                                session.add(hier_obj)
                                                records_imported += 1

                                        # flush batch to DB to validate, but do not commit yet
                                        await session.flush()

                                    # Update progress incrementally in separate session/transaction
                                    progress_val = int((idx / total_files) * 90)
                                    await update_job_progress(
                                        job_id=job_id,
                                        status=ImportState.PROCESSING,
                                        progress=progress_val,
                                        records_count=records_imported,
                                        errors_encountered=0,
                                        error_details=None,
                                        session_maker=session_maker,
                                    )

                            elif dictionary_type == "WHODRUG":
                                parser = WHODrugParser(dictionary_version=version)

                                # Pre-fetch existing WHODrug records for this version for idempotency
                                existing_drugs = set(
                                    r[0]
                                    for r in (
                                        await session.execute(
                                            select(WHODrugRecord.drug_code).where(
                                                WHODrugRecord.dictionary_version
                                                == version
                                            )
                                        )
                                    ).all()
                                )
                                existing_ingredients = set(
                                    r[0]
                                    for r in (
                                        await session.execute(
                                            select(
                                                WHODrugIngredient.ingredient_code
                                            ).where(
                                                WHODrugIngredient.dictionary_version
                                                == version
                                            )
                                        )
                                    ).all()
                                )
                                existing_atcs = set(
                                    r[0]
                                    for r in (
                                        await session.execute(
                                            select(WHODrugATC.atc_code).where(
                                                WHODrugATC.dictionary_version == version
                                            )
                                        )
                                    ).all()
                                )
                                existing_drug_atcs = set(
                                    (r[0], r[1])
                                    for r in (
                                        await session.execute(
                                            select(
                                                WHODrugDrugATC.drug_code,
                                                WHODrugDrugATC.atc_code,
                                            ).where(
                                                WHODrugDrugATC.dictionary_version
                                                == version
                                            )
                                        )
                                    ).all()
                                )
                                existing_drug_ingredients = set(
                                    (r[0], r[1])
                                    for r in (
                                        await session.execute(
                                            select(
                                                WHODrugDrugIngredient.drug_code,
                                                WHODrugDrugIngredient.ingredient_code,
                                            ).where(
                                                WHODrugDrugIngredient.dictionary_version
                                                == version
                                            )
                                        )
                                    ).all()
                                )

                                drug_files = [
                                    name
                                    for name in z.namelist()
                                    if name.lower().endswith((".txt", ".asc", ".csv"))
                                ]

                                total_files = len(drug_files)
                                for idx, file_name in enumerate(drug_files, start=1):
                                    try:
                                        file_type = parser.detect_file_type(file_name)
                                    except ValueError:
                                        # Skip files that don't match any known WHODrug types
                                        continue

                                    with z.open(file_name) as f:
                                        lines = [
                                            line.decode("utf-8", errors="replace")
                                            for line in f
                                        ]

                                    for batch in parser.parse_in_batches(
                                        lines,
                                        file_type=file_type,
                                        file_name=file_name,
                                        batch_size=1000,
                                    ):
                                        for record in batch:
                                            if record["type"] == "drug_record":
                                                drug_code = record["data"]["drug_code"]
                                                if drug_code in existing_drugs:
                                                    continue
                                                existing_drugs.add(drug_code)
                                                obj = WHODrugRecord(
                                                    dictionary_version=version,
                                                    drug_code=drug_code,
                                                    preferred_name=record["data"][
                                                        "preferred_name"
                                                    ],
                                                    drug_name=record["data"][
                                                        "drug_name"
                                                    ],
                                                )
                                                session.add(obj)
                                                records_imported += 1
                                            elif record["type"] == "ingredient":
                                                ing_code = record["data"][
                                                    "ingredient_code"
                                                ]
                                                if ing_code in existing_ingredients:
                                                    continue
                                                existing_ingredients.add(ing_code)
                                                obj = WHODrugIngredient(
                                                    dictionary_version=version,
                                                    ingredient_code=ing_code,
                                                    ingredient_name=record["data"][
                                                        "ingredient_name"
                                                    ],
                                                )
                                                session.add(obj)
                                                records_imported += 1
                                            elif record["type"] == "atc":
                                                atc_code = record["data"]["atc_code"]
                                                if atc_code in existing_atcs:
                                                    continue
                                                existing_atcs.add(atc_code)
                                                obj = WHODrugATC(
                                                    dictionary_version=version,
                                                    atc_code=atc_code,
                                                    description=record["data"][
                                                        "description"
                                                    ],
                                                )
                                                session.add(obj)
                                                records_imported += 1
                                            elif record["type"] == "drug_atc":
                                                drug_atc_key = (
                                                    record["data"]["drug_code"],
                                                    record["data"]["atc_code"],
                                                )
                                                if drug_atc_key in existing_drug_atcs:
                                                    continue
                                                existing_drug_atcs.add(drug_atc_key)
                                                obj = WHODrugDrugATC(
                                                    dictionary_version=version,
                                                    drug_code=record["data"][
                                                        "drug_code"
                                                    ],
                                                    atc_code=record["data"]["atc_code"],
                                                )
                                                session.add(obj)
                                                records_imported += 1
                                            elif record["type"] == "drug_ingredient":
                                                drug_ing_key = (
                                                    record["data"]["drug_code"],
                                                    record["data"]["ingredient_code"],
                                                )
                                                if (
                                                    drug_ing_key
                                                    in existing_drug_ingredients
                                                ):
                                                    continue
                                                existing_drug_ingredients.add(
                                                    drug_ing_key
                                                )
                                                obj = WHODrugDrugIngredient(
                                                    dictionary_version=version,
                                                    drug_code=record["data"][
                                                        "drug_code"
                                                    ],
                                                    ingredient_code=record["data"][
                                                        "ingredient_code"
                                                    ],
                                                )
                                                session.add(obj)
                                                records_imported += 1

                                        await session.flush()

                                    # Update progress incrementally in separate session/transaction
                                    progress_val = int((idx / total_files) * 90)
                                    await update_job_progress(
                                        job_id=job_id,
                                        status=ImportState.PROCESSING,
                                        progress=progress_val,
                                        records_count=records_imported,
                                        errors_encountered=0,
                                        error_details=None,
                                        session_maker=session_maker,
                                    )

                    # 2. Transition job to COMPLETED on successful commit
                    await update_job_progress(
                        job_id=job_id,
                        status=ImportState.COMPLETED,
                        progress=100,
                        records_count=records_imported,
                        errors_encountered=0,
                        error_details=None,
                        session_maker=session_maker,
                    )

                    # Trigger a post-import impact analysis for the imported dictionary/version
                    from apps.execution.coding.impact import run_impact_analysis

                    async with session_maker() as analysis_session:
                        async with analysis_session.begin():
                            await run_impact_analysis(
                                session=analysis_session,
                                dictionary_type=dictionary_type,
                                new_version=version,
                                actor="system",
                            )

                finally:
                    if token is not None:
                        current_session.reset(token)

        except Exception as e:
            logger.exception("Failed to import dictionary package.")
            # Ensure any failure is recorded as FAILED status with error count and details
            await update_job_progress(
                job_id=job_id,
                status=ImportState.FAILED,
                progress=100,
                records_count=0,  # rolled back, so 0 records were imported successfully
                errors_encountered=1,
                error_details=str(e),
                session_maker=session_maker,
            )
        finally:
            # Clean up the temporary uploaded distribution archive
            if temp_zip_path and os.path.exists(temp_zip_path):
                with contextlib.suppress(Exception):
                    os.remove(temp_zip_path)
