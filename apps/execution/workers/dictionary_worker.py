import asyncio
import logging
import os
import sys
from datetime import UTC, datetime
from typing import Any

from sqlalchemy import select

from apps.execution.coding.importer import process_dictionary_import
from apps.execution.database.models import DictionaryImportJob, ImportState

logger = logging.getLogger("dictionary-import-worker")
dictionary_task = None
_session_maker = None


async def poll_and_process_imports() -> None:
    global _session_maker
    if not _session_maker:
        return

    now = datetime.now(UTC).replace(tzinfo=None)

    async with _session_maker() as session:
        dialect_name = session.bind.dialect.name.lower()

        # Select jobs that are eligible for processing:
        # 1. Status is PENDING and (next_attempt_at is None or <= now)
        # 2. Status is FAILED, has "interrupted by a server reboot", and retry_count < 3
        stmt = select(DictionaryImportJob).where(
            (
                (DictionaryImportJob.status == ImportState.PENDING)
                & (
                    (DictionaryImportJob.next_attempt_at.is_(None))
                    | (DictionaryImportJob.next_attempt_at <= now)
                )
            )
            | (
                (DictionaryImportJob.status == ImportState.FAILED)
                & (
                    DictionaryImportJob.error_details.like(
                        "%interrupted by a server reboot%"
                    )
                )
                & (DictionaryImportJob.retry_count < 3)
            )
        )

        if "postgresql" in dialect_name:
            stmt = stmt.with_for_update(skip_locked=True)
        else:
            stmt = stmt.with_for_update()

        res = await session.execute(stmt)
        jobs = res.scalars().all()

        if not jobs:
            return

        for job in jobs:
            logger.info(
                f"Background worker claiming dictionary import job {job.id} (type: {job.dictionary_type}, version: {job.dictionary_version})"
            )

            # Since process_dictionary_import will claim the job inside its own transaction (checking eligibility,
            # transition state, incrementing retry count, setting audit context, etc.), we can simply run it!
            # That decouples claiming/locking perfectly and avoids duplicate execution.
            try:
                await process_dictionary_import(
                    job_id=job.id,
                    dictionary_type=job.dictionary_type.value,
                    version=job.dictionary_version,
                    temp_zip_path=job.temp_zip_path,
                    session_maker=_session_maker,
                    user_id=job.user_id,
                    change_reason=job.change_reason,
                )
            except Exception as e:
                logger.error(
                    f"Error processing dictionary import job {job.id}: {e}",
                    exc_info=True,
                )


async def dictionary_lifecycle_worker() -> None:
    poll_interval = float(os.getenv("DICTIONARY_POLL_INTERVAL_SECONDS", "5.0"))
    while True:
        try:
            await poll_and_process_imports()
        except asyncio.CancelledError:
            break
        except Exception as e:
            logger.error("Error in Dictionary Import worker loop: %s", e, exc_info=True)
        await asyncio.sleep(poll_interval)


def start_dictionary_worker(session_maker: Any = None) -> None:
    global dictionary_task, _session_maker
    if "pytest" in sys.modules or os.getenv("TESTING") == "true":
        return
    _session_maker = session_maker
    dictionary_task = asyncio.create_task(dictionary_lifecycle_worker())


def stop_dictionary_worker() -> None:
    global dictionary_task
    if dictionary_task:
        dictionary_task.cancel()
