"""Asynchronous cross-domain eCRF anomaly detection worker.

Requirements: PRD-QRY-008, PRD-SYS-102
"""

import asyncio
import logging
import os
import sys
from typing import Any

from sqlalchemy import select, text
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker

from apps.execution.database.context import audit_context
from apps.execution.database.core import db_manager
from apps.execution.database.models import ClinicalSubject
from apps.execution.services.cross_domain_anomaly_service import (
    CrossDomainAnomalyService,
)

logger = logging.getLogger("execution-anomaly-worker")
anomaly_task: asyncio.Task | None = None
_session_maker: Any = None
_stop_requested = False


async def poll_and_evaluate_anomalies(session_maker: Any = None) -> None:
    """Executes a periodic sweep across clinical subjects to detect cross-domain inconsistencies.

    Uses PostgreSQL advisory lock 42004 to coordinate worker instances across replicas.
    """
    maker = session_maker or _session_maker or db_manager.get_session_maker()
    service = CrossDomainAnomalyService()

    async with maker() as session:
        # Advisory lock on PostgreSQL to ensure single-worker dispatch per cycle
        if session.bind and session.bind.dialect.name == "postgresql":
            lock_res = await session.execute(
                text("SELECT pg_try_advisory_xact_lock(42004);")
            )
            acquired = lock_res.scalar()
            if not acquired:
                logger.info(
                    "Advisory lock 42004 already held by another anomaly worker. Skipping cycle."
                )
                return

        # Fetch active subjects to evaluate
        stmt = (
            select(ClinicalSubject.subject_id, ClinicalSubject.study_id)
            .where(
                ClinicalSubject.status.in_(
                    ["SCREENING", "ENROLLED", "RANDOMIZED", "ACTIVE", "COMPLETED"]
                ),
                ClinicalSubject.is_deleted.is_(False),
            )
            .limit(50)
        )
        res = await session.execute(stmt)
        subjects = res.all()

        if not subjects:
            return

        with audit_context(
            user_id="ANOMALY_DETECTOR_WORKER",
            change_reason="Asynchronous background cross-domain anomaly evaluation cycle",
        ):
            for subj_id, study_id in subjects:
                try:
                    await service.evaluate_subject_cross_domain_anomalies(
                        session=session,
                        subject_id=subj_id,
                        study_id=study_id,
                        enable_ai=False,  # Use deterministic rules for background sweep
                        auto_stage_queries=True,
                    )
                except Exception as err:
                    logger.warning(
                        f"Error evaluating anomalies for subject {subj_id}: {err}",
                        exc_info=True,
                    )

            await session.commit()


async def run_asynchronous_subject_anomaly_checks(
    session_factory: async_sessionmaker[AsyncSession] | Any,
    subject_id: str,
    study_id: str,
    user_id: str | None = None,
    change_reason: str | None = None,
) -> None:
    """Immediate post-submission background task runner for subject-scoped cross-domain anomaly checks.

    Args:
        session_factory: SQLAlchemy async sessionmaker.
        subject_id: The target clinical subject ID.
        study_id: The target clinical study ID.
        user_id: Optional triggering user ID.
        change_reason: Optional triggering change reason.
    """
    logger.info(
        f"Starting immediate cross-domain anomaly evaluation for subject {subject_id} in study {study_id}"
    )
    service = CrossDomainAnomalyService()

    with audit_context(
        user_id or "ANOMALY_DETECTOR_WORKER",
        change_reason or "Post-submission cross-domain anomaly evaluation trigger",
    ):
        async with session_factory() as session:
            async with session.begin():
                await service.evaluate_subject_cross_domain_anomalies(
                    session=session,
                    subject_id=subject_id,
                    study_id=study_id,
                    enable_ai=True,
                    auto_stage_queries=True,
                )


async def anomaly_lifecycle_worker() -> None:
    """Main background loop polling for cross-domain clinical anomalies."""
    global _stop_requested
    poll_interval = float(os.getenv("ANOMALY_WORKER_POLL_INTERVAL_SECONDS", "30.0"))
    logger.info(
        f"Cross-domain anomaly detection worker loop started (interval: {poll_interval}s)"
    )

    while not _stop_requested:
        try:
            await poll_and_evaluate_anomalies()
        except asyncio.CancelledError:
            break
        except Exception as e:
            logger.error(
                "Error in cross-domain anomaly worker loop: %s", e, exc_info=True
            )
        try:
            await asyncio.sleep(poll_interval)
        except asyncio.CancelledError:
            break


def start_anomaly_worker(session_maker: Any = None) -> None:
    """Starts the cross-domain anomaly detection background worker if not in testing mode."""
    global anomaly_task, _session_maker, _stop_requested
    if "pytest" in sys.modules or os.getenv("TESTING") == "true":
        return
    _stop_requested = False
    _session_maker = session_maker
    anomaly_task = asyncio.create_task(anomaly_lifecycle_worker())
    logger.info("Asynchronous cross-domain anomaly detection worker initialized.")


def stop_anomaly_worker() -> None:
    """Stops the cross-domain anomaly detection background worker."""
    global anomaly_task, _stop_requested
    _stop_requested = True
    if anomaly_task:
        anomaly_task.cancel()
        anomaly_task = None
    logger.info("Asynchronous cross-domain anomaly detection worker stopped.")
