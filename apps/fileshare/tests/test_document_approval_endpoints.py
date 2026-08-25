"""Integration tests for 21 CFR Part 11 electronic signature document approval endpoints.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002
"""

import time
import uuid
from datetime import UTC, datetime

import httpx
import pytest
from httpx import ASGITransport
from jose import jwt
from sqlalchemy.ext.asyncio import AsyncSession

from apps.fileshare.adapters.credential_verifier import (
    CredentialVerifierPort,
    set_credential_verifier,
)
from apps.fileshare.adapters.database import get_db_session
from apps.fileshare.adapters.repositories import (
    SqlAlchemyFileRecordRepository,
)
from apps.fileshare.domain.models import (
    ApprovalStatus,
    FileRecord,
    FileStatus,
    ScanStatus,
)
from apps.fileshare.main import app
from packages.security.signature import SignatureManifestation
from packages.testing.security import create_test_auth_headers


class MockCredentialVerifier(CredentialVerifierPort):
    """Mock credential verifier for deterministic testing of Part 11 re-authentication."""

    def __init__(self, reject_users: list[str] | None = None) -> None:
        self.reject_users = reject_users or []

    async def verify_credential(self, user_id: str, credential: str) -> bool:
        if user_id in self.reject_users:
            return False
        if not credential or not credential.strip():
            return False
        clean = credential.strip().lower()
        return not any(
            w in clean
            for w in ("invalid", "wrong", "bad_pin", "bad_password", "incorrect")
        )


@pytest.fixture(autouse=True)
def setup_credential_verifier():
    """Ensure credential verifier is reset for each test."""
    set_credential_verifier(MockCredentialVerifier())
    yield
    set_credential_verifier(None)


GATEWAY_SECRET = "valid-test-gateway-secret-key-2026"  # pragma: allowlist secret


def get_auth_headers_with_sig(
    user_id: str, roles: list[str], path: str
) -> dict[str, str]:
    """Generates authentic gateway headers with a valid eSignature token for Part 11 actions."""
    sig_payload = {
        "sub": user_id,
        "username": user_id,
        "action": path,
        "roles": roles,
        "iat": time.time(),
        "exp": time.time() + 300.0,
        "jti": str(uuid.uuid4()),
    }
    sig_token = jwt.encode(sig_payload, GATEWAY_SECRET, algorithm="HS256")
    return create_test_auth_headers(
        user_id=user_id,
        roles=roles,
        tenant_id="tenant_trial_01",
        sig_token=sig_token,
    )


@pytest.fixture
def auth_headers_uploader() -> dict[str, str]:
    """Generates authentic gateway authentication headers for a CRC uploader."""
    return create_test_auth_headers(
        user_id="crc_signer_01",
        roles=["crc", "site_crc"],
        tenant_id="tenant_trial_01",
    )


@pytest.fixture
def auth_headers_reviewer() -> dict[str, str]:
    """Generates authentic gateway authentication headers for an authorized reviewer."""
    return create_test_auth_headers(
        user_id="reviewer_user_01",
        roles=["data_manager"],
        tenant_id="tenant_trial_01",
    )


@pytest.fixture
def auth_headers_admin() -> dict[str, str]:
    """Generates authentic gateway authentication headers for a super_admin."""
    return create_test_auth_headers(
        user_id="admin_user_01",
        roles=["super_admin", "admin"],
        tenant_id="tenant_trial_01",
    )


@pytest.fixture
def auth_headers_unauthorized() -> dict[str, str]:
    """Generates authentic gateway headers for an unauthorized user."""
    return create_test_auth_headers(
        user_id="unauthorized_user",
        roles=["guest"],
        tenant_id="tenant_trial_01",
    )


async def create_test_file(
    session: AsyncSession,
    file_id: str = "file-approve-test-01",
    uploader: str = "crc_signer_01",
    approval_status: ApprovalStatus = ApprovalStatus.PENDING_APPROVAL,
    is_on_hold: bool = False,
    scan_status: ScanStatus = ScanStatus.CLEAN,
    status: FileStatus = FileStatus.READY,
) -> FileRecord:
    """Helper creating a test FileRecord in database."""
    repo = SqlAlchemyFileRecordRepository(session)
    record = FileRecord(
        id=file_id,
        study_id="STUDY-2026",
        site_id="SITE-101",
        filename="Clinical_Protocol_v1.0.pdf",
        mime_type="application/pdf",
        size_bytes=2048576,
        object_key=f"tenant_trial_01/STUDY-2026/{file_id}/Clinical_Protocol_v1.0.pdf",
        checksum_sha256="b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9",
        version_index=1,
        uploaded_by=uploader,
        uploaded_at=datetime.now(UTC),
        is_on_hold=is_on_hold,
        hold_reason="Litigation Hold" if is_on_hold else None,
        is_deleted=False,
        status=status,
        scan_status=scan_status,
        approval_status=approval_status,
        created_at=datetime.now(UTC),
        created_by=uploader,
        reason_for_change="Protocol initial submission",
    )
    return await repo.save(record)


@pytest.mark.asyncio
async def test_approve_document_success(
    db_session: AsyncSession,
    auth_headers_uploader: dict[str, str],
):
    """Verify document approval succeeds with valid credential and produces a verifiable signature manifestation.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    file_id = "doc-appr-001"
    await create_test_file(db_session, file_id=file_id, uploader="crc_signer_01")

    approve_headers = get_auth_headers_with_sig(
        "crc_signer_01",
        ["crc", "site_crc"],
        f"/api/v1/fileshare/files/{file_id}/approve",
    )

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        response = await client.post(
            f"/api/v1/fileshare/files/{file_id}/approve",
            json={
                "signing_reason": "APPROVAL",
                "credential": "ValidPIN1234!",
            },
            headers=approve_headers,
        )

        assert response.status_code == 200
        data = response.json()
        assert data["id"] == file_id
        assert data["approval_status"] == "APPROVED"
        assert data["approved_by"] == "crc_signer_01"
        assert data["approved_at"] is not None
        assert data["approval_reason"] == "APPROVAL"

        # Verify signature manifestation structure
        manifest_data = data["signature_manifestation"]
        assert manifest_data is not None
        assert manifest_data["signer_id"] == "crc_signer_01"
        assert manifest_data["signature"] is not None
        assert manifest_data["certificate_pem"] is not None
        assert manifest_data["key_identifier"] is not None

        # Verify cryptographic validity using domain model
        manifest = SignatureManifestation(**manifest_data)
        assert manifest.verify() is True

        # Verify Part 11 Audit Log recorded
        audit_res = await client.get(
            f"/api/v1/fileshare/files/{file_id}/audit-logs",
            headers=auth_headers_uploader,
        )
        assert audit_res.status_code == 200
        audit_logs = audit_res.json()
        approve_log = next(
            (
                log_entry
                for log_entry in audit_logs
                if log_entry["action"] in ("FILE_APPROVED", "DOCUMENT_APPROVED")
            ),
            None,
        )
        assert approve_log is not None
        assert approve_log["user_id"] == "crc_signer_01"
        assert approve_log["detail"]["outcome"] == "APPROVED"
        assert (
            approve_log["detail"]["signature_manifestation"]["signer_id"]
            == "crc_signer_01"
        )


@pytest.mark.asyncio
async def test_approve_document_invalid_credential_rejected(
    db_session: AsyncSession,
    auth_headers_uploader: dict[str, str],
):
    """Verify document approval fails with 401 when invalid credential is provided.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    file_id = "doc-appr-bad-cred"
    await create_test_file(db_session, file_id=file_id, uploader="crc_signer_01")

    approve_headers = get_auth_headers_with_sig(
        "crc_signer_01",
        ["crc", "site_crc"],
        f"/api/v1/fileshare/files/{file_id}/approve",
    )

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        response = await client.post(
            f"/api/v1/fileshare/files/{file_id}/approve",
            json={
                "signing_reason": "APPROVAL",
                "credential": "invalid_wrong_pin",
            },
            headers=approve_headers,
        )

        assert response.status_code == 401
        assert "Invalid re-authentication" in response.text


@pytest.mark.asyncio
async def test_approved_document_is_immutable_and_blocks_revision(
    db_session: AsyncSession,
    auth_headers_uploader: dict[str, str],
):
    """Verify an approved document becomes immutable and blocks new revisions with 409 Conflict.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    @req:PRD-DOC-002
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    file_id = "doc-appr-immutable-01"
    await create_test_file(
        db_session,
        file_id=file_id,
        uploader="crc_signer_01",
        approval_status=ApprovalStatus.APPROVED,
    )

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        # Attempting to upload a new revision must return 409 DOCUMENT_APPROVED_IMMUTABLE
        response = await client.post(
            f"/api/v1/fileshare/files/{file_id}/upload-revision",
            json={
                "filename": "Clinical_Protocol_v2.0.pdf",
                "mime_type": "application/pdf",
                "size_bytes": 2100000,
                "reason_for_change": "Updating protocol after approval",
            },
            headers=auth_headers_uploader,
        )

        assert response.status_code == 409
        body = response.json()
        assert body["reason"] == "DOCUMENT_APPROVED_IMMUTABLE"

        # Attempting to re-approve an already approved document also returns 409
        reapprove_headers = get_auth_headers_with_sig(
            "crc_signer_01",
            ["crc", "site_crc"],
            f"/api/v1/fileshare/files/{file_id}/approve",
        )
        reapprove_res = await client.post(
            f"/api/v1/fileshare/files/{file_id}/approve",
            json={
                "signing_reason": "APPROVAL",
                "credential": "ValidPIN1234!",
            },
            headers=reapprove_headers,
        )
        assert reapprove_res.status_code == 409
        assert reapprove_res.json()["reason"] == "DOCUMENT_APPROVED_IMMUTABLE"


@pytest.mark.asyncio
async def test_reject_document_workflow(
    db_session: AsyncSession,
    auth_headers_uploader: dict[str, str],
):
    """Verify rejecting a document sets approval_status to REJECTED and records audit log.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    file_id = "doc-appr-reject-01"
    await create_test_file(db_session, file_id=file_id, uploader="crc_signer_01")

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        response = await client.post(
            f"/api/v1/fileshare/files/{file_id}/reject",
            json={
                "reason_for_change": "Document contains typographical errors in Section 4",
            },
            headers=auth_headers_uploader,
        )

        assert response.status_code == 200
        data = response.json()
        assert data["id"] == file_id
        assert data["approval_status"] == "REJECTED"
        assert data["signature_manifestation"] is None


@pytest.mark.asyncio
async def test_rescind_approval_and_reopen_for_revisions(
    db_session: AsyncSession,
    auth_headers_uploader: dict[str, str],
    auth_headers_admin: dict[str, str],
):
    """Verify super_admin can rescind approval, clearing immutability and allowing new revisions.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    @req:PRD-DOC-002
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    file_id = "doc-appr-rescind-01"
    await create_test_file(
        db_session,
        file_id=file_id,
        uploader="crc_signer_01",
        approval_status=ApprovalStatus.APPROVED,
    )

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        # Non-super_admin attempting to rescind must be rejected with 403
        non_admin_res = await client.post(
            f"/api/v1/fileshare/files/{file_id}/rescind-approval",
            json={"reason_for_change": "Attempted non-admin rescission"},
            headers=auth_headers_uploader,
        )
        assert non_admin_res.status_code == 403

        # super_admin rescinds approval
        rescind_res = await client.post(
            f"/api/v1/fileshare/files/{file_id}/rescind-approval",
            json={"reason_for_change": "Administrative protocol amendment rescission"},
            headers=auth_headers_admin,
        )
        assert rescind_res.status_code == 200
        data = rescind_res.json()
        assert data["approval_status"] == "APPROVAL_RESCINDED"
        assert data["signature_manifestation"] is None

        # Now uploading a revision succeeds
        rev_res = await client.post(
            f"/api/v1/fileshare/files/{file_id}/upload-revision",
            json={
                "filename": "Clinical_Protocol_v2.0.pdf",
                "mime_type": "application/pdf",
                "size_bytes": 2200000,
                "reason_for_change": "Revised protocol uploaded after rescission",
            },
            headers=auth_headers_uploader,
        )
        assert rev_res.status_code == 201
        rev_data = rev_res.json()
        assert rev_data["file_id"] == file_id
        assert "v2_Clinical_Protocol_v2.0.pdf" in rev_data["object_key"]
