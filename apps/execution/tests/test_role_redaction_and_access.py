import time

import pytest
import pytest_asyncio
from fastapi import HTTPException
from fastapi.testclient import TestClient

from apps.execution.database.core import db_manager as exec_db_manager
from apps.execution.database.models import Base as ExecBase
from apps.execution.database.models import (
    ClinicalSubject,
    ClinicalVisit,
    SubjectRandomization,
)
from apps.execution.main import app as exec_app
from packages.testing.security import generate_signature
from packages.security import TrialRole, check_trial_role, enforce_site_isolation
from packages.security.audit_logger import audit_logger_engine
from packages.security.rbac import Principal


@pytest_asyncio.fixture(autouse=True)
async def setup_dbs():
    """Setup in-memory SQLite database for testing Execution APIs."""
    exec_db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with exec_db_manager.engine.begin() as conn:
        await conn.run_sync(ExecBase.metadata.create_all)
    yield
    async with exec_db_manager.engine.begin() as conn:
        await conn.run_sync(ExecBase.metadata.drop_all)
    await exec_db_manager.close()


def get_auth_headers(
    roles: str = "admin", site_id: str = "", change_reason: str = "Authorized change"
) -> dict:
    """Helper to generate valid gateway V2 signed headers for testing."""
    timestamp = str(time.time())
    user_id = "test_user_uuid"
    sig = generate_signature(
        user_id,
        roles,
        timestamp,
        version="2",
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


class DummyRequest:
    def __init__(self, roles_val, ip_address="127.0.0.1"):
        self.state = DummyState(roles_val, ip_address)
        self.headers = {}
        self.client = DummyClient(ip_address)


class DummyState:
    def __init__(self, roles_val, ip_address):
        self.roles = roles_val
        self.ip_address = ip_address


class DummyClient:
    def __init__(self, host):
        self.host = host


# =====================================================================
# Task 1 Tests: TrialRole Enum and Helpers & Site Isolation Guard
# =====================================================================


def test_trial_role_enum_and_helper():
    """Verify TrialRole enum covers the required roles and check_trial_role resolves correctly."""
    assert TrialRole.SITE_PI == "principal_investigator"
    assert TrialRole.CRA_MONITOR == "cra"
    assert TrialRole.DATA_MANAGER == "sponsor_dm"
    assert TrialRole.UNBLINDED_STATISTICIAN == "unblinded_statistician"
    assert TrialRole.IDMC == "idmc"
    assert TrialRole.PHARMACIST == "pharmacist"

    # Match investigator
    req1 = DummyRequest("principal_investigator")
    assert check_trial_role(req1, TrialRole.SITE_PI) is True
    assert check_trial_role(req1, TrialRole.CRA_MONITOR) is False

    # Match list format / synonyms
    req2 = DummyRequest(["cra", "monitor"])
    assert check_trial_role(req2, TrialRole.CRA_MONITOR) is True

    # Match data manager
    req3 = DummyRequest("sponsor_dm")
    assert check_trial_role(req3, TrialRole.DATA_MANAGER) is True


def test_site_isolation_guard_and_audit():
    """Verify site isolation guard (PRD-SYS-004) raises 403 and writes security audit alerts."""
    # Reset audit chain
    audit_logger_engine._chain = []

    req = DummyRequest("investigator", ip_address="10.0.0.5")
    principal = Principal(
        user_id="user_uuid_123",
        roles=["investigator"],
        assigned_sites=["site_boston"],
        unblinded_access=False,
    )

    # Accessing same site -> OK
    enforce_site_isolation(req, "site_boston", principal)
    assert len(audit_logger_engine._chain) == 0

    # Accessing another site -> Blocked + Security Alert logged
    with pytest.raises(HTTPException) as exc:
        enforce_site_isolation(req, "site_chicago", principal)

    assert exc.value.status_code == 403
    assert "unauthorized" in exc.value.detail.lower()

    # Verify audit alert
    assert len(audit_logger_engine._chain) == 1
    alert = audit_logger_engine._chain[0]
    assert alert.action_type == "SECURITY_ALERT"
    assert alert.user_id == "user_uuid_123"
    assert alert.details["ip_address"] == "10.0.0.5"
    assert alert.details["requested_site_id"] == "site_chicago"
    assert "site_boston" in alert.details["assigned_sites"]


# =====================================================================
# Task 2 Tests: Dynamic Blinding Redaction on subject/visit GET APIs
# =====================================================================


@pytest.mark.asyncio
async def test_get_subject_api_blinding_and_isolation():
    """Test subject GET endpoint applies site isolation and dynamic blinding."""
    client = TestClient(exec_app)

    # 1. Seed database with subject and randomization
    async with exec_db_manager.get_session_maker()() as session, session.begin():
        subj = ClinicalSubject(
            subject_id="SUBJ_BOSTON", study_id="study_001", site_id="site_boston"
        )
        session.add(subj)
        await session.flush()

        # Transition to ENROLLED then to RANDOMIZED
        subj.status = "ENROLLED"
        subj.status = "RANDOMIZED"

        # Seed a randomized treatment assignment
        # Encrypted allocation block (reusing standard crypt format)
        # From apps/execution/cryptography.py: encrypt returns hex-encoded payload
        from apps.execution.cryptography import AllocationKeyManager

        key_mgr = AllocationKeyManager()
        await key_mgr.load_from_db(session)
        encrypted = key_mgr.encrypt({"allocation": "Active Treatment Arm"})

        rand = SubjectRandomization(
            study_id="study_001",
            site_id="site_boston",
            subject_id="SUBJ_BOSTON",
            encrypted_allocation=encrypted,
            kit_reference="IP-KIT-999",
        )
        session.add(rand)

    # 2. Query subject as Admin (unblinded, globally authorized)
    headers_admin = get_auth_headers(roles="admin")
    resp_admin = client.get(
        "/api/v1/execution/subjects/SUBJ_BOSTON", headers=headers_admin
    )
    assert resp_admin.status_code == 200
    data_admin = resp_admin.json()
    assert data_admin["subject_id"] == "SUBJ_BOSTON"
    assert data_admin["treatment_group"] == "Active Treatment Arm"
    assert data_admin["randomization_seed"] == "12345"
    assert data_admin["investigational_product_id"] == "IP-KIT-999"

    # 3. Query subject as Site Investigator from same site (blinded, site-authorized)
    headers_inv_boston = get_auth_headers(roles="investigator", site_id="site_boston")
    resp_inv = client.get(
        "/api/v1/execution/subjects/SUBJ_BOSTON", headers=headers_inv_boston
    )
    assert resp_inv.status_code == 200
    data_inv = resp_inv.json()
    assert data_inv["subject_id"] == "SUBJ_BOSTON"
    # Should be blinded!
    assert data_inv["treatment_group"] == "MASKED"
    assert data_inv["randomization_seed"] == "MASKED"
    assert data_inv["investigational_product_id"] == "MASKED"

    # 4. Query subject as Site Investigator from another site (blocked by site isolation)
    headers_inv_chicago = get_auth_headers(roles="investigator", site_id="site_chicago")
    resp_bad = client.get(
        "/api/v1/execution/subjects/SUBJ_BOSTON", headers=headers_inv_chicago
    )
    assert resp_bad.status_code == 403


@pytest.mark.asyncio
async def test_get_visit_api_blinding_and_isolation():
    """Test visit GET endpoint applies site isolation and dynamic blinding."""
    client = TestClient(exec_app)

    visit_id = "visit_999"

    # 1. Seed database with subject, visit and randomization
    async with exec_db_manager.get_session_maker()() as session, session.begin():
        subj = ClinicalSubject(
            subject_id="SUBJ_BOSTON", study_id="study_001", site_id="site_boston"
        )
        session.add(subj)
        await session.flush()

        # Transition to ENROLLED then to RANDOMIZED
        subj.status = "ENROLLED"
        subj.status = "RANDOMIZED"

        from apps.execution.cryptography import AllocationKeyManager

        key_mgr = AllocationKeyManager()
        await key_mgr.load_from_db(session)
        encrypted = key_mgr.encrypt({"allocation": "Active Treatment Arm"})

        rand = SubjectRandomization(
            study_id="study_001",
            site_id="site_boston",
            subject_id="SUBJ_BOSTON",
            encrypted_allocation=encrypted,
            kit_reference="IP-KIT-999",
        )
        session.add(rand)

        visit = ClinicalVisit(
            id=visit_id,
            subject_id="SUBJ_BOSTON",
            visit_name="Week 4 Follow-up",
            study_id="study_001",
        )
        session.add(visit)

    # 2. Query visit as Admin (unblinded, globally authorized)
    headers_admin = get_auth_headers(roles="admin")
    resp_admin = client.get(
        f"/api/v1/execution/visits/{visit_id}", headers=headers_admin
    )
    assert resp_admin.status_code == 200
    data_admin = resp_admin.json()
    assert data_admin["id"] == visit_id
    assert data_admin["treatment_group"] == "Active Treatment Arm"
    assert data_admin["randomization_seed"] == "12345"
    assert data_admin["investigational_product_id"] == "IP-KIT-999"

    # 3. Query visit as Unblinded Statistician (unblinded)
    headers_stat = get_auth_headers(roles="unblinded_statistician")
    resp_stat = client.get(f"/api/v1/execution/visits/{visit_id}", headers=headers_stat)
    assert resp_stat.status_code == 200
    data_stat = resp_stat.json()
    assert data_stat["treatment_group"] == "Active Treatment Arm"
    assert data_stat["randomization_seed"] == "12345"
    assert data_stat["investigational_product_id"] == "IP-KIT-999"

    # 4. Query visit as Site Investigator from same site (blinded, site-authorized)
    headers_inv_boston = get_auth_headers(roles="investigator", site_id="site_boston")
    resp_inv = client.get(
        f"/api/v1/execution/visits/{visit_id}", headers=headers_inv_boston
    )
    assert resp_inv.status_code == 200
    data_inv = resp_inv.json()
    assert data_inv["treatment_group"] == "MASKED"
    assert data_inv["randomization_seed"] == "MASKED"
    assert data_inv["investigational_product_id"] == "MASKED"

    # 5. Query visit as Site Investigator from another site (blocked by site isolation)
    headers_inv_chicago = get_auth_headers(roles="investigator", site_id="site_chicago")
    resp_bad = client.get(
        f"/api/v1/execution/visits/{visit_id}", headers=headers_inv_chicago
    )
    assert resp_bad.status_code == 403
