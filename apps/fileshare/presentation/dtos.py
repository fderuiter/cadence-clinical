"""Pydantic v2 request and response DTO schemas for Fileshare API.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002, PRD-DOC-003
"""

from datetime import datetime

from pydantic import BaseModel, ConfigDict, Field

from apps.fileshare.domain.models import PermissionLevel, ShareScope


class FileUploadUrlRequest(BaseModel):
    """Request payload to initiate a single-part or multipart upload session."""

    model_config = ConfigDict(strict=True)

    study_id: str = Field(min_length=1, max_length=255)
    site_id: str | None = Field(default=None, max_length=255)
    filename: str = Field(min_length=1, max_length=500)
    mime_type: str = Field(min_length=1, max_length=255)
    size_bytes: int = Field(ge=0)
    reason_for_change: str = Field(min_length=5, max_length=1000)
    is_multipart: bool = Field(default=False)
    parts_count: int = Field(default=1, ge=1, le=10000)


class FileUploadUrlResponse(BaseModel):
    """Response containing allocated file record details and presigned upload URLs."""

    model_config = ConfigDict(strict=True)

    file_id: str
    object_key: str
    upload_id: str | None = None
    upload_url: str | None = None
    upload_urls: dict[int, str] | None = None
    expires_in: int = 3600


class FileDownloadUrlResponse(BaseModel):
    """Response containing short-lived presigned GET download URL and watermark policy."""

    model_config = ConfigDict(strict=True)

    file_id: str
    filename: str
    mime_type: str
    download_url: str
    expires_in: int = 3600
    is_watermarked: bool = False


class FileRecordResponse(BaseModel):
    """Response schema representing file record metadata."""

    model_config = ConfigDict(strict=True, from_attributes=True)

    id: str
    study_id: str
    site_id: str | None
    filename: str
    mime_type: str
    size_bytes: int
    object_key: str
    checksum_sha256: str | None
    version_index: int
    uploaded_by: str
    uploaded_at: datetime
    is_on_hold: bool
    created_at: datetime
    created_by: str
    reason_for_change: str


class ShareGrantCreateRequest(BaseModel):
    """Request payload to create a new internal share grant."""

    model_config = ConfigDict(strict=True)

    granted_to_user_id: str | None = Field(default=None, max_length=255)
    scope: ShareScope = Field(default=ShareScope.INDIVIDUAL)
    permission_level: PermissionLevel = Field(default=PermissionLevel.VIEW)
    expires_at: datetime | None = Field(default=None)
    reason_for_change: str = Field(min_length=5, max_length=1000)


class ShareGrantResponse(BaseModel):
    """Response schema for share grant delegations."""

    model_config = ConfigDict(strict=True, from_attributes=True)

    id: str
    file_record_id: str
    granted_to_user_id: str | None
    granted_by_user_id: str
    scope: ShareScope
    permission_level: PermissionLevel
    expires_at: datetime | None
    revoked_at: datetime | None
    is_active: bool
    created_at: datetime
    created_by: str
    reason_for_change: str


class GuestLinkCreateRequest(BaseModel):
    """Request payload to generate an external guest link."""

    model_config = ConfigDict(strict=True)

    expires_in_hours: int = Field(default=24, ge=1, le=168)
    reason_for_change: str = Field(min_length=5, max_length=1000)


class GuestLinkResponse(BaseModel):
    """Response schema for created guest links."""

    model_config = ConfigDict(strict=True, from_attributes=True)

    id: str
    file_record_id: str
    guest_url: str
    expires_at: datetime
    created_by: str
    access_count: int
    is_valid: bool
