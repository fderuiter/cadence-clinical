import logging
import os
import time
from typing import Any, Optional

import httpx
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.safety.database import db_manager
from apps.safety.models import SAEReconciliationJob, write_audit_log
from apps.safety.reconciliation import run_reconciliation
from packages.security.context import audit_context
from packages.security.signing import generate_gateway_signature

logger = logging.getLogger("safety-processor")


async def write_safety_audit_log(
    session: AsyncSession,
    user_id: str,
    action: str,
    details: str,
    record_id: Optional[str] = None,
    change_reason: Optional[str] = None,
    version_index: int = 1,
) -> None:
    """
    Utility function to write to the immutable Safety audit ledger.
    """
    await write_audit_log(
        session=session,
        created_by=user_id,
        action=action,
        details=details,
        reason_for_change=change_reason,
        version_index=version_index,
        record_id=record_id,
    )


async def send_medical_monitor_alert(
    job_id: str,
    run_id: str,
    study_id: str,
    discrepancy_count: int,
    test_client: Optional[Any],
    session: AsyncSession,
    user_id: str,
    change_reason: str,
) -> None:
    gateway_secret_env = os.getenv("GATEWAY_SECRET")
    if not gateway_secret_env:
        raise RuntimeError(
            "GATEWAY_SECRET environment variable is not set. "
            "Refusing to sign internal requests with a default/empty secret."
        )
    gateway_secret = gateway_secret_env.encode("utf-8")

    caller_user_id = "safety-service"
    roles = "sponsor_statistician"
    timestamp = str(time.time())

    signature = generate_gateway_signature(
        user_id=caller_user_id,
        roles=roles,
        timestamp=timestamp,
        secret=gateway_secret,
        change_reason=change_reason,
    )

    headers = {
        "X-User-Id": caller_user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }

    payload = {
        "recipient_role": "sponsor_mm",
        "category": "ALERTS",
        "priority": "HIGH",
        "channels": "IN_APP",
        "message_content": f"SAE reconciliation run {run_id} identified {discrepancy_count} discrepancies for study {study_id}.",
        "related_entity_id": run_id,
        "related_entity_type": "SAEReconciliationRun",
    }

    notifications_url = os.getenv("NOTIFICATIONS_URL") or "http://localhost:8006"
    url = f"{notifications_url.rstrip('/')}/api/v1/notifications"

    try:
        if test_client is not None:
            response = await test_client.post(
                url, json=payload, headers=headers, timeout=10.0
            )
        else:
            async with httpx.AsyncClient(timeout=10.0) as cli:
                response = await cli.post(url, json=payload, headers=headers)

        if response.status_code == 201:
            logger.info("Successfully dispatched alert to Sponsor Medical Monitor.")
            await write_safety_audit_log(
                session=session,
                user_id=user_id,
                action="RECONCILIATION_ALERT_SENT",
                details=f"Sponsor Medical Monitor alert successfully dispatched for run {run_id}. Identified {discrepancy_count} discrepancies.",
                record_id=job_id,
                change_reason=change_reason,
            )
        else:
            logger.error(
                f"Notifications service returned error {response.status_code}: {response.text}"
            )
            await write_safety_audit_log(
                session=session,
                user_id=user_id,
                action="RECONCILIATION_ALERT_FAILED",
                details=f"Sponsor Medical Monitor alert dispatch failed with status {response.status_code}.",
                record_id=job_id,
                change_reason=change_reason,
            )
    except Exception as e:
        logger.exception("Failed to dispatch Sponsor Medical Monitor alert")
        await write_safety_audit_log(
            session=session,
            user_id=user_id,
            action="RECONCILIATION_ALERT_FAILED",
            details=f"Sponsor Medical Monitor alert dispatch exception: {str(e)[:200]}.",
            record_id=job_id,
            change_reason=change_reason,
        )


async def process_sae_reconciliation(
    job_id: str,
    study_id: str,
    user_id: str,
    change_reason: str,
    test_client: Optional[Any] = None,
) -> None:
    session_maker = db_manager.get_session_maker()
    with audit_context(user_id, change_reason):
        async with session_maker() as session:
            try:
                # 1. Update status to PROCESSING
                stmt = select(SAEReconciliationJob).where(
                    SAEReconciliationJob.id == job_id
                )
                result = await session.execute(stmt)
                job = result.scalars().first()
                if not job:
                    logger.error(f"Reconciliation job {job_id} not found in database.")
                    return

                job.status = "PROCESSING"
                await session.flush()

                await write_safety_audit_log(
                    session=session,
                    user_id=user_id,
                    action="RECONCILIATION_JOB_PROCESSING",
                    details=f"SAE reconciliation job {job_id} status changed to PROCESSING.",
                    record_id=job_id,
                    change_reason=change_reason,
                )
                await session.commit()

                # 2. Run reconciliation logic
                results = await run_reconciliation(
                    study_id=study_id,
                    session=session,
                    created_by=user_id,
                    reason_for_change=change_reason,
                    client=test_client,
                )

                run = results["run"]
                discrepancies = results["discrepancies"]

                # 3. Update status to COMPLETED
                stmt = select(SAEReconciliationJob).where(
                    SAEReconciliationJob.id == job_id
                )
                result = await session.execute(stmt)
                job = result.scalars().first()
                if job:
                    job.status = "COMPLETED"
                    job.run_id = run.id
                    await session.flush()

                    await write_safety_audit_log(
                        session=session,
                        user_id=user_id,
                        action="RECONCILIATION_JOB_COMPLETED",
                        details=f"SAE reconciliation job {job_id} status changed to COMPLETED. Created run {run.id}.",
                        record_id=job_id,
                        change_reason=change_reason,
                    )
                    await session.commit()

                    # 4. Handle alerts for material discrepancies
                    if len(discrepancies) > 0:
                        await send_medical_monitor_alert(
                            job_id=job_id,
                            run_id=run.id,
                            study_id=study_id,
                            discrepancy_count=len(discrepancies),
                            test_client=test_client,
                            session=session,
                            user_id=user_id,
                            change_reason=change_reason,
                        )
                        await session.commit()

            except Exception as e:
                logger.exception(f"Error processing reconciliation job {job_id}")
                # Ensure safe transition to FAILED with a sanitized, non-PII error message.
                # Roll back any failed/dirty session state before issuing new DML.
                try:
                    await session.rollback()
                    stmt = select(SAEReconciliationJob).where(
                        SAEReconciliationJob.id == job_id
                    )
                    result = await session.execute(stmt)
                    job = result.scalars().first()
                    if job:
                        job.status = "FAILED"
                        # Truncate to first line and 200 chars to prevent PII leakage
                        # via exception messages that may contain subject IDs or URLs.
                        err_msg = str(type(e).__name__)
                        await session.flush()

                        await write_safety_audit_log(
                            session=session,
                            user_id=user_id,
                            action="RECONCILIATION_JOB_FAILED",
                            details=f"SAE reconciliation job {job_id} status changed to FAILED. Error type: {err_msg}",
                            record_id=job_id,
                            change_reason=change_reason,
                        )
                        await session.commit()
                except Exception as inner_e:
                    logger.error(
                        f"Failed to record FAILED status for job {job_id}: {inner_e}"
                    )
