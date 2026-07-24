import time

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.eisf.database import db_manager
from apps.eisf.main import app as eisf_app
from apps.eisf.models import Base, ISFAuditLog
from apps.gateway.main import generate_signature


@pytest_asyncio.fixture(autouse=True)
async def setup_eisf_db():
    """
    Setup in-memory eISF database for testing FastAPI endpoints.
    """
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_eisf_auth_headers(
    user_id: str = "test_user_eisf",
    roles: str = "admin",
    site_id: str = None,
    change_reason: str = "Valid Change Reason",
) -> dict:
    """
    Helper to generate valid gateway V2 signed headers for testing.
    """
    timestamp = str(time.time())
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
    if site_id:
        headers["X-Site-Id"] = site_id
    return headers


# =====================================================================
# Health Check Endpoint
# =====================================================================


def test_eisf_health_unauthenticated() -> None:
    """
    Verify /health is reachable without gateway authentication.
    """
    client = TestClient(eisf_app)
    resp = client.get("/health")
    assert resp.status_code == 200
    assert resp.json() == {"status": "ok", "service": "eisf"}


# =====================================================================
# Gateway Authentication Gates
# =====================================================================


def test_eisf_documents_endpoint_blocks_unauthenticated() -> None:
    """
    Verify GET /api/v1/eisf/documents blocks requests without gateway authentication.
    """
    client = TestClient(eisf_app)
    resp = client.get("/api/v1/eisf/documents")
    assert resp.status_code == 401


# =====================================================================
# Site isolation (PRD-SYS-004) & Auditing (PRD-SYS-001) Tests
# =====================================================================


@pytest.mark.asyncio
async def test_eisf_document_lifecycle_same_site() -> None:
    """
    Verify that an authorized site user (investigator) can successfully
    perform CRUD operations on documents belonging to their assigned site.
    """
    client = TestClient(eisf_app)
    headers = get_eisf_auth_headers(
        user_id="pi-boston",
        roles="site investigator",
        site_id="site-boston-01",
    )

    # 1. Create document
    payload = {
        "study_id": "study-100",
        "site_id": "site-boston-01",
        "binder_classification": "Delegation of Authority Log",
        "filename": "doa_boston_2026.pdf",
        "content": "DOA Log Boston site",
        "mime_type": "application/pdf",
        "metadata_json": {"version": "1.0"},
        "correlation_key": "corr-doa-boston",
        "content_checksum": "checksum-123",
        "source_system": "eISF",
        "reason_for_change": "Initial upload of DOA log",
    }
    create_resp = client.post("/api/v1/eisf/documents", json=payload, headers=headers)
    assert create_resp.status_code == 201
    doc_id = create_resp.json()["id"]
    assert create_resp.json()["filename"] == "doa_boston_2026.pdf"
    assert create_resp.json()["version_index"] == 1

    # 2. Retrieve document
    get_resp = client.get(f"/api/v1/eisf/documents/{doc_id}", headers=headers)
    assert get_resp.status_code == 200
    assert get_resp.json()["content"] == "DOA Log Boston site"

    # 3. List documents (should return the created document)
    list_resp = client.get(
        "/api/v1/eisf/documents?site_id=site-boston-01", headers=headers
    )
    assert list_resp.status_code == 200
    assert len(list_resp.json()) == 1
    assert list_resp.json()[0]["id"] == doc_id

    # 4. Update document
    update_payload = payload.copy()
    update_payload["content"] = "Updated DOA Log Boston site content"
    update_payload["reason_for_change"] = "Update DOA log with new signatures"
    update_resp = client.put(
        f"/api/v1/eisf/documents/{doc_id}", json=update_payload, headers=headers
    )
    assert update_resp.status_code == 200
    assert update_resp.json()["content"] == "Updated DOA Log Boston site content"
    assert update_resp.json()["version_index"] == 2

    # 5. Delete document
    delete_resp = client.delete(
        f"/api/v1/eisf/documents/{doc_id}?reason_for_change=Archived_obsolete_DOA",
        headers=headers,
    )
    assert delete_resp.status_code == 204


@pytest.mark.asyncio
async def test_eisf_document_cross_site_rejection_and_audit() -> None:
    """
    Verify that an investigator belonging to site-boston-01 is blocked from
    accessing, updating, deleting, or listing site-london-02 documents.
    Also, verify that a SECURITY_ALERT audit trail record is generated upon failure.
    """
    client = TestClient(eisf_app)

    # Pre-populate one document at site-london-02 (using admin headers to bypass isolation check)
    admin_headers = get_eisf_auth_headers(
        user_id="admin-user", roles="admin", site_id="site-london-02"
    )
    london_payload = {
        "study_id": "study-100",
        "site_id": "site-london-02",
        "binder_classification": "Investigator CVs",
        "filename": "london_cv.pdf",
        "content": "London investigator CV content",
        "mime_type": "application/pdf",
        "reason_for_change": "Admin pre-population",
    }
    setup_resp = client.post(
        "/api/v1/eisf/documents", json=london_payload, headers=admin_headers
    )
    assert setup_resp.status_code == 201
    london_doc_id = setup_resp.json()["id"]

    # Site investigator at Boston site tries to access/modify London documents
    boston_headers = get_eisf_auth_headers(
        user_id="pi-boston",
        roles="site investigator",
        site_id="site-boston-01",
    )

    # 1. Attempt to create document for London site
    cross_create_payload = london_payload.copy()
    cross_create_payload["reason_for_change"] = "Unauthorized cross site create"
    resp_create = client.post(
        "/api/v1/eisf/documents", json=cross_create_payload, headers=boston_headers
    )
    assert resp_create.status_code == 403

    # 2. Attempt to read London document
    resp_get = client.get(
        f"/api/v1/eisf/documents/{london_doc_id}", headers=boston_headers
    )
    assert resp_get.status_code == 403

    # 3. Attempt to update London document
    cross_update_payload = london_payload.copy()
    cross_update_payload["reason_for_change"] = "Unauthorized cross site update"
    resp_update = client.put(
        f"/api/v1/eisf/documents/{london_doc_id}",
        json=cross_update_payload,
        headers=boston_headers,
    )
    assert resp_update.status_code == 403

    # 4. Attempt to delete London document
    resp_delete = client.delete(
        f"/api/v1/eisf/documents/{london_doc_id}?reason_for_change=Unauthorized_delete",
        headers=boston_headers,
    )
    assert resp_delete.status_code == 403

    # 5. Verify SECURITY_ALERT audit trail is written in database
    async with db_manager.get_session_maker()() as session:
        stmt = select(ISFAuditLog).where(ISFAuditLog.action == "SECURITY_ALERT")
        res = await session.execute(stmt)
        alerts = res.scalars().all()

        assert (
            len(alerts) >= 4
        )  # Each rejected attempt should have generated an audit event!
        for alert in alerts:
            assert alert.actor_id == "pi-boston"
            assert "pi-boston" in alert.details
            assert "site-london-02" in alert.details
            assert alert.actor_role == "site investigator"


# =====================================================================
# Auditor Personas Read-only Restriction Tests
# =====================================================================


@pytest.mark.asyncio
async def test_eisf_auditor_write_forbidden() -> None:
    """
    Verify that auditor roles cannot perform write operations (create/update/delete).
    """
    client = TestClient(eisf_app)
    auditor_headers = get_eisf_auth_headers(
        user_id="auditor-01", roles="auditor", site_id="site-boston-01"
    )

    # 1. Reject create
    payload = {
        "study_id": "study-100",
        "site_id": "site-boston-01",
        "binder_classification": "Investigator CVs",
        "filename": "cv_test.pdf",
        "content": "CV content",
        "mime_type": "application/pdf",
        "reason_for_change": "Auditor try to create",
    }
    resp_create = client.post(
        "/api/v1/eisf/documents", json=payload, headers=auditor_headers
    )
    assert resp_create.status_code == 403

    # Pre-populate one document using admin headers
    admin_headers = get_eisf_auth_headers(
        user_id="admin", roles="admin", site_id="site-boston-01"
    )
    setup_resp = client.post(
        "/api/v1/eisf/documents", json=payload, headers=admin_headers
    )
    doc_id = setup_resp.json()["id"]

    # 2. Reject update
    resp_update = client.put(
        f"/api/v1/eisf/documents/{doc_id}", json=payload, headers=auditor_headers
    )
    assert resp_update.status_code == 403

    # 3. Reject delete
    resp_delete = client.delete(
        f"/api/v1/eisf/documents/{doc_id}?reason_for_change=auditor_delete",
        headers=auditor_headers,
    )
    assert resp_delete.status_code == 403

    # 4. Allow read operations (retrieve/list)
    resp_get = client.get(f"/api/v1/eisf/documents/{doc_id}", headers=auditor_headers)
    assert resp_get.status_code == 200

    resp_list = client.get("/api/v1/eisf/documents", headers=auditor_headers)
    assert resp_list.status_code == 200
    assert len(resp_list.json()) == 1
