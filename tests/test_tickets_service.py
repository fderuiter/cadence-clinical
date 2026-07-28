"""
Integration and unit tests for the Tickets service.
"""

import time
from datetime import datetime

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from httpx import ASGITransport, AsyncClient
from sqlalchemy import select

from apps.gateway.main import generate_signature
from apps.tickets.database import db_manager
from apps.tickets.main import app
from apps.tickets.models import (
    Base,
    Ticket,
    TicketAuditLog,
    TicketCategory,
    TicketComment,
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
        "version_index": 1,
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
        "version_index": 2,
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


def test_tickets_enums_and_models_attributes():
    """
    Verify enums and models attributes exist and conform to spec.
    """
    # Enums assertions and docstrings
    assert TicketCategory.TECHNICAL.value == "TECHNICAL"
    assert TicketCategory.CLINICAL.value == "CLINICAL"
    assert TicketCategory.HARDWARE.value == "HARDWARE"
    assert TicketCategory.ACCESS.value == "ACCESS"
    assert TicketCategory.OTHER.value == "OTHER"
    assert TicketCategory.__doc__ is not None

    assert TicketPriority.LOW.value == "LOW"
    assert TicketPriority.MEDIUM.value == "MEDIUM"
    assert TicketPriority.HIGH.value == "HIGH"
    assert TicketPriority.CRITICAL.value == "CRITICAL"
    assert TicketPriority.__doc__ is not None

    assert TicketStatus.OPEN.value == "OPEN"
    assert TicketStatus.IN_PROGRESS.value == "IN_PROGRESS"
    assert TicketStatus.RESOLVED.value == "RESOLVED"
    assert TicketStatus.CLOSED.value == "CLOSED"
    assert TicketStatus.__doc__ is not None


@pytest.mark.asyncio
async def test_comments_creation_and_retrieval_scoped():
    """
    Verify creation of ticket comments and efficient, ascending chronological retrieval.
    """
    client = TestClient(app)
    headers = get_auth_headers(
        roles="admin", change_reason="Creating a ticket for comments test"
    )

    # 1. Create a Ticket
    payload = {
        "title": "Comment test ticket",
        "description": "Ticket description.",
        "category": "TECHNICAL",
        "priority": "LOW",
    }
    res_create = client.post("/api/v1/tickets", json=payload, headers=headers)
    assert res_create.status_code == 201
    ticket_id = res_create.json()["id"]

    # 2. Add multiple comments to the ticket
    comment_headers = get_auth_headers(
        roles="admin", change_reason="Adding first comment"
    )
    res_comment1 = client.post(
        f"/api/v1/tickets/{ticket_id}/comments",
        json={"body": "This is the first comment."},
        headers=comment_headers,
    )
    assert res_comment1.status_code == 201
    data_comment1 = res_comment1.json()
    assert data_comment1["ticket_id"] == ticket_id
    assert data_comment1["body"] == "This is the first comment."
    assert data_comment1["created_by"] == "tickets_test_user"
    assert data_comment1["reason_for_change"] == "Adding first comment"
    assert data_comment1["version_index"] == 1

    # Sleep slightly to ensure distinct created_at timestamps if needed (SQLite datetime defaults may be identical in the same millisecond)
    time.sleep(0.1)

    comment_headers2 = get_auth_headers(
        roles="admin", change_reason="Adding second comment"
    )
    res_comment2 = client.post(
        f"/api/v1/tickets/{ticket_id}/comments",
        json={"body": "This is the second comment."},
        headers=comment_headers2,
    )
    assert res_comment2.status_code == 201

    # 3. List comments and verify referential integrity and ascending chronological ordering
    res_list = client.get(f"/api/v1/tickets/{ticket_id}/comments", headers=headers)
    assert res_list.status_code == 200
    comments = res_list.json()
    assert len(comments) == 2
    assert comments[0]["body"] == "This is the first comment."
    assert comments[1]["body"] == "This is the second comment."

    # 4. Verify cascade delete referential integrity
    async with db_manager.get_session_maker()() as session:
        # Fetch comments in DB
        db_comments = await session.execute(
            select(TicketComment).where(TicketComment.ticket_id == ticket_id)
        )
        assert len(db_comments.scalars().all()) == 2

        # Delete the ticket
        db_ticket = await session.get(Ticket, ticket_id)
        await session.delete(db_ticket)
        await session.commit()

        # Verify comment rows are gone via CASCADE
        db_comments_after = await session.execute(
            select(TicketComment).where(TicketComment.ticket_id == ticket_id)
        )
        assert len(db_comments_after.scalars().all()) == 0


@pytest.mark.asyncio
async def test_ticket_scoped_audit_logs():
    """
    Verify audit records can be retrieved filtered by ticket_id.
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="admin", change_reason="Audit scoping test")

    # Create Ticket 1
    t1_res = client.post(
        "/api/v1/tickets",
        json={"title": "Ticket 1", "description": "D1", "priority": "LOW"},
        headers=headers,
    )
    t1_id = t1_res.json()["id"]

    # Create Ticket 2
    t2_res = client.post(
        "/api/v1/tickets",
        json={"title": "Ticket 2", "description": "D2", "priority": "HIGH"},
        headers=headers,
    )
    t2_id = t2_res.json()["id"]
    assert t2_id is not None

    # Retrieve audit logs filtered by Ticket 1
    res_audit_t1 = client.get(
        f"/api/v1/tickets/audit-logs?ticket_id={t1_id}", headers=headers
    )
    assert res_audit_t1.status_code == 200
    t1_logs = res_audit_t1.json()

    # Filter log entries that have ticket_id == t1_id
    # Note: there might be other log entries (like list logs) in the return if not fully filtered,
    # but the endpoint filters by ticket_id in SQL if passed. Let's verify all returned items have ticket_id == t1_id
    # wait, our endpoint writes a self-auditing TICKET_AUDIT_LOG_LIST entry with ticket_id=ticket_id, which is also filtered correctly!
    for log in t1_logs:
        assert log["ticket_id"] == t1_id


@pytest.mark.asyncio
async def test_ticket_concurrent_reference_generation():
    """
    Verify ticket reference generation is unique and safe under concurrent creations.
    """
    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as ac:
        headers = get_auth_headers(
            roles="admin", change_reason="Concurrent creation test"
        )
        payload = {
            "title": "Concurrent Ticket",
            "description": "Testing uniqueness under concurrency.",
            "priority": "LOW",
            "category": "TECHNICAL",
        }
        # Run 10 requests concurrently
        import asyncio

        tasks = [
            ac.post("/api/v1/tickets", json=payload, headers=headers) for _ in range(10)
        ]
        responses = await asyncio.gather(*tasks)

        references = []
        for r in responses:
            assert r.status_code == 201, f"Failed concurrent insert: {r.text}"
            data = r.json()
            assert data["reference"] is not None
            references.append(data["reference"])

        # Ensure all references are unique and formatted correctly
        assert len(references) == 10
        assert len(set(references)) == 10
        for ref in references:
            assert ref.startswith("TKT-")


@pytest.mark.asyncio
async def test_tickets_rbac_auditor_cannot_mutate_but_can_read():
    """
    Verify that auditor/inspector roles cannot create or update tickets but can read them.
    """
    client = TestClient(app)

    # 1. Auditor tries to create a ticket -> Expect 403 Forbidden
    auditor_headers = get_auth_headers(
        roles="auditor", change_reason="Creating as auditor"
    )
    payload = {
        "title": "Auditor ticket",
        "description": "Should fail",
        "priority": "LOW",
    }
    res_create = client.post("/api/v1/tickets", json=payload, headers=auditor_headers)
    assert res_create.status_code == 403
    assert (
        "Auditor personas are restricted to read-only access"
        in res_create.json()["detail"]
    )

    # 2. Setup a ticket using admin role first
    admin_headers = get_auth_headers(
        roles="admin", change_reason="Setting up for auditor read"
    )
    res_setup = client.post("/api/v1/tickets", json=payload, headers=admin_headers)
    assert res_setup.status_code == 201
    ticket_id = res_setup.json()["id"]

    # 3. Auditor tries to update the ticket -> Expect 403 Forbidden
    res_update = client.put(
        f"/api/v1/tickets/{ticket_id}",
        json={"title": "Auditor update"},
        headers=auditor_headers,
    )
    assert res_update.status_code == 403

    # 4. Auditor lists tickets -> Expect 200 OK and able to read
    res_list = client.get("/api/v1/tickets", headers=auditor_headers)
    assert res_list.status_code == 200
    assert len(res_list.json()) >= 1

    # 5. Auditor gets specific ticket -> Expect 200 OK and able to read
    res_get = client.get(f"/api/v1/tickets/{ticket_id}", headers=auditor_headers)
    assert res_get.status_code == 200
    assert res_get.json()["id"] == ticket_id


@pytest.mark.asyncio
async def test_tickets_terminal_state_rejection():
    """
    Verify that updates to terminal tickets (CLOSED) are rejected.
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="admin", change_reason="Terminal state test")

    # 1. Create ticket
    res_create = client.post(
        "/api/v1/tickets",
        json={"title": "Test Terminal", "description": "Desc", "priority": "LOW"},
        headers=headers,
    )
    assert res_create.status_code == 201
    ticket_id = res_create.json()["id"]

    # 2. Update to CLOSED (terminal status)
    res_closed = client.put(
        f"/api/v1/tickets/{ticket_id}",
        json={"status": "CLOSED", "version_index": 1},
        headers=headers,
    )
    assert res_closed.status_code == 200
    assert res_closed.json()["status"] == "CLOSED"

    # 3. Try to update a closed/terminal ticket -> Expect 400 Bad Request
    res_fail_closed = client.put(
        f"/api/v1/tickets/{ticket_id}",
        json={"description": "Updated closed desc", "version_index": 2},
        headers=headers,
    )
    assert res_fail_closed.status_code == 400
    assert (
        "Cannot update ticket because it is in terminal state"
        in res_fail_closed.json()["detail"]
    )


@pytest.mark.asyncio
async def test_tickets_get_by_reference():
    """
    Verify that ticket detail retrieval works by sequential reference as well as ID.
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="admin", change_reason="Reference retrieval test")

    # 1. Create ticket
    res_create = client.post(
        "/api/v1/tickets",
        json={
            "title": "Lookup by ref",
            "description": "Lookup description",
            "priority": "MEDIUM",
        },
        headers=headers,
    )
    assert res_create.status_code == 201
    ticket_id = res_create.json()["id"]
    ticket_ref = res_create.json()["reference"]

    # 2. Get by reference
    res_get_ref = client.get(f"/api/v1/tickets/{ticket_ref}", headers=headers)
    assert res_get_ref.status_code == 200
    assert res_get_ref.json()["id"] == ticket_id
    assert res_get_ref.json()["reference"] == ticket_ref


@pytest.mark.asyncio
async def test_tickets_scope_aware_filtering():
    """
    Verify scope-aware filtering and access enforcement for site-scoped users.
    """
    client = TestClient(app)

    # 1. Create tickets with different site scopes
    admin_headers = get_auth_headers(
        roles="admin", change_reason="Create scope tickets"
    )

    # Ticket at Site A
    res_a = client.post(
        "/api/v1/tickets",
        json={"title": "Site A Ticket", "description": "A", "site_id": "SITE-A"},
        headers=admin_headers,
    )
    assert res_a.status_code == 201
    ticket_a_id = res_a.json()["id"]

    # Ticket at Site B
    res_b = client.post(
        "/api/v1/tickets",
        json={"title": "Site B Ticket", "description": "B", "site_id": "SITE-B"},
        headers=admin_headers,
    )
    assert res_b.status_code == 201
    ticket_b_id = res_b.json()["id"]

    # 2. Query as a site-scoped user restricted to Site A (e.g. CRC at Site A)
    # Generate signature with site restriction matching only SITE-A
    timestamp = str(time.time())
    change_reason = "Querying CRC A"
    sig = generate_signature(
        "crc_user",
        "crc",
        timestamp,
        version="2",
        change_reason=change_reason,
        site_id="SITE-A",
    )
    crc_headers = {
        "X-User-Id": "crc_user",
        "X-User-Roles": "crc",
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Site-Id": "SITE-A",
        "X-Change-Reason": change_reason,
    }

    # CRC A lists tickets -> Expect only Site A ticket to be returned
    res_list = client.get("/api/v1/tickets", headers=crc_headers)
    assert res_list.status_code == 200
    tickets = res_list.json()
    assert len(tickets) == 1
    assert tickets[0]["id"] == ticket_a_id

    # CRC A requests Ticket B directly -> Expect 403 Forbidden
    res_get_b = client.get(f"/api/v1/tickets/{ticket_b_id}", headers=crc_headers)
    assert res_get_b.status_code == 403
    assert "Insufficient scope access" in res_get_b.json()["detail"]


@pytest.mark.asyncio
async def test_tickets_validation_invalid_enums():
    """
    Verify that invalid enum parameters or body payloads are rejected with 422 Unprocessable Entity.
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="admin", change_reason="Validation test")

    # 1. Invalid priority in body on creation
    res_create = client.post(
        "/api/v1/tickets",
        json={"title": "Bad enum", "description": "d", "priority": "EXTREME"},
        headers=headers,
    )
    assert res_create.status_code == 422

    # 2. Invalid status in query parameter on listing
    res_list = client.get("/api/v1/tickets?status=SUPER_OPEN", headers=headers)
    assert res_list.status_code == 422


@pytest.mark.asyncio
async def test_tickets_optimistic_locking_and_explicit_endpoints():
    """
    Verify:
    (a) optimistic locking 409 responses for mismatched version indexes,
    (b) 400 responses for invalid state transitions (undeclared),
    (c) blocked modifications on terminal tickets unless reopening, and
    (d) verification of the detailed audit log structure.
    """
    client = TestClient(app)
    headers = get_auth_headers(
        roles="admin", change_reason="Lifecycle transition tests"
    )

    # 1. Create a ticket
    res_create = client.post(
        "/api/v1/tickets",
        json={
            "title": "Locking Test",
            "description": "Lock desc",
            "priority": "MEDIUM",
        },
        headers=headers,
    )
    assert res_create.status_code == 201
    ticket = res_create.json()
    ticket_id = ticket["id"]
    assert ticket["version_index"] == 1

    # (a) Optimistic locking check: mismatch version index should return 409
    res_mismatch = client.put(
        f"/api/v1/tickets/{ticket_id}",
        json={"description": "Stale update", "version_index": 999},
        headers=headers,
    )
    assert res_mismatch.status_code == 409
    assert "Stale version index" in res_mismatch.json()["detail"]

    # (a) Check missing version index returns 409
    res_missing = client.put(
        f"/api/v1/tickets/{ticket_id}",
        json={"description": "Missing version index"},
        headers=headers,
    )
    assert res_missing.status_code == 409
    assert "Missing expected version index" in res_missing.json()["detail"]

    # (b) Reject invalid state transitions
    # Transitioning directly from OPEN to REOPENED is not valid/declared
    res_invalid_transition = client.put(
        f"/api/v1/tickets/{ticket_id}",
        json={"status": "REOPENED", "version_index": 1},
        headers=headers,
    )
    assert res_invalid_transition.status_code == 400
    assert "Invalid transition" in res_invalid_transition.json()["detail"]

    # Check invalid transition on explicit transition endpoint as well
    res_invalid_transition_endpoint = client.post(
        f"/api/v1/tickets/{ticket_id}/transition",
        json={"status": "REOPENED", "version_index": 1},
        headers=headers,
    )
    assert res_invalid_transition_endpoint.status_code == 400
    assert "Invalid transition" in res_invalid_transition_endpoint.json()["detail"]

    # Explicit /assign endpoint check
    res_assign = client.post(
        f"/api/v1/tickets/{ticket_id}/assign",
        json={
            "assignee_user": "bob_developer",
            "assignee_role": "developer",
            "version_index": 1,
        },
        headers=headers,
    )
    assert res_assign.status_code == 200
    ticket = res_assign.json()
    assert ticket["assignee_user"] == "bob_developer"
    assert ticket["assignee_role"] == "developer"
    assert ticket["version_index"] == 2

    # Explicit /transition endpoint check: Transition OPEN -> IN_PROGRESS
    res_transition = client.post(
        f"/api/v1/tickets/{ticket_id}/transition",
        json={"status": "IN_PROGRESS", "version_index": 2},
        headers=headers,
    )
    assert res_transition.status_code == 200
    ticket = res_transition.json()
    assert ticket["status"] == "IN_PROGRESS"
    assert ticket["version_index"] == 3

    # Transition IN_PROGRESS -> CANCELLED (cancelled is terminal)
    res_cancel = client.post(
        f"/api/v1/tickets/{ticket_id}/transition",
        json={"status": "CANCELLED", "version_index": 3},
        headers=headers,
    )
    assert res_cancel.status_code == 200
    ticket = res_cancel.json()
    assert ticket["status"] == "CANCELLED"
    assert ticket["version_index"] == 4

    # (c) Blocked modifications on terminal tickets unless reopening
    # Try to change title of cancelled ticket -> Expect 400
    res_mod_cancelled = client.put(
        f"/api/v1/tickets/{ticket_id}",
        json={"title": "Changed Title", "version_index": 4},
        headers=headers,
    )
    assert res_mod_cancelled.status_code == 400
    assert (
        "Cannot update ticket because it is in terminal state"
        in res_mod_cancelled.json()["detail"]
    )

    # Reopening terminal ticket is allowed (from CANCELLED to REOPENED)
    res_reopen = client.put(
        f"/api/v1/tickets/{ticket_id}",
        json={"status": "REOPENED", "version_index": 4},
        headers=headers,
    )
    assert res_reopen.status_code == 200
    ticket = res_reopen.json()
    assert ticket["status"] == "REOPENED"
    assert ticket["version_index"] == 5

    # Check explicit assignment error when ticket is CLOSED (terminal)
    # First, let's resolve and then close the ticket
    res_resolve = client.post(
        f"/api/v1/tickets/{ticket_id}/transition",
        json={"status": "RESOLVED", "version_index": 5},
        headers=headers,
    )
    assert res_resolve.status_code == 200
    res_close = client.post(
        f"/api/v1/tickets/{ticket_id}/transition",
        json={"status": "CLOSED", "version_index": 6},
        headers=headers,
    )
    assert res_close.status_code == 200

    # Try assigning closed ticket -> Expect 400
    res_assign_closed = client.post(
        f"/api/v1/tickets/{ticket_id}/assign",
        json={"assignee_user": "alice", "version_index": 7},
        headers=headers,
    )
    assert res_assign_closed.status_code == 400
    assert "terminal state" in res_assign_closed.json()["detail"]

    # (d) Verification of the detailed audit log structure
    # Fetch audit logs for this ticket
    res_audit = client.get(
        f"/api/v1/tickets/audit-logs?ticket_id={ticket_id}", headers=headers
    )
    assert res_audit.status_code == 200
    logs = res_audit.json()
    # Find TICKET_ASSIGN log
    assign_log = next((log for log in logs if log["action"] == "TICKET_ASSIGN"), None)
    assert assign_log is not None
    assert assign_log["created_by"] == "tickets_test_user"
    assert "Actor:" in assign_log["details"]
    assert "Roles:" in assign_log["details"]
    assert "Source State:" in assign_log["details"]
    assert "Target State:" in assign_log["details"]
    assert "Assignment Changes:" in assign_log["details"]
    assert "Reason:" in assign_log["details"]
    assert "assignee_user:" in assign_log["details"]
    assert "assignee_role:" in assign_log["details"]

    # Find TICKET_TRANSITION log
    transition_logs = [log for log in logs if log["action"] == "TICKET_TRANSITION"]
    assert len(transition_logs) > 0
    assert any(
        "Source State: 'OPEN', Target State: 'IN_PROGRESS'" in log["details"]
        for log in transition_logs
    )
