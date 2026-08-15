"""
End-to-end integration seam verification tests for the Tickets-to-Notifications flow.
Bypasses manual mocks and intercepts real outgoing HTTP requests to verify Gateway V2 signing,
de-duplication tokens, recipient policies, and delivery state tracking.
"""

import time
from datetime import datetime, timedelta
from unittest.mock import patch

import httpx
import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.gateway.main import generate_signature
from apps.notifications.adapters.database import db_manager as notifications_db_manager
from apps.notifications.adapters.models import Base as NotificationsBase
from apps.notifications.adapters.models import Notification
from apps.notifications.main import app as notifications_app
from apps.tickets.adapters.database import db_manager as tickets_db_manager
from apps.tickets.adapters.escalation import execute_ticket_escalation_cycle
from apps.tickets.adapters.models import Base as TicketsBase
from apps.tickets.adapters.models import Ticket, TicketPriority, TicketStatus
from apps.tickets.main import app as tickets_app


@pytest_asyncio.fixture(autouse=True)
async def setup_databases():
    """
    Setup in-memory Tickets and Notifications databases for integration verification.
    """
    # Initialize tickets database
    tickets_db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with tickets_db_manager.engine.begin() as conn:
        await conn.run_sync(TicketsBase.metadata.create_all)

    # Initialize notifications database
    notifications_db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with notifications_db_manager.engine.begin() as conn:
        await conn.run_sync(NotificationsBase.metadata.create_all)

    yield

    # Teardown tickets
    if tickets_db_manager.engine is not None:
        async with tickets_db_manager.engine.begin() as conn:
            await conn.run_sync(TicketsBase.metadata.drop_all)
        await tickets_db_manager.close()

    # Teardown notifications
    if notifications_db_manager.engine is not None:
        async with notifications_db_manager.engine.begin() as conn:
            await conn.run_sync(NotificationsBase.metadata.drop_all)
        await notifications_db_manager.close()


def get_auth_headers(
    user_id: str = "tickets_test_user", roles: str = "admin", change_reason: str = ""
) -> dict:
    """
    Helper to generate valid gateway V2 signed headers for testing.
    """
    timestamp = str(time.time())
    sig = generate_signature(
        user_id, roles, timestamp, version="2", change_reason=change_reason
    )
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
    }
    if change_reason:
        headers["X-Change-Reason"] = change_reason
    return headers


@pytest.fixture
def intercept_notifications():
    """
    Monkeypatch httpx.AsyncClient.send to intercept requests targeting Notifications,
    forwarding them directly to the notifications app with active ASGI transport.
    """
    original_send = httpx.AsyncClient.send

    async def mock_send(
        self, request: httpx.Request, *args, **kwargs
    ) -> httpx.Response:
        url_str = str(request.url)
        if "api/v1/notifications" in url_str:
            # Route to the notifications ASGI application
            transport = httpx.ASGITransport(app=notifications_app)
            async with httpx.AsyncClient(transport=transport) as local_client:
                return await original_send(local_client, request, *args, **kwargs)
        return await original_send(self, request, *args, **kwargs)

    with patch("httpx.AsyncClient.send", mock_send):
        yield


@pytest.mark.asyncio
async def test_end_to_end_ticket_creation_and_comment_flow(intercept_notifications):
    """
    # @req:Trace-16
    Create a ticket and append a comment. Ensure the asynchronous background task
    generates V2 HMAC signatures, bypasses mocks, and successfully persists
    In-App notifications in the notifications DB with correct de-duplication related_entity_id tokens.
    """
    client = TestClient(tickets_app)

    # 1. Create a ticket (acting as reporter "reporter_user")
    headers = get_auth_headers(user_id="reporter_user", change_reason="Initial ticket")
    payload = {
        "title": "Database pool saturated",
        "description": "Form submissions are failing due to pool exhaustion.",
        "priority": "HIGH",
        "assignee_user": "developer_bob",
    }
    res_create = client.post("/api/v1/tickets", json=payload, headers=headers)
    assert res_create.status_code == 201
    ticket_id = res_create.json()["id"]

    # 2. Append a comment to the ticket (acting as "reporter_user")
    comment_headers = get_auth_headers(
        user_id="reporter_user", change_reason="Query updates"
    )
    comment_payload = {"body": "Bob, are you seeing any thread locks?"}
    res_comment = client.post(
        f"/api/v1/tickets/{ticket_id}/comments",
        json=comment_payload,
        headers=comment_headers,
    )
    assert res_comment.status_code == 201

    # 3. Query the notifications database directly to assert that
    # the comment notification landed successfully via GatewayAuthMiddleware.
    async with notifications_db_manager.get_session_maker()() as session:
        stmt = select(Notification).where(
            Notification.related_entity_id == f"{ticket_id}:comment:1"
        )
        res = await session.execute(stmt)
        notification = res.scalar_one_or_none()

        assert notification is not None
        assert notification.recipient_user_id == "developer_bob"  # Targets assignee
        assert notification.recipient_role is None
        assert "ACTION_ITEMS" in str(notification.category)
        assert "MEDIUM" in str(
            notification.priority
        )  # Comment notifications default to MEDIUM priority per policy
        assert "Bob, are you seeing any thread locks?" in notification.message_content
        assert (
            notification.created_by == "reporter_user"
        )  # Correctly attributed to the actor
        assert (
            notification.reason_for_change == "Query updates"
        )  # Correctly propagated via signature v2


@pytest.mark.asyncio
async def test_end_to_end_escalation_worker_flow_and_retries(intercept_notifications):
    """
    # @req:Trace-16
    Run an escalation cycle where first the notifications service is unavailable
    (failure leaves last_escalation_notified_at stale). Then restore the service
    and run a second cycle to verify successful retry (success stamps last_escalation_notified_at).
    """
    session_maker = tickets_db_manager.get_session_maker()
    now = datetime.now()

    # Create an overdue ticket in the database
    t = Ticket(
        reference="TKT-ESCALATE-01",
        title="SLA Breach Ticket",
        description="This ticket has breached its resolution SLA.",
        priority=TicketPriority.LOW,
        status=TicketStatus.OPEN,
        reporter="user_reporter",
        due_date=now - timedelta(days=2),
        created_by="user_reporter",
        reason_for_change="Initial",
    )
    async with session_maker() as db:
        db.add(t)
        await db.commit()

    # 1. First escalation cycle fails on notification dispatch (notifications_app unavailable)
    # We simulate this by patching httpx.AsyncClient.send to raise an error
    async def mock_fail_send(self, request: httpx.Request, *args, **kwargs):
        raise httpx.ConnectError("Service Unavailable")

    with patch("httpx.AsyncClient.send", mock_fail_send):
        await execute_ticket_escalation_cycle(session_maker)

    async with session_maker() as db:
        r = await db.execute(
            select(Ticket).where(Ticket.reference == "TKT-ESCALATE-01")
        )
        ticket_db = r.scalar_one()
        assert ticket_db.priority == TicketPriority.MEDIUM  # escalated LOW -> MEDIUM
        assert ticket_db.last_escalated_at is not None
        assert (
            ticket_db.last_escalation_notified_at is None
        )  # Remains stale because of notify failure

    # 2. Second escalation cycle succeeds on notification dispatch
    # Our normal intercept_notifications will route it to notifications_app successfully
    await execute_ticket_escalation_cycle(session_maker)

    async with session_maker() as db:
        r = await db.execute(
            select(Ticket).where(Ticket.reference == "TKT-ESCALATE-01")
        )
        ticket_db2 = r.scalar_one()
        assert (
            ticket_db2.priority == TicketPriority.MEDIUM
        )  # Remains MEDIUM (did not re-escalate)
        assert ticket_db2.last_escalation_notified_at is not None  # Success stamps it!

    # 3. Assert notification is stored in Notifications DB with f"{ticket_id}:escalation:{version}" pattern
    async with notifications_db_manager.get_session_maker()() as session:
        stmt = select(Notification).where(
            Notification.related_entity_id == f"{ticket_db2.id}:escalation:2"
        )
        res = await session.execute(stmt)
        notification = res.scalar_one_or_none()
        assert notification is not None
        assert "TKT-ESCALATE-01" in notification.message_content
        assert notification.recipient_user_id is None
        assert notification.recipient_role is None
