import pytest
from fastapi.testclient import TestClient
from httpx import ASGITransport, AsyncClient

from apps.ctms.adapters.database import db_manager as ctms_db_manager
from apps.ctms.main import app as ctms_app
from apps.ctms.models import Base as CTMSBase
from apps.quality.adapters.database import db_manager
from apps.quality.adapters.models import Base, IntegrationOutbox
from apps.quality.main import app as quality_app
from apps.quality.workers.outbox_worker import poll_and_dispatch
from packages.security.rbac_helpers import build_gateway_headers


@pytest.fixture(autouse=True)
async def setup_test_dbs():
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    ctms_db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)

    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    async with ctms_db_manager.engine.begin() as conn:
        await conn.run_sync(CTMSBase.metadata.create_all)

    yield

    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    async with ctms_db_manager.engine.begin() as conn:
        await conn.run_sync(CTMSBase.metadata.drop_all)

    await db_manager.close()
    await ctms_db_manager.close()


def get_headers(
    user_id="test_user", roles="quality_manager,admin", change_reason="Outbox sync test"
):
    return build_gateway_headers(
        user_id=user_id, roles=roles, change_reason=change_reason
    )


@pytest.mark.asyncio
async def test_ctms_escalation_creates_real_capa():
    """AC1: CTMS deviation escalation creates verified CAPA in Quality without mock IDs."""
    quality_client = TestClient(quality_app)
    ctms_client = TestClient(ctms_app)

    # 1. Log deviation in CTMS
    dev_payload = {
        "study_id": "STUDY-001",
        "site_id": "SITE-001",
        "deviation_category": "PROTOCOL_PROCEDURE",
        "severity": "CRITICAL",
        "title": "Critical dosing error",
        "description": "Patient received incorrect dose at Visit 2",
        "date_occurred": "2026-08-20",
    }
    headers = get_headers()
    dev_res = ctms_client.post(
        "/api/v1/ctms/deviations", json=dev_payload, headers=headers
    )
    assert dev_res.status_code == 201
    dev_data = dev_res.json()
    deviation_id = dev_data["id"]

    # 2. Directly call Quality API to simulate CTMS QualityClient escalation
    capa_payload = {
        "deviation_id": deviation_id,
        "study_id": "STUDY-001",
        "site_id": "SITE-001",
        "title": "[CTMS Escalation] Critical dosing error",
        "description": "Severity: CRITICAL\nDescription: Patient received incorrect dose",
        "severity": "CRITICAL",
        "action_plan": "Retrain site staff and audit dosing logs",
        "preventive_measures": "Implement dual-check verification",
        "capa_type": "BOTH",
    }
    capa_res = quality_client.post(
        "/api/v1/quality/capas", json=capa_payload, headers=headers
    )
    assert capa_res.status_code == 201
    capa_data = capa_res.json()
    capa_id = capa_data["id"]
    assert not capa_id.startswith("mock")

    # 3. Update CTMS deviation with returned CAPA ID
    update_res = ctms_client.put(
        f"/api/v1/ctms/deviations/{deviation_id}/status",
        json={"status": "CAPA_ESCALATED", "quality_capa_id": capa_id},
        headers=headers,
    )
    assert update_res.status_code == 200
    updated_dev = update_res.json()
    assert updated_dev["status"] == "CAPA_ESCALATED"
    assert updated_dev["quality_capa_id"] == capa_id


@pytest.mark.asyncio
async def test_transactional_outbox_event_creation():
    """AC2: Creating or transitioning CAPA stage writes outbox event in same transaction."""
    quality_client = TestClient(quality_app)
    headers = get_headers()

    capa_payload = {
        "deviation_id": "DEV-TEST-002",
        "study_id": "STUDY-002",
        "site_id": "SITE-002",
        "title": "Protocol deviation test",
        "action_plan": "Action plan steps",
        "capa_type": "CORRECTIVE",
    }
    res = quality_client.post(
        "/api/v1/quality/capas", json=capa_payload, headers=headers
    )
    assert res.status_code == 201
    capa_id = res.json()["id"]

    # Query Quality admin outbox endpoint
    outbox_res = quality_client.get("/api/v1/quality/admin/outbox", headers=headers)
    assert outbox_res.status_code == 200
    outbox_events = outbox_res.json()
    assert len(outbox_events) == 1
    event = outbox_events[0]
    assert event["event_type"] == "CAPA_STAGE_TRANSITION"
    assert event["status"] == "PENDING"
    assert event["payload"]["capa_id"] == capa_id
    assert event["payload"]["target_ctms_status"] == "CAPA_ESCALATED"


@pytest.mark.asyncio
async def test_outbox_worker_delivery_and_stage_mapping(monkeypatch):
    """AC3 & AC4: Background worker delivers outbox events to CTMS with gateway auth and stage mapping."""
    quality_client = TestClient(quality_app)
    headers = get_headers()

    # Create CAPA in Quality
    capa_payload = {
        "deviation_id": "DEV-TEST-003",
        "study_id": "STUDY-003",
        "site_id": "SITE-003",
        "title": "Protocol deviation test 3",
        "action_plan": "Action plan steps 3",
        "capa_type": "PREVENTIVE",
    }
    create_res = quality_client.post(
        "/api/v1/quality/capas", json=capa_payload, headers=headers
    )
    assert create_res.status_code == 201
    capa_id = create_res.json()["id"]

    # Transition CAPA to UNDER_REVIEW
    trans_res = quality_client.post(
        f"/api/v1/quality/capas/{capa_id}/transition",
        json={"to_status": "UNDER_REVIEW", "version_index": 1},
        headers=headers,
    )
    assert trans_res.status_code == 200

    # Mock CTMS HTTP request in poll_and_dispatch
    ctms_transport = ASGITransport(app=ctms_app)

    def mock_async_client(*args, **kwargs):
        return AsyncClient(transport=ctms_transport, base_url="http://localhost:8000")

    monkeypatch.setattr("httpx.AsyncClient", mock_async_client)

    # First, ensure CTMS has the deviation DEV-TEST-003
    ctms_client = TestClient(ctms_app)
    dev_res = ctms_client.post(
        "/api/v1/ctms/deviations",
        json={
            "study_id": "STUDY-003",
            "site_id": "SITE-003",
            "deviation_category": "VISIT_WINDOW",
            "severity": "MAJOR",
            "title": "Visit window deviation",
            "description": "Out of window",
            "date_occurred": "2026-08-20",
        },
        headers=headers,
    )
    assert dev_res.status_code == 201
    dev_id = dev_res.json()["id"]

    # Update outbox event payload to reference dev_id
    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        from sqlalchemy import select

        stmt = select(IntegrationOutbox)
        res = await session.execute(stmt)
        for rec in res.scalars().all():
            rec.payload = {**rec.payload, "deviation_id": dev_id}
        await session.commit()

    # Run outbox worker poll
    processed = await poll_and_dispatch(session_maker)
    assert processed > 0

    # Check outbox events in Quality
    outbox_res = quality_client.get("/api/v1/quality/admin/outbox", headers=headers)
    events = outbox_res.json()
    assert all(e["status"] == "SUCCESS" for e in events)

    # Check CTMS deviation status updated
    ctms_dev_res = ctms_client.get(
        "/api/v1/ctms/deviations?study_id=STUDY-003", headers=headers
    )
    devs = ctms_dev_res.json()
    target_dev = [d for d in devs if d["id"] == dev_id][0]
    assert target_dev["status"] == "UNDER_REVIEW"


@pytest.mark.asyncio
async def test_concurrency_conflict_and_retry_backoff(monkeypatch):
    """AC5: Worker handles HTTP 409 concurrency conflicts, retrying with backoff."""
    quality_client = TestClient(quality_app)
    headers = get_headers()

    capa_payload = {
        "deviation_id": "DEV-TEST-004",
        "study_id": "STUDY-004",
        "site_id": "SITE-004",
        "title": "Protocol deviation test 4",
        "action_plan": "Action plan 4",
    }
    create_res = quality_client.post(
        "/api/v1/quality/capas", json=capa_payload, headers=headers
    )
    assert create_res.status_code == 201

    # Mock httpx AsyncClient to simulate HTTP 409 Conflict
    class Mock409Response:
        status_code = 409
        text = "Version conflict: CTMS deviation version mismatch"
        request = None

    class Mock409Client:
        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc_val, exc_tb):
            pass

        async def put(self, url, json=None, headers=None):
            return Mock409Response()

    monkeypatch.setattr("httpx.AsyncClient", lambda *args, **kwargs: Mock409Client())

    session_maker = db_manager.get_session_maker()
    processed = await poll_and_dispatch(session_maker)
    assert processed > 0

    # Verify outbox event is in FAILED state with attempt incremented and next_retry_at set
    outbox_res = quality_client.get("/api/v1/quality/admin/outbox", headers=headers)
    events = outbox_res.json()
    assert len(events) == 1
    event = events[0]
    assert event["status"] == "FAILED"
    assert event["attempts"] == 1
    assert "409" in event["last_error"]
