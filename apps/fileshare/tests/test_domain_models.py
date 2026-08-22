"""Unit tests validating domain models, enums, and business invariants for Fileshare microservice.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002, PRD-DOC-003
"""

import uuid
from datetime import UTC, datetime, timedelta

import pytest
from pydantic import ValidationError

from apps.fileshare.domain.models import (
    ConversionStatus,
    FileAuditLog,
    FileRecord,
    GuestLink,
    PermissionLevel,
    PlaybackAuditEvent,
    PlaybackEventSource,
    PlaybackEventType,
    ShareGrant,
    ShareScope,
    TranscodeStatus,
    is_convertible_office_mime_type,
    is_video_mime_type,
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

    revoked_link = GuestLink(
        file_record_id=file_id,
        token_hmac="c" * 64,
        expires_at=datetime.now(UTC) + timedelta(hours=12),
        created_by="sponsor_user",
        revoked_at=datetime.now(UTC),
        reason_for_change="Revoked guest review",
    )
    assert revoked_link.is_valid is False


def test_share_scope_and_permission_levels_enum_extensions():
    """Verify ROLE scope, EDIT and ADMIN permissions, case-insensitive parsing, and ranks.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    @req:PRD-DOC-003
    """
    assert ShareScope.ROLE == "role"
    assert ShareScope.from_str("ROLE") == ShareScope.ROLE
    assert ShareScope.from_str("individual") == ShareScope.INDIVIDUAL

    assert PermissionLevel.EDIT == "edit"
    assert PermissionLevel.ADMIN == "admin"
    assert PermissionLevel.from_str("EDIT") == PermissionLevel.EDIT
    assert PermissionLevel.from_str("ADMIN") == PermissionLevel.ADMIN

    # Rank hierarchy checks
    assert PermissionLevel.ADMIN.rank > PermissionLevel.EDIT.rank
    assert PermissionLevel.EDIT.rank > PermissionLevel.DOWNLOAD.rank
    assert PermissionLevel.DOWNLOAD.rank > PermissionLevel.COMMENT.rank
    assert PermissionLevel.COMMENT.rank > PermissionLevel.VIEW.rank

    # Watermark policy checks
    assert PermissionLevel.VIEW.requires_watermark() is True
    assert PermissionLevel.COMMENT.requires_watermark() is True
    assert PermissionLevel.DOWNLOAD.requires_watermark() is False
    assert PermissionLevel.EDIT.requires_watermark() is False
    assert PermissionLevel.ADMIN.requires_watermark() is False

    # Satisfies checks
    assert PermissionLevel.ADMIN.satisfies(PermissionLevel.VIEW) is True
    assert PermissionLevel.ADMIN.satisfies(PermissionLevel.DOWNLOAD) is True
    assert PermissionLevel.ADMIN.satisfies(PermissionLevel.EDIT) is True
    assert PermissionLevel.EDIT.satisfies(PermissionLevel.DOWNLOAD) is True
    assert PermissionLevel.DOWNLOAD.satisfies(PermissionLevel.EDIT) is False


def test_video_mime_type_detection():
    """Verify is_video_mime_type helper for standard and clinical video formats.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    """
    assert is_video_mime_type("video/mp4") is True
    assert is_video_mime_type("video/webm") is True
    assert is_video_mime_type("video/quicktime") is True
    assert is_video_mime_type("video/x-matroska") is True
    assert is_video_mime_type("application/vnd.apple.mpegurl") is True
    assert is_video_mime_type("application/pdf") is False
    assert is_video_mime_type("image/png") is False
    assert is_video_mime_type(None) is False
    assert is_video_mime_type("") is False


def test_transcode_status_enum():
    """Verify TranscodeStatus enum values and case-insensitive parsing.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    """
    assert TranscodeStatus.NOT_REQUIRED == "NOT_REQUIRED"
    assert TranscodeStatus.PENDING == "PENDING"
    assert TranscodeStatus.IN_PROGRESS == "IN_PROGRESS"
    assert TranscodeStatus.READY == "READY"
    assert TranscodeStatus.FAILED == "FAILED"

    assert TranscodeStatus.from_str("ready") == TranscodeStatus.READY
    assert TranscodeStatus.from_str("PENDING") == TranscodeStatus.PENDING


def test_playback_audit_event_and_enums():
    """Verify PlaybackEventType, PlaybackEventSource, and PlaybackAuditEvent instantiation.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    """
    assert PlaybackEventType.PLAY_START == "PLAY_START"
    assert PlaybackEventType.PLAY_25 == "PLAY_25"
    assert PlaybackEventType.PLAY_50 == "PLAY_50"
    assert PlaybackEventType.PLAY_75 == "PLAY_75"
    assert PlaybackEventType.PLAY_END == "PLAY_END"
    assert PlaybackEventType.from_str("play_start") == PlaybackEventType.PLAY_START

    assert PlaybackEventSource.INTERNAL == "internal"
    assert PlaybackEventSource.GUEST_LINK == "guest_link"
    assert PlaybackEventSource.from_str("GUEST_LINK") == PlaybackEventSource.GUEST_LINK

    event_id = str(uuid.uuid4())
    file_id = str(uuid.uuid4())
    audit = PlaybackAuditEvent(
        id=event_id,
        file_record_id=file_id,
        event=PlaybackEventType.PLAY_50,
        source=PlaybackEventSource.INTERNAL,
        session_id="sess_12345",
        user_id="crc_user_01",
        reason_for_change="50% video progress checkpoint",
    )
    assert audit.id == event_id
    assert audit.file_record_id == file_id
    assert audit.event == PlaybackEventType.PLAY_50
    assert audit.source == PlaybackEventSource.INTERNAL
    assert audit.session_id == "sess_12345"
    assert audit.user_id == "crc_user_01"


def test_file_record_hold_fields():
    """Verify FileRecord hold properties and default hold_reason is None.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    """
    file_id = str(uuid.uuid4())
    record = FileRecord(
        id=file_id,
        study_id="STUDY-101",
        filename="protocol.pdf",
        mime_type="application/pdf",
        size_bytes=2048,
        object_key="tenant_1/STUDY-101/doc/protocol.pdf",
        uploaded_by="data_mgr_01",
        created_by="data_mgr_01",
        is_on_hold=True,
        hold_reason="Pending regulatory review audit",
        reason_for_change="Retention hold applied",
    )
    assert record.is_on_hold is True
    assert record.hold_reason == "Pending regulatory review audit"


def test_file_audit_log_model():
    """Verify FileAuditLog entity creation and attributes.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    """
    log_id = str(uuid.uuid4())
    file_id = str(uuid.uuid4())
    audit_log = FileAuditLog(
        id=log_id,
        file_record_id=file_id,
        action="HOLD_SET",
        user_id="super_admin_01",
        reason_for_change="Court order retention hold",
        detail={"hold_reason": "Subpoena 2026-A"},
    )
    assert audit_log.id == log_id
    assert audit_log.file_record_id == file_id
    assert audit_log.action == "HOLD_SET"
    assert audit_log.user_id == "super_admin_01"
    assert audit_log.reason_for_change == "Court order retention hold"
    assert audit_log.detail == {"hold_reason": "Subpoena 2026-A"}
    assert isinstance(audit_log.timestamp, datetime)


def test_conversion_status_enum_and_helpers():
    """Verify ConversionStatus normalization and office MIME type detection.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    @req:PRD-DOC-002
    """
    assert ConversionStatus.from_str("ready") == ConversionStatus.READY
    assert ConversionStatus.from_str("pending") == ConversionStatus.PENDING
    assert ConversionStatus.from_str("FAILED") == ConversionStatus.FAILED
    assert ConversionStatus.from_str("not_required") == ConversionStatus.NOT_REQUIRED

    # Word / Excel / PowerPoint MIME types
    assert (
        is_convertible_office_mime_type(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
        )
        is True
    )
    assert (
        is_convertible_office_mime_type(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        )
        is True
    )
    assert (
        is_convertible_office_mime_type(
            "application/vnd.openxmlformats-officedocument.presentationml.presentation"
        )
        is True
    )
    assert is_convertible_office_mime_type("application/msword") is True
    assert is_convertible_office_mime_type("application/vnd.ms-excel") is True
    assert is_convertible_office_mime_type("application/vnd.ms-powerpoint") is True

    # By filename extension
    assert (
        is_convertible_office_mime_type("application/octet-stream", "protocol.docx")
        is True
    )
    assert (
        is_convertible_office_mime_type("application/octet-stream", "budget.xlsx")
        is True
    )
    assert (
        is_convertible_office_mime_type("application/octet-stream", "slides.pptx")
        is True
    )

    # Non-convertible MIME types
    assert is_convertible_office_mime_type("application/pdf") is False
    assert is_convertible_office_mime_type("video/mp4") is False
    assert is_convertible_office_mime_type("image/png") is False
    assert is_convertible_office_mime_type(None, "image.png") is False


def test_file_record_conversion_fields():
    """Verify FileRecord auto format conversion default and custom values.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    @req:PRD-DOC-002
    """
    file_id = str(uuid.uuid4())
    record = FileRecord(
        id=file_id,
        study_id="STUDY-101",
        filename="study_synopsis.docx",
        mime_type="application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        size_bytes=40960,
        object_key="tenant_1/STUDY-101/doc/study_synopsis.docx",
        uploaded_by="crc_user_01",
        created_by="crc_user_01",
        conversion_status=ConversionStatus.READY,
        pdf_rendition_key="tenant_1/STUDY-101/doc/renditions/study_synopsis.pdf",
        reason_for_change="Protocol synopsis uploaded",
    )
    assert record.conversion_status == ConversionStatus.READY
    assert (
        record.pdf_rendition_key
        == "tenant_1/STUDY-101/doc/renditions/study_synopsis.pdf"
    )
    assert record.conversion_error is None
