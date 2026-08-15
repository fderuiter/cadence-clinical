import asyncio
import logging
import os
import sys
from datetime import UTC, datetime, timedelta

from sqlalchemy import select

import apps.econsent.adapters.etmf_client as etmf_client_module
from apps.econsent.adapters.database import db_manager
from apps.econsent.adapters.models import (
    ConsentAuditLog,
    ConsentTemplate,
    EtmfArchivalDelivery,
)

logger = logging.getLogger("econsent-worker")
dispatcher_task = None


async def write_audit_log(
    session,
    actor_id: str,
    actor_role: str,
    action: str,
    document_id: str | None,
    details: str,
    reason_for_change: str,
) -> None:
    log_entry = ConsentAuditLog(
        actor_id=actor_id,
        actor_role=actor_role,
        action=action,
        document_id=document_id,
        details=details,
        reason_for_change=reason_for_change,
    )
    session.add(log_entry)
    await session.flush()


async def poll_and_dispatch() -> None:
    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        now = datetime.now(UTC)
        stmt = (
            select(EtmfArchivalDelivery)
            .where(
                EtmfArchivalDelivery.status.in_(("PENDING", "FAILED")),
                EtmfArchivalDelivery.retry_eligible.is_(True),
                (EtmfArchivalDelivery.next_retry_at.is_(None))
                | (EtmfArchivalDelivery.next_retry_at <= now),
            )
            .with_for_update()
        )
        res = await session.execute(stmt)
        deliveries = res.scalars().all()

        for delivery in deliveries:
            if (
                delivery.status not in ("PENDING", "FAILED")
                or not delivery.retry_eligible
            ):
                continue

            try:
                stmt_tpl = select(ConsentTemplate).where(
                    ConsentTemplate.template_id == delivery.template_id,
                    ConsentTemplate.version_index == delivery.version_index,
                )
                res_tpl = await session.execute(stmt_tpl)
                template = res_tpl.scalars().first()
                protocol_version = template.protocol_version if template else "1.0"

                filename = f"icf_{delivery.template_id}_{delivery.subject_pseudonym}_v{delivery.version_index}.json"

                doc_id = await etmf_client_module.forward_icf_to_etmf(
                    study_id=delivery.study_id,
                    site_id=delivery.site_id,
                    filename=filename,
                    content=delivery.artifact_content,
                    mime_type="application/json",
                    protocol_version=protocol_version,
                    metadata_json={
                        "template_id": delivery.template_id,
                        "version_index": delivery.version_index,
                        "subject_pseudonym": delivery.subject_pseudonym,
                    },
                    idempotency_key=delivery.correlation_id,
                )

                delivery.status = "SUCCESS"
                delivery.etmf_document_id = doc_id
                delivery.completed_at = datetime.now(UTC).replace(tzinfo=None)
                delivery.last_error = None

                await write_audit_log(
                    session=session,
                    actor_id="system",
                    actor_role="system",
                    action="ARCHIVAL_ACCEPTED",
                    document_id=delivery.id,
                    details=f"ICF archival delivery accepted by eTMF. eTMF Document ID: {doc_id}. Correlation ID: {delivery.correlation_id}",
                    reason_for_change="eConsent ICF Archival Dispatch success",
                )

            except Exception as e:
                delivery.status = "FAILED"
                delivery.attempts += 1
                delivery.last_error = str(e)

                attempt_cap = int(os.getenv("ECONSENT_ARCHIVAL_ATTEMPT_CAP", "5"))
                if delivery.attempts >= attempt_cap:
                    delivery.retry_eligible = False

                backoff_seconds = min(60, 2**delivery.attempts)
                delivery.next_retry_at = datetime.now(UTC) + timedelta(
                    seconds=backoff_seconds
                )

                await write_audit_log(
                    session=session,
                    actor_id="system",
                    actor_role="system",
                    action="ARCHIVAL_FAILED",
                    document_id=delivery.id,
                    details=f"ICF archival delivery failed (Attempt {delivery.attempts}/{attempt_cap}). Error: {str(e)}. Correlation ID: {delivery.correlation_id}",
                    reason_for_change="eConsent ICF Archival Dispatch failure",
                )

        await session.commit()


async def dispatcher_lifecycle_worker() -> None:
    poll_interval = float(os.getenv("ECONSENT_ARCHIVAL_POLL_INTERVAL_SECONDS", "5.0"))
    while True:
        try:
            await poll_and_dispatch()
        except asyncio.CancelledError:
            break
        except Exception as e:
            logger.error(
                "Error in eConsent archival dispatcher loop: %s", e, exc_info=True
            )
        await asyncio.sleep(poll_interval)


def start_dispatcher():
    global dispatcher_task
    if "pytest" in sys.modules or os.getenv("TESTING") == "true":
        return
    dispatcher_task = asyncio.create_task(dispatcher_lifecycle_worker())


def stop_dispatcher():
    global dispatcher_task
    if dispatcher_task:
        dispatcher_task.cancel()


async def econsent_startup() -> None:
    start_dispatcher()


async def econsent_shutdown() -> None:
    stop_dispatcher()
