"""Integration test suite for eISF regulatory binder browsing and site permission enforcement API endpoints.

Requirements: PRD-SYS-001
"""

import time

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient

from apps.eisf.database import db_manager
from apps.eisf.main import app as eisf_app
from apps.eisf.models import Base
from apps.gateway.main import generate_signature
from packages.security.audit_logger import audit_logger_engine as CentralAuditLogger


@pytest_asyncio.fixture(autouse=True)
async def setup_eisf_db():
    """Setup in-memory eISF database for testing."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    # Clear the global CentralAuditLogger chain for clean testing
    CentralAuditLogger._chain.clear()
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_site_auth_headers(
    roles: str = "investigator",
    site_id: str = "site_alpha",
    change_reason: str = "Test Change Reason Long enough",
) -> dict:
    """Helper to generate valid site-scoped V2 signed headers."""
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
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Site-Id": site_id,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }
    return headers


def get_global_auth_headers(
    roles: str = "admin",
    change_reason: str = "Test Change Reason Long enough",
) -> dict:
    """Helper to generate valid global unscoped V2 signed headers."""
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


@pytest.mark.asyncio
async def test_get_site_eisf_binder_authorized() -> None:
    """Test 200 OK fetching site binder for authorized site staff.

    Requirements: PRD-SYS-001
    """
    client = TestClient(eisf_app)
    headers = get_site_auth_headers(roles="investigator", site_id="site_alpha")

    response = client.get(
        "/api/v1/eisf/sites/site_alpha/binder",
        headers=headers,
    )

    assert response.status_code == 200
    data = response.json()
    assert isinstance(data, list)
    assert len(data) == 4
    section_codes = [node["section_code"] for node in data]
    assert "SEC_01" in section_codes
    assert "SEC_02" in section_codes


@pytest.mark.asyncio
async def test_get_site_eisf_binder_unauthorized_cross_site() -> None:
    """Test 403 Forbidden when user attempts cross-site access.

    Requirements: PRD-SYS-001
    """
    client = TestClient(eisf_app)
    # site_alpha user trying to fetch site_beta binder
    headers = get_site_auth_headers(roles="investigator", site_id="site_alpha")

    response = client.get(
        "/api/v1/eisf/sites/site_beta/binder",
        headers=headers,
    )

    assert response.status_code == 403
    assert "Access is restricted to your assigned site" in response.json()["detail"]


@pytest.mark.asyncio
async def test_upload_and_get_site_document() -> None:
    """Test successful GxP document details upload, fetch, and audit trail logging.

    Requirements: PRD-SYS-001
    """
    client = TestClient(eisf_app)
    headers = get_site_auth_headers(roles="cra", site_id="site_alpha")

    # 1. Upload new site document with GxP metadata
    payload = {
        "study_id": "study_001",
        "section_code": "SEC_01",
        "filename": "qualification.pdf",
        "content": "SGVsbG8gV29ybGQ=",  # Base64 for 'Hello World'
        "mime_type": "application/pdf",
        "reason_for_change": "Initial upload of investigator qualification document",
        "expiration_date": "2027-12-31",
    }

    response_upload = client.post(
        "/api/v1/eisf/sites/site_alpha/documents/upload",
        json=payload,
        headers=headers,
    )

    assert response_upload.status_code == 201
    uploaded_doc = response_upload.json()
    assert uploaded_doc["filename"] == "qualification.pdf"
    assert uploaded_doc["version"] == "1"
    assert uploaded_doc["expiration_date"] == "2027-12-31"
    assert uploaded_doc["download_url"] is not None

    doc_id = uploaded_doc["id"]

    # 2. Retrieve document details
    response_detail = client.get(
        f"/api/v1/eisf/sites/site_alpha/documents/{doc_id}",
        headers=headers,
    )
    assert response_detail.status_code == 200
    doc_detail = response_detail.json()
    assert doc_detail["id"] == doc_id
    assert doc_detail["filename"] == "qualification.pdf"

    # 3. Stream/Download document content
    response_download = client.get(
        f"/api/v1/eisf/sites/site_alpha/documents/{doc_id}/download",
        headers=headers,
    )
    assert response_download.status_code == 200
    assert response_download.content == b"SGVsbG8gV29ybGQ="

    # 4. Check binder has correct counted document
    response_binder = client.get(
        "/api/v1/eisf/sites/site_alpha/binder",
        headers=headers,
    )
    assert response_binder.status_code == 200
    binder_data = response_binder.json()
    sec01_node = next(n for n in binder_data if n["section_code"] == "SEC_01")
    assert sec01_node["document_count"] == 1

    # 5. Verify GxP audit trail via CentralAuditLogger has records of EISF_DOCUMENT_ACCESSED
    audit_events = [record.action_type for record in CentralAuditLogger._chain]
    assert "EISF_DOCUMENT_ACCESSED" in audit_events
    # Check that details are populated correctly
    accessed_event = next(
        record
        for record in CentralAuditLogger._chain
        if record.action_type == "EISF_DOCUMENT_ACCESSED"
    )
    assert accessed_event.service_name == "eisf"
    assert accessed_event.entity_name == "ISFDocument"
