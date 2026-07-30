import asyncio
import os
from datetime import datetime, timedelta
from unittest.mock import AsyncMock, MagicMock, patch

import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.tickets.database import db_manager
from apps.tickets.escalation import (
    execute_ticket_escalation_cycle,
    start_background_ticket_escalation,
    stop_background_ticket_escalation,
)
from apps.tickets.models import (
    Base,
    Ticket,
    TicketAuditLog,
    TicketPriority,
    TicketStatus,
)


@pytest_asyncio.fixture(autouse=True)
async def setup_tickets_db():
    """
    Setup in-memory Tickets database for unit and integration testing.
    """
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    if db_manager.engine is not None:
        async with db_manager.engine.begin() as conn:
            await conn.run_sync(Base.metadata.drop_all)
        await db_manager.close()


@pytest.mark.asyncio
@patch("apps.tickets.notifications_client.publish_notification", new_callable=AsyncMock)
async def test_escalation_eligibility_rules(mock_publish):
    """
    Verify escalation eligibility rules:
    - Only non-terminal, due_date-past tickets escalate.
    - No due_date tickets are untouched.
    - Terminal tickets (CLOSED/CANCELLED) are skipped.
    """
    mock_publish.return_value = True
    session_maker = db_manager.get_session_maker()
    now = datetime.now()

    # 1. Ticket overdue (should escalate)
    t_overdue = Ticket(
        reference="TKT-00001",
        title="Overdue Ticket",
        description="This ticket is past its due date.",
        priority=TicketPriority.LOW,
        status=TicketStatus.OPEN,
        reporter="user_1",
        due_date=now - timedelta(days=2),
        created_by="user_1",
        reason_for_change="Initial",
    )

    # 2. Ticket not overdue (should NOT escalate)
    t_future = Ticket(
        reference="TKT-00002",
        title="Future Ticket",
        description="This ticket is due in the future.",
        priority=TicketPriority.LOW,
        status=TicketStatus.OPEN,
        reporter="user_1",
        due_date=now + timedelta(days=2),
        created_by="user_1",
        reason_for_change="Initial",
    )

    # 3. Ticket with no due date (should NOT escalate)
    t_no_due = Ticket(
        reference="TKT-00003",
        title="No Due Date Ticket",
        description="This ticket has no due date.",
        priority=TicketPriority.LOW,
        status=TicketStatus.OPEN,
        reporter="user_1",
        due_date=None,
        created_by="user_1",
        reason_for_change="Initial",
    )

    # 4. Terminal ticket overdue (should NOT escalate)
    t_closed = Ticket(
        reference="TKT-00004",
        title="Closed Overdue Ticket",
        description="Overdue but closed.",
        priority=TicketPriority.LOW,
        status=TicketStatus.CLOSED,
        reporter="user_1",
        due_date=now - timedelta(days=5),
        created_by="user_1",
        reason_for_change="Initial",
    )

    # 5. Cancelled ticket overdue (should NOT escalate)
    t_cancelled = Ticket(
        reference="TKT-00005",
        title="Cancelled Overdue Ticket",
        description="Overdue but cancelled.",
        priority=TicketPriority.LOW,
        status=TicketStatus.CANCELLED,
        reporter="user_1",
        due_date=now - timedelta(days=5),
        created_by="user_1",
        reason_for_change="Initial",
    )

    async with session_maker() as db:
        db.add_all([t_overdue, t_future, t_no_due, t_closed, t_cancelled])
        await db.commit()

    # Run escalation cycle
    await execute_ticket_escalation_cycle(session_maker)

    # Re-verify results from DB
    async with session_maker() as db:
        # t_overdue must be escalated to MEDIUM
        r1 = await db.execute(select(Ticket).where(Ticket.reference == "TKT-00001"))
        t1 = r1.scalar_one()
        assert t1.priority == TicketPriority.MEDIUM
        assert t1.last_escalated_at is not None
        assert t1.last_escalation_notified_at is not None
        assert t1.escalation_count == 1

        # t_future must remain LOW
        r2 = await db.execute(select(Ticket).where(Ticket.reference == "TKT-00002"))
        t2 = r2.scalar_one()
        assert t2.priority == TicketPriority.LOW
        assert t2.last_escalated_at is None

        # t_no_due must remain LOW
        r3 = await db.execute(select(Ticket).where(Ticket.reference == "TKT-00003"))
        t3 = r3.scalar_one()
        assert t3.priority == TicketPriority.LOW
        assert t3.last_escalated_at is None

        # t_closed must remain LOW
        r4 = await db.execute(select(Ticket).where(Ticket.reference == "TKT-00004"))
        t4 = r4.scalar_one()
        assert t4.priority == TicketPriority.LOW
        assert t4.last_escalated_at is None

        # t_cancelled must remain LOW
        r5 = await db.execute(select(Ticket).where(Ticket.reference == "TKT-00005"))
        t5 = r5.scalar_one()
        assert t5.priority == TicketPriority.LOW
        assert t5.last_escalated_at is None


@pytest.mark.asyncio
@patch("apps.tickets.notifications_client.publish_notification", new_callable=AsyncMock)
async def test_bounded_priority_advancement(mock_publish):
    """
    Verify stepwise bounded priority progression capped strictly at CRITICAL.
    LOW -> MEDIUM -> HIGH -> CRITICAL -> CRITICAL
    """
    mock_publish.return_value = True
    session_maker = db_manager.get_session_maker()
    now = datetime.now()

    # Create tickets at different priority levels
    t_low = Ticket(
        reference="TKT-LOW",
        title="Low Ticket",
        description="D",
        priority=TicketPriority.LOW,
        reporter="user_1",
        due_date=now - timedelta(days=2),
        created_by="user_1",
        reason_for_change="I",
    )
    t_med = Ticket(
        reference="TKT-MED",
        title="Med Ticket",
        description="D",
        priority=TicketPriority.MEDIUM,
        reporter="user_1",
        due_date=now - timedelta(days=2),
        created_by="user_1",
        reason_for_change="I",
    )
    t_high = Ticket(
        reference="TKT-HIGH",
        title="High Ticket",
        description="D",
        priority=TicketPriority.HIGH,
        reporter="user_1",
        due_date=now - timedelta(days=2),
        created_by="user_1",
        reason_for_change="I",
    )
    t_crit = Ticket(
        reference="TKT-CRIT",
        title="Crit Ticket",
        description="D",
        priority=TicketPriority.CRITICAL,
        reporter="user_1",
        due_date=now - timedelta(days=2),
        created_by="user_1",
        reason_for_change="I",
    )

    async with session_maker() as db:
        db.add_all([t_low, t_med, t_high, t_crit])
        await db.commit()

    await execute_ticket_escalation_cycle(session_maker)

    async with session_maker() as db:
        # LOW -> MEDIUM
        r_low = await db.execute(select(Ticket).where(Ticket.reference == "TKT-LOW"))
        assert r_low.scalar_one().priority == TicketPriority.MEDIUM

        # MEDIUM -> HIGH
        r_med = await db.execute(select(Ticket).where(Ticket.reference == "TKT-MED"))
        assert r_med.scalar_one().priority == TicketPriority.HIGH

        # HIGH -> CRITICAL
        r_high = await db.execute(select(Ticket).where(Ticket.reference == "TKT-HIGH"))
        assert r_high.scalar_one().priority == TicketPriority.CRITICAL

        # CRITICAL -> CRITICAL (Unchanged)
        r_crit = await db.execute(select(Ticket).where(Ticket.reference == "TKT-CRIT"))
        assert r_crit.scalar_one().priority == TicketPriority.CRITICAL


@pytest.mark.asyncio
@patch("apps.tickets.notifications_client.publish_notification", new_callable=AsyncMock)
async def test_cooldown_gating_and_idempotency(mock_publish):
    """
    Verify that cooldown gating prevents re-escalation within the cooldown window,
    and allows it once the window has elapsed.
    """
    mock_publish.return_value = True
    session_maker = db_manager.get_session_maker()
    now = datetime.now()

    os.environ["TICKETS_ESCALATION_INTERVAL_SECONDS"] = "3600.0"  # 1 hour cooldown

    t = Ticket(
        reference="TKT-COOLDOWN",
        title="Cooldown Ticket",
        description="D",
        priority=TicketPriority.LOW,
        reporter="user_1",
        due_date=now - timedelta(days=2),
        last_escalated_at=now - timedelta(minutes=30),  # inside cooldown
        escalation_count=1,
        created_by="user_1",
        reason_for_change="I",
    )

    async with session_maker() as db:
        db.add(t)
        await db.commit()

    # 1. Run cycle within cooldown -> Should not escalate
    await execute_ticket_escalation_cycle(session_maker)

    async with session_maker() as db:
        r = await db.execute(select(Ticket).where(Ticket.reference == "TKT-COOLDOWN"))
        ticket_db = r.scalar_one()
        assert ticket_db.priority == TicketPriority.LOW  # unchanged
        assert ticket_db.escalation_count == 1

    # 2. Update last_escalated_at to be outside cooldown -> Should escalate
    async with session_maker() as db:
        r = await db.execute(select(Ticket).where(Ticket.reference == "TKT-COOLDOWN"))
        tdb = r.scalar_one()
        tdb.last_escalated_at = now - timedelta(hours=2)
        await db.commit()

    await execute_ticket_escalation_cycle(session_maker)

    async with session_maker() as db:
        r = await db.execute(select(Ticket).where(Ticket.reference == "TKT-COOLDOWN"))
        ticket_db = r.scalar_one()
        assert ticket_db.priority == TicketPriority.MEDIUM  # escalated
        assert ticket_db.escalation_count == 2


@pytest.mark.asyncio
@patch("apps.tickets.notifications_client.publish_notification", new_callable=AsyncMock)
async def test_audit_log_creation_on_escalate(mock_publish):
    """
    Verify that an audit log with TICKET_ESCALATE action is created for effective escalations.
    """
    mock_publish.return_value = True
    session_maker = db_manager.get_session_maker()
    now = datetime.now()

    t = Ticket(
        reference="TKT-AUDIT-E",
        title="Audit Test",
        description="D",
        priority=TicketPriority.LOW,
        reporter="user_1",
        due_date=now - timedelta(days=2),
        created_by="user_1",
        reason_for_change="I",
    )

    async with session_maker() as db:
        db.add(t)
        await db.commit()

    await execute_ticket_escalation_cycle(session_maker)

    async with session_maker() as db:
        r = await db.execute(
            select(TicketAuditLog)
            .where(TicketAuditLog.action == "TICKET_ESCALATE")
            .order_by(TicketAuditLog.created_at.desc())
        )
        logs = r.scalars().all()
        assert len(logs) == 1
        log = logs[0]
        assert log.created_by == "system"
        assert "Priority increased from LOW to MEDIUM" in log.details
        assert log.version_index == 2


@pytest.mark.asyncio
@patch("apps.tickets.notifications_client.publish_notification", new_callable=AsyncMock)
async def test_notification_deduplication_and_partial_failures(mock_publish):
    """
    Simulate a 'commit-but-no-notify' gap:
    If ticket last_escalated_at is set but last_escalation_notified_at is missing,
    running the cycle must NOT re-escalate (blocked by cooldown) but MUST successfully
    retry and dispatch exactly one notification, updating last_escalation_notified_at upon success.
    """
    # 1. First run, notify fails
    mock_publish.return_value = False

    session_maker = db_manager.get_session_maker()
    now = datetime.now()

    t = Ticket(
        reference="TKT-GAP",
        title="Gap Ticket",
        description="D",
        priority=TicketPriority.LOW,
        reporter="user_1",
        due_date=now - timedelta(days=2),
        created_by="user_1",
        reason_for_change="I",
    )

    async with session_maker() as db:
        db.add(t)
        await db.commit()

    # Executing cycle -> will escalate but notification dispatch returns False (fails)
    await execute_ticket_escalation_cycle(session_maker)

    async with session_maker() as db:
        r = await db.execute(select(Ticket).where(Ticket.reference == "TKT-GAP"))
        t_db = r.scalar_one()
        assert t_db.priority == TicketPriority.MEDIUM
        assert t_db.last_escalated_at is not None
        assert (
            t_db.last_escalation_notified_at is None
        )  # remains None because notify failed

    # 2. Second run, notify succeeds.
    # Cooldown is 86400s, so the ticket won't escalate again. But the notification is still owed!
    mock_publish.reset_mock()
    mock_publish.return_value = True

    await execute_ticket_escalation_cycle(session_maker)

    # Verify that mock_publish was called exactly once to send the missed notification
    assert mock_publish.call_count == 1
    assert mock_publish.call_args[0][0]["priority"] == "MEDIUM"

    async with session_maker() as db:
        r = await db.execute(select(Ticket).where(Ticket.reference == "TKT-GAP"))
        t_db2 = r.scalar_one()
        assert (
            t_db2.priority == TicketPriority.MEDIUM
        )  # unchanged (did not re-escalate)
        assert t_db2.last_escalated_at is not None
        assert t_db2.last_escalation_notified_at is not None  # updated successfully


@pytest.mark.asyncio
async def test_startup_shutdown_and_resilience():
    """
    Test background task startup, shutdown, and resilience to database errors.
    """
    session_maker = MagicMock()
    # Mock database session to raise exception during candidate querying to test error resilience
    session_maker.return_value.__aenter__.side_effect = Exception(
        "Database transient error"
    )

    # Temporarily set poll interval to a small value
    os.environ["TICKETS_ESCALATION_POLL_INTERVAL_SECONDS"] = "0.05"

    # Patch the "pytest" check to allow worker to run in tests
    with patch("sys.modules", {}), patch.dict(os.environ, {}):
        if "PYTEST_CURRENT_TEST" in os.environ:
            del os.environ["PYTEST_CURRENT_TEST"]

        await start_background_ticket_escalation()

        import apps.tickets.escalation as esc

        assert esc._escalation_task is not None
        assert esc._should_run is True

        # Let the loop execute a couple of cycles and recover from the exception
        await asyncio.sleep(0.15)

        # Stop background loop cleanly
        await stop_background_ticket_escalation()
        assert esc._escalation_task is None
        assert esc._should_run is False
