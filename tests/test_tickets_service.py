"""
Integration and unit tests for the Tickets service.
"""

import time
from datetime import datetime

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.gateway.main import generate_signature
from apps.tickets.database import db_manager
from apps.tickets.main import app
from apps.tickets.models import Base, Ticket, TicketAuditLog


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


def get_auth_headers(roles: str = "admin", change_reason: str = "") -> dict:
    """
    Helper to generate valid gateway V2 signed headers for testing.
    """
    timestamp = str(time.time())
    user_id = "tickets_test_user"
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


def test_tickets_health_check():
    """
    Verify health check of independent Tickets service is unauthenticated and works correctly.
    """
    client = TestClient(app)
    response = client.get("/health")
    assert response.status_code == 200
    data = response.json()
    assert data["status"] == "ok"
    assert data["service"] == "tickets"


def test_unauthenticated_requests_are_rejected():
    """
    Verify direct/untrusted requests are rejected by GatewayAuthMiddleware.
    """
    client = TestClient(app)
    response = client.get("/api/v1/tickets")
    assert response.status_code == 401
    assert "Missing gateway authentication headers" in response.json()["detail"]


@pytest.mark.asyncio
async def test_tickets_database_schema_creation():
    """
    Verify that tickets tables are created and queried successfully.
    """
    async with db_manager.get_session_maker()() as session:
        tickets = await session.execute(select(Ticket))
        logs = await session.execute(select(TicketAuditLog))

        assert tickets.scalars().all() == []
        assert logs.scalars().all() == []


@pytest.mark.asyncio
async def test_tickets_lifecycle():
    """
    Verify that a ticket can be created, listed, viewed, and updated with proper GxP fields.
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="admin", change_reason="Initial issue creation")

    payload = {
        "title": "System connection failure on study 102",
        "description": "Database connection timed out during subject randomization.",
        "priority": "HIGH",
    }

    # 1. Create Ticket
    res_create = client.post("/api/v1/tickets", json=payload, headers=headers)
    assert res_create.status_code == 201
    data_create = res_create.json()
    assert data_create["id"] is not None
    assert data_create["title"] == "System connection failure on study 102"
    assert (
        data_create["description"]
        == "Database connection timed out during subject randomization."
    )
    assert data_create["priority"] == "HIGH"
    assert data_create["status"] == "OPEN"
    assert data_create["is_deleted"] is False
    assert data_create["created_by"] == "tickets_test_user"
    assert data_create["reason_for_change"] == "Initial issue creation"
    assert data_create["version_index"] == 1

    ticket_id = data_create["id"]

    # 2. Get specific ticket
    res_get = client.get(f"/api/v1/tickets/{ticket_id}", headers=headers)
    assert res_get.status_code == 200
    assert res_get.json()["id"] == ticket_id

    # 3. List tickets (unfiltered)
    res_list = client.get("/api/v1/tickets", headers=headers)
    assert res_list.status_code == 200
    assert len(res_list.json()) == 1
    assert res_list.json()[0]["id"] == ticket_id

    # 4. List tickets (filtered by status)
    res_list_filtered = client.get("/api/v1/tickets?status=OPEN", headers=headers)
    assert res_list_filtered.status_code == 200
    assert len(res_list_filtered.json()) == 1

    res_list_filtered_other = client.get(
        "/api/v1/tickets?status=CLOSED", headers=headers
    )
    assert res_list_filtered_other.status_code == 200
    assert len(res_list_filtered_other.json()) == 0

    # 5. Update Ticket (Transition status and description)
    update_headers = get_auth_headers(
        roles="admin", change_reason="Issue resolved by network reboot"
    )
    update_payload = {
        "status": "RESOLVED",
        "description": "Resolved with reboot.",
    }
    res_update = client.put(
        f"/api/v1/tickets/{ticket_id}", json=update_payload, headers=update_headers
    )
    assert res_update.status_code == 200
    data_update = res_update.json()
    assert data_update["status"] == "RESOLVED"
    assert data_update["description"] == "Resolved with reboot."
    assert data_update["version_index"] == 2
    assert data_update["reason_for_change"] == "Issue resolved by network reboot"

    # 6. Soft Delete Ticket
    delete_headers = get_auth_headers(
        roles="admin", change_reason="Soft deleting duplicate ticket"
    )
    delete_payload = {
        "is_deleted": True,
    }
    res_delete = client.put(
        f"/api/v1/tickets/{ticket_id}", json=delete_payload, headers=delete_headers
    )
    assert res_delete.status_code == 200
    assert res_delete.json()["is_deleted"] is True
    assert res_delete.json()["version_index"] == 3

    # Listing tickets without include_deleted should return 0 results
    res_list_no_deleted = client.get("/api/v1/tickets", headers=headers)
    assert res_list_no_deleted.status_code == 200
    assert len(res_list_no_deleted.json()) == 0

    # Listing tickets with include_deleted should return 1 result
    res_list_with_deleted = client.get(
        "/api/v1/tickets?include_deleted=true", headers=headers
    )
    assert res_list_with_deleted.status_code == 200
    assert len(res_list_with_deleted.json()) == 1


@pytest.mark.asyncio
async def test_ticket_audit_log_immutable_ledger():
    """
    Verify TicketAuditLog is append-only and rejects updates/deletions.
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="admin", change_reason="Auditing demonstration")

    # 1. Perform some actions to populate audit logs
    client.post(
        "/api/v1/tickets",
        json={
            "title": "Audit ticket",
            "description": "For testing audit logs.",
            "priority": "LOW",
        },
        headers=headers,
    )

    # 2. Query TicketAuditLog table to confirm logs exist
    async with db_manager.get_session_maker()() as session:
        stmt = select(TicketAuditLog)
        res = await session.execute(stmt)
        logs = res.scalars().all()
        assert len(logs) > 0

        # Verify mandatory Part 11 fields are present on the logs
        log = logs[0]
        assert log.created_by == "tickets_test_user"
        assert log.reason_for_change == "Auditing demonstration"
        assert log.version_index == 1
        assert isinstance(log.created_at, datetime)

        # 3. Try to update an audit log record -> Expect ValueError
        log.details = "Hacked/Modified Details"
        session.add(log)
        with pytest.raises(ValueError) as exc_info:
            await session.commit()
        assert "Updates to TicketAuditLog are strictly forbidden" in str(exc_info.value)
        await session.rollback()

        # 4. Try to delete an audit log record -> Expect ValueError
        # Re-fetch log to ensure session is clean
        stmt_refetch = select(TicketAuditLog).limit(1)
        res_refetch = await session.execute(stmt_refetch)
        log_to_delete = res_refetch.scalar_one()

        await session.delete(log_to_delete)
        with pytest.raises(ValueError) as exc_info_del:
            await session.commit()
        assert "Deletions from TicketAuditLog are strictly forbidden" in str(
            exc_info_del.value
        )


@pytest.mark.asyncio
async def test_list_ticket_audit_logs_endpoint():
    """
    Verify list_ticket_audit_logs endpoint is protected and returns descending order logs.
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="admin", change_reason="List logs test reason")

    # Access endpoint
    res = client.get("/api/v1/tickets/audit-logs", headers=headers)
    assert res.status_code == 200
    data = res.json()
    assert len(data) > 0
    # The logs should be returned in descending chronological order
    created_ats = [d["created_at"] for d in data]
    assert created_ats == sorted(created_ats, reverse=True)


def test_missing_change_reason_fails_mutations():
    """
    Verify mutations fail if X-Change-Reason is missing.
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="admin")  # Missing change_reason

    payload = {
        "title": "No justification",
        "description": "Should fail creation.",
        "priority": "LOW",
    }

    res = client.post("/api/v1/tickets", json=payload, headers=headers)
    assert res.status_code == 403
    assert "Missing change justification reason" in res.json()["detail"]


def test_nonexistent_resources_return_404():
    """
    Verify 404 is returned when attempting to access nonexistent resources.
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="admin", change_reason="Accessing nonexistent")

    res_ticket = client.get("/api/v1/tickets/nonexistent-ticket-id", headers=headers)
    assert res_ticket.status_code == 404
    assert "Ticket with ID" in res_ticket.json()["detail"]
