"""Integration tests for Fileshare microservice endpoints and security gating.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002, PRD-DOC-003
"""

import httpx
import pytest
from httpx import ASGITransport
from sqlalchemy.ext.asyncio import AsyncSession

from apps.fileshare.adapters.database import get_db_session
from apps.fileshare.domain.models import PermissionLevel, ShareScope
from apps.fileshare.main import app
from packages.testing.fakes import InMemoryStoragePort
from packages.testing.security import create_test_auth_headers


@pytest.fixture
def auth_headers_uploader() -> dict[str, str]:
    """Generates authentic gateway authentication headers for a CRC uploader."""
    return create_test_auth_headers(
        user_id="crc_user_01",
        roles=["crc", "site_crc"],
        tenant_id="tenant_trial_01",
    )


@pytest.fixture
def auth_headers_auditor() -> dict[str, str]:
    """Generates authentic gateway authentication headers for an auditor."""
    return create_test_auth_headers(
        user_id="auditor_user_01",
        roles=["auditor"],
        tenant_id="tenant_trial_01",
    )


@pytest.fixture
def auth_headers_pi() -> dict[str, str]:
    """Generates authentic gateway authentication headers for a PI."""
    return create_test_auth_headers(
        user_id="pi_user_01",
        roles=["investigator", "site_investigator"],
        tenant_id="tenant_trial_01",
    )


@pytest.fixture
def auth_headers_unauthorized() -> dict[str, str]:
    """Generates authentic gateway headers for an unrelated user."""
    return create_test_auth_headers(
        user_id="unrelated_user",
        roles=["crc"],
        tenant_id="tenant_trial_01",
    )


@pytest.mark.asyncio
async def test_health_check():
    """Verify Fileshare service health endpoint.

    @req:PRD-SYS-001
    """
    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        resp = await client.get("/health")
        assert resp.status_code == 200
        data = resp.json()
        assert data["status"] == "ok"
        assert data["service"] == "fileshare"


@pytest.mark.asyncio
async def test_singlepart_upload_url_generation(
    db_session: AsyncSession,
    mock_storage: InMemoryStoragePort,
    auth_headers_uploader: dict[str, str],
):
    """Verify singlepart presigned upload URL allocation and database persistence.

    @req:PRD-DOC-001
    @req:PRD-DOC-002
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        payload = {
            "study_id": "STUDY-PHASE3-001",
            "site_id": "SITE-101",
            "filename": "laboratory_report_cbc.pdf",
            "mime_type": "application/pdf",
            "size_bytes": 204800,
            "reason_for_change": "Initial lab document upload for subject screening",
            "is_multipart": False,
            "parts_count": 1,
        }
        resp = await client.post(
            "/api/v1/fileshare/files/upload-url",
            json=payload,
            headers=auth_headers_uploader,
        )
        assert resp.status_code == 201
        data = resp.json()
        assert "file_id" in data
        assert "object_key" in data
        assert "upload_url" in data
        assert data["upload_id"] is None
        assert "laboratory_report_cbc.pdf" in data["object_key"]
        assert data["expires_in"] == 3600

        # Verify get file metadata
        file_id = data["file_id"]
        get_resp = await client.get(
            f"/api/v1/fileshare/files/{file_id}",
            headers=auth_headers_uploader,
        )
        assert get_resp.status_code == 200
        meta = get_resp.json()
        assert meta["id"] == file_id
        assert meta["study_id"] == "STUDY-PHASE3-001"
        assert meta["filename"] == "laboratory_report_cbc.pdf"
        assert meta["uploaded_by"] == "crc_user_01"

    app.dependency_overrides.clear()


@pytest.mark.asyncio
async def test_multipart_upload_url_generation(
    db_session: AsyncSession,
    mock_storage: InMemoryStoragePort,
    auth_headers_uploader: dict[str, str],
):
    """Verify multipart presigned upload URLs for large file chunks.

    @req:PRD-DOC-001
    @req:PRD-DOC-002
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        payload = {
            "study_id": "STUDY-PHASE3-001",
            "site_id": "SITE-101",
            "filename": "endoscopy_video_procedure.mp4",
            "mime_type": "video/mp4",
            "size_bytes": 524288000,
            "reason_for_change": "Large procedural endoscopic video capture",
            "is_multipart": True,
            "parts_count": 5,
        }
        resp = await client.post(
            "/api/v1/fileshare/files/upload-url",
            json=payload,
            headers=auth_headers_uploader,
        )
        assert resp.status_code == 201
        data = resp.json()
        assert data["upload_id"] is not None
        assert data["upload_urls"] is not None
        assert len(data["upload_urls"]) == 5
        assert "1" in data["upload_urls"]
        assert "5" in data["upload_urls"]

    app.dependency_overrides.clear()


@pytest.mark.asyncio
async def test_download_url_permissions_and_watermarking(
    db_session: AsyncSession,
    mock_storage: InMemoryStoragePort,
    auth_headers_uploader: dict[str, str],
    auth_headers_auditor: dict[str, str],
    auth_headers_pi: dict[str, str],
    auth_headers_unauthorized: dict[str, str],
):
    """Verify download URL access control, permission evaluation, and view-only watermarking flag.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    @req:PRD-DOC-003
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        # 1. Create file record by uploader
        up_resp = await client.post(
            "/api/v1/fileshare/files/upload-url",
            json={
                "study_id": "STUDY-PHASE3-001",
                "site_id": "SITE-101",
                "filename": "protocol_amendment_signed.pdf",
                "mime_type": "application/pdf",
                "size_bytes": 102400,
                "reason_for_change": "Signed protocol amendment upload",
            },
            headers=auth_headers_uploader,
        )
        file_id = up_resp.json()["file_id"]

        # 2. Original uploader downloads -> success, is_watermarked=False
        dl_up = await client.get(
            f"/api/v1/fileshare/files/{file_id}/download-url",
            headers=auth_headers_uploader,
        )
        assert dl_up.status_code == 200
        assert dl_up.json()["is_watermarked"] is False
        assert "download_url" in dl_up.json()

        # 3. Unauthorized user downloads -> 403 Forbidden
        unauth_dl = await client.get(
            f"/api/v1/fileshare/files/{file_id}/download-url",
            headers=auth_headers_unauthorized,
        )
        assert unauth_dl.status_code == 403

        # 4. Grant VIEW permission to auditor
        grant_auditor = await client.post(
            f"/api/v1/fileshare/files/{file_id}/grants",
            json={
                "granted_to_user_id": "auditor_user_01",
                "scope": ShareScope.INDIVIDUAL.value,
                "permission_level": PermissionLevel.VIEW.value,
                "reason_for_change": "Granting auditor view-only access",
            },
            headers=auth_headers_uploader,
        )
        assert grant_auditor.status_code == 201

        # Auditor downloads -> success, is_watermarked=True
        dl_auditor = await client.get(
            f"/api/v1/fileshare/files/{file_id}/download-url",
            headers=auth_headers_auditor,
        )
        assert dl_auditor.status_code == 200
        assert dl_auditor.json()["is_watermarked"] is True

        # 5. Grant DOWNLOAD permission to PI
        grant_pi = await client.post(
            f"/api/v1/fileshare/files/{file_id}/grants",
            json={
                "granted_to_user_id": "pi_user_01",
                "scope": ShareScope.INDIVIDUAL.value,
                "permission_level": PermissionLevel.DOWNLOAD.value,
                "reason_for_change": "Granting PI full download permission",
            },
            headers=auth_headers_uploader,
        )
        assert grant_pi.status_code == 201

        # PI downloads -> success, is_watermarked=False
        dl_pi = await client.get(
            f"/api/v1/fileshare/files/{file_id}/download-url",
            headers=auth_headers_pi,
        )
        assert dl_pi.status_code == 200
        assert dl_pi.json()["is_watermarked"] is False

    app.dependency_overrides.clear()


@pytest.mark.asyncio
async def test_guest_link_creation(
    db_session: AsyncSession,
    mock_storage: InMemoryStoragePort,
    auth_headers_uploader: dict[str, str],
):
    """Verify external guest link token generation.

    @req:PRD-DOC-001
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        up_resp = await client.post(
            "/api/v1/fileshare/files/upload-url",
            json={
                "study_id": "STUDY-PHASE3-001",
                "filename": "site_briefing.pdf",
                "mime_type": "application/pdf",
                "size_bytes": 50000,
                "reason_for_change": "External guest briefing",
            },
            headers=auth_headers_uploader,
        )
        file_id = up_resp.json()["file_id"]

        guest_resp = await client.post(
            f"/api/v1/fileshare/files/{file_id}/guest-links",
            json={
                "expires_in_hours": 48,
                "reason_for_change": "Generating external partner guest link",
            },
            headers=auth_headers_uploader,
        )
        assert guest_resp.status_code == 201
        data = guest_resp.json()
        assert data["file_record_id"] == file_id
        assert data["is_valid"] is True
        assert "/api/v1/fileshare/guest/" in data["guest_url"]

    app.dependency_overrides.clear()


@pytest.mark.asyncio
async def test_presigned_upload_url_with_checksum_validation(
    db_session: AsyncSession,
    mock_storage: InMemoryStoragePort,
    auth_headers_uploader: dict[str, str],
):
    """Verify presigned upload URL with SHA-256 checksum header requirement.

    @req:PRD-DOC-001
    @req:PRD-DOC-002
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        sha256_hash = "a" * 64
        payload = {
            "study_id": "STUDY-PHASE3-001",
            "site_id": "SITE-101",
            "filename": "ecg_trace_diagnostic.pdf",
            "mime_type": "application/pdf",
            "size_bytes": 102400,
            "checksum_sha256": sha256_hash,
            "reason_for_change": "Diagnostic ECG upload with SHA-256 integrity hash",
            "is_multipart": False,
            "parts_count": 1,
        }
        resp = await client.post(
            "/api/v1/files/upload/presigned-url",
            json=payload,
            headers=auth_headers_uploader,
        )
        assert resp.status_code == 201
        data = resp.json()
        assert data["checksum_sha256"] == sha256_hash
        assert data["required_headers"]["x-amz-checksum-sha256"] == sha256_hash
        assert "upload_url" in data

    app.dependency_overrides.clear()


@pytest.mark.asyncio
async def test_role_scope_share_grant_and_revocation(
    db_session: AsyncSession,
    mock_storage: InMemoryStoragePort,
    auth_headers_uploader: dict[str, str],
    auth_headers_auditor: dict[str, str],
    auth_headers_unauthorized: dict[str, str],
):
    """Verify ROLE-scoped share grant access control and explicit grant deletion.

    @req:PRD-DOC-001
    @req:PRD-DOC-003
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        # 1. Create file record
        up_resp = await client.post(
            "/api/v1/fileshare/files/upload-url",
            json={
                "study_id": "STUDY-PHASE3-001",
                "filename": "monitoring_log.pdf",
                "mime_type": "application/pdf",
                "size_bytes": 45000,
                "reason_for_change": "Initial upload",
            },
            headers=auth_headers_uploader,
        )
        file_id = up_resp.json()["file_id"]

        # Auditor without grant cannot access yet
        auditor_dl = await client.get(
            f"/api/v1/fileshare/files/{file_id}/download-url",
            headers=auth_headers_auditor,
        )
        assert auditor_dl.status_code == 403

        # 2. Grant ROLE scope to 'auditor' role
        grant_resp = await client.post(
            f"/api/v1/files/{file_id}/grants",
            json={
                "granted_to_user_id": "auditor",
                "scope": ShareScope.ROLE.value,
                "permission_level": PermissionLevel.VIEW.value,
                "reason_for_change": "Grant all auditors view access",
            },
            headers=auth_headers_uploader,
        )
        assert grant_resp.status_code == 201
        grant_id = grant_resp.json()["id"]

        # Auditor now can access -> view only (watermarked)
        dl_auditor = await client.get(
            f"/api/v1/fileshare/files/{file_id}/download-url",
            headers=auth_headers_auditor,
        )
        assert dl_auditor.status_code == 200
        assert dl_auditor.json()["is_watermarked"] is True

        # Unauthorized user without auditor role still 403
        unauth_dl = await client.get(
            f"/api/v1/fileshare/files/{file_id}/download-url",
            headers=auth_headers_unauthorized,
        )
        assert unauth_dl.status_code == 403

        # 3. Revoke the grant via DELETE
        del_resp = await client.delete(
            f"/api/v1/files/{file_id}/grants/{grant_id}",
            headers=auth_headers_uploader,
        )
        assert del_resp.status_code == 200
        assert del_resp.json()["is_active"] is False

        # Auditor access is now revoked -> 403
        dl_after_revoke = await client.get(
            f"/api/v1/fileshare/files/{file_id}/download-url",
            headers=auth_headers_auditor,
        )
        assert dl_after_revoke.status_code == 403

    app.dependency_overrides.clear()


@pytest.mark.asyncio
async def test_guest_link_resolution_counter_and_revocation(
    db_session: AsyncSession,
    mock_storage: InMemoryStoragePort,
    auth_headers_uploader: dict[str, str],
):
    """Verify external guest link resolution, download counting, and revocation.

    @req:PRD-DOC-001
    @req:PRD-DOC-002
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        # 1. Create file record
        up_resp = await client.post(
            "/api/v1/fileshare/files/upload-url",
            json={
                "study_id": "STUDY-PHASE3-001",
                "filename": "site_delegation_log.pdf",
                "mime_type": "application/pdf",
                "size_bytes": 80000,
                "reason_for_change": "Delegation log for site partners",
            },
            headers=auth_headers_uploader,
        )
        file_id = up_resp.json()["file_id"]

        # 2. Create guest link
        link_resp = await client.post(
            f"/api/v1/files/{file_id}/guest-links",
            json={
                "expires_in_hours": 24,
                "reason_for_change": "External inspector review",
            },
            headers=auth_headers_uploader,
        )
        assert link_resp.status_code == 201
        link_data = link_resp.json()
        guest_link_id = link_data["id"]
        guest_url = link_data["guest_url"]
        token = guest_url.split("/")[-1]

        # 3. Access guest link externally (publicly without gateway headers)
        guest_dl_1 = await client.get(f"/api/v1/files/guest/{token}")
        assert guest_dl_1.status_code == 200
        data1 = guest_dl_1.json()
        assert data1["file_id"] == file_id
        assert data1["access_count"] == 1
        assert "download_url" in data1

        # Second access increments counter
        guest_dl_2 = await client.get(f"/api/v1/fileshare/guest/{token}")
        assert guest_dl_2.status_code == 200
        data2 = guest_dl_2.json()
        assert data2["access_count"] == 2

        # 4. Explicitly revoke the guest link
        rev_resp = await client.delete(
            f"/api/v1/files/{file_id}/guest-links/{guest_link_id}",
            headers=auth_headers_uploader,
        )
        assert rev_resp.status_code == 200
        assert rev_resp.json()["is_valid"] is False

        # Attempting to access revoked token -> 403 Forbidden
        guest_dl_revoked = await client.get(f"/api/v1/files/guest/{token}")
        assert guest_dl_revoked.status_code == 403

    app.dependency_overrides.clear()
