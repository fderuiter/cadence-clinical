# @req:Trace-16
"""
In-process end-to-end integration and seam tests for tickets notifications.
Exercises real gateway routing, ASGI-based service-to-service communication,
and SLA escalation retry mechanics.
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
from apps.notifications.database import db_manager as notifications_db_manager
from apps.notifications.main import app as notifications_app
from apps.notifications.models import Base as NotificationsBase
from apps.notifications.models import Notification, NotificationDelivery
from apps.tickets.database import db_manager as tickets_db_manager
from apps.tickets.escalation import execute_ticket_escalation_cycle
from apps.tickets.main import app as tickets_app
from apps.tickets.models import Base as TicketsBase
from apps.tickets.models import Ticket, TicketPriority, TicketStatus


@pytest_asyncio.fixture(autouse=True)
async def setup_dual_dbs():
    """
    Setup in-memory SQLite databases for both Tickets and Notifications.
    """
    tickets_db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with tickets_db_manager.engine.begin() as conn:
        await conn.run_sync(TicketsBase.metadata.create_all)

    notifications_db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with notifications_db_manager.engine.begin() as conn:
        await conn.run_sync(NotificationsBase.metadata.create_all)

    yield

    if tickets_db_manager.engine is not None:
        async with tickets_db_manager.engine.begin() as conn:
            await conn.run_sync(TicketsBase.metadata.drop_all)
        await tickets_db_manager.close()

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


# Global control variable to simulate notifications endpoint failures
fail_notifications_route = False


@pytest_asyncio.fixture(autouse=True)
def route_tickets_to_notifications():
    """
    Intercept httpx.AsyncClient instantiation inside the tickets service
    and route all outgoing notification client HTTP requests directly to the
    notifications service in-process using ASGITransport.
    Supports simulated route failures for retry testing.
    """
    from packages.security.gateway_client import GatewayBaseClient
    GatewayBaseClient._shared_client = None

    transport = httpx.ASGITransport(app=notifications_app)
    original_init = httpx.AsyncClient.__init__

    def patched_init(self, *args, **kwargs):
        original_init(self, *args, **kwargs)
        original_send = self.send

        async def patched_send(request, *s_args, **s_kwargs):
            global fail_notifications_route
            if fail_notifications_route and "/api/v1/notifications" in str(request.url):
                return httpx.Response(
                    status_code=500,
                    content=b"Simulated service failure",
                    request=request,
                )
            if "/api/v1/notifications" in str(request.url):
                self._transport = transport
                request.url = request.url.copy_with(host="localhost", port=8006)
            return await original_send(request, *s_args, **s_kwargs)

        self.send = patched_send

    with patch.object(httpx.AsyncClient, "__init__", patched_init):
        yield
        GatewayBaseClient._shared_client = None


@pytest.mark.asyncio
async def test_end_to_end_tickets_and_notifications_handshake():
    """
    # @req:Trace-16
    Verify that creating and assigning a ticket in Tickets service triggers an
    HMAC-signed request that successfully lands on the Notifications service over
    GatewayAuthMiddleware, registering the notification and delivery channels correctly.
    Asserts de-duplication related_entity_id ({ticket_id}:{event_type}:{version_index})
    and recipient policy (actor excluded).
    """
    global fail_notifications_route
    fail_notifications_route = False

    client = TestClient(tickets_app)
    create_headers = get_auth_headers(
        user_id="reporter_user", change_reason="Initial issue description"
    )

    payload = {
        "title": "Database connection drop",
        "description": "Database connection timed out during subject randomization.",
        "priority": "HIGH",
    }

    res_create = client.post("/api/v1/tickets", json=payload, headers=create_headers)
    assert res_create.status_code == 201
    ticket_data = res_create.json()
    ticket_id = ticket_data["id"]

    assign_headers = get_auth_headers(
        user_id="reporter_user", change_reason="Routing to development team"
    )
    assign_payload = {
        "assignee_user": "bob_dev",
        "version_index": 1,
    }

    res_assign = client.post(
        f"/api/v1/tickets/{ticket_id}/assign",
        json=assign_payload,
        headers=assign_headers,
    )
    assert res_assign.status_code == 200

    notif_session_maker = notifications_db_manager.get_session_maker()
    async with notif_session_maker() as session:
        stmt = select(Notification).where(
            Notification.related_entity_id == f"{ticket_id}:assignment:2"
        )
        res = await session.execute(stmt)
        notification = res.scalar_one_or_none()

        assert notification is not None
        assert notification.recipient_user_id == "bob_dev"
        assert notification.created_by == "reporter_user"
        assert "assigned" in notification.message_content
        assert notification.related_entity_type == "ticket"

        stmt_deliv = select(NotificationDelivery).where(
            NotificationDelivery.notification_id == notification.id
        )
        res_deliv = await session.execute(stmt_deliv)
        deliveries = res_deliv.scalars().all()
        assert len(deliveries) >= 1
        assert any(d.channel == "IN_APP" for d in deliveries)


@pytest.mark.asyncio
async def test_escalation_worker_notifications_retry_mechanics():
    """
    # @req:Trace-16
    Verify escalation worker overdue retry behavior:
    1. Escalate priority from LOW to MEDIUM with the notifications endpoint failing.
       Assert that priority is updated, but last_escalation_notified_at remains None (stale).
    2. Run a second escalation cycle with the notifications endpoint succeeding.
       Assert that the priority does NOT escalate further (blocked by cooldown),
       but the missed notification is retried and last_escalation_notified_at is updated.
    """
    global fail_notifications_route
    now = datetime.now()

    tickets_session_maker = tickets_db_manager.get_session_maker()
    async with tickets_session_maker() as db:
        overdue_ticket = Ticket(
            reference="TKT-OVERDUE",
            title="SLA Overdue issue",
            description="Testing SLA worker retry loops.",
            priority=TicketPriority.LOW,
            status=TicketStatus.OPEN,
            reporter="investigator_1",
            assignee_user="crc_bob",
            due_date=now - timedelta(days=5),
            created_by="investigator_1",
            reason_for_change="Initial",
        )
        db.add(overdue_ticket)
        await db.commit()
        ticket_id = overdue_ticket.id

    # 1. First cycle: Simulate notifications endpoint failure
    fail_notifications_route = True

    await execute_ticket_escalation_cycle(tickets_session_maker)

    async with tickets_session_maker() as db:
        res = await db.execute(select(Ticket).where(Ticket.id == ticket_id))
        ticket = res.scalar_one()
        assert ticket.priority == TicketPriority.MEDIUM
        assert ticket.last_escalated_at is not None
        assert ticket.last_escalation_notified_at is None

    # 2. Second cycle: Restore notifications endpoint success
    fail_notifications_route = False

    await execute_ticket_escalation_cycle(tickets_session_maker)

    async with tickets_session_maker() as db:
        res = await db.execute(select(Ticket).where(Ticket.id == ticket_id))
        ticket = res.scalar_one()
        assert ticket.priority == TicketPriority.MEDIUM
        assert ticket.last_escalation_notified_at is not None

    notif_session_maker = notifications_db_manager.get_session_maker()
    async with notif_session_maker() as session:
        stmt = select(Notification).where(
            Notification.related_entity_id == f"{ticket_id}:escalation:2"
        )
        res = await session.execute(stmt)
        notification = res.scalar_one_or_none()
        assert notification is not None
        assert notification.recipient_user_id == "crc_bob"
        assert "Automated escalation" in notification.message_content
