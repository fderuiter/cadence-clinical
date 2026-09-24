"""Consent completion background subscriber worker.

Subscribes to eConsent completion events via Redis Pub/Sub to auto-create and
initialize clinical subjects in the execution portal.
"""

import asyncio
import json
import logging
import os
import sys
import threading
from datetime import UTC, datetime
from typing import Any

import redis
from sqlalchemy import func, select

from apps.execution.database.core import db_manager
from apps.execution.database.models import AuditLog, ClinicalSubject, SubjectConsent

logger = logging.getLogger("execution-consent-subscriber")
subscriber_task = None
_session_maker = None
_stop_event = threading.Event()


async def handle_consent_completed_message(
    data: dict[str, Any], session_maker: Any
) -> None:
    """Processes a background consent completed event and initializes a subject.

    Args:
        data: The JSON payload containing consent completion event information.
        session_maker: SQLAlchemy session maker for accessing the database.

    Returns:
        None
    """
    subject_id = data.get("subject_id")
    study_id = data.get("study_id")
    site_id = data.get("site_id")
    version_tag = data.get("version_tag", "1.0")
    version_index = data.get("version_index", 1)

    if not subject_id or not study_id:
        logger.warning(f"Invalid consent completed event payload: {data}")
        return

    async with session_maker() as session:
        async with session.begin():
            # Check if subject already exists
            stmt = select(ClinicalSubject).where(
                (ClinicalSubject.subject_id == subject_id)
                | (ClinicalSubject.id == subject_id)
            )
            existing_subj = (await session.execute(stmt)).scalars().first()

            if not existing_subj:
                # Query max enrollment_index for the study inside the active transaction
                stmt_max = select(func.max(ClinicalSubject.enrollment_index)).where(
                    ClinicalSubject.study_id == study_id
                )
                res_max = await session.execute(stmt_max)
                max_idx = res_max.scalar()
                new_idx = 0 if max_idx is None else max_idx + 1

                subj = ClinicalSubject(
                    subject_id=subject_id,
                    study_id=study_id,
                    site_id=site_id,
                    enrollment_index=new_idx,
                    status="SCREENING",
                )
                session.add(subj)
                await session.flush()
                logger.info(
                    f"Auto-created subject {subject_id} in SCREENING status with index {new_idx}"
                )

                # Create AuditLog for subject creation
                audit_log = AuditLog(
                    table_name="clinical_subjects",
                    record_id=subj.id,
                    action="INSERT",
                    user_id="system-pubsub",
                    change_reason="Automatically created via background eConsent completion trigger",
                )
                session.add(audit_log)
            else:
                logger.info(
                    f"Subject {subject_id} already exists, skipping auto-creation."
                )

            # Also ensure local SubjectConsent cache is populated so that writes are unblocked
            stmt_consent = select(SubjectConsent).where(
                SubjectConsent.subject_id == subject_id,
                SubjectConsent.study_id == study_id,
                SubjectConsent.version_index == version_index,
            )
            existing_consent = (await session.execute(stmt_consent)).scalars().first()
            if not existing_consent:
                consent_db = SubjectConsent(
                    subject_id=subject_id,
                    study_id=study_id,
                    version_tag=version_tag,
                    version_index=version_index,
                    icf_signed=True,
                    icf_signed_date=datetime.now(UTC),
                    requires_reconsent=False,
                )
                session.add(consent_db)
                await session.flush()
                logger.info(
                    f"Auto-created SubjectConsent cache for subject {subject_id}"
                )

                audit_consent = AuditLog(
                    table_name="subject_consents",
                    record_id=consent_db.id,
                    action="INSERT",
                    user_id="system-pubsub",
                    change_reason="Automatically populated via background eConsent completion trigger",
                )
                session.add(audit_consent)


def _run_subscriber_sync(session_maker: Any) -> None:
    """Synchronous worker target to run the Redis listen subscription loop.

    Args:
        session_maker: SQLAlchemy session maker for executing database operations.

    Returns:
        None
    """
    redis_host = os.getenv("REDIS_HOST")
    redis_port = int(os.getenv("REDIS_PORT", "6379"))
    redis_password = os.getenv("REDIS_PASSWORD") or None
    redis_channel = os.getenv("REDIS_CHANNEL_CONSENT", "econsent_consent_completed")

    logger.info(
        f"Starting consent completion Redis subscriber thread on channel: {redis_channel}"
    )

    while not _stop_event.is_set():
        try:
            r = redis.Redis(
                host=redis_host,
                port=redis_port,
                password=redis_password,
                decode_responses=True,
                socket_timeout=5,
                socket_keepalive=True,
            )
            r.ping()

            pubsub = r.pubsub()
            pubsub.subscribe(redis_channel)
            logger.info(
                f"Subscribed to Redis consent completion channel: {redis_channel}"
            )

            for message in pubsub.listen():
                if _stop_event.is_set():
                    break
                if message["type"] == "message":
                    try:
                        data = json.loads(message["data"])
                        action = data.get("action")
                        if action == "consent_completed":
                            loop = asyncio.new_event_loop()
                            try:
                                loop.run_until_complete(
                                    handle_consent_completed_message(
                                        data, session_maker
                                    )
                                )
                            finally:
                                loop.close()
                    except Exception as e:
                        logger.warning(
                            f"Failed to process consent completed message: {e}"
                        )
        except Exception as e:
            logger.warning(
                f"Redis consent subscriber disconnected or failed to connect: {e}. Retrying in 5 seconds..."
            )
            if _stop_event.wait(5):
                break


def start_consent_subscriber(session_maker: Any = None) -> None:
    """Starts the consent completion background subscriber thread if Redis is configured.

    Args:
        session_maker: Optional session maker to use instead of default.

    Returns:
        None
    """
    global subscriber_task, _session_maker
    if "pytest" in sys.modules or os.getenv("TESTING") == "true":
        return
    _session_maker = session_maker or db_manager.get_session_maker()
    _stop_event.clear()

    redis_host = os.getenv("REDIS_HOST")
    if not redis_host:
        logger.info(
            "REDIS_HOST not set. Consent subscriber background worker will not be started."
        )
        return

    subscriber_task = threading.Thread(
        target=_run_subscriber_sync,
        args=(_session_maker,),
        daemon=True,
        name="ConsentCompletionSubscriberThread",
    )
    subscriber_task.start()


def stop_consent_subscriber() -> None:
    """Stops the consent completion background subscriber thread.

    Returns:
        None
    """
    _stop_event.set()
