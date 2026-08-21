"""Application service managing file uploads, downloads, permissions, and sharing.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002, PRD-DOC-003
"""

import hashlib
import hmac
import os
import secrets
import uuid
from dataclasses import dataclass
from datetime import UTC, datetime, timedelta
from typing import Any

from apps.fileshare.domain.exceptions import (
    FileNotFoundError,
    FileSharePermissionDeniedError,
)
from apps.fileshare.domain.models import (
    FileRecord,
    GuestLink,
    PermissionLevel,
    ShareGrant,
    ShareScope,
)
from apps.fileshare.ports.repository_port import (
    FileRecordRepositoryPort,
    GuestLinkRepositoryPort,
    ShareGrantRepositoryPort,
)
from packages.storage.ports.storage_port import StoragePort

ADMIN_ROLES = {"super_admin", "sysadmin", "admin", "data_manager", "sponsor_admin"}


@dataclass(frozen=True)
class FileUploadSession:
    """Represents an initialized upload session with presigned singlepart or multipart target URLs."""

    file_id: str
    object_key: str
    upload_id: str | None = None
    upload_url: str | None = None
    upload_urls: dict[int, str] | None = None
    expires_in: int = 3600


@dataclass(frozen=True)
class FileDownloadSession:
    """Represents a validated download session with watermark flag and presigned download URL."""

    file_id: str
    filename: str
    mime_type: str
    download_url: str
    expires_in: int = 3600
    is_watermarked: bool = False


@dataclass(frozen=True)
class GuestLinkResult:
    """Represents a generated guest access link."""

    id: str
    file_record_id: str
    guest_url: str
    expires_at: datetime
    created_by: str
    access_count: int
    is_valid: bool


class FileShareService:
    """Service orchestrating file metadata lifecycle, permission gating, and object storage transfers."""

    def __init__(
        self,
        file_repo: FileRecordRepositoryPort,
        grant_repo: ShareGrantRepositoryPort,
        guest_repo: GuestLinkRepositoryPort,
        storage_port: StoragePort[dict[str, Any]],
    ) -> None:
        self.file_repo = file_repo
        self.grant_repo = grant_repo
        self.guest_repo = guest_repo
        self.storage_port = storage_port

    async def generate_upload_url(
        self,
        study_id: str,
        filename: str,
        mime_type: str,
        size_bytes: int,
        uploader_id: str,
        reason_for_change: str,
        tenant_id: str = "tenant_default",
        site_id: str | None = None,
        is_multipart: bool = False,
        parts_count: int = 1,
    ) -> FileUploadSession:
        """Allocate a draft FileRecord envelope and generate presigned upload URLs.

        Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002
        """
        file_id = str(uuid.uuid4())
        clean_filename = os.path.basename(filename).replace(" ", "_")
        object_key = f"{tenant_id}/{study_id}/{file_id}/{clean_filename}"

        record = FileRecord(
            id=file_id,
            study_id=study_id,
            site_id=site_id,
            filename=clean_filename,
            mime_type=mime_type,
            size_bytes=size_bytes,
            object_key=object_key,
            checksum_sha256=None,
            version_index=1,
            uploaded_by=uploader_id,
            uploaded_at=datetime.now(UTC),
            is_on_hold=False,
            is_deleted=False,
            created_at=datetime.now(UTC),
            created_by=uploader_id,
            reason_for_change=reason_for_change,
        )
        await self.file_repo.save(record)

        metadata = {
            "file_id": file_id,
            "study_id": study_id,
            "uploader": uploader_id,
            "tenant_id": tenant_id,
        }
        if site_id:
            metadata["site_id"] = site_id

        if is_multipart:
            upload_id = await self.storage_port.create_multipart_upload(
                key=object_key,
                content_type=mime_type,
                metadata=metadata,
            )
            part_numbers = list(range(1, parts_count + 1))
            part_urls = await self.storage_port.generate_presigned_multipart_urls(
                key=object_key,
                upload_id=upload_id,
                part_numbers=part_numbers,
                expires_in=3600,
            )
            return FileUploadSession(
                file_id=file_id,
                object_key=object_key,
                upload_id=upload_id,
                upload_urls=part_urls,
                expires_in=3600,
            )

        upload_url = await self.storage_port.generate_presigned_put_url(
            key=object_key,
            expires_in=3600,
            content_type=mime_type,
        )
        return FileUploadSession(
            file_id=file_id,
            object_key=object_key,
            upload_url=upload_url,
            expires_in=3600,
        )

    async def generate_download_url(
        self,
        file_id: str,
        caller_user_id: str,
        caller_roles: list[str],
        caller_site_id: str | None = None,
    ) -> FileDownloadSession:
        """Verify caller permissions and issue a short-lived presigned download URL.

        Enforces watermark policy for view-only/comment-only grants.
        Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-003
        """
        file_record = await self.file_repo.get_by_id(file_id)
        if not file_record:
            raise FileNotFoundError(f"File record '{file_id}' not found.")

        normalized_roles = {r.strip().lower() for r in caller_roles if r}
        is_admin = bool(normalized_roles.intersection(ADMIN_ROLES))
        is_uploader = file_record.uploaded_by == caller_user_id

        is_watermarked = False

        if not is_admin and not is_uploader:
            grants = await self.grant_repo.list_by_file_id(file_id, active_only=True)
            matching_grants: list[ShareGrant] = []

            for grant in grants:
                if (
                    (
                        grant.scope == ShareScope.INDIVIDUAL
                        and grant.granted_to_user_id == caller_user_id
                    )
                    or grant.scope == ShareScope.STUDY
                    or (
                        grant.scope == ShareScope.SITE
                        and caller_site_id
                        and file_record.site_id == caller_site_id
                    )
                ):
                    matching_grants.append(grant)

            if not matching_grants:
                raise FileSharePermissionDeniedError(
                    f"User '{caller_user_id}' does not have permission to access file '{file_id}'."
                )

            # Determine highest granted permission level
            max_grant = max(matching_grants, key=lambda g: g.permission_level.rank)
            if max_grant.permission_level.requires_watermark():
                is_watermarked = True

        disposition = f'attachment; filename="{file_record.filename}"'
        download_url = await self.storage_port.generate_presigned_get_url(
            key=file_record.object_key,
            expires_in=3600,
            response_content_disposition=disposition,
        )

        return FileDownloadSession(
            file_id=file_record.id,
            filename=file_record.filename,
            mime_type=file_record.mime_type,
            download_url=download_url,
            expires_in=3600,
            is_watermarked=is_watermarked,
        )

    async def create_share_grant(
        self,
        file_id: str,
        grantor_user_id: str,
        grantor_roles: list[str],
        granted_to_user_id: str | None,
        scope: ShareScope,
        permission_level: PermissionLevel,
        reason_for_change: str,
        expires_at: datetime | None = None,
    ) -> ShareGrant:
        """Create and persist a new share grant on a file record."""
        file_record = await self.file_repo.get_by_id(file_id)
        if not file_record:
            raise FileNotFoundError(f"File record '{file_id}' not found.")

        normalized_roles = {r.strip().lower() for r in grantor_roles if r}
        is_admin = bool(normalized_roles.intersection(ADMIN_ROLES))
        is_uploader = file_record.uploaded_by == grantor_user_id

        if not is_admin and not is_uploader:
            user_grant = await self.grant_repo.find_user_grant(file_id, grantor_user_id)
            if not user_grant or not user_grant.permission_level.satisfies(
                PermissionLevel.RESHARE
            ):
                raise FileSharePermissionDeniedError(
                    "Caller lacks reshare permissions for this file."
                )

        scope_enum = ShareScope(scope) if isinstance(scope, str) else scope
        perm_enum = (
            PermissionLevel(permission_level)
            if isinstance(permission_level, str)
            else permission_level
        )

        grant = ShareGrant(
            id=str(uuid.uuid4()),
            file_record_id=file_id,
            granted_to_user_id=granted_to_user_id,
            granted_by_user_id=grantor_user_id,
            scope=scope_enum,
            permission_level=perm_enum,
            expires_at=expires_at,
            revoked_at=None,
            is_deleted=False,
            created_at=datetime.now(UTC),
            created_by=grantor_user_id,
            reason_for_change=reason_for_change,
        )
        return await self.grant_repo.save(grant)

    async def create_guest_link(
        self,
        file_id: str,
        creator_user_id: str,
        reason_for_change: str,
        expires_in_hours: int = 24,
    ) -> GuestLinkResult:
        """Generate a secure, HMAC-hashed guest link for external temporary file access."""
        file_record = await self.file_repo.get_by_id(file_id)
        if not file_record:
            raise FileNotFoundError(f"File record '{file_id}' not found.")

        raw_secret_token = secrets.token_urlsafe(32)
        token_hmac = hmac.new(
            os.getenv("GATEWAY_SECRET", "default-guest-secret").encode(),
            raw_secret_token.encode(),
            hashlib.sha256,
        ).hexdigest()

        expires_at = datetime.now(UTC) + timedelta(hours=expires_in_hours)

        guest_link = GuestLink(
            id=str(uuid.uuid4()),
            file_record_id=file_id,
            token_hmac=token_hmac,
            expires_at=expires_at,
            created_by=creator_user_id,
            last_accessed_at=None,
            access_count=0,
            revoked_at=None,
            created_at=datetime.now(UTC),
            reason_for_change=reason_for_change,
        )
        saved = await self.guest_repo.save(guest_link)

        guest_url = f"/api/v1/fileshare/guest/{raw_secret_token}"
        return GuestLinkResult(
            id=saved.id,
            file_record_id=saved.file_record_id,
            guest_url=guest_url,
            expires_at=saved.expires_at,
            created_by=saved.created_by,
            access_count=saved.access_count,
            is_valid=saved.is_valid,
        )
