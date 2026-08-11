import asyncio
import logging
import os
import sys
import time
from datetime import UTC, datetime, timedelta

import httpx
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import IntegrationOutbox
from packages.security.signing import generate_gateway_signature

logger = logging.getLogger("execution-outbox-worker")
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
            if record.status not in ("PENDING", "FAILED") or not record.retry_eligible:
                continue

            try:
                if record.event_type == "TRIAL_LOCK":
                    # Propagate trial lock to eTMF
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
                    timestamp = str(time.time())
                    reason = record.payload.get("reason", "Outbox Lock Propagation")

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
                "Error in Execution outbox dispatcher loop: %s", e, exc_info=True
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
