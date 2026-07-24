import asyncio
import os
from datetime import datetime, timedelta
from unittest.mock import MagicMock, patch

import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import Base, ClinicalQuery
from apps.execution.queries_escalation import (
    execute_query_escalation_cycle,
    start_background_query_escalation,
    stop_background_query_escalation,
)


@pytest_asyncio.fixture(autouse=True)
async def setup_test_db():
    """Setup in-memory SQLite database before each test and clear down after."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:")
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)

    yield

    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
@patch("apps.execution.queries_escalation.NotificationRouter.send_email")
async def test_threshold_boundaries_and_escalation(mock_send_email):
    """Test that queries are escalated if they are unresolved for more than 14 days, and not otherwise."""
    session_maker = db_manager.get_session_maker()
    now = datetime.now()

    # 1. Create clinical queries of varying ages
    # q_old_open: 15 days old, status OPEN -> should be escalated
    # q_old_reopened: 14.5 days old, status REOPENED -> should be escalated
    # q_young_open: 13 days old, status OPEN -> should NOT be escalated
    # q_old_closed: 15 days old, status CLOSED -> should NOT be escalated
    q_old_open = ClinicalQuery(
        study_id="STUDY-OLD-OPEN",
        site_id="SITE-1",
        subject_id="SUBJ-1",
        test_code="HR",
        status="OPEN",
        created_at=now - timedelta(days=15),
    )
    q_old_reopened = ClinicalQuery(
        study_id="STUDY-OLD-REOPEN",
        site_id="SITE-2",
        subject_id="SUBJ-2",
        test_code="TEMP",
        status="REOPENED",
        created_at=now - timedelta(days=14.5),
    )
    q_young_open = ClinicalQuery(
        study_id="STUDY-YOUNG",
        site_id="SITE-1",
        subject_id="SUBJ-3",
        test_code="SYSBP",
        status="OPEN",
        created_at=now - timedelta(days=13),
    )
    q_old_closed = ClinicalQuery(
        study_id="STUDY-OLD-CLOSED",
        site_id="SITE-1",
        subject_id="SUBJ-4",
        test_code="DIA",
        status="CLOSED",
        created_at=now - timedelta(days=15),
    )

    async with session_maker() as db:
        db.add_all([q_old_open, q_old_reopened, q_young_open, q_old_closed])
        await db.commit()

    # Reset digest tracking for clean testing
    import apps.execution.queries_escalation as qe

    qe._last_digest_sent_at = None

    # Run the query escalation cycle
    await execute_query_escalation_cycle(session_maker)

    # Verify state in database
    async with session_maker() as db:
        # q_old_open should be escalated (priority=HIGH, escalated_at present)
        res = await db.execute(
            select(ClinicalQuery).where(ClinicalQuery.study_id == "STUDY-OLD-OPEN")
        )
        q1 = res.scalar_one()
        assert q1.priority == "HIGH"
        assert q1.escalated_at is not None

        # q_old_reopened should be escalated (priority=HIGH, escalated_at present)
        res = await db.execute(
            select(ClinicalQuery).where(ClinicalQuery.study_id == "STUDY-OLD-REOPEN")
        )
        q2 = res.scalar_one()
        assert q2.priority == "HIGH"
        assert q2.escalated_at is not None

        # q_young_open should NOT be escalated (priority not HIGH, escalated_at is None)
        res = await db.execute(
            select(ClinicalQuery).where(ClinicalQuery.study_id == "STUDY-YOUNG")
        )
        q3 = res.scalar_one()
        assert q3.priority != "HIGH"
        assert q3.escalated_at is None

        # q_old_closed should NOT be escalated (priority not HIGH, escalated_at is None)
        res = await db.execute(
            select(ClinicalQuery).where(ClinicalQuery.study_id == "STUDY-OLD-CLOSED")
        )
        q4 = res.scalar_one()
        assert q4.priority != "HIGH"
        assert q4.escalated_at is None

    # Verify that digest email was sent
    assert mock_send_email.call_count > 0

    # Ensure recipient emails match expectations
    called_recipients = [call.args[0] for call in mock_send_email.call_args_list]
    # SITE-1 PI email and STUDY-OLD-OPEN CRA email
    assert any(
        "pi_SITE-1@cadence.clinical" in r and "cra_STUDY-OLD-OPEN@cadence.clinical" in r
        for r in called_recipients
    )


@pytest.mark.asyncio
@patch("apps.execution.queries_escalation.NotificationRouter.send_email")
async def test_escalation_idempotency(mock_send_email):
    """Test that queries are not repeatedly escalated or reset if already escalated."""
    session_maker = db_manager.get_session_maker()
    now = datetime.now()

    first_escalated_time = now - timedelta(hours=2)

    q = ClinicalQuery(
        study_id="STUDY-IDEMPOTENCY",
        site_id="SITE-1",
        subject_id="SUBJ-1",
        test_code="HR",
        status="OPEN",
        priority="HIGH",
        escalated_at=first_escalated_time,
        created_at=now - timedelta(days=15),
    )

    async with session_maker() as db:
        db.add(q)
        await db.commit()

    import apps.execution.queries_escalation as qe

    qe._last_digest_sent_at = now - timedelta(hours=1)  # within daily window (24h)

    # Run the cycle again
    await execute_query_escalation_cycle(session_maker)

    # Verify escalation metadata was not reset or repeatedly overwritten
    async with session_maker() as db:
        res = await db.execute(
            select(ClinicalQuery).where(ClinicalQuery.study_id == "STUDY-IDEMPOTENCY")
        )
        q_db = res.scalar_one()
        assert q_db.priority == "HIGH"
        assert q_db.escalated_at == first_escalated_time

    # No digest email should have been sent since we are within the daily window
    mock_send_email.assert_not_called()


@pytest.mark.asyncio
@patch("apps.execution.queries_escalation.NotificationRouter.send_email")
async def test_digest_window_configurations(mock_send_email):
    """Test that digest window configuration environment variable behaves correctly."""
    session_maker = db_manager.get_session_maker()
    now = datetime.now()

    q = ClinicalQuery(
        study_id="STUDY-DIGEST",
        site_id="SITE-1",
        subject_id="SUBJ-1",
        test_code="HR",
        status="OPEN",
        created_at=now - timedelta(days=15),
    )

    async with session_maker() as db:
        db.add(q)
        await db.commit()

    import apps.execution.queries_escalation as qe

    qe._last_digest_sent_at = now - timedelta(seconds=10)

    # With high digest interval (e.g. 86400s), digest is NOT sent
    os.environ["QUERY_ESCALATION_DIGEST_SECONDS"] = "86400.0"
    await execute_query_escalation_cycle(session_maker)
    mock_send_email.assert_not_called()

    # With low digest interval (e.g. 1s), digest IS sent
    os.environ["QUERY_ESCALATION_DIGEST_SECONDS"] = "1.0"
    await execute_query_escalation_cycle(session_maker)
    assert mock_send_email.call_count == 1


@pytest.mark.asyncio
async def test_startup_shutdown_and_resilience():
    """Test that the background task starts and stops cleanly, and is resilient to database errors."""
    session_maker = MagicMock()
    # Mock database session to raise an exception during query execution to test error resilience
    session_maker.return_value.__aenter__.side_effect = Exception(
        "Database transient error"
    )

    # Start loop with very short interval
    os.environ["QUERY_ESCALATION_INTERVAL_SECONDS"] = "0.1"
    await start_background_query_escalation(session_maker, interval=0.1)

    import apps.execution.queries_escalation as qe

    assert qe._escalation_task is not None
    assert qe._should_run is True

    # Let it run for a short duration to verify it doesn't crash on exceptions
    await asyncio.sleep(0.3)

    # Stop loop cleanly
    await stop_background_query_escalation()
    assert qe._escalation_task is None
    assert qe._should_run is False


@pytest.mark.asyncio
@patch("apps.execution.queries_escalation.NotificationRouter.send_email")
async def test_escalation_missing_ids_fallback(mock_send_email):
    """Test fallback emails when study_id or site_id is missing/None."""
    session_maker = db_manager.get_session_maker()
    now = datetime.now()

    q = ClinicalQuery(
        study_id="",
        site_id=None,
        subject_id="SUBJ-EMPTY",
        test_code="HR",
        status="OPEN",
        created_at=now - timedelta(days=15),
    )

    async with session_maker() as db:
        db.add(q)
        await db.commit()

    import apps.execution.queries_escalation as qe

    qe._last_digest_sent_at = None

    await execute_query_escalation_cycle(session_maker)

    called_recipients = [call.args[0] for call in mock_send_email.call_args_list]
    assert any(
        "pi@cadence.clinical" in r and "cra@cadence.clinical" in r
        for r in called_recipients
    )


@pytest.mark.asyncio
async def test_no_aging_queries():
    """Test cycle when there are absolutely no queries in the db."""
    session_maker = db_manager.get_session_maker()
    import apps.execution.queries_escalation as qe

    qe._last_digest_sent_at = None

    await execute_query_escalation_cycle(session_maker)
