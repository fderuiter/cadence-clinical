import asyncio
import logging
import os
from datetime import datetime, timedelta, timezone
from typing import Any, Optional

from sqlalchemy import select

from apps.execution.database.models import ClinicalQuery
from apps.execution.trial_lock import NotificationRouter

logger = logging.getLogger("queries_escalation")

_escalation_task: Optional[asyncio.Task] = None
_should_run: bool = False
_last_digest_sent_at: Optional[datetime] = None


async def execute_query_escalation_cycle(session_maker: Any) -> None:
    """Find and escalate queries that are Open/Reopened and unresolved for >14 consecutive days.

    Sends a daily aging digest to the Site PI and Sponsor Lead CRA.
    """
    global _last_digest_sent_at

    now = datetime.now()
    cutoff_date = now - timedelta(days=14)

    async with session_maker() as db:
        # Find all open or reopened clinical queries that are not deleted and are older than 14 days
        stmt = select(ClinicalQuery).where(
            ClinicalQuery.status.in_(["OPEN", "REOPENED"]),
            ClinicalQuery.is_deleted.is_(False),
            ClinicalQuery.created_at <= cutoff_date,
        )
        res = await db.execute(stmt)
        aging_queries = list(res.scalars().all())

        if not aging_queries:
            logger.info("No aging queries found in this cycle.")
            return

        # Escalate any queries that haven't been escalated yet
        escalated_any = False
        for q in aging_queries:
            if q.escalated_at is None:
                q.priority = "HIGH"
                q.escalated_at = now
                escalated_any = True
                logger.info(
                    "Escalating query ID %s: priority=HIGH, escalated_at=%s", q.id, now
                )

        if escalated_any:
            await db.commit()

        # Check if we should send a daily digest
        digest_interval_seconds = float(
            os.getenv("QUERY_ESCALATION_DIGEST_SECONDS", "86400.0")
        )
        should_send_digest = False
        if _last_digest_sent_at is None:
            should_send_digest = True
        else:
            elapsed = (now - _last_digest_sent_at).total_seconds()
            if elapsed >= digest_interval_seconds:
                should_send_digest = True

        if should_send_digest:
            # Group aging queries by (study_id, site_id)
            groups = {}
            for q in aging_queries:
                key = (q.study_id, q.site_id)
                if key not in groups:
                    groups[key] = []
                groups[key].append(q)

            router = NotificationRouter()

            for (study_id, site_id), queries in groups.items():
                pi_email = (
                    f"pi_{site_id}@cadence.clinical"
                    if site_id
                    else "pi@cadence.clinical"
                )
                cra_email = (
                    f"cra_{study_id}@cadence.clinical"
                    if study_id
                    else "cra@cadence.clinical"
                )

                recipients = [pi_email, cra_email]

                message_lines = [
                    "Daily Clinical Query Aging Digest",
                    f"Study: {study_id or 'Unknown'}",
                    f"Site: {site_id or 'Unknown'}",
                    "The following queries have been unresolved for more than 14 consecutive calendar days and have been escalated:",
                    "",
                ]

                for q in queries:
                    message_lines.append(f"- Query ID: {q.id}")
                    message_lines.append(f"  Subject ID: {q.subject_id}")
                    message_lines.append(f"  Visit ID: {q.visit_id or 'N/A'}")
                    message_lines.append(f"  Test Code: {q.test_code}")
                    message_lines.append(f"  Explanation: {q.explanation or 'N/A'}")
                    message_lines.append(f"  Priority: {q.priority}")
                    message_lines.append(
                        f"  Created At: {q.created_at.isoformat() if q.created_at else 'N/A'}"
                    )
                    message_lines.append(
                        f"  Escalated At: {q.escalated_at.isoformat() if q.escalated_at else 'N/A'}"
                    )
                    message_lines.append("")

                message_text = "\n".join(message_lines)
                logger.info(
                    "Sending aging digest email to %s for Study %s, Site %s.",
                    recipients,
                    study_id,
                    site_id,
                )
                router.send_email(recipients, message_text)

            _last_digest_sent_at = now


async def start_background_query_escalation(
    session_maker: Any, interval: Optional[float] = None
) -> None:
    """Start the asynchronous background query escalation polling loop."""
    global _escalation_task, _should_run
    if interval is None:
        interval = float(os.getenv("QUERY_ESCALATION_INTERVAL_SECONDS", "86400.0"))
    _should_run = True

    async def escalation_loop():
        logger.info(
            "Background clinical query escalation started with interval %s seconds.",
            interval,
        )
        while _should_run:
            try:
                await execute_query_escalation_cycle(session_maker)
            except Exception as e:
                logger.error(
                    "Error in clinical query escalation cycle: %s", e, exc_info=True
                )

            # Sleep in small increments to allow responsive shutdown
            for _ in range(int(interval * 10)):
                if not _should_run:
                    break
                await asyncio.sleep(0.1)

    _escalation_task = asyncio.create_task(escalation_loop())


async def stop_background_query_escalation() -> None:
    """Stop the asynchronous background query escalation polling loop."""
    global _escalation_task, _should_run
    _should_run = False
    if _escalation_task:
        try:
            await _escalation_task
        except asyncio.CancelledError:
            pass
        _escalation_task = None
    logger.info("Background clinical query escalation stopped.")
