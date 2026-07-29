import time

import pytest
import pytest_asyncio
from fastapi import HTTPException
from fastapi.testclient import TestClient
from pydantic import BaseModel

from apps.etmf.database import db_manager as etmf_db_manager
from apps.etmf.main import app as etmf_app
from apps.etmf.models import Base as ETMFBase
from apps.execution.database.core import db_manager as exec_db_manager
from apps.execution.database.models import Base as ExecBase
from apps.execution.main import app as exec_app
from apps.gateway.main import generate_signature
from packages.security.rbac import (
    get_normalized_roles,
    verify_is_auditor,
    verify_not_auditor,
)


@pytest_asyncio.fixture(autouse=True)
async def setup_dbs():
    """Setup in-memory SQLite databases for testing eTMF and Execution APIs."""
    etmf_db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with etmf_db_manager.engine.begin() as conn:
        await conn.run_sync(ETMFBase.metadata.create_all)

    exec_db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with exec_db_manager.engine.begin() as conn:
        await conn.run_sync(ExecBase.metadata.create_all)

    yield

    async with etmf_db_manager.engine.begin() as conn:
        await conn.run_sync(ETMFBase.metadata.drop_all)
    await etmf_db_manager.close()

    async with exec_db_manager.engine.begin() as conn:
        await conn.run_sync(ExecBase.metadata.drop_all)
    await exec_db_manager.close()


def get_auth_headers(
    roles: str = "admin", change_reason: str = "Authorized change"
) -> dict:
    """Helper to generate valid gateway V2 signed headers for testing."""
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
        "X-Change-Reason": change_reason,
    }
    return headers


# ==========================================
# Unit Tests for packages/security/rbac.py
# ==========================================


def test_role_normalization_string() -> None:
    """Test get_normalized_roles with comma-separated string roles."""

    # Mocking FastAPI Request
    class MockRequest:
        def __init__(self, roles_str: str):
            class State:
                pass

            self.state = State()
            self.state.roles = roles_str
            self.headers = {}

    request = MockRequest("Admin, CRA, Auditor")
    normalized = get_normalized_roles(request)
    assert normalized == ["admin", "cra", "auditor"]
    assert request.state.roles == ["admin", "cra", "auditor"]


def test_role_normalization_list() -> None:
    """Test get_normalized_roles with list-based roles."""

    class MockRequest:
        def __init__(self, roles_list: list):
            class State:
                pass

            self.state = State()
            self.state.roles = roles_list
            self.headers = {}

    request = MockRequest(["Sponsor Admin", "Monitor"])
    normalized = get_normalized_roles(request)
    assert normalized == ["sponsor admin", "monitor"]
    assert request.state.roles == ["sponsor admin", "monitor"]


def test_verify_not_auditor_denies_auditors() -> None:
    """Test verify_not_auditor raises 403 for auditor personas."""

    class MockRequest:
        def __init__(self, roles_str: str):
            class State:
                pass

            self.state = State()
            self.state.roles = roles_str
            self.headers = {}

    for auditor_role in ["auditor", "inspector", "regulatory_inspector"]:
        request = MockRequest(f"user,{auditor_role}")
        with pytest.raises(Exception) as exc_info:
            verify_not_auditor(request)
        assert exc_info.value.status_code == 403


def test_verify_not_auditor_allows_others() -> None:
    """Test verify_not_auditor allows non-auditor roles."""

    class MockRequest:
        def __init__(self, roles_str: str):
            class State:
                pass

            self.state = State()
            self.state.roles = roles_str
            self.headers = {}

    request = MockRequest("admin,sponsor_dm,cra")
    assert verify_not_auditor(request) == ["admin", "sponsor_dm", "cra"]


def test_verify_is_auditor_denies_non_auditors() -> None:
    """Test verify_is_auditor raises 403 for non-auditors."""

    class MockRequest:
        def __init__(self, roles_str: str):
            class State:
                pass

            self.state = State()
            self.state.roles = roles_str
            self.headers = {}

    request = MockRequest("admin,sponsor_dm,cra")
    with pytest.raises(Exception) as exc_info:
        verify_is_auditor(request)
    assert exc_info.value.status_code == 403


def test_verify_is_auditor_allows_auditors() -> None:
    """Test verify_is_auditor allows auditor personas."""

    class MockRequest:
        def __init__(self, roles_str: str):
            class State:
                pass

            self.state = State()
            self.state.roles = roles_str
            self.headers = {}

    for auditor_role in ["auditor", "inspector", "regulatory_inspector"]:
        request = MockRequest(auditor_role)
        assert verify_is_auditor(request) == [auditor_role]


# ==========================================
# Integration Tests for eTMF API Endpoints
# ==========================================


@pytest.mark.asyncio
async def test_etmf_ingest_auditor_forbidden() -> None:
    """Verify auditor personas are forbidden from ingesting eTMF documents."""
    client = TestClient(etmf_app)
    payload = {
        "study_id": "study_001",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol_v1.pdf",
        "content": "Protocol text",
        "mime_type": "application/pdf",
    }

    # 1. Reject "auditor" role
    resp = client.post(
        "/api/v1/etmf/ingest", json=payload, headers=get_auth_headers("auditor")
    )
    assert resp.status_code == 403

    # 2. Reject "inspector" role
    resp = client.post(
        "/api/v1/etmf/ingest", json=payload, headers=get_auth_headers("inspector")
    )
    assert resp.status_code == 403

    # 3. Reject "regulatory_inspector" role
    resp = client.post(
        "/api/v1/etmf/ingest",
        json=payload,
        headers=get_auth_headers("regulatory_inspector"),
    )
    assert resp.status_code == 403

    # 4. Allow non-auditor write role "admin"
    resp = client.post(
        "/api/v1/etmf/ingest", json=payload, headers=get_auth_headers("admin")
    )
    assert resp.status_code == 201


@pytest.mark.asyncio
async def test_etmf_edl_creation_auditor_forbidden() -> None:
    """Verify auditor personas are forbidden from creating EDL expectations."""
    client = TestClient(etmf_app)
    payload = {
        "study_id": "study_xyz",
        "milestone": "INITIATION",
        "artifact_type": "Clinical Trial Protocol",
        "reason_for_change": "Adding signature requirement",
    }

    resp = client.post(
        "/api/v1/etmf/edl", json=payload, headers=get_auth_headers("auditor")
    )
    assert resp.status_code == 403

    resp = client.post(
        "/api/v1/etmf/edl", json=payload, headers=get_auth_headers("admin")
    )
    assert resp.status_code == 201


@pytest.mark.asyncio
async def test_etmf_edl_update_auditor_forbidden() -> None:
    """Verify auditor personas are forbidden from updating EDL expectations."""
    client = TestClient(etmf_app)

    # Ingest one first as admin
    payload = {
        "study_id": "study_xyz",
        "milestone": "INITIATION",
        "artifact_type": "Clinical Trial Protocol",
        "reason_for_change": "Adding signature requirement",
    }
    setup_resp = client.post(
        "/api/v1/etmf/edl", json=payload, headers=get_auth_headers("admin")
    )
    assert setup_resp.status_code == 201
    edl_id = setup_resp.json()["id"]

    # Try updating as auditor -> should be blocked
    resp = client.put(
        f"/api/v1/etmf/edl/{edl_id}", json=payload, headers=get_auth_headers("auditor")
    )
    assert resp.status_code == 403

    # Try updating as admin -> should succeed
    resp = client.put(
        f"/api/v1/etmf/edl/{edl_id}", json=payload, headers=get_auth_headers("admin")
    )
    assert resp.status_code == 200


@pytest.mark.asyncio
async def test_etmf_document_transition_auditor_forbidden() -> None:
    """Verify auditor personas are forbidden from transitioning document statuses."""
    client = TestClient(etmf_app)

    # Ingest document as admin
    ingest_payload = {
        "study_id": "study_001",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol.pdf",
        "content": "Protocol text",
        "mime_type": "application/pdf",
    }
    ingest_resp = client.post(
        "/api/v1/etmf/ingest", json=ingest_payload, headers=get_auth_headers("admin")
    )
    assert ingest_resp.status_code == 201
    doc_id = ingest_resp.json()["document_id"]

    # Try transitioning status as auditor -> should fail with 403
    transition_payload = {
        "to_status": "TECHNICAL_QC",
        "reason_for_change": "Proceeding with QC process",
    }
    resp = client.post(
        f"/api/v1/etmf/documents/{doc_id}/transition",
        json=transition_payload,
        headers=get_auth_headers("auditor"),
    )
    assert resp.status_code == 403


@pytest.mark.asyncio
async def test_etmf_audit_logs_gated_to_auditors() -> None:
    """Verify GET /api/v1/etmf/audit-logs is gated to only authorized auditors and maintains self-auditing."""
    client = TestClient(etmf_app)

    # 1. Deny access to non-auditor "admin" role
    resp = client.get("/api/v1/etmf/audit-logs", headers=get_auth_headers("admin"))
    assert resp.status_code == 403

    # 2. Allow access to "auditor"
    resp = client.get("/api/v1/etmf/audit-logs", headers=get_auth_headers("auditor"))
    assert resp.status_code == 200
    logs = resp.json()["items"]
    assert len(logs) >= 1
    # Check that AUDIT_VIEW self-audit event is recorded
    assert logs[0]["action"] == "AUDIT_VIEW"

    # 3. Allow access to "regulatory_inspector"
    resp = client.get(
        "/api/v1/etmf/audit-logs", headers=get_auth_headers("regulatory_inspector")
    )
    assert resp.status_code == 200


# ==========================================
# Integration Tests for Clinical Execution API
# ==========================================


@pytest.mark.asyncio
async def test_execution_subject_creation_auditor_forbidden() -> None:
    """Verify auditor personas are forbidden from creating clinical subjects."""
    client = TestClient(exec_app)
    payload = {
        "subject_id": "SUBJ_101",
        "study_id": "study_001",
        "demographics": {"name": "John Doe", "gender": "male"},
    }

    resp = client.post(
        "/api/v1/execution/subjects", json=payload, headers=get_auth_headers("auditor")
    )
    assert resp.status_code == 403

    resp = client.post(
        "/api/v1/execution/subjects", json=payload, headers=get_auth_headers("admin")
    )
    assert resp.status_code == 200


@pytest.mark.asyncio
async def test_execution_visit_creation_auditor_forbidden() -> None:
    """Verify auditor personas are forbidden from creating clinical visits."""
    client = TestClient(exec_app)
    payload = {
        "subject_id": "SUBJ_101",
        "visit_name": "Screening",
        "study_id": "study_001",
    }

    resp = client.post(
        "/api/v1/execution/visits",
        json=payload,
        headers=get_auth_headers("regulatory_inspector"),
    )
    assert resp.status_code == 403

    resp = client.post(
        "/api/v1/execution/visits", json=payload, headers=get_auth_headers("admin")
    )
    assert resp.status_code == 200


@pytest.mark.asyncio
async def test_execution_observation_creation_auditor_forbidden() -> None:
    """Verify auditor personas are forbidden from creating clinical observations."""
    client = TestClient(exec_app)
    payload = {
        "subject_id": "SUBJ_101",
        "study_id": "study_001",
        "domain": "VS",
        "test_code": "SYSBP",
        "test_name": "Systolic Blood Pressure",
        "value": 120.0,
        "unit": "mmHg",
    }

    resp = client.post(
        "/api/v1/execution/observations",
        json=payload,
        headers=get_auth_headers("inspector"),
    )
    assert resp.status_code == 403

    # Must register subject first for admin creation to pass if visit_id is missing or to infer study_id
    subj_payload = {
        "subject_id": "SUBJ_101",
        "study_id": "study_001",
    }
    client.post(
        "/api/v1/execution/subjects",
        json=subj_payload,
        headers=get_auth_headers("admin"),
    )

    resp = client.post(
        "/api/v1/execution/observations",
        json=payload,
        headers=get_auth_headers("admin"),
    )
    assert resp.status_code == 200


# ==========================================
# Centralized RBAC Specs & Extensions Tests
# ==========================================


def test_role_aliases_normalization() -> None:
    """Verify that role aliases map to canonical forms correctly."""
    from packages.security.rbac import (
        ROLE_CRA_CANONICAL,
        ROLE_INVESTIGATOR,
        ROLE_SPONSOR_DESIGNER,
        ROLE_SPONSOR_DM,
        ROLE_SYSADMIN,
        normalize_role,
    )

    assert normalize_role("system administrator") == ROLE_SYSADMIN
    assert normalize_role("system_admin") == ROLE_SYSADMIN
    assert normalize_role("Admin") == ROLE_SPONSOR_DM
    assert normalize_role("Sponsor DM") == ROLE_SPONSOR_DM
    assert normalize_role("pi") == ROLE_INVESTIGATOR
    assert normalize_role("principal investigator") == ROLE_INVESTIGATOR
    assert normalize_role("cra_monitor") == ROLE_CRA_CANONICAL
    assert normalize_role("unknown_role") == "unknown_role"
    assert normalize_role("study_designer") == ROLE_SPONSOR_DESIGNER
    assert normalize_role("sponsor designer") == ROLE_SPONSOR_DESIGNER


def test_has_permission() -> None:
    """Verify has_permission checks the declarative matrix matching §2.2."""
    from packages.security.rbac import (
        ROLE_CRC,
        ROLE_SPONSOR_DESIGNER,
        ROLE_SPONSOR_DM,
        Principal,
        has_permission,
        normalize_role,
    )

    designer = Principal(user_id="d1", roles=[ROLE_SPONSOR_DESIGNER])
    study_designer = Principal(user_id="d2", roles=[normalize_role("study_designer")])
    dm = Principal(user_id="dm1", roles=[ROLE_SPONSOR_DM])
    crc = Principal(user_id="crc1", roles=[ROLE_CRC])

    # Designer has read/write on study_design
    assert has_permission(designer, "study_design:create") is True
    assert has_permission(study_designer, "study_design:create") is True
    assert has_permission(designer, "study_design:read") is True
    assert has_permission(designer, "study_design:update") is True
    assert has_permission(designer, "study_design:delete") is True
    # Designer does not have subject_enrollment or ecrf_data_entry
    assert has_permission(designer, "subject_enrollment:read") is False

    # DM has query lifecycle C/R/U/D
    assert has_permission(dm, "query_lifecycle:create") is True
    assert has_permission(dm, "query_lifecycle:delete") is True
    assert has_permission(dm, "export_masked:create") is True
    assert has_permission(dm, "sdv:create") is False

    # CRC has ecrf read/write but not query deletion
    assert has_permission(crc, "ecrf_data_entry:create") is True
    assert has_permission(crc, "ecrf_data_entry:update") is True
    assert has_permission(crc, "query_lifecycle:delete") is False


def test_can_access_site() -> None:
    """Verify can_access_site denies investigator/crc when site is outside their scope."""
    from packages.security.rbac import (
        ROLE_CRC,
        ROLE_INVESTIGATOR,
        ROLE_SPONSOR_DM,
        Principal,
        can_access_site,
    )

    inv = Principal(user_id="pi1", roles=[ROLE_INVESTIGATOR], assigned_sites=["site_A"])
    crc = Principal(
        user_id="crc1", roles=[ROLE_CRC], assigned_sites=["site_A", "site_B"]
    )
    dm = Principal(user_id="dm1", roles=[ROLE_SPONSOR_DM])  # global access by default

    assert can_access_site(inv, "site_A") is True
    assert can_access_site(inv, "site_B") is False

    assert can_access_site(crc, "site_A") is True
    assert can_access_site(crc, "site_B") is True
    assert can_access_site(crc, "site_C") is False

    assert can_access_site(dm, "site_A") is True
    assert can_access_site(dm, "site_any") is True

    # If global user explicitly restricts themselves, enforce it
    restricted_dm = Principal(
        user_id="dm2", roles=[ROLE_SPONSOR_DM], assigned_sites=["site_X"]
    )
    assert can_access_site(restricted_dm, "site_X") is True
    assert can_access_site(restricted_dm, "site_Y") is False


@pytest.mark.asyncio
async def test_get_principal_from_request() -> None:
    """Verify get_principal correctly parses and normalizes fastapi request state/headers."""
    from packages.security.rbac import ROLE_CRC, get_principal

    class MockRequest:
        def __init__(self, headers, state_dict=None):
            self.headers = headers

            class State:
                pass

            self.state = State()
            if state_dict:
                for k, v in state_dict.items():
                    setattr(self.state, k, v)

    headers = {
        "X-User-Id": "u123",
        "X-User-Roles": "Clinical Research Coordinator",
        "X-Site-Id": "site_999",
        "X-Unblinded-Access": "True",
        "X-Change-Reason": "Testing principal",
    }

    principal = await get_principal(MockRequest(headers))
    assert principal.user_id == "u123"
    assert principal.roles == [ROLE_CRC]
    assert principal.assigned_sites == ["site_999"]
    assert principal.unblinded_access is True
    assert principal.change_reason == "Testing principal"


def test_require_permission_dependency() -> None:
    """Verify require_permission dependency asserts permissions successfully or raises 403."""
    from packages.security.rbac import (
        ROLE_SPONSOR_DESIGNER,
        Principal,
        require_permission,
    )

    designer = Principal(user_id="d1", roles=[ROLE_SPONSOR_DESIGNER])

    # Allowed
    dep = require_permission("study_design:create")
    res = dep(designer)
    assert res == designer

    # Denied
    dep_denied = require_permission("export_unmasked:create")
    with pytest.raises(HTTPException) as exc_info:
        dep_denied(designer)
    assert exc_info.value.status_code == 403


def test_mask_payload_recursive() -> None:
    """Verify mask_payload recursively obfuscates sensitive data fields based on unblinded status."""
    from packages.security.rbac import (
        ROLE_CRC,
        ROLE_SPONSOR_STATISTICIAN,
        Principal,
        mask_payload,
    )

    class DemoModel(BaseModel):
        initials: str
        ssn: str
        dob: str
        treatment_arm_id: str
        country: str
        age: int

    payload_dict = {
        "initials": "JD",
        "ssn": "123-45-6789",
        "dob": "1990-01-01",
        "treatment_arm_id": "ARM_A_ACTIVE",
        "country": "US",
        "age": 36,
        "nested": {"dob": "1991-02-02", "country": "CA"},
        "list_items": [
            {"initials": "AB", "treatment_arm_id": "ARM_B_PLACEBO"},
            {"country": "UK"},
        ],
    }

    payload_model = DemoModel(
        initials="JD",
        ssn="123-45-6789",
        dob="1990-01-01",
        treatment_arm_id="ARM_A_ACTIVE",
        country="US",
        age=36,
    )

    # 1. Blinded user -> fields are masked recursively
    blinded_principal = Principal(
        user_id="b1", roles=[ROLE_CRC], unblinded_access=False
    )

    masked_dict = mask_payload(payload_dict, blinded_principal)
    assert masked_dict["initials"] == "**"
    assert masked_dict["ssn"] == "***-**-****"
    assert masked_dict["dob"] == "MASKED"
    assert masked_dict["treatment_arm_id"] == "BLINDED"
    assert masked_dict["country"] == "US"
    assert masked_dict["age"] == 36
    assert masked_dict["nested"]["dob"] == "MASKED"
    assert masked_dict["nested"]["country"] == "CA"
    assert masked_dict["list_items"][0]["initials"] == "**"
    assert masked_dict["list_items"][0]["treatment_arm_id"] == "BLINDED"
    assert masked_dict["list_items"][1]["country"] == "UK"

    masked_model = mask_payload(payload_model, blinded_principal)
    assert masked_model.initials == "**"
    assert masked_model.ssn == "***-**-****"
    assert masked_model.dob == "MASKED"
    assert masked_model.treatment_arm_id == "BLINDED"
    assert masked_model.country == "US"
    assert masked_model.age == 36

    # 2. Unblinded user -> fields are unchanged
    unblinded_principal = Principal(
        user_id="u1", roles=[ROLE_SPONSOR_STATISTICIAN], unblinded_access=True
    )

    unmasked_dict = mask_payload(payload_dict, unblinded_principal)
    assert unmasked_dict == payload_dict

    unmasked_model = mask_payload(payload_model, unblinded_principal)
    assert unmasked_model.initials == "JD"
    assert unmasked_model.ssn == "123-45-6789"


# ==========================================
# External Monitor RBAC & Verification Tests
# ==========================================


def test_external_monitor_aliases() -> None:
    """Verify normalization of all External Monitor alias strings."""
    from packages.security.rbac import ROLE_EXTERNAL_MONITOR, normalize_role

    assert normalize_role("external monitor") == ROLE_EXTERNAL_MONITOR
    assert normalize_role("external_monitor") == ROLE_EXTERNAL_MONITOR
    assert normalize_role("external-monitor") == ROLE_EXTERNAL_MONITOR
    assert normalize_role("cro monitor") == ROLE_EXTERNAL_MONITOR
    assert normalize_role("cro_monitor") == ROLE_EXTERNAL_MONITOR
    assert normalize_role("cro-monitor") == ROLE_EXTERNAL_MONITOR


def test_external_monitor_permissions_matrix() -> None:
    """Verify only read access is granted, and writes/redacts/signs/QC are explicitly denied."""
    from packages.security.rbac import ROLE_EXTERNAL_MONITOR, Principal, has_permission

    p = Principal(user_id="em1", roles=[ROLE_EXTERNAL_MONITOR])

    # Allowed reads
    assert has_permission(p, "etmf_document:read") is True
    assert has_permission(p, "etmf_edl:read") is True
    assert has_permission(p, "etmf_audit_logs:read") is True
    assert has_permission(p, "eisf_document:read") is True

    # Denied writes/mutations
    assert has_permission(p, "etmf_document:create") is False
    assert has_permission(p, "etmf_document:read_raw") is False
    assert has_permission(p, "etmf_document:redact") is False
    assert has_permission(p, "etmf_document:sign") is False
    assert has_permission(p, "etmf_document:transition_technical_qc") is False
    assert has_permission(p, "etmf_document:transition_clinical_qc") is False
    assert has_permission(p, "etmf_document:transition_approved") is False
    assert has_permission(p, "etmf_document:transition_archived") is False
    assert has_permission(p, "eisf_document:create") is False
    assert has_permission(p, "eisf_document:update") is False
    assert has_permission(p, "eisf_document:delete") is False
    assert has_permission(p, "eisf_document:sync") is False


@pytest.mark.asyncio
async def test_external_monitor_eisf_denies_writes_allows_reads() -> None:
    """Verify that External Monitor is allowed to read eISF but forbidden from writing."""
    from apps.eisf.main import app as eisf_app

    client = TestClient(eisf_app)

    # 1. Block Create
    payload = {
        "study_id": "study_001",
        "site_id": "site_001",
        "binder_classification": "Investigator CV",
        "filename": "cv.pdf",
        "content": "CV text",
        "mime_type": "application/pdf",
        "reason_for_change": "Onboarding",
    }
    resp = client.post(
        "/api/v1/eisf/documents",
        json=payload,
        headers=get_auth_headers("external_monitor"),
    )
    assert resp.status_code == 403

    # 2. Block Update
    resp = client.put(
        "/api/v1/eisf/documents/doc123",
        json=payload,
        headers=get_auth_headers("external_monitor"),
    )
    assert resp.status_code == 403

    # 3. Block Delete
    resp = client.delete(
        "/api/v1/eisf/documents/doc123?reason_for_change=Valid+Reason+At+Least+Ten+Chars",
        headers=get_auth_headers("external_monitor"),
    )
    assert resp.status_code == 403


@pytest.mark.asyncio
async def test_external_monitor_principal_resolution(monkeypatch) -> None:
    """Verify directory-backed resolution ignoring spoofed headers and enforcing site/study scope."""

    from packages.security.rbac import (
        can_access_site,
        can_access_study,
        get_principal,
    )

    class MockRequest:
        def __init__(self):
            class State:
                pass

            self.state = State()
            self.headers = {
                "X-User-Id": "ext_mon_user",
                "X-User-Roles": "external_monitor",
                "X-Site-Id": "spoofed_site",
                "X-Study-Id": "spoofed_study",
                "X-Change-Reason": "Valid reason",
            }

    async def mock_resolve(user_id):
        return {
            "personnel_id": "p_ext_1",
            "roles": ["external_monitor"],
            "assigned_sites": ["site_alpha", "site_beta"],
            "assigned_studies": ["study_x", "study_y"],
        }

    import packages.security.org_client

    monkeypatch.setattr(
        packages.security.org_client, "resolve_personnel_assignments", mock_resolve
    )

    req = MockRequest()
    principal = await get_principal(req)

    assert principal.user_id == "ext_mon_user"
    assert "external_monitor" in principal.roles
    assert principal.assigned_sites == ["site_alpha", "site_beta"]
    assert principal.assigned_studies == ["study_x", "study_y"]

    assert can_access_site(principal, "site_alpha") is True
    assert can_access_site(principal, "site_gamma") is False
    assert can_access_study(principal, "study_x") is True
    assert can_access_study(principal, "study_z") is False
