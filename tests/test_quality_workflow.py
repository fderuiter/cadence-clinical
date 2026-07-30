import time

import pytest
from fastapi.testclient import TestClient

from apps.gateway.main import generate_signature
from apps.quality.database import db_manager
from apps.quality.main import app
from apps.quality.models import (
    Base,
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


@pytest.fixture(autouse=True)
async def setup_quality_db():
    """
    Setup in-memory Quality database for workflow tests.
    """
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_auth_headers(
    roles: str = "admin", change_reason: str = "Compliance change justification"
) -> dict:
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
        "X-Change-Reason": change_reason,
    }
    return headers


def test_create_and_list_deviations():
    """
    Verify that a deviation can be created and retrieved via API.
    """
    # @req:PRD-SYS-001
    # @req:Trace-7
    client = TestClient(app)
    headers = get_auth_headers(change_reason="Reporting protocol deviation")
    payload = {
        "study_id": "study_123",
        "site_id": "site_abc",
        "title": "Informed consent missing signature",
        "description": "The subject signed the form but did not date it.",
        "severity": "MAJOR",
        "type": "INFORMED_CONSENT",
        "is_protocol_violation": True,
    }
    response = client.post("/api/v1/quality/deviations", json=payload, headers=headers)
    assert response.status_code == 201
    data = response.json()
    assert data["id"] is not None
    assert data["study_id"] == "study_123"
    assert data["site_id"] == "site_abc"
    assert data["title"] == "Informed consent missing signature"
    assert data["status"] == "REPORTED"
    assert data["version_index"] == 1
    assert data["reason_for_change"] == "Reporting protocol deviation"

    # List deviations with filters
    response_list = client.get(
        "/api/v1/quality/deviations?study_id=study_123", headers=headers
    )
    assert response_list.status_code == 200
    list_data = response_list.json()
    assert len(list_data) == 1
    assert list_data[0]["id"] == data["id"]

    # View deviation
    response_view = client.get(
        f"/api/v1/quality/deviations/{data['id']}", headers=headers
    )
    assert response_view.status_code == 200
    assert response_view.json()["id"] == data["id"]


def test_create_and_update_rca():
    """
    Verify that an RCA can be created or updated for an existing deviation.
    """
    # @req:PRD-SYS-001
    client = TestClient(app)
    headers = get_auth_headers(change_reason="Create deviation for RCA test")

    # 1. Create Deviation
    dev_payload = {
        "study_id": "study_123",
        "title": "Temp excursion",
        "description": "IP storage excursion",
        "severity": "CRITICAL",
        "type": "IP_MANAGEMENT",
        "is_protocol_violation": False,
    }
    dev_res = client.post(
        "/api/v1/quality/deviations", json=dev_payload, headers=headers
    )
    dev_id = dev_res.json()["id"]

    # 2. Attach RCA
    rca_headers = get_auth_headers(change_reason="Perform initial RCA investigation")
    rca_payload = {
        "methodology": "5 Whys",
        "investigation_details": "Power went down in facility",
        "root_cause_summary": "Backup generator failed",
    }
    rca_res = client.post(
        f"/api/v1/quality/deviations/{dev_id}/rca",
        json=rca_payload,
        headers=rca_headers,
    )
    assert rca_res.status_code == 200
    rca_data = rca_res.json()
    assert rca_data["deviation_id"] == dev_id
    assert rca_data["methodology"] == "5 Whys"
    assert rca_data["version_index"] == 1

    # Verify parent deviation transitioned to RCA_COMPLETE
    dev_view = client.get(f"/api/v1/quality/deviations/{dev_id}", headers=rca_headers)
    assert dev_view.json()["status"] == "RCA_COMPLETE"
    assert dev_view.json()["version_index"] == 2

    # 3. Update RCA with optimistic lock check
    update_headers = get_auth_headers(change_reason="Update RCA with details")
    rca_payload_update = {
        "methodology": "Fishbone Diagram",
        "investigation_details": "Updated investigation details",
        "root_cause_summary": "Updated root cause summary",
        "version_index": 1,
    }
    rca_update_res = client.put(
        f"/api/v1/quality/deviations/{dev_id}/rca",
        json=rca_payload_update,
        headers=update_headers,
    )
    assert rca_update_res.status_code == 200
    assert rca_update_res.json()["version_index"] == 2
    assert rca_update_res.json()["methodology"] == "Fishbone Diagram"

    # 4. Trigger version conflict
    conflict_payload = {
        "methodology": "Fishbone Diagram",
        "investigation_details": "Conflict",
        "root_cause_summary": "Conflict",
        "version_index": 1,  # Stale version index
    }
    conflict_res = client.put(
        f"/api/v1/quality/deviations/{dev_id}/rca",
        json=conflict_payload,
        headers=update_headers,
    )
    assert conflict_res.status_code == 409
    assert "Version conflict" in conflict_res.json()["detail"]


def test_capa_creation_validations():
    """
    Verify that CAPA creation validates parent records and statuses.
    """
    # @req:PRD-SUB-001
    client = TestClient(app)
    headers = get_auth_headers(change_reason="Create deviation for CAPA validations")

    # 1. Non-existent deviation
    capa_payload = {
        "deviation_id": "non-existent-id",
        "capa_type": "CORRECTIVE",
        "action_plan": "Testing",
    }
    res = client.post("/api/v1/quality/capas", json=capa_payload, headers=headers)
    assert res.status_code == 422
    assert "Parent deviation" in res.json()["detail"]

    # 2. Create valid deviation
    dev_payload = {
        "study_id": "study_123",
        "title": "Temp excursion",
        "description": "IP storage excursion",
        "severity": "CRITICAL",
        "type": "IP_MANAGEMENT",
    }
    dev_res = client.post(
        "/api/v1/quality/deviations", json=dev_payload, headers=headers
    )
    dev_id = dev_res.json()["id"]

    # 3. Supply invalid/mismatched RCA ID
    capa_payload_invalid_rca = {
        "deviation_id": dev_id,
        "rca_id": "non-existent-rca-id",
        "capa_type": "CORRECTIVE",
        "action_plan": "Testing",
    }
    res_rca = client.post(
        "/api/v1/quality/capas", json=capa_payload_invalid_rca, headers=headers
    )
    assert res_rca.status_code == 422
    assert "RCA with ID" in res_rca.json()["detail"]


def test_capa_lifecycle_transitions():
    """
    Verify legal and illegal CAPA status transitions.
    """
    # @req:PRD-SUB-001
    client = TestClient(app)
    headers = get_auth_headers(change_reason="CAPA lifecycle testing")

    # 1. Create deviation and CAPA
    dev_payload = {
        "study_id": "study_123",
        "title": "Protocol violation",
        "description": "Protocol violation desc",
        "severity": "MAJOR",
        "type": "ELIGIBILITY",
    }
    dev_res = client.post(
        "/api/v1/quality/deviations", json=dev_payload, headers=headers
    )
    dev_id = dev_res.json()["id"]

    capa_payload = {
        "deviation_id": dev_id,
        "capa_type": "CORRECTIVE",
        "action_plan": "Do training on eligibility",
    }
    capa_res = client.post("/api/v1/quality/capas", json=capa_payload, headers=headers)
    assert capa_res.status_code == 201
    capa_id = capa_res.json()["id"]
    assert capa_res.json()["status"] == "INITIATED"
    assert capa_res.json()["version_index"] == 1

    # Verify deviation status is CAPA_INITIATED
    dev_view = client.get(f"/api/v1/quality/deviations/{dev_id}", headers=headers)
    assert dev_view.json()["status"] == "CAPA_INITIATED"

    # 2. Transition: INITIATED -> UNDER_REVIEW (Legal)
    trans_res1 = client.post(
        f"/api/v1/quality/capas/{capa_id}/transition",
        json={"to_status": "UNDER_REVIEW", "version_index": 1},
        headers=headers,
    )
    assert trans_res1.status_code == 200
    assert trans_res1.json()["status"] == "UNDER_REVIEW"
    assert trans_res1.json()["version_index"] == 2

    # 3. Transition: UNDER_REVIEW -> EFFECTIVENESS_CHECK (Illegal - bypasses IMPLEMENTATION)
    trans_res_illegal = client.post(
        f"/api/v1/quality/capas/{capa_id}/transition",
        json={"to_status": "EFFECTIVENESS_CHECK", "version_index": 2},
        headers=headers,
    )
    assert trans_res_illegal.status_code == 422
    assert "Invalid transition" in trans_res_illegal.json()["detail"]

    # 4. Transition: UNDER_REVIEW -> IMPLEMENTATION (Legal)
    trans_res2 = client.post(
        f"/api/v1/quality/capas/{capa_id}/transition",
        json={"to_status": "IMPLEMENTATION", "version_index": 2},
        headers=headers,
    )
    assert trans_res2.status_code == 200
    assert trans_res2.json()["status"] == "IMPLEMENTATION"
    assert trans_res2.json()["version_index"] == 3

    # 5. Transition: IMPLEMENTATION -> EFFECTIVENESS_CHECK (Legal)
    trans_res3 = client.post(
        f"/api/v1/quality/capas/{capa_id}/transition",
        json={"to_status": "EFFECTIVENESS_CHECK", "version_index": 3},
        headers=headers,
    )
    assert trans_res3.status_code == 200
    assert trans_res3.json()["status"] == "EFFECTIVENESS_CHECK"
    assert trans_res3.json()["version_index"] == 4

    # 6. Transition: EFFECTIVENESS_CHECK -> CLOSED (Legal)
    action_path = f"/api/v1/quality/capas/{capa_id}/transition"
    sig_token = make_step_up_token(
        user_id="quality_test_user",
        action=action_path,
        semantic_action="quality.capa.close",
    )
    headers_gated = headers.copy()
    headers_gated["X-Sig-Token"] = sig_token
    trans_res4 = client.post(
        f"/api/v1/quality/capas/{capa_id}/transition",
        json={"to_status": "CLOSED", "version_index": 4},
        headers=headers_gated,
    )
    assert trans_res4.status_code == 200
    assert trans_res4.json()["status"] == "CLOSED"
    assert trans_res4.json()["version_index"] == 5

    # Verify deviation is CLOSED following CAPA closure
    dev_view2 = client.get(f"/api/v1/quality/deviations/{dev_id}", headers=headers)
    assert dev_view2.json()["status"] == "CLOSED"
    assert dev_view2.json()["version_index"] > 1

    # 7. Try to transition out of terminal state (Illegal)
    trans_terminal = client.post(
        f"/api/v1/quality/capas/{capa_id}/transition",
        json={"to_status": "UNDER_REVIEW", "version_index": 5},
        headers=headers,
    )
    assert trans_terminal.status_code == 422
    assert "terminal state" in trans_terminal.json()["detail"]


def test_capa_updates_and_concurrency():
    """
    Verify updates to CAPA records and optimistic locking behavior.
    """
    # @req:PRD-SUB-001
    client = TestClient(app)
    headers = get_auth_headers(change_reason="Concurrency testing")

    # 1. Create parent deviation and CAPA
    dev_payload = {
        "study_id": "study_123",
        "title": "IP Temp Excursion",
        "description": "IP excursion",
        "severity": "CRITICAL",
        "type": "IP_MANAGEMENT",
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

    # 2. Update CAPA details (Legal)
    update_res = client.put(
        f"/api/v1/quality/capas/{capa_id}",
        json={"action_plan": "Updated Action Plan", "version_index": 1},
        headers=headers,
    )
    assert update_res.status_code == 200
    assert update_res.json()["action_plan"] == "Updated Action Plan"
    assert update_res.json()["version_index"] == 2

    # 3. Update CAPA with stale version index (Illegal -> 409)
    stale_update_res = client.put(
        f"/api/v1/quality/capas/{capa_id}",
        json={"action_plan": "Stale Update Plan", "version_index": 1},
        headers=headers,
    )
    assert stale_update_res.status_code == 409
    assert "Version conflict" in stale_update_res.json()["detail"]

    # 4. Update parent deviation status compatibility gate
    # Once closed, we can't create CAPA anymore
    client.post(
        f"/api/v1/quality/capas/{capa_id}/transition",
        json={"to_status": "UNDER_REVIEW", "version_index": 2},
        headers=headers,
    )
    client.post(
        f"/api/v1/quality/capas/{capa_id}/transition",
        json={"to_status": "IMPLEMENTATION", "version_index": 3},
        headers=headers,
    )
    client.post(
        f"/api/v1/quality/capas/{capa_id}/transition",
        json={"to_status": "EFFECTIVENESS_CHECK", "version_index": 4},
        headers=headers,
    )
    action_path = f"/api/v1/quality/capas/{capa_id}/transition"
    sig_token = make_step_up_token(
        user_id="quality_test_user",
        action=action_path,
        semantic_action="quality.capa.close",
    )
    headers_gated = headers.copy()
    headers_gated["X-Sig-Token"] = sig_token
    client.post(
        f"/api/v1/quality/capas/{capa_id}/transition",
        json={"to_status": "CLOSED", "version_index": 5},
        headers=headers_gated,
    )

    # 5. Try updating details of a closed CAPA (Illegal -> 422)
    closed_update = client.put(
        f"/api/v1/quality/capas/{capa_id}",
        json={"action_plan": "No changes allowed", "version_index": 6},
        headers=headers,
    )
    assert closed_update.status_code == 422
    assert "terminal state" in closed_update.json()["detail"]


def test_read_only_roles_forbidden():
    """
    Verify that read-only roles (auditor, inspector, viewer, etc.) receive 403 Forbidden on all mutations.
    """
    client = TestClient(app)
    ro_headers = get_auth_headers(roles="auditor", change_reason="Trying to write")

    # 1. Create deviation
    payload = {
        "study_id": "study_123",
        "title": "Unpermitted deviation",
        "description": "Should fail",
        "severity": "MAJOR",
        "type": "INFORMED_CONSENT",
    }
    res = client.post("/api/v1/quality/deviations", json=payload, headers=ro_headers)
    assert res.status_code == 403
    assert "Forbidden" in res.json()["detail"]

    # Let's create a deviation with admin first so we have an ID to test other mutations
    admin_headers = get_auth_headers(
        roles="admin", change_reason="Creating base deviation"
    )
    dev_res = client.post(
        "/api/v1/quality/deviations", json=payload, headers=admin_headers
    )
    assert dev_res.status_code == 201
    dev_id = dev_res.json()["id"]

    # 2. Attach/Update RCA
    rca_payload = {
        "methodology": "5 Whys",
        "investigation_details": "Failed attempt",
        "root_cause_summary": "Should fail",
    }
    rca_res = client.post(
        f"/api/v1/quality/deviations/{dev_id}/rca", json=rca_payload, headers=ro_headers
    )
    assert rca_res.status_code == 403

    # 3. Create CAPA
    capa_payload = {
        "deviation_id": dev_id,
        "capa_type": "CORRECTIVE",
        "action_plan": "Should fail",
    }
    capa_res = client.post(
        "/api/v1/quality/capas", json=capa_payload, headers=ro_headers
    )
    assert capa_res.status_code == 403

    # Create CAPA with admin for transition/update tests
    admin_capa_res = client.post(
        "/api/v1/quality/capas", json=capa_payload, headers=admin_headers
    )
    assert admin_capa_res.status_code == 201
    capa_id = admin_capa_res.json()["id"]

    # 4. Transition CAPA
    trans_res = client.post(
        f"/api/v1/quality/capas/{capa_id}/transition",
        json={"to_status": "UNDER_REVIEW", "version_index": 1},
        headers=ro_headers,
    )
    assert trans_res.status_code == 403

    # 5. Update CAPA
    update_res = client.put(
        f"/api/v1/quality/capas/{capa_id}",
        json={"action_plan": "Should fail", "version_index": 1},
        headers=ro_headers,
    )
    assert update_res.status_code == 403


def test_capa_approval_closure_requires_quality_oversight():
    """
    Verify that general write roles (e.g. cra) can perform general transitions but only quality oversight roles
    (e.g. quality_manager, qa_lead, quality_oversight, admin) can perform approval/closure transitions.
    """
    client = TestClient(app)
    admin_headers = get_auth_headers(
        roles="admin", change_reason="Creating deviation and CAPA"
    )

    # Create deviation
    dev_payload = {
        "study_id": "study_123",
        "title": "Base deviation",
        "description": "Desc",
        "severity": "MAJOR",
        "type": "INFORMED_CONSENT",
    }
    dev_res = client.post(
        "/api/v1/quality/deviations", json=dev_payload, headers=admin_headers
    )
    dev_id = dev_res.json()["id"]

    # Create CAPA
    capa_payload = {
        "deviation_id": dev_id,
        "capa_type": "CORRECTIVE",
        "action_plan": "Action",
    }
    capa_res = client.post(
        "/api/v1/quality/capas", json=capa_payload, headers=admin_headers
    )
    capa_id = capa_res.json()["id"]

    # Check broader write role can transition to UNDER_REVIEW
    cra_headers = get_auth_headers(
        roles="cra", change_reason="Transitioning to under review"
    )
    trans_res1 = client.post(
        f"/api/v1/quality/capas/{capa_id}/transition",
        json={"to_status": "UNDER_REVIEW", "version_index": 1},
        headers=cra_headers,
    )
    assert trans_res1.status_code == 200
    assert trans_res1.json()["status"] == "UNDER_REVIEW"

    # Transition to IMPLEMENTATION via cra
    trans_res2 = client.post(
        f"/api/v1/quality/capas/{capa_id}/transition",
        json={"to_status": "IMPLEMENTATION", "version_index": 2},
        headers=cra_headers,
    )
    assert trans_res2.status_code == 200

    # Transition to EFFECTIVENESS_CHECK via cra
    trans_res3 = client.post(
        f"/api/v1/quality/capas/{capa_id}/transition",
        json={"to_status": "EFFECTIVENESS_CHECK", "version_index": 3},
        headers=cra_headers,
    )
    assert trans_res3.status_code == 200

    # Try transitioning to CLOSED (closure) via general write role (cra) - should fail with 403 (with valid sig-token)
    action_path = f"/api/v1/quality/capas/{capa_id}/transition"
    sig_token_cra = make_step_up_token(
        user_id="quality_test_user",
        action=action_path,
        semantic_action="quality.capa.close",
    )
    cra_headers_gated = cra_headers.copy()
    cra_headers_gated["X-Sig-Token"] = sig_token_cra
    trans_res4_fail = client.post(
        f"/api/v1/quality/capas/{capa_id}/transition",
        json={"to_status": "CLOSED", "version_index": 4},
        headers=cra_headers_gated,
    )
    assert trans_res4_fail.status_code == 403
    assert "Quality oversight role required" in trans_res4_fail.json()["detail"]

    # Successfully transition to CLOSED using a quality oversight role (e.g. quality_manager) with valid sig_token
    qm_headers = get_auth_headers(
        roles="quality_manager", change_reason="Closing CAPA and deviation"
    )
    action_path = f"/api/v1/quality/capas/{capa_id}/transition"
    sig_token = make_step_up_token(
        user_id="quality_test_user",
        action=action_path,
        semantic_action="quality.capa.close",
    )
    qm_headers_gated = qm_headers.copy()
    qm_headers_gated["X-Sig-Token"] = sig_token
    trans_res4_success = client.post(
        f"/api/v1/quality/capas/{capa_id}/transition",
        json={"to_status": "CLOSED", "version_index": 4},
        headers=qm_headers_gated,
    )
    assert trans_res4_success.status_code == 200
    assert trans_res4_success.json()["status"] == "CLOSED"


def test_mutation_without_change_reason_rejected():
    """
    Verify that mutations without X-Change-Reason are rejected by the gateway middleware.
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="admin")
    # Remove X-Change-Reason
    headers.pop("X-Change-Reason", None)

    # Re-sign with empty/none change reason to trigger gateway reject or lack of header
    payload = {
        "study_id": "study_123",
        "title": "Failed deviation",
        "description": "Missing reason",
        "severity": "MINOR",
        "type": "OTHER",
    }
    res = client.post("/api/v1/quality/deviations", json=payload, headers=headers)
    assert res.status_code in (401, 403)
    assert "change justification" in res.json()["detail"].lower()


def test_successful_mutation_creates_audit_log_and_is_atomic():
    """
    Verify that every successful mutation creates an atomic audit log entry with detailed information.
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="admin", change_reason="Initial mutation testing")

    # 1. Post a deviation
    payload = {
        "study_id": "study_123",
        "title": "Audit-logged Deviation",
        "description": "Testing audit log creation",
        "severity": "CRITICAL",
        "type": "PROTOCOL_PROCEDURE",
    }
    res = client.post("/api/v1/quality/deviations", json=payload, headers=headers)
    assert res.status_code == 201
    dev_id = res.json()["id"]

    # 2. Get audit logs
    audit_res = client.get("/api/v1/quality/audit-logs", headers=headers)
    assert audit_res.status_code == 200
    logs = audit_res.json()

    # Find DEVIATION_CREATE log
    create_log = next(
        (log for log in logs if log["action"] == "DEVIATION_CREATE"), None
    )
    assert create_log is not None
    assert create_log["user_id"] == "quality_test_user"
    assert create_log["user_role"] == "admin"
    assert create_log["record_id"] == dev_id
    assert create_log["change_reason"] == "Initial mutation testing"
    assert (
        "Testing audit log creation" in create_log["details"]
        or "Audit-logged Deviation" in create_log["details"]
    )


def test_audit_log_endpoint_properties():
    """
    Verify that the audit logs endpoint:
    - returns entries in newest-first (descending chronological) order.
    - exposes no write endpoints (POST/PUT/DELETE return 405).
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="admin", change_reason="Audit check")

    # Perform multiple actions to generate chronological logs
    payload = {
        "study_id": "study_123",
        "title": "Dev 1",
        "description": "Desc 1",
        "severity": "MINOR",
        "type": "OTHER",
    }
    client.post("/api/v1/quality/deviations", json=payload, headers=headers)

    payload["title"] = "Dev 2"
    client.post("/api/v1/quality/deviations", json=payload, headers=headers)

    # Fetch audit logs
    res = client.get("/api/v1/quality/audit-logs", headers=headers)
    assert res.status_code == 200
    logs = res.json()

    assert len(logs) >= 2
    # Newest first: let's assert timestamps are descending
    from datetime import datetime

    timestamps = [datetime.fromisoformat(log["timestamp"]) for log in logs]
    for i in range(len(timestamps) - 1):
        assert timestamps[i] >= timestamps[i + 1]

    # Verify write endpoints are blocked with 405 Method Not Allowed
    res_post = client.post("/api/v1/quality/audit-logs", json={}, headers=headers)
    assert res_post.status_code == 405


def test_permission_failure_leaves_no_misleading_audit_entry():
    """
    Verify that if permission checks fail, no domain entities are created/modified,
    and no misleading audit log is written (atomicity & consistency).
    """
    client = TestClient(app)
    ro_headers = get_auth_headers(roles="auditor", change_reason="Unpermitted attempt")

    # Count database rows or get current audit logs count
    admin_headers = get_auth_headers(roles="admin", change_reason="Counting")
    initial_audit_res = client.get("/api/v1/quality/audit-logs", headers=admin_headers)
    initial_log_count = len(initial_audit_res.json())
    assert initial_log_count >= 0

    # Try creating deviation with auditor
    payload = {
        "study_id": "study_123",
        "title": "Forbidden deviation",
        "description": "No record should be created",
        "severity": "MINOR",
        "type": "OTHER",
    }
    res = client.post("/api/v1/quality/deviations", json=payload, headers=ro_headers)
    assert res.status_code == 403

    # Check deviations count (should be empty for this study_id since the POST failed)
    dev_list_res = client.get(
        "/api/v1/quality/deviations?study_id=study_123", headers=admin_headers
    )
    assert len(dev_list_res.json()) == 0

    # Check audit logs (excluding the listing action, the count of mutation-related logs should have not increased)
    final_audit_res = client.get("/api/v1/quality/audit-logs", headers=admin_headers)
    final_logs = final_audit_res.json()
    # Find any mutation-related actions like DEVIATION_CREATE. None should exist for "Forbidden deviation"
    assert not any(
        log["action"] == "DEVIATION_CREATE" and "Forbidden deviation" in log["details"]
        for log in final_logs
    )


def test_transition_capa_sig_token_matrix():
    """
    Test missing, valid, mismatched, expired, and replayed tokens for CAPA status transitions.
    """
    client = TestClient(app)
    headers = get_auth_headers(
        roles="quality_manager", change_reason="Capa transition signature validation"
    )

    # 1. Create parent deviation
    dev_payload = {
        "study_id": "study_123",
        "title": "Deviation for Matrix Test",
        "description": "Base description",
        "severity": "MINOR",
        "type": "OTHER",
    }
    dev_res = client.post(
        "/api/v1/quality/deviations", json=dev_payload, headers=headers
    )
    dev_id = dev_res.json()["id"]

    # 2. Create CAPA
    capa_payload = {
        "deviation_id": dev_id,
        "capa_type": "CORRECTIVE",
        "action_plan": "Action plan",
    }
    capa_res = client.post("/api/v1/quality/capas", json=capa_payload, headers=headers)
    capa_id = capa_res.json()["id"]

    # Transition to UNDER_REVIEW, then IMPLEMENTATION, then EFFECTIVENESS_CHECK (not gated)
    client.post(
        f"/api/v1/quality/capas/{capa_id}/transition",
        json={"to_status": "UNDER_REVIEW", "version_index": 1},
        headers=headers,
    )
    client.post(
        f"/api/v1/quality/capas/{capa_id}/transition",
        json={"to_status": "IMPLEMENTATION", "version_index": 2},
        headers=headers,
    )
    client.post(
        f"/api/v1/quality/capas/{capa_id}/transition",
        json={"to_status": "EFFECTIVENESS_CHECK", "version_index": 3},
        headers=headers,
    )

    action_path = f"/api/v1/quality/capas/{capa_id}/transition"

    # Test missing token
    res_missing = client.post(
        action_path, json={"to_status": "CLOSED", "version_index": 4}, headers=headers
    )
    assert res_missing.status_code == 401

    # Test expired token
    t_expired = make_step_up_token(
        user_id="quality_test_user",
        action=action_path,
        semantic_action="quality.capa.close",
        expired=True,
    )
    headers_expired = headers.copy()
    headers_expired["X-Sig-Token"] = t_expired
    res_expired = client.post(
        action_path,
        json={"to_status": "CLOSED", "version_index": 4},
        headers=headers_expired,
    )
    assert res_expired.status_code == 401

    # Test mismatched semantic action (CANCEL instead of CLOSE)
    t_mismatched_semantic = make_step_up_token(
        user_id="quality_test_user",
        action=action_path,
        semantic_action="quality.capa.cancel",
    )
    headers_mismatched_semantic = headers.copy()
    headers_mismatched_semantic["X-Sig-Token"] = t_mismatched_semantic
    res_mismatched_semantic = client.post(
        action_path,
        json={"to_status": "CLOSED", "version_index": 4},
        headers=headers_mismatched_semantic,
    )
    assert res_mismatched_semantic.status_code == 401

    # Test mismatched user
    t_mismatched_user = make_step_up_token(
        user_id="wrong_user", action=action_path, semantic_action="quality.capa.close"
    )
    headers_mismatched_user = headers.copy()
    headers_mismatched_user["X-Sig-Token"] = t_mismatched_user
    res_mismatched_user = client.post(
        action_path,
        json={"to_status": "CLOSED", "version_index": 4},
        headers=headers_mismatched_user,
    )
    assert res_mismatched_user.status_code == 401

    # Test valid token
    t_valid = make_step_up_token(
        user_id="quality_test_user",
        action=action_path,
        semantic_action="quality.capa.close",
    )
    headers_valid = headers.copy()
    headers_valid["X-Sig-Token"] = t_valid
    res_valid = client.post(
        action_path,
        json={"to_status": "CLOSED", "version_index": 4},
        headers=headers_valid,
    )
    assert res_valid.status_code == 200

    # Test replayed token (resubmit with same token)
    res_replayed = client.post(
        action_path,
        json={"to_status": "CLOSED", "version_index": 4},
        headers=headers_valid,
    )
    assert res_replayed.status_code == 401
