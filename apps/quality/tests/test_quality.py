import time
from datetime import datetime

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select
from sqlalchemy.exc import IntegrityError
from sqlalchemy.orm import selectinload

from apps.gateway.main import generate_signature
from apps.quality.database import db_manager
from apps.quality.main import app, write_audit_log
from apps.quality.models import (
    Base,
    CAPARecord,
    CAPAStatus,
    Deviation,
    DeviationSeverity,
    DeviationStatus,
    DeviationType,
    QualityAuditLog,
    RootCauseAnalysis,
)


def make_step_up_token(
    user_id: str,
    action: str,
    semantic_action: str,
    secret: str = "internal-gateway-secret-12345",
    expired: bool = False,
    wrong_user: bool = False,
    wrong_action: bool = False,
    wrong_semantic: bool = False,
) -> str:
    import time

    from jose import jwt

    now = time.time()
    payload = {
        "sub": "wrong_user" if wrong_user else user_id,
        "username": "test_signer",
        "action": "/api/v1/wrong_path" if wrong_action else action,
        "roles": ["admin"],
        "iat": now - 100 if expired else now,
        "exp": now - 40 if expired else now + 60,
        "jti": f"jti_test_{user_id}_{now}",
        "semantic_action": "wrong_semantic" if wrong_semantic else semantic_action,
        "sig_ver": "v3",
    }
    return jwt.encode(payload, secret, algorithm="HS256")


@pytest_asyncio.fixture(autouse=True)
async def setup_quality_db():
    """
    Setup in-memory Quality database for unit and integration testing.
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
    user_id = "quality_test_user"
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


def test_quality_health_check():
    """
    Verify health check of independent Quality & CAPA service.
    """
    client = TestClient(app)
    response = client.get("/health")
    assert response.status_code == 200
    data = response.json()
    assert data["status"] == "ok"
    assert data["service"] == "quality"


@pytest.mark.asyncio
async def test_quality_database_schema_creation():
    """
    Verify that all four quality tables can be created and queried successfully.
    """
    async with db_manager.get_session_maker()() as session:
        # Querying each table using empty selects to ensure they exist and have columns defined correctly
        deviations = await session.execute(select(Deviation))
        rcas = await session.execute(select(RootCauseAnalysis))
        capas = await session.execute(select(CAPARecord))
        logs = await session.execute(select(QualityAuditLog))

        assert deviations.scalars().all() == []
        assert rcas.scalars().all() == []
        assert capas.scalars().all() == []
        assert logs.scalars().all() == []


@pytest.mark.asyncio
async def test_deviation_lifecycle_and_traceability_fields():
    """
    Verify that a Deviation can be persisted with string-backed enums and all mandatory traceability fields.
    """
    async with db_manager.get_session_maker()() as session:
        dev = Deviation(
            study_id="study_999",
            site_id="site_111",
            title="Informed Consent Missing Signature Date",
            description="Subject 002 signed consent but did not date it.",
            severity=DeviationSeverity.MAJOR,
            status=DeviationStatus.REPORTED,
            type=DeviationType.INFORMED_CONSENT,
            is_protocol_violation=True,
            created_by="auditor_jane",
            version_index=1,
            reason_for_change="Initial reporting of informed consent deviation.",
        )
        session.add(dev)
        await session.commit()

    async with db_manager.get_session_maker()() as session:
        stmt = select(Deviation).where(Deviation.study_id == "study_999")
        result = await session.execute(stmt)
        retrieved = result.scalar_one()

        assert retrieved.id is not None
        assert retrieved.site_id == "site_111"
        assert retrieved.title == "Informed Consent Missing Signature Date"
        assert retrieved.severity == DeviationSeverity.MAJOR
        # Ensure enums persist and compare properly
        assert retrieved.status == DeviationStatus.REPORTED
        assert retrieved.type == DeviationType.INFORMED_CONSENT
        assert retrieved.is_protocol_violation is True
        assert retrieved.created_by == "auditor_jane"
        assert retrieved.version_index == 1
        assert (
            retrieved.reason_for_change
            == "Initial reporting of informed consent deviation."
        )
        assert isinstance(retrieved.created_at, datetime)


@pytest.mark.asyncio
async def test_deviation_rca_capa_relationships_and_cascading():
    """
    Verify the relationships and Cascade constraints (SQLite foreign-key enforcement is enabled).
    - Deviation (1) -> (0..1) RootCauseAnalysis
    - Deviation (1) -> (0..N) CAPARecord
    - RootCauseAnalysis (1) -> (0..N) CAPARecord
    """
    async with db_manager.get_session_maker()() as session:
        # 1. Create Deviation
        dev = Deviation(
            study_id="study_999",
            site_id="site_111",
            title="Temperature Excursion",
            description="Investigational product stored at 15C instead of 2-8C.",
            severity=DeviationSeverity.CRITICAL,
            status=DeviationStatus.UNDER_INVESTIGATION,
            type=DeviationType.IP_MANAGEMENT,
            is_protocol_violation=False,
            created_by="cra_bob",
            version_index=1,
            reason_for_change="IP storage excursion.",
        )
        session.add(dev)
        await session.flush()

        # 2. Create RCA linked to Deviation
        rca = RootCauseAnalysis(
            deviation_id=dev.id,
            methodology="5 Whys",
            investigation_details="Power outage -> Backup generator failed to start -> Temp rose -> Monitored after 6 hours.",
            root_cause_summary="Generator maintenance oversight.",
            study_id="study_999",
            site_id="site_111",
            created_by="cra_bob",
            version_index=1,
            reason_for_change="RCA investigation complete.",
        )
        session.add(rca)
        await session.flush()

        # 3. Create CAPA Record linked to Deviation and RCA
        capa = CAPARecord(
            deviation_id=dev.id,
            rca_id=rca.id,
            capa_type="PREVENTIVE",
            action_plan="Perform weekly generator testing and sign-offs.",
            status=CAPAStatus.INITIATED,
            preventive_measures="Update SOP-QA-09 for facilities monitoring.",
            target_completion_date=datetime(2026, 12, 31),
            study_id="study_999",
            site_id="site_111",
            created_by="cra_bob",
            version_index=1,
            reason_for_change="Initiate preventive action plan.",
        )
        session.add(capa)
        await session.commit()

    # Verify relationships are loaded correctly using selectinload
    async with db_manager.get_session_maker()() as session:
        stmt = (
            select(Deviation)
            .where(Deviation.study_id == "study_999")
            .options(
                selectinload(Deviation.root_cause_analysis),
                selectinload(Deviation.capa_records).selectinload(CAPARecord.rca),
            )
        )
        result = await session.execute(stmt)
        retrieved_dev = result.scalar_one()

        assert retrieved_dev.root_cause_analysis is not None
        assert retrieved_dev.root_cause_analysis.methodology == "5 Whys"
        assert len(retrieved_dev.capa_records) == 1
        assert retrieved_dev.capa_records[0].capa_type == "PREVENTIVE"
        assert retrieved_dev.capa_records[0].rca is not None
        assert (
            retrieved_dev.capa_records[0].rca.id == retrieved_dev.root_cause_analysis.id
        )

    # Verify cascading deletes (on deleting Deviation, RCA and CAPAs are deleted)
    async with db_manager.get_session_maker()() as session:
        stmt = select(Deviation).where(Deviation.study_id == "study_999")
        result = await session.execute(stmt)
        retrieved_dev = result.scalar_one()

        await session.delete(retrieved_dev)
        await session.commit()

    async with db_manager.get_session_maker()() as session:
        # Verify RCA and CAPA are deleted due to CASCADE
        deviations = await session.execute(select(Deviation))
        rcas = await session.execute(select(RootCauseAnalysis))
        capas = await session.execute(select(CAPARecord))

        assert len(deviations.scalars().all()) == 0
        assert len(rcas.scalars().all()) == 0
        assert len(capas.scalars().all()) == 0


@pytest.mark.asyncio
async def test_sqlite_foreign_key_constraints():
    """
    Ensure that SQLite foreign key constraint is enforced and throws IntegrityError on invalid ForeignKey.
    """
    async with db_manager.get_session_maker()() as session:
        rca = RootCauseAnalysis(
            deviation_id="non-existent-deviation-id",
            methodology="5 Whys",
            investigation_details="No investigation",
            root_cause_summary="None",
            study_id="study_001",
            created_by="test",
            version_index=1,
            reason_for_change="Invalid RCA",
        )
        session.add(rca)
        with pytest.raises(IntegrityError):
            await session.commit()


@pytest.mark.asyncio
async def test_quality_audit_log_append_only():
    """
    Verify writing to the QualityAuditLog as an append-only ledger.
    """
    async with db_manager.get_session_maker()() as session:
        await write_audit_log(
            session=session,
            user_id="user_admin",
            user_role="Admin",
            action="DEVIATION_CREATE",
            details="Created deviation event for study_abc",
        )
        await session.commit()

    async with db_manager.get_session_maker()() as session:
        stmt = select(QualityAuditLog).order_by(QualityAuditLog.timestamp.desc())
        result = await session.execute(stmt)
        logs = result.scalars().all()

        assert len(logs) == 1
        log = logs[0]
        assert log.user_id == "user_admin"
        assert log.user_role == "Admin"
        assert log.action == "DEVIATION_CREATE"
        assert log.details == "Created deviation event for study_abc"
        assert isinstance(log.timestamp, datetime)


@pytest.mark.asyncio
async def test_database_manager_uninitialized_raises_exception():
    """
    Verify that QualityDatabaseManager raises an exception if get_session_maker is called before init_db.
    """
    from packages.database import RelationalDatabaseManager

    uninit_manager = RelationalDatabaseManager(service_name="Quality")
    with pytest.raises(Exception) as exc_info:
        uninit_manager.get_session_maker()
    assert "not initialized" in str(exc_info.value)


def test_lifespan_coverage():
    """
    Exercise the FastAPI app lifespan startup and shutdown context manager.
    """
    with TestClient(app) as client:
        response = client.get("/health")
        assert response.status_code == 200


@pytest.mark.asyncio
async def test_sqlite_pragma_exception_handling():
    """
    Ensure that any exception raised during the sqlite PRAGMA setting is caught and handled.
    """
    from unittest.mock import MagicMock

    from packages.database import RelationalDatabaseManager

    local_db_mgr = RelationalDatabaseManager(service_name="Quality")
    local_db_mgr.init_db("sqlite+aiosqlite:///:memory:")

    # Find the set_sqlite_pragma listener in local_db_mgr.engine.sync_engine.pool.dispatch.connect
    pragma_listener = None
    for listener in local_db_mgr.engine.sync_engine.pool.dispatch.connect:
        if (
            "set_sqlite_pragma" in str(listener)
            or getattr(listener, "__name__", None) == "set_sqlite_pragma"
        ):
            pragma_listener = listener
            break

    assert pragma_listener is not None

    # Create a mock dbapi_connection that raises an Exception on cursor or execute
    mock_cursor = MagicMock()
    mock_cursor.execute.side_effect = Exception("Mock execute exception for PRAGMA")
    mock_dbapi_conn = MagicMock()
    mock_dbapi_conn.cursor.return_value = mock_cursor

    # Call the listener function directly to exercise the try-except-finally block
    pragma_listener(mock_dbapi_conn, None)

    # Ensure cursor.close() was called in the finally block
    mock_cursor.close.assert_called_once()

    await local_db_mgr.close()


def test_missing_change_reasons_unauthorized():
    """
    Ensure all mutation paths return 403 when X-Change-Reason is missing or empty.
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="admin")
    # No X-Change-Reason header included!

    # 1. Deviation creation
    payload = {
        "study_id": "study_123",
        "title": "Missing reason test",
        "description": "desc",
        "severity": "MINOR",
        "type": "OTHER",
    }
    res = client.post("/api/v1/quality/deviations", json=payload, headers=headers)
    assert res.status_code == 403
    assert "Missing change justification reason" in res.json()["detail"]

    # Let's create a valid deviation to use its ID for other checks
    valid_headers = get_auth_headers(roles="admin", change_reason="Let us set up base")
    dev_res = client.post(
        "/api/v1/quality/deviations", json=payload, headers=valid_headers
    )
    assert dev_res.status_code == 201
    dev_id = dev_res.json()["id"]

    # 2. RCA creation
    rca_payload = {
        "methodology": "5 Whys",
        "investigation_details": "details",
        "root_cause_summary": "summary",
    }
    res = client.post(
        f"/api/v1/quality/deviations/{dev_id}/rca", json=rca_payload, headers=headers
    )
    assert res.status_code == 403
    assert "Missing change justification reason" in res.json()["detail"]

    # 3. CAPA creation
    capa_payload = {
        "deviation_id": dev_id,
        "capa_type": "CORRECTIVE",
        "action_plan": "plan",
    }
    res = client.post("/api/v1/quality/capas", json=capa_payload, headers=headers)
    assert res.status_code == 403
    assert "Missing change justification reason" in res.json()["detail"]

    # Let's create a valid CAPA
    capa_res = client.post(
        "/api/v1/quality/capas", json=capa_payload, headers=valid_headers
    )
    assert capa_res.status_code == 201
    capa_id = capa_res.json()["id"]

    # 4. CAPA transition
    trans_payload = {"to_status": "UNDER_REVIEW"}
    res = client.post(
        f"/api/v1/quality/capas/{capa_id}/transition",
        json=trans_payload,
        headers=headers,
    )
    assert res.status_code == 403
    assert "Missing change justification reason" in res.json()["detail"]

    # 5. CAPA update
    update_payload = {"action_plan": "new plan"}
    res = client.put(
        f"/api/v1/quality/capas/{capa_id}", json=update_payload, headers=headers
    )
    assert res.status_code == 403
    assert "Missing change justification reason" in res.json()["detail"]


def test_deviation_not_found_404():
    """
    Ensure view deviation and RCA create/update return 404 when deviation doesn't exist.
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="admin", change_reason="Searching nonexistent")

    # View deviation
    res = client.get("/api/v1/quality/deviations/nonexistent_id", headers=headers)
    assert res.status_code == 404
    assert "Deviation not found" in res.json()["detail"]

    # Create RCA for nonexistent deviation
    rca_payload = {
        "methodology": "5 Whys",
        "investigation_details": "details",
        "root_cause_summary": "summary",
    }
    res = client.post(
        "/api/v1/quality/deviations/nonexistent_id/rca",
        json=rca_payload,
        headers=headers,
    )
    assert res.status_code == 404
    assert "Parent deviation not found" in res.json()["detail"]


def test_list_deviations_filters():
    """
    Verify listing filter logic when site_id and status are supplied.
    """
    client = TestClient(app)
    headers = get_auth_headers(
        roles="admin", change_reason="Creating filterable deviations"
    )

    # Setup deviations
    dev_payload = {
        "study_id": "study_filter",
        "site_id": "site_99",
        "title": "Filtered Dev",
        "description": "desc",
        "severity": "MINOR",
        "type": "OTHER",
    }
    client.post("/api/v1/quality/deviations", json=dev_payload, headers=headers)

    # Filter with site_id and status
    res = client.get(
        "/api/v1/quality/deviations?study_id=study_filter&site_id=site_99&status=REPORTED",
        headers=headers,
    )
    assert res.status_code == 200
    data = res.json()
    assert len(data) == 1
    assert data[0]["site_id"] == "site_99"
    assert data[0]["status"] == "REPORTED"


def test_capa_creation_validations_and_closed_deviation():
    """
    Verify that CAPA creation fails with 422 if deviation is in CLOSED/RESOLVED status,
    or if RCA ID is mismatched with the deviation.
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="admin", change_reason="Closed deviation testing")

    # 1. Create deviation and CAPA
    dev_payload = {
        "study_id": "study_123",
        "title": "Base deviation",
        "description": "desc",
        "severity": "MAJOR",
        "type": "INFORMED_CONSENT",
    }
    dev_res = client.post(
        "/api/v1/quality/deviations", json=dev_payload, headers=headers
    )
    dev_id = dev_res.json()["id"]

    capa_payload = {
        "deviation_id": dev_id,
        "capa_type": "CORRECTIVE",
        "action_plan": "Action",
    }
    capa_res = client.post("/api/v1/quality/capas", json=capa_payload, headers=headers)
    capa_id = capa_res.json()["id"]

    # Transition CAPA to CLOSED via quality oversight role
    qo_headers = get_auth_headers(roles="quality_manager", change_reason="Closing CAPA")
    client.post(
        f"/api/v1/quality/capas/{capa_id}/transition",
        json={"to_status": "UNDER_REVIEW", "version_index": 1},
        headers=qo_headers,
    )
    client.post(
        f"/api/v1/quality/capas/{capa_id}/transition",
        json={"to_status": "IMPLEMENTATION", "version_index": 2},
        headers=qo_headers,
    )
    client.post(
        f"/api/v1/quality/capas/{capa_id}/transition",
        json={"to_status": "EFFECTIVENESS_CHECK", "version_index": 3},
        headers=qo_headers,
    )
    action_path = f"/api/v1/quality/capas/{capa_id}/transition"
    sig_token = make_step_up_token(
        user_id="quality_test_user",
        action=action_path,
        semantic_action="quality.capa.close",
    )
    qo_headers_gated = qo_headers.copy()
    qo_headers_gated["X-Sig-Token"] = sig_token
    client.post(
        f"/api/v1/quality/capas/{capa_id}/transition",
        json={"to_status": "CLOSED", "version_index": 4},
        headers=qo_headers_gated,
    )

    # Now the parent deviation is CLOSED. Let's try creating a new CAPA on this CLOSED deviation
    res_closed = client.post(
        "/api/v1/quality/capas", json=capa_payload, headers=headers
    )
    assert res_closed.status_code == 422
    assert (
        "Cannot create CAPA for a settled or closed deviation"
        in res_closed.json()["detail"]
    )

    # 2. RCA Mismatch check: Create a second deviation
    dev2_res = client.post(
        "/api/v1/quality/deviations", json=dev_payload, headers=headers
    )
    dev2_id = dev2_res.json()["id"]

    # Create RCA for deviation 1
    rca_payload = {
        "methodology": "5 Whys",
        "investigation_details": "details",
        "root_cause_summary": "summary",
    }
    rca_res = client.post(
        f"/api/v1/quality/deviations/{dev_id}/rca", json=rca_payload, headers=headers
    )
    rca_id = rca_res.json()["id"]

    # Try creating CAPA for deviation 2 but linking it to RCA of deviation 1
    mismatched_capa_payload = {
        "deviation_id": dev2_id,
        "rca_id": rca_id,
        "capa_type": "CORRECTIVE",
        "action_plan": "Mismatched",
    }
    res_mismatched = client.post(
        "/api/v1/quality/capas", json=mismatched_capa_payload, headers=headers
    )
    assert res_mismatched.status_code == 422
    assert "is not linked to deviation ID" in res_mismatched.json()["detail"]


def test_capa_transition_edge_cases_and_optimistic_locking():
    """
    Test CAPA transitions when CAPA doesn't exist, and when version_index mismatch causes a 409 conflict.
    """
    client = TestClient(app)
    headers = get_auth_headers(
        roles="admin", change_reason="CAPA transition edge cases"
    )

    # 1. CAPA not found (404)
    res = client.post(
        "/api/v1/quality/capas/nonexistent_capa/transition",
        json={"to_status": "UNDER_REVIEW"},
        headers=headers,
    )
    assert res.status_code == 404
    assert "CAPA record with ID" in res.json()["detail"]

    # 2. Setup deviation and CAPA to test 409 conflict
    dev_payload = {
        "study_id": "study_concurrency",
        "title": "Concurrency Deviation",
        "description": "desc",
        "severity": "MINOR",
        "type": "OTHER",
    }
    dev_res = client.post(
        "/api/v1/quality/deviations", json=dev_payload, headers=headers
    )
    dev_id = dev_res.json()["id"]

    capa_payload = {
        "deviation_id": dev_id,
        "capa_type": "CORRECTIVE",
        "action_plan": "Initial Plan",
    }
    capa_res = client.post("/api/v1/quality/capas", json=capa_payload, headers=headers)
    capa_id = capa_res.json()["id"]

    # Post transition with invalid version_index to trigger conflict (409)
    res_conflict = client.post(
        f"/api/v1/quality/capas/{capa_id}/transition",
        json={"to_status": "UNDER_REVIEW", "version_index": 99},
        headers=headers,
    )
    assert res_conflict.status_code == 409
    assert "Version conflict" in res_conflict.json()["detail"]


def test_capa_update_edge_cases_and_optional_fields():
    """
    Test CAPA updates when CAPA is not found, and updating optional preventive_measures and target_completion_date attributes.
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="admin", change_reason="CAPA update edge cases")

    # 1. CAPA not found (404)
    update_payload = {"action_plan": "Updated Action Plan"}
    res = client.put(
        "/api/v1/quality/capas/nonexistent_capa", json=update_payload, headers=headers
    )
    assert res.status_code == 404
    assert "CAPA record with ID" in res.json()["detail"]

    # 2. Setup deviation and CAPA
    dev_payload = {
        "study_id": "study_fields",
        "title": "Optional Fields Deviation",
        "description": "desc",
        "severity": "MINOR",
        "type": "OTHER",
    }
    dev_res = client.post(
        "/api/v1/quality/deviations", json=dev_payload, headers=headers
    )
    dev_id = dev_res.json()["id"]

    capa_payload = {
        "deviation_id": dev_id,
        "capa_type": "PREVENTIVE",
        "action_plan": "Initial Plan",
    }
    capa_res = client.post("/api/v1/quality/capas", json=capa_payload, headers=headers)
    capa_id = capa_res.json()["id"]

    # 3. Update optional attributes: preventive_measures and target_completion_date
    date_str = "2027-01-01T00:00:00"
    update_fields = {
        "preventive_measures": "Weekly training SOP",
        "target_completion_date": date_str,
        "version_index": 1,
    }
    res_update = client.put(
        f"/api/v1/quality/capas/{capa_id}", json=update_fields, headers=headers
    )
    assert res_update.status_code == 200
    data = res_update.json()
    assert data["preventive_measures"] == "Weekly training SOP"
    assert data["target_completion_date"] is not None
    assert "2027-01-01" in data["target_completion_date"]


def test_endpoint_change_reason_check_via_mock(monkeypatch):
    """
    Directly verify the endpoints' internal change reason validation logic
    by mocking get_user_context to return an empty change reason.
    """
    client = TestClient(app)

    # 1. Mock get_user_context to return empty change reason
    import apps.quality.main

    monkeypatch.setattr(
        apps.quality.main, "get_user_context", lambda req: ("mock_user", "admin", "")
    )

    # Generate headers that will pass the middleware auth check (e.g., with some dummy change reason)
    headers = get_auth_headers(roles="admin", change_reason="Dummy Reason")

    # 2. Deviation creation
    payload = {
        "study_id": "study_123",
        "title": "Mocked test",
        "description": "desc",
        "severity": "MINOR",
        "type": "OTHER",
    }
    res = client.post("/api/v1/quality/deviations", json=payload, headers=headers)
    assert res.status_code == 403
    assert "Missing change justification reason" in res.json()["detail"]

    # Restore the original get_user_context so we can set up a base deviation and CAPA
    monkeypatch.undo()

    # Let's create a valid deviation and CAPA
    valid_headers = get_auth_headers(roles="admin", change_reason="Setting up base")
    dev_res = client.post(
        "/api/v1/quality/deviations", json=payload, headers=valid_headers
    )
    assert dev_res.status_code == 201
    dev_id = dev_res.json()["id"]

    capa_payload = {
        "deviation_id": dev_id,
        "capa_type": "CORRECTIVE",
        "action_plan": "plan",
    }
    capa_res = client.post(
        "/api/v1/quality/capas", json=capa_payload, headers=valid_headers
    )
    assert capa_res.status_code == 201
    capa_id = capa_res.json()["id"]

    # Now re-apply the mock of get_user_context
    monkeypatch.setattr(
        apps.quality.main, "get_user_context", lambda req: ("mock_user", "admin", "")
    )

    # 3. RCA creation/update
    rca_payload = {
        "methodology": "5 Whys",
        "investigation_details": "details",
        "root_cause_summary": "summary",
    }
    res = client.post(
        f"/api/v1/quality/deviations/{dev_id}/rca", json=rca_payload, headers=headers
    )
    assert res.status_code == 403
    assert "Missing change justification reason" in res.json()["detail"]

    # 4. CAPA creation
    res = client.post("/api/v1/quality/capas", json=capa_payload, headers=headers)
    assert res.status_code == 403
    assert "Missing change justification reason" in res.json()["detail"]

    # 5. CAPA transition
    trans_payload = {"to_status": "UNDER_REVIEW"}
    res = client.post(
        f"/api/v1/quality/capas/{capa_id}/transition",
        json=trans_payload,
        headers=headers,
    )
    assert res.status_code == 403
    assert "Missing change justification reason" in res.json()["detail"]

    # 6. CAPA update
    update_payload = {"action_plan": "new plan"}
    res = client.put(
        f"/api/v1/quality/capas/{capa_id}", json=update_payload, headers=headers
    )
    assert res.status_code == 403
    assert "Missing change justification reason" in res.json()["detail"]
