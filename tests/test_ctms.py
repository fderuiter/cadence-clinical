import time
from datetime import datetime, timedelta

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.ctms.database import db_manager
from apps.ctms.main import app
from apps.ctms.models import (
    Base,
    CRAAllocation,
    CTMSAuditLog,
    GeneratedLetter,
)
from apps.gateway.main import generate_signature


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """
    Setup in-memory CTMS database for unit and integration testing.
    """
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_auth_headers(
    roles: str = "admin", change_reason: str = "Authorized change", action: str = None
) -> dict:
    """
    Helper to generate valid gateway V2 signed headers for testing.
    """
    from jose import jwt

    timestamp = str(time.time())
    user_id = "test_user"
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
    if action:
        sig_payload = {
            "sub": user_id,
            "username": "test_user",
            "action": action,
            "roles": [roles],
            "iat": time.time(),
            "exp": time.time() + 300.0,
        }
        headers["X-Sig-Token"] = jwt.encode(
            sig_payload, "internal-gateway-secret-12345", algorithm="HS256"
        )
    return headers


def test_ctms_health_check():
    """
    Verify health check of independent CTMS service.
    """
    client = TestClient(app)
    response = client.get("/health")
    assert response.status_code == 200
    data = response.json()
    assert data["status"] == "ok"
    assert data["service"] == "ctms"


@pytest.mark.asyncio
async def test_create_and_list_studies_rbac():
    # @req:PRD-CTMS-004
    # @req:Trace-6
    """
    Verify creation of studies with appropriate RBAC roles, Part 11 audit fields,
    and automatic writing to the append-only CTMSAuditLog.
    """
    client = TestClient(app)

    # 1. Non-permitted role (e.g. Site Investigator) trying to POST study -> 403 Forbidden
    unauthorized_headers = get_auth_headers(
        roles="Site Investigator", change_reason="Unauthorized attempt"
    )
    payload = {
        "study_id": "study_ctms_101",
        "name": "CTMS Trial site operations",
        "status": "ACTIVE",
    }
    response = client.post(
        "/api/v1/ctms/studies", json=payload, headers=unauthorized_headers
    )
    assert response.status_code == 403
    assert "Access denied" in response.json()["detail"]

    # 2. Permitted role (e.g. Monitor) trying to POST study -> 201 Created
    authorized_headers = get_auth_headers(
        roles="Monitor", change_reason="Initiate study tracking in CTMS"
    )
    response_created = client.post(
        "/api/v1/ctms/studies", json=payload, headers=authorized_headers
    )
    assert response_created.status_code == 201
    created_data = response_created.json()
    assert created_data["study_id"] == "study_ctms_101"
    assert created_data["name"] == "CTMS Trial site operations"
    assert created_data["status"] == "ACTIVE"
    assert created_data["created_by"] == "test_user"
    assert created_data["reason_for_change"] == "Initiate study tracking in CTMS"
    assert created_data["version_index"] == 1

    # 3. Permitted role (e.g. Grants Manager) listing studies -> 200 OK
    grants_headers = get_auth_headers(roles="Grants Manager")
    response_list = client.get("/api/v1/ctms/studies", headers=grants_headers)
    assert response_list.status_code == 200
    studies = response_list.json()
    assert len(studies) == 1
    assert studies[0]["study_id"] == "study_ctms_101"

    # 4. Check CTMSAuditLog database records
    async with db_manager.get_session_maker()() as session:
        stmt = select(CTMSAuditLog).order_by(CTMSAuditLog.timestamp.desc())
        result = await session.execute(stmt)
        logs = result.scalars().all()

        assert len(logs) >= 2
        actions = [log.action for log in logs]
        assert "LIST_STUDIES" in actions
        assert "CREATE_STUDY" in actions

        create_log = next(log for log in logs if log.action == "CREATE_STUDY")
        assert create_log.user_id == "test_user"
        assert create_log.user_role == "Monitor"
        assert "Initiate study tracking" in create_log.details


@pytest.mark.asyncio
async def test_get_audit_trail_rbac():
    # @req:PRD-CTMS-004
    # @req:Trace-6
    """
    Verify auditing trail endpoint RBAC restriction and logging behavior.
    """
    client = TestClient(app)

    # 1. Auditor retrieves the audit log list
    auditor_headers = get_auth_headers(roles="Auditor")
    response_logs = client.get("/api/v1/ctms/audit-logs", headers=auditor_headers)
    assert response_logs.status_code == 200
    logs = response_logs.json()
    assert len(logs) >= 1
    assert logs[0]["action"] == "VIEW_AUDIT_LOGS"

    # 2. Non-auditor (e.g. Site Investigator) denied access to audit logs
    investigator_headers = get_auth_headers(roles="Site Investigator")
    response_denied = client.get(
        "/api/v1/ctms/audit-logs", headers=investigator_headers
    )
    assert response_denied.status_code == 403


@pytest.mark.asyncio
async def test_database_manager_uninitialized():
    """
    Test exception when getting session maker from an uninitialized CTMS database manager.
    """
    from packages.database import RelationalDatabaseManager

    mgr = RelationalDatabaseManager(service_name="CTMS")
    with pytest.raises(Exception) as exc_info:
        mgr.get_session_maker()
    assert "not initialized" in str(exc_info.value)


# --- New Monitoring Visits & Correspondence Tests ---


@pytest.mark.asyncio
async def test_monitoring_visit_workflow_happy_path():
    # @req:PRD-CTMS-002
    # @req:Trace-6
    """
    Verify scheduling, completing, retrieving, and signing off on monitoring visits.
    Ensure letters are generated, stored, and retrieved without re-rendering.
    Ensure CTMSAuditLog is updated on every mutation.
    """
    client = TestClient(app)
    cra_headers = get_auth_headers(roles="CRA", change_reason="CRA operations")
    get_auth_headers(roles="Monitor", change_reason="Monitor operations")

    # 1. Schedule a Visit (CRA role)
    scheduled_date = datetime.utcnow() + timedelta(days=5)
    payload = {
        "study_id": "study_001",
        "site_id": "site_99",
        "cra_id": "cra_fderuiter",
        "visit_type": "IMV",
        "scheduled_date": scheduled_date.isoformat(),
    }
    response = client.post(
        "/api/v1/ctms/monitoring-visits", json=payload, headers=cra_headers
    )
    assert response.status_code == 201
    visit_data = response.json()
    visit_id = visit_data["id"]
    assert visit_data["status"] == "SCHEDULED"
    assert visit_data["study_id"] == "study_001"
    assert visit_data["version_index"] == 1
    assert visit_data["created_by"] == "test_user"

    # Verify confirmation letter exists and is stored
    response_letters = client.get(
        f"/api/v1/ctms/monitoring-visits/{visit_id}/letters", headers=cra_headers
    )
    assert response_letters.status_code == 200
    letters = response_letters.json()
    assert len(letters) == 1
    assert letters[0]["letter_type"] == "CONFIRMATION"
    assert "CONFIRMATION OF CLINICAL MONITORING VISIT" in letters[0]["rendered_content"]
    assert "study_001" in letters[0]["rendered_content"]
    assert "site_99" in letters[0]["rendered_content"]

    # Retrieve specific letter by type
    response_letter_type = client.get(
        f"/api/v1/ctms/monitoring-visits/{visit_id}/letters/CONFIRMATION",
        headers=cra_headers,
    )
    assert response_letter_type.status_code == 200
    assert response_letter_type.json()["id"] == letters[0]["id"]

    # 2. Complete the Visit (CRA role) with findings
    actual_date = datetime.utcnow()
    completion_payload = {
        "actual_date": actual_date.isoformat(),
        "findings": [
            {
                "text": "Informed consent form missing date for Subject 01.",
                "severity": "CRITICAL",
                "resolution_status": "OPEN",
            },
            {
                "text": "Temperature log gap of 2 hours.",
                "severity": "MINOR",
                "resolution_status": "OPEN",
            },
        ],
    }
    response_complete = client.post(
        f"/api/v1/ctms/monitoring-visits/{visit_id}/complete",
        json=completion_payload,
        headers=cra_headers,
    )
    assert response_complete.status_code == 200
    completed_data = response_complete.json()
    assert completed_data["status"] == "COMPLETED"
    assert completed_data["version_index"] == 2
    assert completed_data["actual_date"] is not None

    # Verify follow-up letter exists and is stored
    response_letters_2 = client.get(
        f"/api/v1/ctms/monitoring-visits/{visit_id}/letters", headers=cra_headers
    )
    assert response_letters_2.status_code == 200
    letters_2 = response_letters_2.json()
    assert len(letters_2) == 2
    types = [let["letter_type"] for let in letters_2]
    assert "CONFIRMATION" in types
    assert "FOLLOW_UP" in types

    follow_up = next(let for let in letters_2 if let["letter_type"] == "FOLLOW_UP")
    assert "CLINICAL MONITORING VISIT FOLLOW-UP LETTER" in follow_up["rendered_content"]
    assert "Informed consent form missing date" in follow_up["rendered_content"]
    assert "CRITICAL" in follow_up["rendered_content"]
    assert "Temperature log gap" in follow_up["rendered_content"]

    # Verify direct retrieval from database without dynamic re-rendering
    # We modify the stored letter in the DB directly and check if the API returns our modified content.
    async with db_manager.get_session_maker()() as session:
        stmt = select(GeneratedLetter).where(
            GeneratedLetter.visit_id == visit_id,
            GeneratedLetter.letter_type == "FOLLOW_UP",
        )
        res = await session.execute(stmt)
        stored_letter = res.scalars().first()
        assert stored_letter is not None
        stored_letter.rendered_content = "MODIFIED_TAMPERED_LETTER_CONTENT"
        session.add(stored_letter)
        await session.commit()

    # Query via API again
    response_tampered = client.get(
        f"/api/v1/ctms/monitoring-visits/{visit_id}/letters/FOLLOW_UP",
        headers=cra_headers,
    )
    assert response_tampered.status_code == 200
    assert (
        response_tampered.json()["rendered_content"]
        == "MODIFIED_TAMPERED_LETTER_CONTENT"
    )

    # 3. Monitor supervisory sign-off (Monitor role)
    response_signoff = client.post(
        f"/api/v1/ctms/monitoring-visits/{visit_id}/sign-off",
        headers=get_auth_headers(
            roles="Monitor",
            change_reason="Monitor operations",
            action=f"/api/v1/ctms/monitoring-visits/{visit_id}/sign-off",
        ),
    )
    assert response_signoff.status_code == 200
    signed_data = response_signoff.json()
    assert signed_data["status"] == "SIGNED_OFF"
    assert signed_data["version_index"] == 3

    # 4. Check CTMSAuditLog entries for all changes
    async with db_manager.get_session_maker()() as session:
        stmt = select(CTMSAuditLog).order_by(CTMSAuditLog.timestamp.desc())
        result = await session.execute(stmt)
        logs = result.scalars().all()

        actions = [log_entry.action for log_entry in logs]
        assert "CREATE_VISIT" in actions
        assert "GENERATE_LETTER" in actions
        assert "COMPLETE_VISIT" in actions
        assert "CREATE_FINDING" in actions
        assert "SIGN_OFF_VISIT" in actions

        signoff_log = next(
            log_entry for log_entry in logs if log_entry.action == "SIGN_OFF_VISIT"
        )
        assert signoff_log.user_role == "Monitor"
        assert "Monitor supervisory sign-off" in signoff_log.details


@pytest.mark.asyncio
async def test_monitoring_visit_workflow_rbac_denials():
    # @req:PRD-CTMS-002
    # @req:Trace-6
    """
    Ensure strict RBAC enforcement:
    - Site Investigator cannot create, complete, list, retrieve, or sign off.
    - CRA cannot sign off.
    - Monitor cannot create or complete visits.
    """
    client = TestClient(app)

    # Pre-populate a scheduled visit using System/CRA role
    cra_headers = get_auth_headers(roles="CRA")
    scheduled_date = datetime.utcnow() + timedelta(days=2)
    payload = {
        "study_id": "study_001",
        "site_id": "site_99",
        "cra_id": "cra_fderuiter",
        "visit_type": "IMV",
        "scheduled_date": scheduled_date.isoformat(),
    }
    response_create = client.post(
        "/api/v1/ctms/monitoring-visits", json=payload, headers=cra_headers
    )
    assert response_create.status_code == 201
    visit_id = response_create.json()["id"]

    # 1. Site Investigator attempting to schedule visit -> 403
    si_headers = get_auth_headers(roles="Site Investigator")
    response_si_create = client.post(
        "/api/v1/ctms/monitoring-visits", json=payload, headers=si_headers
    )
    assert response_si_create.status_code == 403

    # 2. Monitor attempting to schedule visit -> 403 (Only CRA / Admin)
    monitor_headers = get_auth_headers(roles="Monitor")
    response_m_create = client.post(
        "/api/v1/ctms/monitoring-visits", json=payload, headers=monitor_headers
    )
    assert response_m_create.status_code == 403

    # 3. Monitor attempting to complete visit -> 403 (Only CRA / Admin)
    completion_payload = {
        "actual_date": datetime.utcnow().isoformat(),
        "findings": [],
    }
    response_m_complete = client.post(
        f"/api/v1/ctms/monitoring-visits/{visit_id}/complete",
        json=completion_payload,
        headers=monitor_headers,
    )
    assert response_m_complete.status_code == 403

    # Complete it via CRA role so we can test sign-off
    response_comp = client.post(
        f"/api/v1/ctms/monitoring-visits/{visit_id}/complete",
        json=completion_payload,
        headers=cra_headers,
    )
    assert response_comp.status_code == 200

    # 4. CRA attempting to sign off -> 403 (Only Monitor / Admin)
    response_cra_signoff = client.post(
        f"/api/v1/ctms/monitoring-visits/{visit_id}/sign-off",
        headers=get_auth_headers(
            roles="CRA", action=f"/api/v1/ctms/monitoring-visits/{visit_id}/sign-off"
        ),
    )
    assert response_cra_signoff.status_code == 403

    # 5. Site Investigator attempting to retrieve letters -> 403
    response_si_letters = client.get(
        f"/api/v1/ctms/monitoring-visits/{visit_id}/letters", headers=si_headers
    )
    assert response_si_letters.status_code == 403


@pytest.mark.asyncio
async def test_monitoring_visit_invalid_state_and_findings():
    # @req:PRD-CTMS-002
    # @req:Trace-6
    """
    Test various edge-case failures:
    - Completing a non-existent visit
    - Signing off a non-completed (scheduled) visit
    - Completing a visit that is already completed
    - Finding with invalid severity
    - Retrieving non-existent letter
    """
    client = TestClient(app)
    cra_headers = get_auth_headers(roles="CRA")
    get_auth_headers(roles="Monitor")

    # Schedule a visit
    scheduled_date = datetime.utcnow() + timedelta(days=2)
    payload = {
        "study_id": "study_001",
        "site_id": "site_99",
        "cra_id": "cra_fderuiter",
        "visit_type": "IMV",
        "scheduled_date": scheduled_date.isoformat(),
    }
    response_create = client.post(
        "/api/v1/ctms/monitoring-visits", json=payload, headers=cra_headers
    )
    assert response_create.status_code == 201
    visit_id = response_create.json()["id"]

    # Try signing off a scheduled visit -> 400
    response_invalid_signoff = client.post(
        f"/api/v1/ctms/monitoring-visits/{visit_id}/sign-off",
        headers=get_auth_headers(
            roles="Monitor",
            action=f"/api/v1/ctms/monitoring-visits/{visit_id}/sign-off",
        ),
    )
    assert response_invalid_signoff.status_code == 400
    assert "Only completed" in response_invalid_signoff.json()["detail"]

    # Try completing with invalid severity -> 400
    bad_completion_payload = {
        "actual_date": datetime.utcnow().isoformat(),
        "findings": [
            {
                "text": "Bad severity finding",
                "severity": "SUPER_CRITICAL",
            }
        ],
    }
    response_bad_severity = client.post(
        f"/api/v1/ctms/monitoring-visits/{visit_id}/complete",
        json=bad_completion_payload,
        headers=cra_headers,
    )
    assert response_bad_severity.status_code == 400
    assert "Invalid finding severity" in response_bad_severity.json()["detail"]

    # Complete it properly
    good_completion_payload = {
        "actual_date": datetime.utcnow().isoformat(),
        "findings": [],
    }
    response_ok_completion = client.post(
        f"/api/v1/ctms/monitoring-visits/{visit_id}/complete",
        json=good_completion_payload,
        headers=cra_headers,
    )
    assert response_ok_completion.status_code == 200

    # Try completing a completed visit -> 400
    response_already_completed = client.post(
        f"/api/v1/ctms/monitoring-visits/{visit_id}/complete",
        json=good_completion_payload,
        headers=cra_headers,
    )
    assert response_already_completed.status_code == 400

    # Try retrieving non-existent letter type -> 404
    response_no_letter = client.get(
        "/api/v1/ctms/monitoring-visits/non-existent-id/letters/CONFIRMATION",
        headers=cra_headers,
    )
    assert response_no_letter.status_code == 404


@pytest.mark.asyncio
async def test_recruitment_records_crud_and_audit():
    # @req:PRD-CTMS-004
    # @req:Trace-6
    """
    Verify creation, listing, and audit trails of recruitment records.
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="CRA", change_reason="Record site enrollment")

    # 1. Create a Recruitment Record
    payload = {
        "site_id": "site_A",
        "study_id": "study_X",
        "screened_count": 10,
        "enrolled_count": 5,
        "target_count": 20,
    }
    response = client.post("/api/v1/ctms/recruitment", json=payload, headers=headers)
    assert response.status_code == 201
    data = response.json()
    assert data["site_id"] == "site_A"
    assert data["study_id"] == "study_X"
    assert data["screened_count"] == 10
    assert data["enrolled_count"] == 5
    assert data["target_count"] == 20
    assert data["version_index"] == 1
    assert data["created_by"] == "test_user"
    assert data["reason_for_change"] == "Record site enrollment"

    # 2. List Recruitment Records
    list_headers = get_auth_headers(roles="Monitor")
    list_response = client.get(
        "/api/v1/ctms/recruitment?study_id=study_X", headers=list_headers
    )
    assert list_response.status_code == 200
    records = list_response.json()
    assert len(records) >= 1
    assert records[0]["site_id"] == "site_A"

    # Filter with site_id that does not exist should return empty
    empty_response = client.get(
        "/api/v1/ctms/recruitment?site_id=site_B", headers=list_headers
    )
    assert empty_response.status_code == 200
    assert len(empty_response.json()) == 0

    # 3. Verify Audit Log entry
    async with db_manager.get_session_maker()() as session:
        stmt = select(CTMSAuditLog).where(
            CTMSAuditLog.action == "CREATE_RECRUITMENT_RECORD"
        )
        result = await session.execute(stmt)
        logs = result.scalars().all()
        assert len(logs) == 1
        assert "Recorded recruitment metrics" in logs[0].details


@pytest.mark.asyncio
async def test_site_milestones_crud_and_audit():
    # @req:PRD-CTMS-001
    # @req:Trace-6
    """
    Verify site milestones can be created, updated, and logged in audit log.
    """
    client = TestClient(app)
    cra_headers = get_auth_headers(roles="CRA", change_reason="Initial planning")

    # 1. Create site milestone
    payload = {
        "site_id": "site_A",
        "study_id": "study_X",
        "milestone_type": "SITE_ACTIVATION",
        "planned_date": (datetime.utcnow() + timedelta(days=30)).isoformat(),
        "status": "PLANNED",
    }
    response = client.post(
        "/api/v1/ctms/site-milestones", json=payload, headers=cra_headers
    )
    assert response.status_code == 201
    m_data = response.json()
    assert m_data["status"] == "PLANNED"
    assert m_data["milestone_type"] == "SITE_ACTIVATION"
    m_id = m_data["id"]

    # 2. Update site milestone to ACHIEVED
    update_headers = get_auth_headers(
        roles="Monitor", change_reason="Site is activated"
    )
    update_payload = {
        "actual_date": datetime.utcnow().isoformat(),
        "status": "ACHIEVED",
    }
    update_response = client.put(
        f"/api/v1/ctms/site-milestones/{m_id}",
        json=update_payload,
        headers=update_headers,
    )
    assert update_response.status_code == 200
    updated_data = update_response.json()
    assert updated_data["status"] == "ACHIEVED"
    assert updated_data["actual_date"] is not None
    assert updated_data["version_index"] == 2
    assert updated_data["reason_for_change"] == "Site is activated"

    # Try updating non-existent milestone -> 404
    response_404 = client.put(
        "/api/v1/ctms/site-milestones/missing-id",
        json=update_payload,
        headers=update_headers,
    )
    assert response_404.status_code == 404

    # 3. List Milestones
    list_response = client.get(
        "/api/v1/ctms/site-milestones?site_id=site_A", headers=update_headers
    )
    assert list_response.status_code == 200
    milestones = list_response.json()
    assert len(milestones) == 1
    assert milestones[0]["id"] == m_id

    # 4. Check Audit logs
    async with db_manager.get_session_maker()() as session:
        stmt = select(CTMSAuditLog).where(
            CTMSAuditLog.action.in_(["CREATE_MILESTONE", "UPDATE_MILESTONE"])
        )
        result = await session.execute(stmt)
        logs = result.scalars().all()
        actions = [log.action for log in logs]
        assert "CREATE_MILESTONE" in actions
        assert "UPDATE_MILESTONE" in actions


@pytest.mark.asyncio
async def test_cra_allocations_rbac_reassignment_workload():
    # @req:PRD-CTMS-003
    # @req:Trace-6
    """
    Verify RBAC on CRA allocations (Sponsor Admin only), automatic reassignment/deactivation,
    and CRA workload summaries.
    """
    client = TestClient(app)
    sponsor_admin_headers = get_auth_headers(
        roles="Sponsor Admin", change_reason="Allocate CRA to Site"
    )
    cra_headers = get_auth_headers(roles="CRA")

    # 1. RBAC: Non Sponsor Admin tries to allocate CRA -> 403
    payload = {
        "cra_id": "cra_alice",
        "site_id": "site_A",
        "study_id": "study_X",
        "status": "ACTIVE",
    }
    response_forbidden = client.post(
        "/api/v1/ctms/cra-allocations", json=payload, headers=cra_headers
    )
    assert response_forbidden.status_code == 403

    # 2. Allocate CRA (Sponsor Admin) -> 201
    response_ok = client.post(
        "/api/v1/ctms/cra-allocations", json=payload, headers=sponsor_admin_headers
    )
    assert response_ok.status_code == 201
    alloc1_data = response_ok.json()
    assert alloc1_data["cra_id"] == "cra_alice"
    assert alloc1_data["status"] == "ACTIVE"
    alloc1_id = alloc1_data["id"]

    # 3. Retrieve workload summary -> cra_alice has 1 active allocation
    workload_headers = get_auth_headers(roles="Monitor")
    response_wl = client.get(
        "/api/v1/ctms/cra-allocations/workload", headers=workload_headers
    )
    assert response_wl.status_code == 200
    workload = response_wl.json()
    assert len(workload) == 1
    assert workload[0]["cra_id"] == "cra_alice"
    assert workload[0]["active_allocations_count"] == 1
    assert "site_A" in workload[0]["allocated_sites"]

    # 4. Reassign site to cra_bob -> cra_alice's allocation becomes INACTIVE, cra_bob's becomes ACTIVE
    reassign_payload = {
        "cra_id": "cra_bob",
        "site_id": "site_A",
        "study_id": "study_X",
        "status": "ACTIVE",
    }
    response_reassign = client.post(
        "/api/v1/ctms/cra-allocations",
        json=reassign_payload,
        headers=sponsor_admin_headers,
    )
    assert response_reassign.status_code == 201
    alloc2_data = response_reassign.json()
    assert alloc2_data["cra_id"] == "cra_bob"
    assert alloc2_data["status"] == "ACTIVE"

    # Verify cra_alice's allocation is now INACTIVE in DB
    async with db_manager.get_session_maker()() as session:
        stmt = select(CRAAllocation).where(CRAAllocation.id == alloc1_id)
        result = await session.execute(stmt)
        a1 = result.scalars().first()
        assert a1.status == "INACTIVE"
        assert a1.effective_end_date is not None

    # Retrieve workload summary again -> cra_bob has 1 active allocation, cra_alice has 0 (not in active map)
    response_wl2 = client.get(
        "/api/v1/ctms/cra-allocations/workload", headers=workload_headers
    )
    assert response_wl2.status_code == 200
    workload2 = response_wl2.json()
    assert len(workload2) == 1
    assert workload2[0]["cra_id"] == "cra_bob"

    # 5. Non Sponsor Admin tries to update CRA allocation -> 403
    update_payload = {"status": "INACTIVE"}
    response_update_forbidden = client.put(
        f"/api/v1/ctms/cra-allocations/{alloc1_id}",
        json=update_payload,
        headers=cra_headers,
    )
    assert response_update_forbidden.status_code == 403

    # Sponsor Admin updates allocation -> 200
    response_update_ok = client.put(
        f"/api/v1/ctms/cra-allocations/{alloc1_id}",
        json=update_payload,
        headers=sponsor_admin_headers,
    )
    assert response_update_ok.status_code == 200


@pytest.mark.asyncio
async def test_monitoring_visit_scheduling_respects_cra_allocation():
    # @req:PRD-CTMS-003
    # @req:Trace-6
    """
    Verify scheduling a monitoring visit identifies/respects active CRA allocations.
    """
    client = TestClient(app)
    sponsor_admin_headers = get_auth_headers(roles="Sponsor Admin")
    cra_headers = get_auth_headers(roles="CRA")

    # 1. Allocate cra_bob to site_B and study_Y
    alloc_payload = {
        "cra_id": "cra_bob",
        "site_id": "site_B",
        "study_id": "study_Y",
        "status": "ACTIVE",
    }
    alloc_res = client.post(
        "/api/v1/ctms/cra-allocations",
        json=alloc_payload,
        headers=sponsor_admin_headers,
    )
    assert alloc_res.status_code == 201

    # 2. Try to schedule visit for study_Y / site_B using cra_charlie -> 400 Bad Request
    visit_date = datetime.utcnow() + timedelta(days=5)
    visit_payload = {
        "study_id": "study_Y",
        "site_id": "site_B",
        "cra_id": "cra_charlie",
        "visit_type": "IMV",
        "scheduled_date": visit_date.isoformat(),
    }
    visit_res_bad = client.post(
        "/api/v1/ctms/monitoring-visits", json=visit_payload, headers=cra_headers
    )
    assert visit_res_bad.status_code == 400
    assert "is not allocated" in visit_res_bad.json()["detail"]

    # 3. Schedule visit using allocated cra_bob -> 201 Created
    visit_payload["cra_id"] = "cra_bob"
    visit_res_ok = client.post(
        "/api/v1/ctms/monitoring-visits", json=visit_payload, headers=cra_headers
    )
    assert visit_res_ok.status_code == 201


# --- New Site Financial Management (Grants, Budgets, Milestones, Payables) Tests ---


@pytest.mark.asyncio
async def test_grant_creation_rbac():
    """Verify that only Sponsor Admin and Grants Manager can create grants."""
    client = TestClient(app)
    payload = {
        "study_id": "study_finance",
        "site_id": "site_finance",
        "total_budget": 100000.0,
        "currency": "USD",
    }

    # Denied role (CRA) -> 403
    cra_headers = get_auth_headers(roles="CRA")
    response_denied = client.post(
        "/api/v1/ctms/grants", json=payload, headers=cra_headers
    )
    assert response_denied.status_code == 403

    # Permitted role (Grants Manager) -> 201
    gm_headers = get_auth_headers(roles="Grants Manager")
    response_ok = client.post("/api/v1/ctms/grants", json=payload, headers=gm_headers)
    assert response_ok.status_code == 201
    data = response_ok.json()
    assert data["study_id"] == "study_finance"
    assert data["site_id"] == "site_finance"
    assert data["total_budget"] == 100000.0
    assert data["status"] == "DRAFT"
    assert data["version_index"] == 1


@pytest.mark.asyncio
async def test_grant_locked_when_approved():
    """Verify approved grants are locked from editing, budget items, and milestones additions."""
    client = TestClient(app)
    gm_headers = get_auth_headers(roles="Grants Manager")

    # Create Grant
    payload = {
        "study_id": "study_locked",
        "site_id": "site_locked",
        "total_budget": 50000.0,
    }
    response_created = client.post(
        "/api/v1/ctms/grants", json=payload, headers=gm_headers
    )
    assert response_created.status_code == 201
    grant_id = response_created.json()["id"]

    # Budget Item Addition Works on Draft
    item_payload = {
        "category": "TRAVEL",
        "description": "Travel cost",
        "amount": 1000.0,
    }
    response_item = client.post(
        f"/api/v1/ctms/grants/{grant_id}/budget-items",
        json=item_payload,
        headers=gm_headers,
    )
    assert response_item.status_code == 201

    # Milestone Addition Works on Draft
    ms_payload = {
        "milestone_name": "SIV Complete",
        "trigger_condition": "STUDY_APPROVED",
        "amount": 5000.0,
    }
    response_ms = client.post(
        f"/api/v1/ctms/grants/{grant_id}/milestones",
        json=ms_payload,
        headers=gm_headers,
    )
    assert response_ms.status_code == 201

    # Approve Grant
    update_payload = {"status": "APPROVED"}
    response_approved = client.put(
        f"/api/v1/ctms/grants/{grant_id}", json=update_payload, headers=gm_headers
    )
    assert response_approved.status_code == 200
    assert response_approved.json()["status"] == "APPROVED"

    # Mutations on Approved Grant are Blocked -> 400
    # Try updating grant total budget -> 400
    response_update_blocked = client.put(
        f"/api/v1/ctms/grants/{grant_id}",
        json={"total_budget": 60000.0},
        headers=gm_headers,
    )
    assert response_update_blocked.status_code == 400
    assert "locked from editing" in response_update_blocked.json()["detail"]

    # Try adding budget item -> 400
    response_item_blocked = client.post(
        f"/api/v1/ctms/grants/{grant_id}/budget-items",
        json=item_payload,
        headers=gm_headers,
    )
    assert response_item_blocked.status_code == 400
    assert "locked from editing" in response_item_blocked.json()["detail"]

    # Try adding milestone -> 400
    response_ms_blocked = client.post(
        f"/api/v1/ctms/grants/{grant_id}/milestones",
        json=ms_payload,
        headers=gm_headers,
    )
    assert response_ms_blocked.status_code == 400
    assert "locked from editing" in response_ms_blocked.json()["detail"]


@pytest.mark.asyncio
async def test_milestone_trigger_study_approved():
    """Verify that approving a grant triggers STUDY_APPROVED milestones and creates single payables."""
    client = TestClient(app)
    gm_headers = get_auth_headers(roles="Grants Manager")

    # Create Grant
    payload = {
        "study_id": "study_m1",
        "site_id": "site_m1",
        "total_budget": 80000.0,
    }
    response_created = client.post(
        "/api/v1/ctms/grants", json=payload, headers=gm_headers
    )
    grant_id = response_created.json()["id"]

    # Create STUDY_APPROVED milestone
    ms_payload = {
        "milestone_name": "Initial Study Approval Payment",
        "trigger_condition": "STUDY_APPROVED",
        "amount": 10000.0,
    }
    client.post(
        f"/api/v1/ctms/grants/{grant_id}/milestones",
        json=ms_payload,
        headers=gm_headers,
    )

    # Transition to APPROVED
    client.put(
        f"/api/v1/ctms/grants/{grant_id}",
        json={"status": "APPROVED"},
        headers=gm_headers,
    )

    # Check payables
    response_payables = client.get(
        f"/api/v1/ctms/grants/{grant_id}/payables", headers=gm_headers
    )
    payables = response_payables.json()
    assert len(payables) == 1
    assert payables[0]["amount"] == 10000.0
    assert payables[0]["payment_status"] == "PENDING"

    # Re-evaluate Study Approved condition -> Should be idempotent (no extra payable created)
    client.post(
        f"/api/v1/ctms/grants/{grant_id}/evaluate?condition=STUDY_APPROVED",
        headers=gm_headers,
    )
    response_payables_2 = client.get(
        f"/api/v1/ctms/grants/{grant_id}/payables", headers=gm_headers
    )
    assert len(response_payables_2.json()) == 1


@pytest.mark.asyncio
async def test_milestone_trigger_visit_completed_automated():
    """Verify that completing a monitoring visit automatically triggers VISIT_COMPLETED milestones on approved grants."""
    client = TestClient(app)
    cra_headers = get_auth_headers(roles="CRA")
    gm_headers = get_auth_headers(roles="Grants Manager")

    # 1. Create and Approve Grant for study_visit, site_visit
    grant_payload = {
        "study_id": "study_visit",
        "site_id": "site_visit",
        "total_budget": 50000.0,
    }
    g_res = client.post("/api/v1/ctms/grants", json=grant_payload, headers=gm_headers)
    grant_id = g_res.json()["id"]

    # Add VISIT_COMPLETED milestone
    ms_payload = {
        "milestone_name": "CRA Visit Completed",
        "trigger_condition": "VISIT_COMPLETED",
        "amount": 2000.0,
    }
    client.post(
        f"/api/v1/ctms/grants/{grant_id}/milestones",
        json=ms_payload,
        headers=gm_headers,
    )

    # Approve the grant
    client.put(
        f"/api/v1/ctms/grants/{grant_id}",
        json={"status": "APPROVED"},
        headers=gm_headers,
    )

    # 2. Allocate CRA and Schedule/Complete a visit
    # Allocate cra_bob
    alloc_headers = get_auth_headers(roles="Sponsor Admin")
    alloc_payload = {
        "cra_id": "cra_bob",
        "site_id": "site_visit",
        "study_id": "study_visit",
        "status": "ACTIVE",
    }
    client.post(
        "/api/v1/ctms/cra-allocations", json=alloc_payload, headers=alloc_headers
    )

    # Schedule visit
    scheduled_date = datetime.utcnow() + timedelta(days=2)
    visit_payload = {
        "study_id": "study_visit",
        "site_id": "site_visit",
        "cra_id": "cra_bob",
        "visit_type": "IMV",
        "scheduled_date": scheduled_date.isoformat(),
    }
    v_res = client.post(
        "/api/v1/ctms/monitoring-visits", json=visit_payload, headers=cra_headers
    )
    visit_id = v_res.json()["id"]

    # Complete the visit -> Should automatically trigger evaluation of VISIT_COMPLETED milestone
    completion_payload = {
        "actual_date": datetime.utcnow().isoformat(),
        "findings": [],
    }
    client.post(
        f"/api/v1/ctms/monitoring-visits/{visit_id}/complete",
        json=completion_payload,
        headers=cra_headers,
    )

    # 3. Verify payable was generated
    response_payables = client.get(
        f"/api/v1/ctms/grants/{grant_id}/payables", headers=gm_headers
    )
    payables = response_payables.json()
    assert len(payables) == 1
    assert payables[0]["amount"] == 2000.0

    # Completing another visit (if we had another) or running evaluate manually -> should be idempotent
    client.post(
        f"/api/v1/ctms/grants/{grant_id}/evaluate?condition=VISIT_COMPLETED",
        headers=gm_headers,
    )
    response_payables_2 = client.get(
        f"/api/v1/ctms/grants/{grant_id}/payables", headers=gm_headers
    )
    assert len(response_payables_2.json()) == 1


@pytest.mark.asyncio
async def test_milestone_trigger_manual():
    """Verify that MANUAL milestones can be manually triggered and are idempotent."""
    client = TestClient(app)
    gm_headers = get_auth_headers(roles="Grants Manager")

    # Create Grant
    payload = {
        "study_id": "study_manual",
        "site_id": "site_manual",
        "total_budget": 30000.0,
    }
    response_created = client.post(
        "/api/v1/ctms/grants", json=payload, headers=gm_headers
    )
    grant_id = response_created.json()["id"]

    # Create MANUAL milestone
    ms_payload = {
        "milestone_name": "Equipment Sourcing",
        "trigger_condition": "MANUAL",
        "amount": 15000.0,
    }
    ms_res = client.post(
        f"/api/v1/ctms/grants/{grant_id}/milestones",
        json=ms_payload,
        headers=gm_headers,
    )
    milestone_id = ms_res.json()["id"]

    # Try triggering before Grant is Approved -> 400
    response_early_trigger = client.post(
        f"/api/v1/ctms/grants/{grant_id}/milestones/{milestone_id}/trigger",
        headers=gm_headers,
    )
    assert response_early_trigger.status_code == 400

    # Approve the grant
    client.put(
        f"/api/v1/ctms/grants/{grant_id}",
        json={"status": "APPROVED"},
        headers=gm_headers,
    )

    # Trigger manually
    response_trigger = client.post(
        f"/api/v1/ctms/grants/{grant_id}/milestones/{milestone_id}/trigger",
        headers=gm_headers,
    )
    assert response_trigger.status_code == 200
    assert response_trigger.json()["is_triggered"] is True

    # Check payables
    response_payables = client.get(
        f"/api/v1/ctms/grants/{grant_id}/payables", headers=gm_headers
    )
    payables = response_payables.json()
    assert len(payables) == 1
    assert payables[0]["amount"] == 15000.0

    # Re-trigger manually -> Should be idempotent (no extra payable created)
    client.post(
        f"/api/v1/ctms/grants/{grant_id}/milestones/{milestone_id}/trigger",
        headers=gm_headers,
    )
    response_payables_2 = client.get(
        f"/api/v1/ctms/grants/{grant_id}/payables", headers=gm_headers
    )
    assert len(response_payables_2.json()) == 1
