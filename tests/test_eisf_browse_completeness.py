import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.eisf.database import db_manager
from apps.eisf.main import app as eisf_app
from apps.eisf.models import Base, ISFAuditLog
from tests.test_eisf_api import get_eisf_auth_headers


@pytest_asyncio.fixture(autouse=True)
async def setup_eisf_db_for_browse():
    """
    Setup in-memory eISF database for testing endpoints.
    """
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_eisf_document_listing_with_binder_filters() -> None:
    """
    Verify listing with binder_section/binder_classification and site claims.
    """
    client = TestClient(eisf_app)
    headers = get_eisf_auth_headers(
        user_id="pi-boston",
        roles="site investigator",
        site_id="site-boston-01",
    )

    # Ingest test documents
    payload1 = {
        "study_id": "study-100",
        "site_id": "site-boston-01",
        "binder_classification": "Investigator CV",
        "filename": "cv.pdf",
        "content": "CV content",
        "mime_type": "application/pdf",
        "reason_for_change": "Initial filing of CV",
    }
    client.post("/api/v1/eisf/documents", json=payload1, headers=headers)

    payload2 = {
        "study_id": "study-100",
        "site_id": "site-boston-01",
        "binder_classification": "FDA Form 1572",
        "filename": "1572.pdf",
        "content": "1572 content",
        "mime_type": "application/pdf",
        "reason_for_change": "Filing Form 1572",
    }
    client.post("/api/v1/eisf/documents", json=payload2, headers=headers)

    # 1. List all documents for site-boston-01
    resp = client.get("/api/v1/eisf/documents?study_id=study-100", headers=headers)
    assert resp.status_code == 200
    docs = resp.json()
    assert len(docs) == 2

    # 2. Filter by binder_section
    resp_filter1 = client.get(
        "/api/v1/eisf/documents?study_id=study-100&binder_section=Investigator CV",
        headers=headers,
    )
    assert resp_filter1.status_code == 200
    assert len(resp_filter1.json()) == 1
    assert resp_filter1.json()[0]["binder_classification"] == "Investigator CV"

    # 3. Filter by binder_classification
    resp_filter2 = client.get(
        "/api/v1/eisf/documents?study_id=study-100&binder_classification=FDA Form 1572",
        headers=headers,
    )
    assert resp_filter2.status_code == 200
    assert len(resp_filter2.json()) == 1
    assert resp_filter2.json()[0]["binder_classification"] == "FDA Form 1572"

    # Verify audit logs
    async with db_manager.get_session_maker()() as session:
        stmt = select(ISFAuditLog).where(ISFAuditLog.action == "LIST")
        res = await session.execute(stmt)
        audit_logs = res.scalars().all()
        assert len(audit_logs) >= 3


@pytest.mark.asyncio
async def test_eisf_document_view_and_download_site_isolation() -> None:
    """
    Verify document metadata view and content download endpoints, enforcing site isolation.
    """
    client = TestClient(eisf_app)

    # Create document at site-boston-01 using admin headers
    admin_headers = get_eisf_auth_headers(
        user_id="admin-user", roles="admin", site_id="site-boston-01"
    )
    payload = {
        "study_id": "study-100",
        "site_id": "site-boston-01",
        "binder_classification": "Investigator CV",
        "filename": "cv.pdf",
        "content": "Secret CV content",
        "mime_type": "application/pdf",
        "reason_for_change": "Admin prepopulation",
    }
    create_resp = client.post(
        "/api/v1/eisf/documents", json=payload, headers=admin_headers
    )
    assert create_resp.status_code == 201
    doc_id = create_resp.json()["id"]

    # 1. Authorized Boston Investigator access
    boston_headers = get_eisf_auth_headers(
        user_id="pi-boston",
        roles="site investigator",
        site_id="site-boston-01",
    )

    # View metadata
    view_resp = client.get(f"/api/v1/eisf/documents/{doc_id}", headers=boston_headers)
    assert view_resp.status_code == 200
    assert view_resp.json()["filename"] == "cv.pdf"

    # Download content
    download_resp = client.get(
        f"/api/v1/eisf/documents/{doc_id}/download", headers=boston_headers
    )
    assert download_resp.status_code == 200
    assert download_resp.text == "Secret CV content"
    assert download_resp.headers["content-type"] == "application/pdf"
    assert "attachment; filename=cv.pdf" in download_resp.headers["content-disposition"]

    # 2. Unauthorized London Investigator access -> should be blocked with 403
    london_headers = get_eisf_auth_headers(
        user_id="pi-london",
        roles="site investigator",
        site_id="site-london-02",
    )

    view_resp_london = client.get(
        f"/api/v1/eisf/documents/{doc_id}", headers=london_headers
    )
    assert view_resp_london.status_code == 403

    download_resp_london = client.get(
        f"/api/v1/eisf/documents/{doc_id}/download", headers=london_headers
    )
    assert download_resp_london.status_code == 403

    # Verify audit logs
    async with db_manager.get_session_maker()() as session:
        # Check VIEW audit log
        stmt_view = select(ISFAuditLog).where(ISFAuditLog.action == "VIEW")
        res_view = await session.execute(stmt_view)
        assert len(res_view.scalars().all()) == 1

        # Check DOWNLOAD audit log
        stmt_dl = select(ISFAuditLog).where(ISFAuditLog.action == "DOWNLOAD")
        res_dl = await session.execute(stmt_dl)
        assert len(res_dl.scalars().all()) == 1


@pytest.mark.asyncio
async def test_eisf_completeness_workflow() -> None:
    """
    Test the eISF completeness checking endpoint, ensuring accurate comparison
    of filed artifacts to required binder artifact set.
    """
    client = TestClient(eisf_app)
    headers = get_eisf_auth_headers(
        user_id="pi-boston",
        roles="site investigator",
        site_id="site-boston-01",
    )

    # 1. Check completeness initially (should be incomplete, all 6 artifacts missing)
    comp_resp = client.get(
        "/api/v1/eisf/completeness?study_id=study-100", headers=headers
    )
    assert comp_resp.status_code == 200
    data = comp_resp.json()
    assert data["site_id"] == "site-boston-01"
    assert data["is_complete"] is False

    # Check sections
    sections = {s["section_name"]: s for s in data["sections"]}
    assert len(sections) == 3
    assert "Investigator & Staff" in sections
    assert "Protocols & Amendments" in sections
    assert "Regulatory Approvals" in sections

    assert len(sections["Investigator & Staff"]["missing"]) == 2
    assert len(sections["Investigator & Staff"]["present"]) == 0

    # 2. Ingest "Investigator CV"
    payload1 = {
        "study_id": "study-100",
        "site_id": "site-boston-01",
        "binder_classification": "Investigator CV",
        "filename": "cv.pdf",
        "content": "CV content",
        "mime_type": "application/pdf",
        "reason_for_change": "Initial filing of CV",
    }
    client.post("/api/v1/eisf/documents", json=payload1, headers=headers)

    # Check completeness again
    comp_resp = client.get(
        "/api/v1/eisf/completeness?study_id=study-100", headers=headers
    )
    data = comp_resp.json()
    sections = {s["section_name"]: s for s in data["sections"]}
    assert "Investigator CV" in sections["Investigator & Staff"]["present"]
    assert "Delegation of Authority Log" in sections["Investigator & Staff"]["missing"]
    assert data["is_complete"] is False

    # 3. Ingest all remaining required artifacts (case-insensitively)
    remaining_payloads = [
        ("Investigator & Staff", "Delegation of Authority Log", "doa.pdf"),
        ("Protocols & Amendments", "Approved Protocol", "protocol.pdf"),
        ("Protocols & Amendments", "Protocol Sign-off", "signoff.pdf"),
        ("Regulatory Approvals", "IRB Approval", "irb.pdf"),
        ("Regulatory Approvals", "FDA Form 1572", "1572.pdf"),
    ]

    for section, classification, filename in remaining_payloads:
        p = {
            "study_id": "study-100",
            "site_id": "site-boston-01",
            "binder_classification": classification,
            "filename": filename,
            "content": f"{classification} content",
            "mime_type": "application/pdf",
            "reason_for_change": f"Filing {classification}",
        }
        client.post("/api/v1/eisf/documents", json=p, headers=headers)

    # Check completeness now -> should be complete!
    comp_resp = client.get(
        "/api/v1/eisf/completeness?study_id=study-100", headers=headers
    )
    assert comp_resp.status_code == 200
    data = comp_resp.json()
    assert data["is_complete"] is True

    # Re-verify sections have everything present
    for s in data["sections"]:
        assert len(s["missing"]) == 0
        assert len(s["present"]) == len(s["required_artifacts"])

    # Verify audit log entry
    async with db_manager.get_session_maker()() as session:
        stmt = select(ISFAuditLog).where(ISFAuditLog.action == "COMPLETENESS")
        res = await session.execute(stmt)
        audit_logs = res.scalars().all()
        assert len(audit_logs) >= 3


@pytest.mark.asyncio
async def test_eisf_auditor_view_and_download_permissions() -> None:
    """
    Verify that read-only auditors can list, view, download, and check completeness
    but are blocked from write mutations.
    """
    client = TestClient(eisf_app)
    auditor_headers = get_eisf_auth_headers(
        user_id="auditor-01", roles="auditor", site_id="site-boston-01"
    )

    # Prepopulate document using admin
    admin_headers = get_eisf_auth_headers(
        user_id="admin", roles="admin", site_id="site-boston-01"
    )
    payload = {
        "study_id": "study-100",
        "site_id": "site-boston-01",
        "binder_classification": "Approved Protocol",
        "filename": "protocol.pdf",
        "content": "Protocol contents",
        "mime_type": "application/pdf",
        "reason_for_change": "Admin setup",
    }
    create_resp = client.post(
        "/api/v1/eisf/documents", json=payload, headers=admin_headers
    )
    assert create_resp.status_code == 201
    doc_id = create_resp.json()["id"]

    # 1. Auditor list
    list_resp = client.get(
        "/api/v1/eisf/documents?study_id=study-100", headers=auditor_headers
    )
    assert list_resp.status_code == 200
    assert len(list_resp.json()) == 1

    # 2. Auditor view metadata
    view_resp = client.get(f"/api/v1/eisf/documents/{doc_id}", headers=auditor_headers)
    assert view_resp.status_code == 200

    # 3. Auditor download content
    dl_resp = client.get(
        f"/api/v1/eisf/documents/{doc_id}/download", headers=auditor_headers
    )
    assert dl_resp.status_code == 200
    assert dl_resp.text == "Protocol contents"

    # 4. Auditor completeness check
    comp_resp = client.get(
        "/api/v1/eisf/completeness?study_id=study-100", headers=auditor_headers
    )
    assert comp_resp.status_code == 200
    assert comp_resp.json()["is_complete"] is False

    # 5. Auditor write attempt should fail
    resp_create = client.post(
        "/api/v1/eisf/documents", json=payload, headers=auditor_headers
    )
    assert resp_create.status_code == 403
