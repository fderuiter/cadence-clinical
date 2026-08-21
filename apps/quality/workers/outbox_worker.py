import asyncio
import logging
import os
import sys
from datetime import UTC, datetime, timedelta

import httpx
from sqlalchemy import select

from apps.quality.adapters.database import db_manager
from apps.quality.infrastructure.models import IntegrationOutbox
from packages.security.rbac_helpers import build_gateway_headers

logger = logging.getLogger("quality-outbox-worker")
outbox_task = None


async def poll_and_dispatch(session_maker=None) -> int:
    if session_maker is None:
        session_maker = db_manager.get_session_maker()

    processed_count = 0
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

        ctms_base_url = os.getenv("CTMS_SERVICE_URL", "http://localhost:8000").rstrip("/")

        for record in records:
            if record.status not in ("PENDING", "FAILED") or not record.retry_eligible:
                continue

            processed_count += 1
            payload = record.payload or {}
            event_type = record.event_type

            try:
                if event_type in ("CAPA_STAGE_TRANSITION", "QUALITY_CAPA_UPDATE"):
                    deviation_id = payload.get("deviation_id")
                    target_status = payload.get("target_ctms_status")
                    capa_id = payload.get("capa_id")

                    if not deviation_id or not target_status:
                        record.status = "FAILED"
                        record.last_error = "Missing deviation_id or target_ctms_status in payload"
                        record.retry_eligible = False
                        continue

                    url = f"{ctms_base_url}/api/v1/ctms/deviations/{deviation_id}/status"
                    user_id = payload.get("user_id", "quality-outbox-worker")
                    user_role = payload.get("user_role", "quality_manager,admin")
                    change_reason = payload.get(
                        "change_reason", "Automated CAPA stage outbox sync"
                    )

                    headers = build_gateway_headers(
                        user_id=user_id,
                        roles=user_role,
                        change_reason=change_reason,
                    )

                    body = {
                        "status": target_status,
                        "quality_capa_id": capa_id,
                    }

                    async with httpx.AsyncClient(timeout=5.0) as client:
                        resp = await client.put(url, json=body, headers=headers)
                        if resp.status_code in (200, 201):
                            record.status = "SUCCESS"
                            record.completed_at = datetime.now(UTC)
                            record.last_error = None
                            record.retry_eligible = False
                        else:
                            raise httpx.HTTPStatusError(
                                f"CTMS returned HTTP {resp.status_code}: {resp.text}",
                                request=resp.request,
                                response=resp,
                            )
                else:
                    record.status = "SUCCESS"
                    record.completed_at = datetime.now(UTC)
                    record.last_error = None
                    record.retry_eligible = False

            except Exception as e:
                record.status = "FAILED"
                record.attempts += 1
                record.last_error = str(e)

                attempt_cap = int(os.getenv("OUTBOX_ATTEMPT_CAP", "5"))
                if record.attempts >= attempt_cap:
                    record.retry_eligible = False

                backoff_seconds = min(300, 2**record.attempts)
                record.next_retry_at = datetime.now(UTC) + timedelta(
                    seconds=backoff_seconds
                )

        await session.commit()
    return processed_count


async def outbox_lifecycle_worker() -> None:
    poll_interval = float(os.getenv("OUTBOX_POLL_INTERVAL_SECONDS", "5.0"))
    while True:
        try:
            await poll_and_dispatch()
        except asyncio.CancelledError:
            break
        except Exception as e:
            logger.error("Error in Quality outbox dispatcher loop: %s", e, exc_info=True)
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
