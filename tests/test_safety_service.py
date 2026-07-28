import time
from datetime import datetime

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.gateway.main import generate_signature
from apps.safety.database import db_manager
from apps.safety.main import app
from apps.safety.models import Base, SafetyAuditLog, SafetyCaseICSR, SafetyExportJob


@pytest_asyncio.fixture(autouse=True)
async def setup_safety_db():
    """
    Setup in-memory Safety database for unit and integration testing.
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
    user_id = "safety_test_user"
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


def test_safety_health_check():
    """
    Verify health check of independent Safety service is unauthenticated and works correctly.
    """
    client = TestClient(app)
    response = client.get("/health")
    assert response.status_code == 200
    data = response.json()
    assert data["status"] == "ok"
    assert data["service"] == "safety"


def test_unauthenticated_requests_are_rejected():
    """
    Verify direct/untrusted requests are rejected by GatewayAuthMiddleware.
    """
    client = TestClient(app)
    # Direct request without gateway headers on an authenticated route
    response = client.get("/api/v1/safety/cases")
    assert response.status_code == 401
    assert "Missing gateway authentication headers" in response.json()["detail"]


@pytest.mark.asyncio
async def test_safety_database_schema_creation():
    """
    Verify that safety tables are created and queried successfully.
    """
    async with db_manager.get_session_maker()() as session:
        cases = await session.execute(select(SafetyCaseICSR))
        jobs = await session.execute(select(SafetyExportJob))
        logs = await session.execute(select(SafetyAuditLog))

        assert cases.scalars().all() == []
        assert jobs.scalars().all() == []
        assert logs.scalars().all() == []


@pytest.mark.asyncio
async def test_safety_case_lifecycle():
    """
    Verify that a safety case can be created, listed, and viewed with proper GxP fields.
    """
    client = TestClient(app)
    headers = get_auth_headers(
        roles="admin", change_reason="Initial ingestion of SAE report"
    )

    payload = {
        "worldwide_unique_case_id": "US-SPONSOR-2026-0001",
        "patient_id": "SUBJ-001",
        "case_data": {"reaction": "Anaphylaxis", "seriousness": "LIFE_THREATENING"},
    }

    # 1. Create Safety Case
    res_create = client.post("/api/v1/safety/cases", json=payload, headers=headers)
    assert res_create.status_code == 201
    data_create = res_create.json()
    assert data_create["id"] is not None
    assert data_create["worldwide_unique_case_id"] == "US-SPONSOR-2026-0001"
    assert data_create["patient_id"] == "SUBJ-001"
    assert data_create["case_data"] == {
        "reaction": "Anaphylaxis",
        "seriousness": "LIFE_THREATENING",
    }
    assert data_create["created_by"] == "safety_test_user"
    assert data_create["reason_for_change"] == "Initial ingestion of SAE report"
    assert data_create["version_index"] == 1

    case_id = data_create["id"]

    # 2. Get specific safety case
    res_get = client.get(f"/api/v1/safety/cases/{case_id}", headers=headers)
    assert res_get.status_code == 200
    assert res_get.json()["id"] == case_id

    # 3. List safety cases (unfiltered)
    res_list = client.get("/api/v1/safety/cases", headers=headers)
    assert res_list.status_code == 200
    assert len(res_list.json()) == 1
    assert res_list.json()[0]["id"] == case_id

    # 4. List safety cases (filtered by patient_id)
    res_list_filtered = client.get(
        "/api/v1/safety/cases?patient_id=SUBJ-001", headers=headers
    )
    assert res_list_filtered.status_code == 200
    assert len(res_list_filtered.json()) == 1

    # 5. List safety cases (filtered by non-existent patient_id)
    res_list_none = client.get(
        "/api/v1/safety/cases?patient_id=SUBJ-999", headers=headers
    )
    assert res_list_none.status_code == 200
    assert len(res_list_none.json()) == 0


@pytest.mark.asyncio
async def test_safety_export_job_lifecycle():
    """
    Verify that safety export jobs can be created and tracked.
    """
    client = TestClient(app)
    headers = get_auth_headers(
        roles="admin", change_reason="Generate E2B(R3) export batch"
    )

    payload = {"job_name": "E2B-XML-EXPORT-JULY2026"}

    # 1. Create Export Job
    res_create = client.post(
        "/api/v1/safety/export-jobs", json=payload, headers=headers
    )
    assert res_create.status_code == 201
    data_create = res_create.json()
    assert data_create["id"] is not None
    assert data_create["job_name"] == "E2B-XML-EXPORT-JULY2026"
    assert data_create["status"] == "PENDING"
    assert data_create["created_by"] == "safety_test_user"
    assert data_create["reason_for_change"] == "Generate E2B(R3) export batch"

    job_id = data_create["id"]

    # 2. Retrieve Specific Job
    res_get = client.get(f"/api/v1/safety/export-jobs/{job_id}", headers=headers)
    assert res_get.status_code == 200
    assert res_get.json()["id"] == job_id

    # 3. List Export Jobs
    res_list = client.get("/api/v1/safety/export-jobs", headers=headers)
    assert res_list.status_code == 200
    assert len(res_list.json()) == 1


@pytest.mark.asyncio
async def test_safety_audit_log_immutable_ledger():
    """
    Verify SafetyAuditLog is append-only and rejects updates/deletions.
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="admin", change_reason="Auditing demonstration")

    # 1. Perform some actions to populate audit logs
    client.post(
        "/api/v1/safety/cases",
        json={
            "worldwide_unique_case_id": "US-SPONSOR-2026-0002",
            "patient_id": "SUBJ-002",
            "case_data": {"symptom": "Fever"},
        },
        headers=headers,
    )

    # 2. Query SafetyAuditLog table to confirm logs exist
    async with db_manager.get_session_maker()() as session:
        stmt = select(SafetyAuditLog)
        res = await session.execute(stmt)
        logs = res.scalars().all()
        assert len(logs) > 0

        # Verify mandatory Part 11 fields are present on the logs
        log = logs[0]
        assert log.created_by == "safety_test_user"
        assert log.reason_for_change == "Auditing demonstration"
        assert log.version_index == 1
        assert isinstance(log.created_at, datetime)

        # 3. Try to update an audit log record -> Expect ValueError
        log.details = "Hacked/Modified Details"
        session.add(log)
        with pytest.raises(ValueError) as exc_info:
            await session.commit()
        assert "Updates to SafetyAuditLog are strictly forbidden" in str(exc_info.value)
        await session.rollback()

        # 4. Try to delete an audit log record -> Expect ValueError
        # Re-fetch log to ensure session is clean
        stmt_refetch = select(SafetyAuditLog).limit(1)
        res_refetch = await session.execute(stmt_refetch)
        log_to_delete = res_refetch.scalar_one()

        await session.delete(log_to_delete)
        with pytest.raises(ValueError) as exc_info_del:
            await session.commit()
        assert "Deletions from SafetyAuditLog are strictly forbidden" in str(
            exc_info_del.value
        )


@pytest.mark.asyncio
async def test_database_manager_uninitialized_raises_exception():
    """
    Verify that SafetyDatabaseManager raises an exception if get_session_maker is called before init_db.
    """
    from packages.database import RelationalDatabaseManager

    uninit_manager = RelationalDatabaseManager(service_name="Safety")
    with pytest.raises(Exception) as exc_info:
        uninit_manager.get_session_maker()
    assert "not initialized" in str(exc_info.value)


@pytest.mark.asyncio
async def test_list_audit_logs_endpoint():
    """
    Verify list_safety_audit_logs endpoint is protected and returns descending order logs.
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="admin", change_reason="List logs test reason")

    # Access endpoint
    res = client.get("/api/v1/safety/audit-logs", headers=headers)
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
        "worldwide_unique_case_id": "US-SPONSOR-2026-0003",
        "patient_id": "SUBJ-003",
        "case_data": {"symptom": "Headache"},
    }

    res = client.post("/api/v1/safety/cases", json=payload, headers=headers)
    assert res.status_code == 403
    assert "Missing change justification reason" in res.json()["detail"]


def test_nonexistent_resources_return_404():
    """
    Verify 404 is returned when attempting to access nonexistent resources.
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="admin", change_reason="Accessing nonexistent")

    res_case = client.get("/api/v1/safety/cases/nonexistent-case-id", headers=headers)
    assert res_case.status_code == 404
    assert "Safety case with ID" in res_case.json()["detail"]

    res_job = client.get(
        "/api/v1/safety/export-jobs/nonexistent-job-id", headers=headers
    )
    assert res_job.status_code == 404
    assert "Safety export job with ID" in res_job.json()["detail"]


@pytest.mark.asyncio
async def test_successful_export_and_transmission():
    """
    Verify that:
    1) Valid ICSR data is successfully exported and transmitted.
    2) The patient_id is pseudonymized, and birth_date is removed from transmission payload.
    3) No raw patient PII exists in the audit trail log details.
    """
    import httpx

    class MockAsyncClient:
        def __init__(self):
            self.posts = []

        async def post(self, url, content, headers=None):
            self.posts.append({"url": url, "content": content, "headers": headers})
            return httpx.Response(status_code=200, content=b"OK")

    mock_client = MockAsyncClient()
    app.state.test_httpx_client = mock_client

    client = TestClient(app)
    headers = get_auth_headers(
        roles="admin", change_reason="Exporting valid SAE to PV database"
    )

    from tests.test_safety_e2b import get_valid_icsr

    icsr = get_valid_icsr()
    icsr.patient.birth_date = "1981-01-15"
    raw_patient_id = icsr.patient.patient_id

    payload = {"job_name": "SAE-EXPORT-001", "icsr": icsr.model_dump()}

    res = client.post("/api/v1/safety/export", json=payload, headers=headers)
    assert res.status_code == 201
    data = res.json()
    assert data["status"] == "COMPLETED"
    assert data["job_name"] == "SAE-EXPORT-001"

    # Verify transmission occurred and the XML content is pseudonymized
    assert len(mock_client.posts) == 1
    transmitted_xml = mock_client.posts[0]["content"]
    assert isinstance(transmitted_xml, str) or isinstance(transmitted_xml, bytes)
    if isinstance(transmitted_xml, bytes):
        transmitted_xml = transmitted_xml.decode("utf-8")

    # Raw patient_id and DOB should NOT be in the transmitted XML
    assert raw_patient_id not in transmitted_xml
    assert "1981-01-15" not in transmitted_xml
    assert "<birth_date>" not in transmitted_xml

    # Check for pseudonymized patient_id
    import os

    from packages.deid.transforms import pseudonymize_value

    salt = os.getenv("SAFETY_SALT", "internal-safety-salt-12345")
    expected_pseudo_id = pseudonymize_value(raw_patient_id, salt)
    assert expected_pseudo_id in transmitted_xml

    # Verify audit logs: PII is absent from audit details
    res_audit = client.get("/api/v1/safety/audit-logs", headers=headers)
    assert res_audit.status_code == 200
    audit_logs = res_audit.json()

    # Let's check the log matching our job id
    export_job_id = data["id"]
    relevant_logs = [log for log in audit_logs if log["record_id"] == export_job_id]
    assert len(relevant_logs) > 0

    for log in relevant_logs:
        assert raw_patient_id not in log["details"]
        assert expected_pseudo_id in log["details"]


@pytest.mark.asyncio
async def test_invalid_xml_validation_fails():
    """
    Verify that structurally invalid XML produces a 422 Client Error and is not transmitted.
    """
    import httpx

    class MockAsyncClient:
        def __init__(self):
            self.posts = []

        async def post(self, url, content, headers=None):
            self.posts.append({"url": url, "content": content, "headers": headers})
            return httpx.Response(status_code=200, content=b"OK")

    mock_client = MockAsyncClient()
    app.state.test_httpx_client = mock_client

    client = TestClient(app)
    headers = get_auth_headers(roles="admin", change_reason="Exporting invalid SAE")

    from tests.test_safety_e2b import get_valid_icsr

    icsr = get_valid_icsr()
    icsr.patient.patient_id = "   "

    payload = {"job_name": "SAE-INVALID-EXPORT", "icsr": icsr.model_dump()}

    res = client.post("/api/v1/safety/export", json=payload, headers=headers)
    assert res.status_code == 422
    assert "Structural validation failure" in res.json()["detail"]

    # No transmission should have happened
    assert len(mock_client.posts) == 0


@pytest.mark.asyncio
async def test_missing_v2_headers_or_change_reason_fails():
    """
    Verify that POST /api/v1/safety/export requires:
    1) V2 signed headers
    2) Non-empty change reason (returns 403)
    """
    client = TestClient(app)

    from tests.test_safety_e2b import get_valid_icsr

    icsr = get_valid_icsr()
    payload = {"job_name": "SAE-EXPORT-UNAUTHORIZED", "icsr": icsr.model_dump()}

    # Case 1: Missing all gateway headers -> 403 (for mutations)
    res = client.post("/api/v1/safety/export", json=payload)
    assert res.status_code == 403
    assert "Missing gateway authentication headers" in res.json()["detail"]

    # Case 2: Missing change reason -> 403
    headers = get_auth_headers(roles="admin")  # Missing change_reason
    res = client.post("/api/v1/safety/export", json=payload, headers=headers)
    assert res.status_code == 403
    assert "Missing change justification reason" in res.json()["detail"]
