import os
import time
from unittest.mock import patch

import httpx
import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.ctms.database import db_manager as ctms_db_manager
from apps.ctms.main import app as ctms_app
from apps.execution.database.core import db_manager as exec_db_manager
from apps.execution.database.models import Base as ExecBase
from apps.execution.database.models import ResupplyEvent
from apps.execution.main import app as exec_app
from packages.security.signing import generate_gateway_signature


@pytest_asyncio.fixture(autouse=True)
async def setup_databases():
    """Initializes clean CTMS and Execution SQLite test databases."""
    from apps.execution.database.migrate import deploy_database_triggers

    # Initialize execution DB
    exec_db_manager.init_db(
        os.getenv("TEST_DATABASE_URL", "sqlite+aiosqlite:///:memory:"),
        echo=False,
    )
    async with exec_db_manager.engine.begin() as conn:
        from sqlalchemy import text

        if exec_db_manager.engine.dialect.name == "postgresql":
            await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))
        await conn.run_sync(ExecBase.metadata.create_all)
        await deploy_database_triggers(conn, exec_db_manager.engine.dialect.name)

    # Initialize CTMS DB
    ctms_db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    from apps.ctms.models import Base as CtmsBase

    async with ctms_db_manager.engine.begin() as conn:
        await conn.run_sync(CtmsBase.metadata.create_all)

    yield

    # Clean up CTMS DB
    async with ctms_db_manager.engine.begin() as conn:
        await conn.run_sync(CtmsBase.metadata.drop_all)
    await ctms_db_manager.close()

    # Clean up execution DB
    async with exec_db_manager.engine.begin() as conn:
        await conn.run_sync(ExecBase.metadata.drop_all)
    await exec_db_manager.close()


def get_auth_headers_v2(
    user_id: str = "test_user",
    roles: str = "CRA",
    change_reason: str = "Test resupply operations",
    site_id: str | None = None,
) -> dict:
    """Generate signed gateway V2 headers using standard signing."""
    timestamp = str(time.time())
    secret = b"internal-gateway-secret-12345"
    sig = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=secret,
        change_reason=change_reason,
        site_id=site_id,
    )
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }
    if site_id:
        headers["X-Site-Id"] = site_id
    return headers


# =====================================================================
# DOWNSTREAM EXECUTION SERVICE TESTS
# =====================================================================


@pytest.mark.asyncio
async def test_execution_list_resupply_events():
    """Verify that execution listing returns events filtered by site access."""
    # Seed execution database with some resupply events
    async with exec_db_manager.get_session_maker()() as session:
        event1 = ResupplyEvent(
            study_id="STUDY_1",
            site_id="SITE_A",
            kit_id="KIT_1",
            requested_qty=20,
            status="PENDING",
        )
        event2 = ResupplyEvent(
            study_id="STUDY_1",
            site_id="SITE_B",
            kit_id="KIT_2",
            requested_qty=20,
            status="PENDING",
        )
        session.add_all([event1, event2])
        await session.commit()

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=exec_app), base_url="http://test"
    ) as client:
        # 1. Sponsor Admin has global access and sees all events
        headers_sa = get_auth_headers_v2(roles="Sponsor Admin")
        response = await client.get(
            "/api/v1/execution/rtsm/resupply-events", headers=headers_sa
        )
        assert response.status_code == 200
        events = response.json()
        assert len(events) == 2

        # 2. CRA assigned to SITE_A only sees SITE_A event
        headers_cra = get_auth_headers_v2(roles="CRA", site_id="SITE_A")
        response = await client.get(
            "/api/v1/execution/rtsm/resupply-events", headers=headers_cra
        )
        assert response.status_code == 200
        events_cra = response.json()
        assert len(events_cra) == 1
        assert events_cra[0]["site_id"] == "SITE_A"

        # 3. User with no roles gets 401 or 403
        headers_none = get_auth_headers_v2(roles="")
        response = await client.get(
            "/api/v1/execution/rtsm/resupply-events", headers=headers_none
        )
        assert response.status_code in (401, 403)


@pytest.mark.asyncio
async def test_execution_approve_reject_resupply_events():
    """Verify that execution approve and reject works correctly with permissions."""
    async with exec_db_manager.get_session_maker()() as session:
        event = ResupplyEvent(
            study_id="STUDY_1",
            site_id="SITE_A",
            kit_id="KIT_1",
            requested_qty=20,
            status="PENDING",
        )
        session.add(event)
        await session.commit()
        event_id = event.id

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=exec_app), base_url="http://test"
    ) as client:
        # 1. Approve without change justification payload is 400
        headers_sa = get_auth_headers_v2(roles="Sponsor Admin")
        response = await client.post(
            f"/api/v1/execution/rtsm/resupply-events/{event_id}/confirm",
            headers=headers_sa,
            json={"change_justification": ""},
        )
        assert response.status_code == 400

        # 2. Approve with unauthorized role (e.g. ClinicalResearchCoordinator / read-only) is 403
        headers_crc = get_auth_headers_v2(
            roles="ClinicalResearchCoordinator", site_id="SITE_A"
        )
        response = await client.post(
            f"/api/v1/execution/rtsm/resupply-events/{event_id}/confirm",
            headers=headers_crc,
            json={"change_justification": "Approved stock replenishment"},
        )
        assert response.status_code == 403

        # 3. Approve with unassigned site scope is 403
        headers_cra_wrong = get_auth_headers_v2(roles="CRA", site_id="SITE_B")
        response = await client.post(
            f"/api/v1/execution/rtsm/resupply-events/{event_id}/confirm",
            headers=headers_cra_wrong,
            json={"change_justification": "Approved stock replenishment"},
        )
        assert response.status_code == 403

        # 4. Successful approval by Sponsor Admin
        response = await client.post(
            f"/api/v1/execution/rtsm/resupply-events/{event_id}/confirm",
            headers=headers_sa,
            json={"change_justification": "Approved stock replenishment"},
        )
        assert response.status_code == 200
        assert response.json()["status"] == "APPROVED"

        # Verify DB is updated
        async with exec_db_manager.get_session_maker()() as session:
            db_event = (
                await session.execute(
                    select(ResupplyEvent).where(ResupplyEvent.id == event_id)
                )
            ).scalar_one()
            assert db_event.status == "APPROVED"


# =====================================================================
# CTMS GATEWAY PROXY TESTS
# =====================================================================


@pytest.mark.asyncio
async def test_ctms_list_resupply_events_success():
    """Verify that CTMS queries execution list and returns correctly."""
    mock_events = [
        {
            "id": "event_1",
            "study_id": "STUDY_1",
            "site_id": "SITE_A",
            "kit_id": "KIT_1",
            "requested_qty": 20,
            "status": "PENDING",
            "triggered_at": "2026-08-13T12:00:00",
        }
    ]

    # Mock the GatewayBaseClient.request call with a real httpx.Response object
    mock_response = httpx.Response(200, json=mock_events)
    with patch(
        "packages.security.gateway_client.GatewayBaseClient.request",
        return_value=mock_response,
    ) as mock_request:
        # Request to CTMS router
        client = TestClient(ctms_app)
        headers = get_auth_headers_v2(roles="CRA", site_id="SITE_A")
        response = client.get(
            "/api/v1/ctms/resupply-events?site_id=SITE_A",
            headers=headers,
        )

        assert response.status_code == 200
        data = response.json()
        assert len(data) == 1
        assert data[0]["id"] == "event_1"
        assert data[0]["site_id"] == "SITE_A"

        # Verify that GatewayBaseClient was called with system credentials/headers mapped
        mock_request.assert_called_once()


@pytest.mark.asyncio
async def test_ctms_list_resupply_events_blocked_read_only():
    """Verify that CTMS blocks unauthorized or read-only users."""
    client = TestClient(ctms_app)
    headers = get_auth_headers_v2(roles="Auditor")  # Read-only role
    response = client.get(
        "/api/v1/ctms/resupply-events",
        headers=headers,
    )
    assert response.status_code == 403


@pytest.mark.asyncio
async def test_ctms_approve_unreachable_downstream():
    """Verify that CTMS returns clear error 502 if downstream execution is unreachable."""
    # Mock the GatewayBaseClient.request to raise ConnectionError
    with patch(
        "packages.security.gateway_client.GatewayBaseClient.request",
        side_effect=httpx.RequestError("Connection refused"),
    ):
        client = TestClient(ctms_app)
        headers = get_auth_headers_v2(roles="CRA", site_id="SITE_A")
        response = client.post(
            "/api/v1/ctms/resupply-events/event_1/confirm",
            headers=headers,
            json={"change_justification": "Replenish stock for study"},
        )
        assert response.status_code == 502
        assert (
            "Failed to communicate with downstream execution service"
            in response.json()["detail"]
        )


@pytest.mark.asyncio
async def test_ctms_approve_unassigned_site():
    """Verify that CTMS blocks approval if user does not have access to the event's site."""
    mock_events = [
        {
            "id": "event_1",
            "study_id": "STUDY_1",
            "site_id": "SITE_B",  # Belong to SITE_B
            "kit_id": "KIT_1",
            "requested_qty": 20,
            "status": "PENDING",
            "triggered_at": "2026-08-13T12:00:00",
        }
    ]

    mock_response = httpx.Response(200, json=mock_events)
    with patch(
        "packages.security.gateway_client.GatewayBaseClient.request",
        return_value=mock_response,
    ):
        client = TestClient(ctms_app)
        headers = get_auth_headers_v2(
            roles="CRA", site_id="SITE_A"
        )  # Assigned to SITE_A only
        response = client.post(
            "/api/v1/ctms/resupply-events/event_1/confirm",
            headers=headers,
            json={"change_justification": "Replenish stock"},
        )
        assert response.status_code == 403
        assert "Forbidden: Access denied" in response.json()["detail"]
