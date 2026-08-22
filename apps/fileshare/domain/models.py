"""Pure domain models, enums, and business invariants for Fileshare microservice.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002, PRD-DOC-003
"""

import uuid
from datetime import UTC, datetime
from enum import StrEnum
from typing import Any

from pydantic import BaseModel, ConfigDict, Field


class ShareScope(StrEnum):
    """Scope of access granted by a share grant."""

    STUDY = "study"
    SITE = "site"
    INDIVIDUAL = "individual"
    ROLE = "role"
    FOLDER = "folder"

    @classmethod
    def from_str(cls, value: str) -> ShareScope:
        """Normalize case-insensitive string into ShareScope enum."""
        return cls(value.strip().lower())


class PermissionLevel(StrEnum):
    """Granular permission levels for file sharing."""

    VIEW = "view"
    COMMENT = "comment"
    DOWNLOAD = "download"
    EDIT = "edit"
    UPLOAD_REVISION = "upload_revision"
    RESHARE = "reshare"
    APPROVE = "approve"
    ADMIN = "admin"
    EXPIRE_REVOKE = "expire_revoke"

    @classmethod
    def from_str(cls, value: str) -> PermissionLevel:
        """Normalize case-insensitive string into PermissionLevel enum."""
        return cls(value.strip().lower())

    @property
    def rank(self) -> int:
        """Numeric rank for hierarchical permission evaluation."""
        ranks = {
            PermissionLevel.VIEW: 1,
            PermissionLevel.COMMENT: 2,
            PermissionLevel.DOWNLOAD: 3,
            PermissionLevel.EDIT: 4,
            PermissionLevel.UPLOAD_REVISION: 4,
            PermissionLevel.RESHARE: 5,
            PermissionLevel.APPROVE: 6,
            PermissionLevel.ADMIN: 7,
            PermissionLevel.EXPIRE_REVOKE: 7,
        }
        return ranks[self]

    def satisfies(self, required: PermissionLevel) -> bool:
        """Check if this permission level is at least as permissive as the required level."""
        return self.rank >= required.rank

    def requires_watermark(self) -> bool:
        """Determines if access under this permission requires watermark enforcement."""
        return self in (PermissionLevel.VIEW, PermissionLevel.COMMENT)


class TranscodeStatus(StrEnum):
    """Lifecycle status of HLS/DASH video transcoding."""

    NOT_REQUIRED = "NOT_REQUIRED"
    PENDING = "PENDING"
    IN_PROGRESS = "IN_PROGRESS"
    READY = "READY"
    FAILED = "FAILED"

    @classmethod
    def from_str(cls, value: str) -> TranscodeStatus:
        """Normalize case-insensitive string into TranscodeStatus enum."""
        return cls(value.strip().upper())


class PlaybackEventType(StrEnum):
    """Types of viewer session video playback audit checkpoint events."""

    PLAY_START = "PLAY_START"
    PLAY_25 = "PLAY_25"
    PLAY_50 = "PLAY_50"
    PLAY_75 = "PLAY_75"
    PLAY_END = "PLAY_END"

    @classmethod
    def from_str(cls, value: str) -> PlaybackEventType:
        """Normalize string into PlaybackEventType enum."""
        return cls(value.strip().upper())


class PlaybackEventSource(StrEnum):
    """Origin source of video playback session."""

    INTERNAL = "internal"
    GUEST_LINK = "guest_link"

    @classmethod
    def from_str(cls, value: str) -> PlaybackEventSource:
        """Normalize string into PlaybackEventSource enum."""
        return cls(value.strip().lower())


def is_video_mime_type(mime_type: str | None) -> bool:
    """Check if the provided MIME type represents a video container format."""
    if not mime_type:
        return False
    normalized = mime_type.strip().lower()
    return normalized.startswith("video/") or normalized in {
        "application/x-mpegurl",
        "application/vnd.apple.mpegurl",
        "application/dash+xml",
    }


class ScanStatus(StrEnum):
    """Lifecycle status of file malware/virus scanning."""

    PENDING = "PENDING"
    CLEAN = "CLEAN"
    INFECTED = "INFECTED"
    SCAN_ERROR = "SCAN_ERROR"

    @classmethod
    def from_str(cls, value: str) -> ScanStatus:
        """Normalize string into ScanStatus enum."""
        return cls(value.strip().upper())


class FileStatus(StrEnum):
    """Lifecycle availability and readiness status of a file record."""

    PENDING_SCAN = "PENDING_SCAN"
    READY = "READY"
    PENDING_TRANSCODE = "PENDING_TRANSCODE"
    QUARANTINED = "QUARANTINED"

    @classmethod
    def from_str(cls, value: str) -> FileStatus:
        """Normalize string into FileStatus enum."""
        return cls(value.strip().upper())


class FileRecord(BaseModel):
    """Domain model representing a managed file and its object storage metadata.

    Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002, PRD-DOC-003
    """

    model_config = ConfigDict(strict=True, frozen=True)

    id: str = Field(default_factory=lambda: str(uuid.uuid4()))
    study_id: str = Field(min_length=1, max_length=255)
    site_id: str | None = Field(default=None, max_length=255)
    filename: str = Field(min_length=1, max_length=500)
    mime_type: str = Field(min_length=1, max_length=255)
    size_bytes: int = Field(ge=0)
    object_key: str = Field(min_length=1, max_length=1000)
    checksum_sha256: str | None = Field(default=None, min_length=64, max_length=64)
    version_index: int = Field(default=1, ge=1)
    uploaded_by: str = Field(min_length=1, max_length=255)
    uploaded_at: datetime = Field(default_factory=lambda: datetime.now(UTC))
    is_on_hold: bool = Field(default=False)
    hold_reason: str | None = Field(default=None, max_length=1000)
    is_deleted: bool = Field(default=False)

    # File lifecycle and virus scan fields
    status: FileStatus = Field(default=FileStatus.PENDING_SCAN)
    scan_status: ScanStatus = Field(default=ScanStatus.PENDING)
    scan_engine: str | None = Field(default=None, max_length=100)
    scan_completed_at: datetime | None = Field(default=None)
    scan_threat_name: str | None = Field(default=None, max_length=255)

    # Video transcoding metadata
    transcode_status: TranscodeStatus = Field(default=TranscodeStatus.NOT_REQUIRED)
    hls_manifest_key: str | None = Field(default=None, max_length=1000)
    transcode_error: str | None = Field(default=None, max_length=2000)

    # GxP audit fields
    created_at: datetime = Field(default_factory=lambda: datetime.now(UTC))
    created_by: str = Field(min_length=1, max_length=255)
    reason_for_change: str = Field(min_length=5, max_length=1000)


class ShareGrant(BaseModel):
    """Domain model representing an access delegation or share grant.

    Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-003
    """

    model_config = ConfigDict(strict=True, frozen=True)

    id: str = Field(default_factory=lambda: str(uuid.uuid4()))
    file_record_id: str = Field(min_length=1, max_length=36)
    granted_to_user_id: str | None = Field(default=None, max_length=255)
    granted_by_user_id: str = Field(min_length=1, max_length=255)
    scope: ShareScope = Field(default=ShareScope.INDIVIDUAL)
    permission_level: PermissionLevel = Field(default=PermissionLevel.VIEW)
    expires_at: datetime | None = Field(default=None)
    revoked_at: datetime | None = Field(default=None)
    is_deleted: bool = Field(default=False)

    # GxP audit fields
    created_at: datetime = Field(default_factory=lambda: datetime.now(UTC))
    created_by: str = Field(min_length=1, max_length=255)
    reason_for_change: str = Field(min_length=5, max_length=1000)

    @property
    def is_active(self) -> bool:
        """Evaluates whether the grant is currently active, non-revoked, and unexpired."""
        if self.is_deleted or self.revoked_at is not None:
            return False
        if self.expires_at is None:
            return True
        now = datetime.now(UTC)
        expires = (
            self.expires_at
            if self.expires_at.tzinfo is not None
            else self.expires_at.replace(tzinfo=UTC)
        )
        return now <= expires


class GuestLink(BaseModel):
    """Domain model representing a time-bounded external guest access token.

    Requirements: PRD-SYS-001, PRD-DOC-001
    """

    model_config = ConfigDict(strict=True, frozen=True)

    id: str = Field(default_factory=lambda: str(uuid.uuid4()))
    file_record_id: str = Field(min_length=1, max_length=36)
    token_hmac: str = Field(min_length=32, max_length=128)
    expires_at: datetime
    created_by: str = Field(min_length=1, max_length=255)
    last_accessed_at: datetime | None = Field(default=None)
    access_count: int = Field(default=0, ge=0)
    revoked_at: datetime | None = Field(default=None)

    # GxP audit fields
    created_at: datetime = Field(default_factory=lambda: datetime.now(UTC))
    reason_for_change: str = Field(min_length=5, max_length=1000)

    @property
    def is_valid(self) -> bool:
        """Evaluates if the guest link is unrevoked and not expired."""
        if self.revoked_at is not None:
            return False
        now = datetime.now(UTC)
        expires = (
            self.expires_at
            if self.expires_at.tzinfo is not None
            else self.expires_at.replace(tzinfo=UTC)
        )
        return now <= expires


class PlaybackAuditEvent(BaseModel):
    """Domain model representing a viewer session playback audit event.

    Requirements: PRD-SYS-001, PRD-DOC-001, 21 CFR Part 11
    """

    model_config = ConfigDict(strict=True, frozen=True)

    id: str = Field(default_factory=lambda: str(uuid.uuid4()))
    file_record_id: str = Field(min_length=1, max_length=36)
    event: PlaybackEventType
    source: PlaybackEventSource = Field(default=PlaybackEventSource.INTERNAL)
    session_id: str = Field(min_length=1, max_length=255)
    user_id: str = Field(min_length=1, max_length=255)
    timestamp: datetime = Field(default_factory=lambda: datetime.now(UTC))

    # GxP audit fields
    created_at: datetime = Field(default_factory=lambda: datetime.now(UTC))
    reason_for_change: str = Field(min_length=5, max_length=1000)


class FileAuditLog(BaseModel):
    """Domain model representing an immutable Part 11 audit trail event for a file record.

    Requirements: PRD-SYS-001, PRD-DOC-001, 21 CFR Part 11
    """

    model_config = ConfigDict(strict=True, frozen=True)

    id: str = Field(default_factory=lambda: str(uuid.uuid4()))
    file_record_id: str = Field(min_length=1, max_length=36)
    action: str = Field(min_length=1, max_length=50)
    user_id: str = Field(min_length=1, max_length=255)
    timestamp: datetime = Field(default_factory=lambda: datetime.now(UTC))
    reason_for_change: str = Field(min_length=1, max_length=1000)
    detail: dict[str, Any] | None = Field(default=None)
