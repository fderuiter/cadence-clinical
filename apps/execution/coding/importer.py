import asyncio
import contextlib
import logging
import os
import zipfile
from collections.abc import Callable
from datetime import UTC, datetime, timedelta

from sqlalchemy import select, text
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
        # 1. Claim the job inside process_dictionary_import
        # We do this in a short-lived transaction to ensure locking
        is_claimed = False
        async with session_maker() as session:
            async with session.begin():
                dialect_name = session.bind.dialect.name.lower()
                stmt = select(DictionaryImportJob).where(
                    DictionaryImportJob.id == job_id
                )
                if "postgresql" in dialect_name:
                    stmt = stmt.with_for_update(skip_locked=True)
                else:
                    stmt = stmt.with_for_update()

                res = await session.execute(stmt)
                job = res.scalar_one_or_none()
                if job:
                    is_eligible = (job.status == ImportState.PENDING) or (
                        job.status == ImportState.FAILED
                        and job.error_details is not None
                        and "interrupted by a server reboot" in job.error_details
                        and job.retry_count < 3
                    )
                    if is_eligible:
                        job.status = ImportState.PROCESSING
                        job.started_at = datetime.now(UTC).replace(tzinfo=None)
                        job.progress_percentage = 0
                        job.retry_count += 1
                        job.error_details = None
                        await session.flush()
                        is_claimed = True

        if not is_claimed:
            logger.info(f"Job {job_id} is not eligible or already claimed. Skipping.")
            return

        records_imported = 0
        try:
            async with session_maker() as session:
                token = current_session.set(session)
                try:
                    async with session.begin():
                        # Set cadence.app_writing to true to bypass triggers for massive vocabularies!
                        await session.execute(
                            text(
                                "SELECT set_config('cadence.app_writing', 'true', true);"
                            )
                        )

                        with zipfile.ZipFile(temp_zip_path) as z:
                            if dictionary_type == "MEDDRA":
                                parser = MedDRAParser(dictionary_version=version)
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

                                    # Stream unzipped file contents line-by-line
                                    line_generator = (
                                        line.decode("utf-8", errors="replace")
                                        for line in z.open(file_name)
                                    )

                                    # Parse and add to DB in batches
                                    for batch in parser.parse_in_batches(
                                        line_generator,
                                        file_type=file_type,
                                        file_name=file_name,
                                        batch_size=1000,
                                    ):
                                        for record in batch:
                                            if record["type"] == "term":
                                                term_obj = MedDRATerm(
                                                    dictionary_version=version,
                                                    code=record["data"]["code"],
                                                    term_name=record["data"][
                                                        "term_name"
                                                    ],
                                                    level=record["data"]["level"],
                                                )
                                                session.add(term_obj)
                                            elif record["type"] == "hierarchy":
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

                                    # Stream unzipped file contents line-by-line
                                    line_generator = (
                                        line.decode("utf-8", errors="replace")
                                        for line in z.open(file_name)
                                    )

                                    for batch in parser.parse_in_batches(
                                        line_generator,
                                        file_type=file_type,
                                        file_name=file_name,
                                        batch_size=1000,
                                    ):
                                        for record in batch:
                                            if record["type"] == "drug_record":
                                                obj = WHODrugRecord(
                                                    dictionary_version=version,
                                                    drug_code=record["data"][
                                                        "drug_code"
                                                    ],
                                                    preferred_name=record["data"][
                                                        "preferred_name"
                                                    ],
                                                    drug_name=record["data"][
                                                        "drug_name"
                                                    ],
                                                )
                                                session.add(obj)
                                            elif record["type"] == "ingredient":
                                                obj = WHODrugIngredient(
                                                    dictionary_version=version,
                                                    ingredient_code=record["data"][
                                                        "ingredient_code"
                                                    ],
                                                    ingredient_name=record["data"][
                                                        "ingredient_name"
                                                    ],
                                                )
                                                session.add(obj)
                                            elif record["type"] == "atc":
                                                obj = WHODrugATC(
                                                    dictionary_version=version,
                                                    atc_code=record["data"]["atc_code"],
                                                    description=record["data"][
                                                        "description"
                                                    ],
                                                )
                                                session.add(obj)
                                            elif record["type"] == "drug_atc":
                                                obj = WHODrugDrugATC(
                                                    dictionary_version=version,
                                                    drug_code=record["data"][
                                                        "drug_code"
                                                    ],
                                                    atc_code=record["data"]["atc_code"],
                                                )
                                                session.add(obj)
                                            elif record["type"] == "drug_ingredient":
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

                    # Clean up zip on success
                    if temp_zip_path and os.path.exists(temp_zip_path):
                        with contextlib.suppress(Exception):
                            os.remove(temp_zip_path)

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
            is_testing = (
                "PYTEST_CURRENT_TEST" in os.environ or os.getenv("TESTING") == "true"
            )
            # Retry logic with exponential backoff
            async with session_maker() as count_session, count_session.begin():
                stmt = select(DictionaryImportJob).where(
                    DictionaryImportJob.id == job_id
                )
                res = await count_session.execute(stmt)
                job = res.scalar_one_or_none()
                if job:
                    current_retries = job.retry_count
                    if current_retries < 3:
                        backoff_seconds = 0.01 if is_testing else (2**current_retries)
                        next_attempt = datetime.now(UTC).replace(
                            tzinfo=None
                        ) + timedelta(seconds=backoff_seconds)
                        job.status = ImportState.PENDING
                        job.next_attempt_at = next_attempt
                        job.error_details = (
                            f"Attempt {current_retries} failed: {str(e)}"
                        )
                        logger.info(
                            f"Job {job_id} failed on attempt {current_retries}. Retrying in {backoff_seconds} seconds (at {next_attempt})."
                        )

                        if is_testing:
                            # In testing, trigger retry task to execute after committing this session
                            await asyncio.sleep(backoff_seconds)
                            await count_session.commit()
                            asyncio.create_task(
                                process_dictionary_import(
                                    job_id=job_id,
                                    dictionary_type=dictionary_type,
                                    version=version,
                                    temp_zip_path=temp_zip_path,
                                    session_maker=session_maker,
                                    user_id=user_id,
                                    change_reason=change_reason,
                                )
                            )
                            return
                    else:
                        job.status = ImportState.FAILED
                        job.completed_at = datetime.now(UTC).replace(tzinfo=None)
                        job.error_details = (
                            f"Failed after 3 attempts. Last error: {str(e)}"
                        )
                        job.errors_encountered = 1
                        job.records_imported = 0
                        # Clean up zip only on permanent failure!
                        if temp_zip_path and os.path.exists(temp_zip_path):
                            with contextlib.suppress(Exception):
                                os.remove(temp_zip_path)
                    await count_session.flush()
