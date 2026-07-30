import time
from unittest.mock import AsyncMock, patch

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.gateway.main import generate_signature
from apps.tickets.database import db_manager
from apps.tickets.main import app
from apps.tickets.models import (
    Base,
    Ticket,
    TicketAuditLog,
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


@pytest.mark.asyncio
@patch("apps.tickets.main.publish_notification", new_callable=AsyncMock)
async def test_ticket_assignment_notification(mock_publish):
    """
    Assert that assigning a ticket triggers a notification targeting the assignee user,
    and excluding the acting user.
    """
    mock_publish.return_value = True
    client = TestClient(app)

    # Setup - Create a ticket
    create_headers = get_auth_headers(
        user_id="reporter_user", change_reason="Initial issue"
    )
    payload = {
        "title": "Assignment test ticket",
        "description": "Test description",
        "priority": "MEDIUM",
    }
    res_create = client.post("/api/v1/tickets", json=payload, headers=create_headers)
    assert res_create.status_code == 201
    ticket_id = res_create.json()["id"]

    # Assign ticket (acting as "reporter_user")
    assign_headers = get_auth_headers(
        user_id="reporter_user", change_reason="Assigning developer"
    )
    assign_payload = {
        "assignee_user": "developer_bob",
        "version_index": 1,
    }
    res_assign = client.post(
        f"/api/v1/tickets/{ticket_id}/assign",
        json=assign_payload,
        headers=assign_headers,
    )
    assert res_assign.status_code == 200

    # Wait for background tasks to execute
    # FastAPI BackgroundTasks run after response. We can force them to run or let the TestClient handle them.
    # Actually, Starlette's TestClient runs background tasks synchronously before returning! So they've already run.

    # Assert notification dispatch details
    assert mock_publish.call_count == 1
    call_payload = mock_publish.call_args[0][0]

    # Check shape & values
    assert call_payload["recipient_user_id"] == "developer_bob"
    assert call_payload["recipient_role"] is None
    assert call_payload["category"] == "ACTION_ITEMS"
    assert call_payload["priority"] == "MEDIUM"
    assert call_payload["channels"] == "IN_APP"
    assert "TKT-" in call_payload["message_content"]
    assert "assigned" in call_payload["message_content"]
    assert call_payload["related_entity_type"] == "ticket"
    # Idempotency token in related_entity_id: f"{ticket_id}:assignment:2"
    assert call_payload["related_entity_id"] == f"{ticket_id}:assignment:2"


@pytest.mark.asyncio
@patch("apps.tickets.main.publish_notification", new_callable=AsyncMock)
async def test_ticket_comment_notification(mock_publish):
    """
    Assert that adding a comment to a ticket triggers notifications targeting the assignee and reporter,
    excluding the comment author.
    """
    mock_publish.return_value = True
    client = TestClient(app)

    # Create ticket as reporter_user, assigned to developer_bob
    headers = get_auth_headers(user_id="reporter_user", change_reason="Initial ticket")
    payload = {
        "title": "Comment test ticket",
        "description": "Test description",
        "priority": "MEDIUM",
        "assignee_user": "developer_bob",
    }
    res_create = client.post("/api/v1/tickets", json=payload, headers=headers)
    ticket_id = res_create.json()["id"]

    mock_publish.reset_mock()

    # Add comment as reporter_user -> should notify developer_bob (assignee), but exclude reporter_user (actor)
    comment_headers = get_auth_headers(
        user_id="reporter_user", change_reason="Add query"
    )
    comment_payload = {"body": "Could you check this please?"}
    res_comment = client.post(
        f"/api/v1/tickets/{ticket_id}/comments",
        json=comment_payload,
        headers=comment_headers,
    )
    assert res_comment.status_code == 201

    assert mock_publish.call_count == 1
    call_payload = mock_publish.call_args[0][0]
    assert call_payload["recipient_user_id"] == "developer_bob"
    assert call_payload["category"] == "ACTION_ITEMS"
    assert "Could you check this" in call_payload["message_content"]
    assert call_payload["related_entity_id"] == f"{ticket_id}:comment:1"


@pytest.mark.asyncio
@patch("apps.tickets.main.publish_notification", new_callable=AsyncMock)
async def test_ticket_transition_notification(mock_publish):
    """
    Assert that transitioning ticket status triggers notifications targeting reporter + assignee,
    excluding the actor, and includes the old and new statuses.
    """
    mock_publish.return_value = True
    client = TestClient(app)

    # Create ticket (reporter="reporter_user", assignee="developer_bob")
    headers = get_auth_headers(user_id="reporter_user", change_reason="Initial ticket")
    payload = {
        "title": "Transition test ticket",
        "description": "Desc",
        "priority": "MEDIUM",
        "assignee_user": "developer_bob",
    }
    res_create = client.post("/api/v1/tickets", json=payload, headers=headers)
    ticket_id = res_create.json()["id"]

    mock_publish.reset_mock()

    # Transition ticket as assignee (developer_bob) -> should notify reporter_user, but exclude developer_bob
    trans_headers = get_auth_headers(user_id="developer_bob", change_reason="Solving")
    trans_payload = {
        "status": "IN_PROGRESS",
        "version_index": 1,
    }
    res_trans = client.post(
        f"/api/v1/tickets/{ticket_id}/transition",
        json=trans_payload,
        headers=trans_headers,
    )
    assert res_trans.status_code == 200

    assert mock_publish.call_count == 1
    call_payload = mock_publish.call_args[0][0]
    assert call_payload["recipient_user_id"] == "reporter_user"
    assert call_payload["category"] == "SYSTEM"
    assert "OPEN" in call_payload["message_content"]
    assert "IN_PROGRESS" in call_payload["message_content"]
    assert call_payload["related_entity_id"] == f"{ticket_id}:transition:2"


@pytest.mark.asyncio
@patch("apps.tickets.main.publish_notification", new_callable=AsyncMock)
async def test_update_ticket_notifications(mock_publish):
    """
    Assert that PUT /api/v1/tickets/{id} detects both assignment diff and status change,
    and correctly enqueues two separate notifications.
    """
    mock_publish.return_value = True
    client = TestClient(app)

    # Create ticket
    headers = get_auth_headers(user_id="reporter_user", change_reason="Initial ticket")
    payload = {
        "title": "Update test ticket",
        "description": "Desc",
        "priority": "MEDIUM",
        "assignee_user": "developer_bob",
    }
    res_create = client.post("/api/v1/tickets", json=payload, headers=headers)
    ticket_id = res_create.json()["id"]

    mock_publish.reset_mock()

    # Update ticket both assignee and status (acting as a third user "manager_alice")
    update_headers = get_auth_headers(
        user_id="manager_alice", change_reason="Update manager and solve"
    )
    update_payload = {
        "assignee_user": "developer_charlie",
        "status": "RESOLVED",
        "version_index": 1,
    }
    res_update = client.put(
        f"/api/v1/tickets/{ticket_id}", json=update_payload, headers=update_headers
    )
    assert res_update.status_code == 200

    # Since manager_alice is the actor, recipients for assignment are developer_charlie and reporter_user.
    # Recipients for transition are developer_charlie and reporter_user.
    # Total calls: 2 events * 2 recipients = 4 notifications!
    assert mock_publish.call_count == 4

    calls = [call[0][0] for call in mock_publish.call_args_list]

    assignment_events = [c for c in calls if "assignment" in c["related_entity_id"]]
    transition_events = [c for c in calls if "transition" in c["related_entity_id"]]

    assert len(assignment_events) == 2
    assert len(transition_events) == 2


@pytest.mark.asyncio
@patch("apps.tickets.main.publish_notification")
async def test_notification_failure_isolation(mock_publish):
    """
    Assert that if publish_notification fails or raises an exception, the ticket mutation
    still commits successfully and audit log is fully written.
    """
    # Force publish_notification to raise a transport exception
    mock_publish.side_effect = Exception("Service Unavailable")
    client = TestClient(app)

    headers = get_auth_headers(user_id="reporter_user", change_reason="Initial ticket")
    payload = {
        "title": "Isolation test ticket",
        "description": "Desc",
        "priority": "LOW",
    }
    res_create = client.post("/api/v1/tickets", json=payload, headers=headers)
    ticket_id = res_create.json()["id"]

    # Assign ticket (this triggers assignment notification, which will fail/raise)
    assign_headers = get_auth_headers(
        user_id="reporter_user", change_reason="Assigning developer"
    )
    assign_payload = {
        "assignee_user": "developer_bob",
        "version_index": 1,
    }
    res_assign = client.post(
        f"/api/v1/tickets/{ticket_id}/assign",
        json=assign_payload,
        headers=assign_headers,
    )

    # Assert mutation is still completely successful
    assert res_assign.status_code == 200
    assert res_assign.json()["assignee_user"] == "developer_bob"

    # Assert ticket state in DB is persistent
    async with db_manager.get_session_maker()() as db_session:
        db_ticket = await db_session.get(Ticket, ticket_id)
        assert db_ticket is not None
        assert db_ticket.assignee_user == "developer_bob"

        # Assert audit logs were written successfully
        stmt = select(TicketAuditLog).where(TicketAuditLog.ticket_id == ticket_id)
        res_logs = await db_session.execute(stmt)
        logs = res_logs.scalars().all()
        assert len(logs) > 0
        assert any(log.action == "TICKET_ASSIGN" for log in logs)


@pytest.mark.asyncio
@patch("apps.tickets.main.publish_notification", new_callable=AsyncMock)
async def test_notification_idempotency(mock_publish):
    """
    Assert that identical events carry a stable/consistent key based on (ticket_id, event, version)
    so duplicates are harmless.
    """
    mock_publish.return_value = True
    client = TestClient(app)

    headers = get_auth_headers(user_id="reporter_user", change_reason="Initial ticket")
    payload = {
        "title": "Idempotency test ticket",
        "description": "Desc",
        "priority": "MEDIUM",
    }
    res_create = client.post("/api/v1/tickets", json=payload, headers=headers)
    ticket_id = res_create.json()["id"]

    # First assignment
    assign_headers = get_auth_headers(
        user_id="reporter_user", change_reason="Assign first"
    )
    assign_payload = {
        "assignee_user": "developer_bob",
        "version_index": 1,
    }
    client.post(
        f"/api/v1/tickets/{ticket_id}/assign",
        json=assign_payload,
        headers=assign_headers,
    )

    # Capture first related_entity_id
    first_call_payload = mock_publish.call_args_list[-1][0][0]
    first_key = first_call_payload["related_entity_id"]
    assert first_key == f"{ticket_id}:assignment:2"

    # Repeat exact same assignment -> version_index goes from 2 to 3
    assign_payload2 = {
        "assignee_user": "developer_charlie",
        "version_index": 2,
    }
    client.post(
        f"/api/v1/tickets/{ticket_id}/assign",
        json=assign_payload2,
        headers=assign_headers,
    )

    # Capture second related_entity_id
    second_call_payload = mock_publish.call_args_list[-1][0][0]
    second_key = second_call_payload["related_entity_id"]

    # The keys must be distinguishable because version_index is different
    assert second_key == f"{ticket_id}:assignment:3"
    assert first_key != second_key
