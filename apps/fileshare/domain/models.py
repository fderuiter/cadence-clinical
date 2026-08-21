"""Pure domain models, enums, and business invariants for Fileshare microservice.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002, PRD-DOC-003
"""

from datetime import UTC, datetime
from enum import StrEnum
import uuid

from pydantic import BaseModel, ConfigDict, Field


class ShareScope(StrEnum):
    """Scope of access granted by a share grant."""

    STUDY = "study"
    SITE = "site"
    INDIVIDUAL = "individual"
    FOLDER = "folder"


class PermissionLevel(StrEnum):
    """Granular permission levels for file sharing."""

    VIEW = "view"
    COMMENT = "comment"
    DOWNLOAD = "download"
    UPLOAD_REVISION = "upload_revision"
    RESHARE = "reshare"
    APPROVE = "approve"
    EXPIRE_REVOKE = "expire_revoke"

    @property
    def rank(self) -> int:
        """Numeric rank for hierarchical permission evaluation."""
        ranks = {
            PermissionLevel.VIEW: 1,
            PermissionLevel.COMMENT: 2,
            PermissionLevel.DOWNLOAD: 3,
            PermissionLevel.UPLOAD_REVISION: 4,
            PermissionLevel.RESHARE: 5,
            PermissionLevel.APPROVE: 6,
            PermissionLevel.EXPIRE_REVOKE: 7,
        }
        return ranks[self]

    def satisfies(self, required: "PermissionLevel") -> bool:
        """Check if this permission level is at least as permissive as the required level."""
        return self.rank >= required.rank

    def requires_watermark(self) -> bool:
        """Determines if access under this permission requires watermark enforcement."""
        return self in (PermissionLevel.VIEW, PermissionLevel.COMMENT)


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
    is_deleted: bool = Field(default=False)

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
        if self.expires_at is not None and datetime.now(UTC) > self.expires_at:
            return False
        return True


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
        if datetime.now(UTC) > self.expires_at:
            return False
        return True

