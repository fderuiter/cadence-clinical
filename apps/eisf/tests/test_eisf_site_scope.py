import time

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient

from apps.eisf.adapters.database import db_manager
from apps.eisf.adapters.models import Base
from apps.eisf.main import app as eisf_app
from apps.gateway.main import generate_signature


@pytest_asyncio.fixture(autouse=True)
async def setup_eisf_db():
    """
    Setup in-memory eISF database for testing.
    """
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_global_auth_headers(
    roles: str = "admin", change_reason: str = "Test Change Reason Long enough"
) -> dict:
    """
    Helper to generate valid global (unscoped) V2 signed headers.
    """
    timestamp = str(time.time())
    user_id = "test_user"
    sig = generate_signature(
        user_id, roles, timestamp, version="2", change_reason=change_reason
    )
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }


def get_site_auth_headers(
    roles: str = "investigator",
    site_id: str = "site_alpha",
    change_reason: str = "Test Change Reason Long enough",
) -> dict:
    """
    Helper to generate valid site-scoped V2 signed headers.
    """
    timestamp = str(time.time())
    user_id = f"user_{site_id}"
    sig = generate_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        version="2",
        change_reason=change_reason,
        site_id=site_id,
    )
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Site-Id": site_id,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }


@pytest.mark.asyncio
async def test_eisf_site_scoped_users_read_isolation():
    """
    Verify site-scoped users can only access matching site records, and cannot see other sites.
    """
    client = TestClient(eisf_app)
    admin_headers = get_global_auth_headers(
        roles="admin", change_reason="Ingest records long reason"
    )

    # Ingest document for site_alpha
    res_alpha = client.post(
        "/api/v1/eisf/documents",
        json={
            "study_id": "study_001",
            "site_id": "site_alpha",
            "binder_classification": "Investigator CV",
            "filename": "cv_alpha.pdf",
            "content": "CV alpha content",
            "mime_type": "application/pdf",
            "reason_for_change": "Initial filing of CV at Alpha site",
        },
        headers=admin_headers,
    )
    assert res_alpha.status_code == 201
    doc_alpha_id = res_alpha.json()["id"]

    # Ingest document for site_beta
    res_beta = client.post(
        "/api/v1/eisf/documents",
        json={
            "study_id": "study_001",
            "site_id": "site_beta",
            "binder_classification": "Investigator CV",
            "filename": "cv_beta.pdf",
            "content": "CV beta content",
            "mime_type": "application/pdf",
            "reason_for_change": "Initial filing of CV at Beta site",
        },
        headers=admin_headers,
    )
    assert res_beta.status_code == 201
    doc_beta_id = res_beta.json()["id"]

    # Set up site_alpha credentials
    alpha_headers = get_site_auth_headers(roles="investigator", site_id="site_alpha")

    # 1. List checks: alpha user should ONLY see site_alpha CV
    list_resp = client.get("/api/v1/eisf/documents", headers=alpha_headers)
    assert list_resp.status_code == 200
    listed_ids = [d["id"] for d in list_resp.json()]
    assert doc_alpha_id in listed_ids
    assert doc_beta_id not in listed_ids

    # 2. View checks
    assert (
        client.get(
            f"/api/v1/eisf/documents/{doc_alpha_id}", headers=alpha_headers
        ).status_code
        == 200
    )
    assert (
        client.get(
            f"/api/v1/eisf/documents/{doc_beta_id}", headers=alpha_headers
        ).status_code
        == 403
    )

    # 3. Download checks
    assert (
        client.get(
            f"/api/v1/eisf/documents/{doc_alpha_id}/download", headers=alpha_headers
        ).status_code
        == 200
    )
    assert (
        client.get(
            f"/api/v1/eisf/documents/{doc_beta_id}/download", headers=alpha_headers
        ).status_code
        == 403
    )


@pytest.mark.asyncio
async def test_eisf_site_scoped_write_restrictions():
    """
    Verify site-scoped users can only ingest documents for their own assigned site.
    """
    client = TestClient(eisf_app)
    alpha_headers = get_site_auth_headers(
        roles="cra", site_id="site_alpha", change_reason="Ingest CV details"
    )

    # Ingest for same site -> should succeed
    payload_ok = {
        "study_id": "study_001",
        "site_id": "site_alpha",
        "binder_classification": "Investigator CV",
        "filename": "cv_smith.pdf",
        "content": "Dr Smith CV.",
        "mime_type": "application/pdf",
        "reason_for_change": "Ingest CV details of Smith",
    }
    assert (
        client.post(
            "/api/v1/eisf/documents", json=payload_ok, headers=alpha_headers
        ).status_code
        == 201
    )

    # Ingest for another site -> should fail
    payload_bad = {
        "study_id": "study_001",
        "site_id": "site_beta",
        "binder_classification": "Investigator CV",
        "filename": "cv_jones.pdf",
        "content": "Dr Jones CV.",
        "mime_type": "application/pdf",
        "reason_for_change": "Ingest CV details of Jones",
    }
    assert (
        client.post(
            "/api/v1/eisf/documents", json=payload_bad, headers=alpha_headers
        ).status_code
        == 403
    )


@pytest.mark.asyncio
async def test_eisf_external_monitor_role(monkeypatch):
    """
    Verify External Monitor role restricts access via resolved assigned_sites.
    """
    client = TestClient(eisf_app)
    admin_headers = get_global_auth_headers(
        roles="admin", change_reason="Setup study docs"
    )

    # Seed site_alpha and site_beta documents
    client.post(
        "/api/v1/eisf/documents",
        json={
            "study_id": "study_001",
            "site_id": "site_alpha",
            "binder_classification": "Investigator CV",
            "filename": "cv_alpha.pdf",
            "content": "CV alpha content",
            "mime_type": "application/pdf",
            "reason_for_change": "Filing CV of Investigator alpha",
        },
        headers=admin_headers,
    )
    client.post(
        "/api/v1/eisf/documents",
        json={
            "study_id": "study_001",
            "site_id": "site_beta",
            "binder_classification": "Investigator CV",
            "filename": "cv_beta.pdf",
            "content": "CV beta content",
            "mime_type": "application/pdf",
            "reason_for_change": "Filing CV of Investigator beta",
        },
        headers=admin_headers,
    )

    # Mock resolve_personnel_assignments to return site_alpha but not site_beta
    async def mock_resolve(keycloak_user_id: str):
        return {
            "personnel_id": "person_em",
            "roles": ["external_monitor"],
            "assigned_sites": ["site_alpha"],
            "assigned_studies": ["study_001"],
        }

    monkeypatch.setattr(
        "packages.security.org_client.resolve_personnel_assignments",
        mock_resolve,
    )

    # Generate external monitor headers
    em_headers = get_site_auth_headers(roles="external_monitor", site_id="site_alpha")

    # List documents: should only see site_alpha
    list_resp = client.get("/api/v1/eisf/documents", headers=em_headers)
    assert list_resp.status_code == 200
    listed_filenames = [d["filename"] for d in list_resp.json()]
    assert "cv_alpha.pdf" in listed_filenames
    assert "cv_beta.pdf" not in listed_filenames


@pytest.mark.asyncio
async def test_eisf_sponsor_admin_global_visibility():
    """
    Verify that sponsor/admin roles (with no assigned sites) can view documents globally.
    """
    client = TestClient(eisf_app)
    admin_headers = get_global_auth_headers(
        roles="admin", change_reason="Setup docs long enough reason"
    )

    # Ingest document for site_alpha and site_beta
    client.post(
        "/api/v1/eisf/documents",
        json={
            "study_id": "study_001",
            "site_id": "site_alpha",
            "binder_classification": "Investigator CV",
            "filename": "cv_alpha.pdf",
            "content": "CV alpha content",
            "mime_type": "application/pdf",
            "reason_for_change": "Filing CV of Alpha site",
        },
        headers=admin_headers,
    )
    client.post(
        "/api/v1/eisf/documents",
        json={
            "study_id": "study_001",
            "site_id": "site_beta",
            "binder_classification": "Investigator CV",
            "filename": "cv_beta.pdf",
            "content": "CV beta content",
            "mime_type": "application/pdf",
            "reason_for_change": "Filing CV of Beta site",
        },
        headers=admin_headers,
    )

    # List documents as admin: should see site_alpha when queried specifically
    list_resp = client.get(
        "/api/v1/eisf/documents?site_id=site_alpha", headers=admin_headers
    )
    assert list_resp.status_code == 200
    listed_filenames = [d["filename"] for d in list_resp.json()]
    assert "cv_alpha.pdf" in listed_filenames
    assert "cv_beta.pdf" not in listed_filenames

    # List documents as admin for site_beta
    list_resp_beta = client.get(
        "/api/v1/eisf/documents?site_id=site_beta", headers=admin_headers
    )
    assert list_resp_beta.status_code == 200
    listed_filenames_beta = [d["filename"] for d in list_resp_beta.json()]
    assert "cv_beta.pdf" in listed_filenames_beta
    assert "cv_alpha.pdf" not in listed_filenames_beta


@pytest.mark.asyncio
async def test_eisf_completeness_site_isolation():
    """
    Verify get_binder_completeness enforces site isolation.
    """
    client = TestClient(eisf_app)
    alpha_headers = get_site_auth_headers(roles="investigator", site_id="site_alpha")

    # Access completeness for matching site
    resp_ok = client.get(
        "/api/v1/eisf/completeness?study_id=study_001&site_id=site_alpha",
        headers=alpha_headers,
    )
    assert resp_ok.status_code == 200

    # Access completeness for other site is ignored or filtered to matched site
    # The response site_id will reflect site_alpha because out-of-scope query params are ignored/overridden
    resp_other = client.get(
        "/api/v1/eisf/completeness?study_id=study_001&site_id=site_beta",
        headers=alpha_headers,
    )
    assert resp_other.status_code == 200
    assert resp_other.json()["site_id"] == "site_alpha"
