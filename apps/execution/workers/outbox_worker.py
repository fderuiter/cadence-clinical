import asyncio
import logging
import os
import sys
import time
from datetime import UTC, datetime, timedelta
from typing import Any

import httpx
from sqlalchemy import select, text

from apps.execution.database.core import db_manager
from apps.execution.database.models import IntegrationOutbox
from packages.security.signing import generate_gateway_signature

logger = logging.getLogger("execution-outbox-worker")
outbox_task = None
_session_maker = None


async def poll_and_dispatch() -> None:
    global _session_maker
    session_maker = _session_maker or db_manager.get_session_maker()

    # Get parameters from environment
    batch_size = int(os.getenv("OUTBOX_BATCH_SIZE", "20"))
    max_concurrency = int(os.getenv("OUTBOX_MAX_CONCURRENCY", "10"))
    attempt_cap = int(os.getenv("OUTBOX_ATTEMPT_CAP", "5"))

    now = datetime.now(UTC)

    # 1. Claiming Phase: open a brief transaction using session_maker()
    async with session_maker() as session:
        if session.bind.dialect.name == "postgresql":
            lock_res = await session.execute(
                text("SELECT pg_try_advisory_xact_lock(42003);")
            )
            acquired = lock_res.scalar()
            if not acquired:
                logger.info(
                    "Could not acquire transaction advisory lock 42003 for outbox dispatcher. Skipping cycle."
                )
                return

        # Construct select statement for records that are eligible for processing
        stmt = select(IntegrationOutbox).where(
            IntegrationOutbox.status.in_(("PENDING", "FAILED")),
            IntegrationOutbox.retry_eligible.is_(True),
            (IntegrationOutbox.next_retry_at.is_(None))
            | (IntegrationOutbox.next_retry_at <= now),
        )

        # Dynamically apply dialect-aware locking
        dialect_name = ""
        if db_manager.engine and db_manager.engine.dialect:
            dialect_name = db_manager.engine.dialect.name.lower()

        if "postgresql" in dialect_name:
            stmt = stmt.with_for_update(skip_locked=True)
        else:
            # SQLite or other: dynamically bypass lock-bypass parameters (skip_locked)
            stmt = stmt.with_for_update()

        # Apply batch size limit
        stmt = stmt.limit(batch_size)

        res = await session.execute(stmt)
        records = res.scalars().all()

        if not records:
            return

        # Extract record data for concurrent dispatch before committing/closing session
        claimed_records_data = []
        for record in records:
            # Set their state in-memory to "PROCESSING"
            record.status = "PROCESSING"
            claimed_records_data.append(
                {
                    "id": record.id,
                    "event_type": record.event_type,
                    "payload": record.payload,
                    "attempts": record.attempts,
                    "correlation_id": record.correlation_id,
                    "created_by": record.created_by,
                    "reason_for_change": record.reason_for_change,
                }
            )

        # Commit the transaction immediately and close the session context.
        # This releases all database locks and connection instances before any network I/O.
        await session.commit()

    # 2. Dispatching Phase: process the claimed batch concurrently
    semaphore = asyncio.Semaphore(max_concurrency)

    async def update_record_status(
        record_id: str, success: bool, error: Exception | None = None
    ) -> None:
        async with session_maker() as update_session:
            update_stmt = select(IntegrationOutbox).where(
                IntegrationOutbox.id == record_id
            )
            update_res = await update_session.execute(update_stmt)
            rec = update_res.scalars().first()
            if not rec:
                return

            if success:
                rec.status = "SUCCESS"
                rec.completed_at = datetime.now(UTC)
                rec.last_error = None
            else:
                rec.status = "FAILED"
                rec.attempts += 1
                rec.last_error = str(error)

                if rec.attempts >= attempt_cap:
                    rec.retry_eligible = False

                backoff_seconds = min(60, 2**rec.attempts)
                rec.next_retry_at = datetime.now(UTC) + timedelta(
                    seconds=backoff_seconds
                )

            await update_session.commit()

    async def dispatch_record(record_data: dict) -> None:
        async with semaphore:
            try:
                if record_data["event_type"] == "TRIAL_LOCK":
                    etmf_url = os.getenv("ETMF_URL", "http://localhost:8003").rstrip(
                        "/"
                    )
                    gateway_secret_env = os.getenv(
                        "GATEWAY_SECRET", "internal-gateway-secret-12345"
                    )
                    gateway_secret = (
                        gateway_secret_env.encode("utf-8")
                        if isinstance(gateway_secret_env, str)
                        else gateway_secret_env
                    )

                    user_id = "execution-outbox-worker"
                    roles = "Data Manager"
                    # Generate cryptographic signatures late right before the HTTP call
                    timestamp = str(time.time())
                    reason = record_data["payload"].get(
                        "reason", "Outbox Lock Propagation"
                    )

                    signature = generate_gateway_signature(
                        user_id=user_id,
                        roles=roles,
                        timestamp=timestamp,
                        secret=gateway_secret,
                        change_reason=reason,
                    )

                    headers = {
                        "X-User-Id": user_id,
                        "X-User-Roles": roles,
                        "X-Gateway-Timestamp": timestamp,
                        "X-Gateway-Signature": signature,
                        "X-Signature-Version": "2",
                        "X-Change-Reason": reason,
                    }

                    url = f"{etmf_url}/api/v1/etmf/locks/trial/lock"

                    async with httpx.AsyncClient(timeout=5.0) as client:
                        resp = await client.post(
                            url, headers=headers, json={"reason": reason}
                        )
                        resp.raise_for_status()

                elif record_data["event_type"] == "EDC_QUERY_RESOLVE":
                    query_id = record_data["payload"].get("query_id")
                    if query_id:
                        from apps.execution.database.models import ClinicalQuery

                        async with session_maker() as resolve_session:
                            stmt_q = select(ClinicalQuery).where(
                                ClinicalQuery.id == query_id,
                                ClinicalQuery.is_deleted.is_(False),
                            )
                            res_q = await resolve_session.execute(stmt_q)
                            query_item = res_q.scalars().first()
                            if query_item:
                                actor = record_data["payload"].get("actor", "system")
                                action = record_data["payload"].get("action", "ACCEPT")
                                coded_code = record_data["payload"].get("coded_code")

                                query_item.status = "CLOSED"
                                query_item.resolver = actor
                                query_item.resolved_at = datetime.now(UTC).replace(
                                    tzinfo=None
                                )
                                query_item.response = (
                                    f"Resolved via manual coding action: "
                                    f"{action} on code {coded_code}."
                                )
                                resolve_session.add(query_item)
                                await resolve_session.commit()

                # On completion (success), update standard status fields within a separate transaction
                await update_record_status(record_data["id"], success=True)
            except Exception as e:
                # On failure, update failure counts, error details, and retry schedules in a separate transaction
                await update_record_status(record_data["id"], success=False, error=e)

    # Execute all dispatches concurrently using asyncio.gather
    await asyncio.gather(*(dispatch_record(data) for data in claimed_records_data))


async def outbox_lifecycle_worker() -> None:
    poll_interval = float(os.getenv("OUTBOX_POLL_INTERVAL_SECONDS", "5.0"))
    while True:
        try:
            await poll_and_dispatch()
        except asyncio.CancelledError:
            break
        except Exception as e:
            logger.error(
                "Error in Execution outbox dispatcher loop: %s", e, exc_info=True
            )
        await asyncio.sleep(poll_interval)


def start_outbox_worker(session_maker: Any = None) -> None:
    global outbox_task, _session_maker
    if "pytest" in sys.modules or os.getenv("TESTING") == "true":
        return
    _session_maker = session_maker
    outbox_task = asyncio.create_task(outbox_lifecycle_worker())


def stop_outbox_worker() -> None:
    global outbox_task
    if outbox_task:
        outbox_task.cancel()
