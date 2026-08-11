import asyncio
import logging
import os
import sys
from datetime import UTC, datetime, timedelta
import httpx

from sqlalchemy import select
from apps.etmf.infrastructure.database import db_manager
from apps.etmf.infrastructure.models import IntegrationOutbox

logger = logging.getLogger("etmf-outbox-worker")
outbox_task = None


async def poll_and_dispatch() -> None:
    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        now = datetime.now(UTC)
        stmt = (
            select(IntegrationOutbox)
            .where(
                IntegrationOutbox.status.in_(("PENDING", "FAILED")),
                IntegrationOutbox.retry_eligible.is_(True),
                (IntegrationOutbox.next_retry_at.is_(None))
                | (IntegrationOutbox.next_retry_at <= now),
            )
            .with_for_update()
        )
        res = await session.execute(stmt)
        records = res.scalars().all()

        for record in records:
            if (
                record.status not in ("PENDING", "FAILED")
                or not record.retry_eligible
            ):
                continue

            try:
                if record.event_type == "DOCUMENT_ARCHIVAL":
                    # Send document to the external archival system
                    external_archival_url = os.getenv("EXTERNAL_ARCHIVAL_URL", "http://localhost:8004").rstrip("/")
                    url = f"{external_archival_url}/archive"
                    payload = record.payload
                    
                    async with httpx.AsyncClient(timeout=5.0) as client:
                        resp = await client.post(url, json=payload)
                        resp.raise_for_status()

                # Mark as success
                record.status = "SUCCESS"
                record.completed_at = datetime.now(UTC)
                record.last_error = None

            except Exception as e:
                record.status = "FAILED"
                record.attempts += 1
                record.last_error = str(e)

                attempt_cap = int(os.getenv("OUTBOX_ATTEMPT_CAP", "5"))
                if record.attempts >= attempt_cap:
                    record.retry_eligible = False

                backoff_seconds = min(60, 2**record.attempts)
                record.next_retry_at = datetime.now(UTC) + timedelta(
                    seconds=backoff_seconds
                )

        await session.commit()


async def outbox_lifecycle_worker() -> None:
    poll_interval = float(os.getenv("OUTBOX_POLL_INTERVAL_SECONDS", "5.0"))
    while True:
        try:
            await poll_and_dispatch()
        except asyncio.CancelledError:
            break
        except Exception as e:
            logger.error(
                "Error in eTMF outbox dispatcher loop: %s", e, exc_info=True
            )
        await asyncio.sleep(poll_interval)


def start_outbox_worker() -> None:
    global outbox_task
    if "pytest" in sys.modules or os.getenv("TESTING") == "true":
        return
    outbox_task = asyncio.create_task(outbox_lifecycle_worker())


def stop_outbox_worker() -> None:
    global outbox_task
    if outbox_task:
        outbox_task.cancel()
