"""Unit tests validating domain models, enums, and business invariants for Fileshare microservice.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002, PRD-DOC-003
"""

import uuid
from datetime import UTC, datetime, timedelta

import pytest
from pydantic import ValidationError

from apps.fileshare.domain.models import (
    FileRecord,
    GuestLink,
    PermissionLevel,
    ShareGrant,
    ShareScope,
)


def test_file_record_creation_and_defaults():
    """Verify FileRecord instantiation with valid attributes and default GxP values.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    """
    file_id = str(uuid.uuid4())
    record = FileRecord(
        id=file_id,
        study_id="STUDY-101",
        site_id="SITE-01",
        filename="informed_consent_v1.pdf",
        mime_type="application/pdf",
        size_bytes=1048576,
        object_key="tenant_1/STUDY-101/doc1/informed_consent_v1.pdf",
        uploaded_by="crc_user_01",
        created_by="crc_user_01",
        reason_for_change="Initial file upload for site conduct",
    )

    assert record.id == file_id
    assert record.study_id == "STUDY-101"
    assert record.site_id == "SITE-01"
    assert record.size_bytes == 1048576
    assert record.version_index == 1
    assert record.is_on_hold is False
    assert record.is_deleted is False
    assert record.checksum_sha256 is None


def test_file_record_validation_errors():
    """Verify FileRecord rejects empty required strings or negative sizes.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    """
    with pytest.raises(ValidationError):
        # Invalid empty study_id and negative size
        FileRecord(
            study_id="",
            filename="doc.pdf",
            mime_type="application/pdf",
            size_bytes=-10,
            object_key="key",
            uploaded_by="user",
            created_by="user",
            reason_for_change="Valid change reason",
        )


def test_share_grant_lifecycle_and_expiry():
    """Verify ShareGrant permission ranking, watermarking requirement, and active status.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    @req:PRD-DOC-003
    """
    file_id = str(uuid.uuid4())

    # 1. View grant - requires watermark
    view_grant = ShareGrant(
        file_record_id=file_id,
        granted_to_user_id="auditor_01",
        granted_by_user_id="lead_cra",
        scope=ShareScope.INDIVIDUAL,
        permission_level=PermissionLevel.VIEW,
        created_by="lead_cra",
        reason_for_change="Audit inspection access",
    )
    assert view_grant.is_active is True
    assert view_grant.permission_level.requires_watermark() is True
    assert view_grant.permission_level.satisfies(PermissionLevel.VIEW) is True
    assert view_grant.permission_level.satisfies(PermissionLevel.DOWNLOAD) is False

    # 2. Download grant - no watermark required
    dl_grant = ShareGrant(
        file_record_id=file_id,
        granted_to_user_id="pi_user_01",
        granted_by_user_id="lead_cra",
        scope=ShareScope.SITE,
        permission_level=PermissionLevel.DOWNLOAD,
        created_by="lead_cra",
        reason_for_change="Site download permission",
    )
    assert dl_grant.is_active is True
    assert dl_grant.permission_level.requires_watermark() is False
    assert dl_grant.permission_level.satisfies(PermissionLevel.VIEW) is True
    assert dl_grant.permission_level.satisfies(PermissionLevel.DOWNLOAD) is True

    # 3. Expired grant
    expired_grant = ShareGrant(
        file_record_id=file_id,
        granted_to_user_id="temporary_contractor",
        granted_by_user_id="lead_cra",
        scope=ShareScope.INDIVIDUAL,
        permission_level=PermissionLevel.VIEW,
        expires_at=datetime.now(UTC) - timedelta(hours=1),
        created_by="lead_cra",
        reason_for_change="Expired grant test",
    )
    assert expired_grant.is_active is False

    # 4. Revoked grant
    revoked_grant = ShareGrant(
        file_record_id=file_id,
        granted_to_user_id="departed_employee",
        granted_by_user_id="admin",
        scope=ShareScope.INDIVIDUAL,
        permission_level=PermissionLevel.DOWNLOAD,
        revoked_at=datetime.now(UTC),
        created_by="admin",
        reason_for_change="Revoking access upon departure",
    )
    assert revoked_grant.is_active is False


def test_guest_link_validity():
    """Verify GuestLink time-bound expiration and validity checking.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    """
    file_id = str(uuid.uuid4())

    valid_link = GuestLink(
        file_record_id=file_id,
        token_hmac="a" * 64,
        expires_at=datetime.now(UTC) + timedelta(hours=12),
        created_by="sponsor_user",
        reason_for_change="External monitor review",
    )
    assert valid_link.is_valid is True

    expired_link = GuestLink(
        file_record_id=file_id,
        token_hmac="b" * 64,
        expires_at=datetime.now(UTC) - timedelta(minutes=5),
        created_by="sponsor_user",
        reason_for_change="Expired guest review",
    )
    assert expired_link.is_valid is False
