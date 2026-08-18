"""
SLA escalation background worker and application services for Tickets microservice.
"""

import asyncio
import contextlib
import logging
import os
import sys
from datetime import datetime, timedelta
from typing import Any

from sqlalchemy import and_, or_, select

from apps.tickets.infrastructure.models import TERMINAL_STATES, Ticket

logger = logging.getLogger("tickets_escalation")

_escalation_task: asyncio.Task | None = None
_should_run: bool = False

NEXT_PRIORITY = {
    "LOW": "MEDIUM",
    "MEDIUM": "HIGH",
    "HIGH": "CRITICAL",
}


def is_notification_owed(ticket: Ticket) -> bool:
    """
    Checks the invariant: a notification is owed only when last_escalated_at
    is set and is newer than last_escalation_notified_at.
    """
    if ticket.last_escalated_at is None:
        return False
    if ticket.last_escalation_notified_at is None:
        return True
    return ticket.last_escalated_at > ticket.last_escalation_notified_at


async def dispatch_escalation_notification(
    ticket_id: str,
    reference: str,
    assignee_user: str | None,
    assignee_role: str | None,
    priority: str,
    version_index: int,
) -> bool:
    """
    Thin internal helper that builds the notification payload and calls publish_notification.
    Swallows and logs failures, returning a boolean indicating success.
    """
    try:
        recipient_user_id = assignee_user
        recipient_role = None if assignee_user else assignee_role
        related_entity_id = f"{ticket_id}:escalation:{version_index}"

        payload = {
            "recipient_user_id": recipient_user_id,
            "recipient_role": recipient_role,
            "category": "ACTION_ITEMS",
            "priority": priority,
            "channels": "IN_APP",
            "message_content": f"Automated escalation: Ticket {reference} is overdue and has been escalated to {priority}.",
            "related_entity_id": related_entity_id,
            "related_entity_type": "ticket",
        }

        import apps.tickets.notifications_client as nc

        return await nc.publish_notification(payload)
    except Exception as e:
        logger.error(
            "Exception in dispatch_escalation_notification for ticket %s: %s",
            ticket_id,
            e,
            exc_info=True,
        )
        return False


async def execute_ticket_escalation_cycle(session_maker: Any) -> None:
    """
    Executes a single cycle of finding overdue eligible tickets, locking them, and escalating.
    Also retries any pending notifications where dispatch failed previously.
    """
    logger.info("SLA Escalation worker cycle started.")
    now = datetime.now()
    cooldown_seconds = float(
        os.getenv("TICKETS_ESCALATION_INTERVAL_SECONDS", "86400.0")  # deid-ignore
    )
    cooldown_cutoff = now - timedelta(seconds=cooldown_seconds)

    terminal_state_strs = [
        s.value if hasattr(s, "value") else str(s) for s in TERMINAL_STATES
    ]

    async with session_maker() as db:
        try:
            escalation_eligible_clause = and_(
                or_(
                    and_(Ticket.due_date.is_not(None), Ticket.due_date <= now),
                    and_(
                        Ticket.sla_target_at.is_not(None),
                        Ticket.sla_target_at <= now,
                    ),
                ),
                or_(
                    Ticket.last_escalated_at.is_(None),
                    Ticket.last_escalated_at <= cooldown_cutoff,
                ),
                Ticket.priority.in_(["LOW", "MEDIUM", "HIGH"]),
            )

            notification_owed_clause = and_(
                Ticket.last_escalated_at.is_not(None),
                or_(
                    Ticket.last_escalation_notified_at.is_(None),
                    Ticket.last_escalated_at > Ticket.last_escalation_notified_at,
                ),
            )

            paused_state_strs = [
                s.value if hasattr(s, "value") else str(s)
                for s in [
                    "WAITING_ON_SITE",
                    "WAITING_ON_SPONSOR",
                    "PENDING_REGULATORY_REVIEW",
                ]
            ]

            stmt = select(Ticket).where(
                Ticket.status.not_in(terminal_state_strs),
                Ticket.status.not_in(paused_state_strs),
                Ticket.is_deleted.is_(False),
                or_(escalation_eligible_clause, notification_owed_clause),
            )
            res = await db.execute(stmt)
            candidates = res.scalars().all()
        except Exception as e:
            logger.error(
                "Error querying candidate tickets for escalation: %s",
                e,
                exc_info=True,
            )
            return

        for cand in candidates:
            try:
                lock_stmt = select(Ticket).where(Ticket.id == cand.id).with_for_update()
                lock_res = await db.execute(lock_stmt)
                ticket = lock_res.scalars().first()

                if not ticket:
                    continue

                if (
                    ticket.status in TERMINAL_STATES
                    or str(ticket.status) in terminal_state_strs
                ):
                    continue
                if str(ticket.status) in paused_state_strs:
                    continue
                if ticket.is_deleted:
                    continue

                effective_due = ticket.due_date
                if effective_due is None and ticket.sla_target_at is not None:
                    effective_due = ticket.sla_target_at + timedelta(
                        seconds=max(0, ticket.sla_total_paused_seconds)
                    )

                escalation_eligible = (
                    effective_due is not None
                    and effective_due <= now
                    and (
                        ticket.last_escalated_at is None
                        or (now - ticket.last_escalated_at).total_seconds()
                        >= cooldown_seconds
                    )
                    and ticket.priority in ["LOW", "MEDIUM", "HIGH"]
                )

                if escalation_eligible:
                    ticket_id = ticket.id
                    reference = ticket.reference
                    old_priority = ticket.priority
                    new_priority = NEXT_PRIORITY[old_priority]

                    ticket.priority = new_priority
                    ticket.last_escalated_at = now
                    ticket.sla_breached = True
                    ticket.escalation_count += 1
                    ticket.version_index += 1
                    ticket.reason_for_change = (
                        "Automated escalation: overdue past due_date"
                    )
                    ticket.created_by = "system"

                    await db.flush()

                    from apps.tickets.infrastructure.repositories import (
                        TICKET_ESCALATE,
                        write_ticket_audit_log,
                    )

                    await write_ticket_audit_log(
                        session=db,
                        user_id="system",
                        action=TICKET_ESCALATE,
                        details=f"Automated escalation: overdue past due_date. Priority increased from {old_priority} to {new_priority}.",
                        record_id=ticket_id,
                        ticket_id=ticket_id,
                        change_reason=ticket.reason_for_change,
                        version_index=ticket.version_index,
                    )

                    await db.commit()
                    logger.info(
                        "Escalated ticket %s (ref: %s) priority from %s to %s.",
                        ticket_id,
                        reference,
                        old_priority,
                        new_priority,
                    )

                if is_notification_owed(ticket):
                    success = await dispatch_escalation_notification(
                        ticket_id=ticket.id,
                        reference=ticket.reference,
                        assignee_user=ticket.assignee_user,
                        assignee_role=ticket.assignee_role,
                        priority=ticket.priority,
                        version_index=ticket.version_index,
                    )
                    if success:
                        ticket.last_escalation_notified_at = now
                        await db.flush()
                        await db.commit()
                        logger.info(
                            "Persisted last_escalation_notified_at for ticket %s.",
                            ticket.id,
                        )
                    else:
                        logger.warning(
                            "Failed to dispatch notification for ticket %s. Will retry next cycle.",
                            ticket.id,
                        )
            except Exception as e:
                await db.rollback()
                logger.error(
                    "Error during escalation processing for ticket %s: %s",
                    cand.id,
                    e,
                    exc_info=True,
                )


async def start_background_ticket_escalation() -> None:
    """
    Startup hook to start the support ticket escalation background worker.
    """
    global _escalation_task, _should_run
    if "pytest" in sys.modules or os.getenv("PYTEST_CURRENT_TEST"):
        logger.info(
            "Pytest environment detected. Skipping background ticket escalation worker auto-spin."
        )
        return

    from apps.tickets.adapters.database import db_manager

    session_maker = db_manager.get_session_maker()

    poll_interval = float(os.getenv("TICKETS_ESCALATION_POLL_INTERVAL_SECONDS", "60.0"))
    _should_run = True

    async def escalation_loop():
        logger.info(
            "Background ticket escalation worker loop started with poll interval of %s seconds.",
            poll_interval,
        )
        while _should_run:
            try:
                await execute_ticket_escalation_cycle(session_maker)
            except Exception as e:
                logger.error(
                    "Exception in background ticket escalation loop: %s",
                    e,
                    exc_info=True,
                )

            total_sleep = poll_interval
            while total_sleep > 0 and _should_run:
                sleep_chunk = min(0.1, total_sleep)
                await asyncio.sleep(sleep_chunk)
                total_sleep -= sleep_chunk

    _escalation_task = asyncio.create_task(escalation_loop())


async def stop_background_ticket_escalation() -> None:
    """
    Shutdown hook to stop the support ticket escalation background worker cleanly.
    """
    global _escalation_task, _should_run
    _should_run = False
    if _escalation_task:
        with contextlib.suppress(asyncio.CancelledError):
            await _escalation_task
        _escalation_task = None
    logger.info("Background ticket escalation worker stopped.")
