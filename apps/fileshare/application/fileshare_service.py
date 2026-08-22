import asyncio
import contextlib
import hashlib
import hmac
import logging
import os
import secrets
import uuid
from collections.abc import AsyncIterable
from dataclasses import dataclass
from datetime import UTC, datetime, timedelta
from typing import Any

from apps.fileshare.domain.exceptions import (
    FileNotFoundError,
    FileOnHoldError,
    FileQuarantinedError,
    FileSharePermissionDeniedError,
)
from apps.fileshare.domain.models import (
    FileAuditLog,
    FileRecord,
    FileStatus,
    GuestLink,
    PermissionLevel,
    PlaybackAuditEvent,
    PlaybackEventSource,
    PlaybackEventType,
    ScanStatus,
    ShareGrant,
    ShareScope,
    TranscodeStatus,
    is_video_mime_type,
)
from apps.fileshare.ports.repository_port import (
    FileAuditLogRepositoryPort,
    FileRecordRepositoryPort,
    GuestLinkRepositoryPort,
    PlaybackAuditRepositoryPort,
    ShareGrantRepositoryPort,
)
from apps.fileshare.ports.scan_port import ScanPort, ScanResult
from apps.fileshare.workers.transcode_worker import enqueue_transcode_task
from packages.security.notifications import (
    NotificationCategory,
    NotificationEvent,
    NotificationPriority,
    publish_notification,
)
from packages.storage.ports.storage_port import StoragePort

logger = logging.getLogger("apps.fileshare.service")

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
    checksum_sha256: str | None = None
    required_headers: dict[str, str] | None = None


@dataclass(frozen=True)
class FileDownloadSession:
    """Represents a validated download session with watermark flag and presigned download URL."""

    file_id: str
    filename: str
    mime_type: str
    download_url: str
    expires_in: int = 3600
    is_watermarked: bool = False
    access_count: int | None = None


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
        audit_repo: PlaybackAuditRepositoryPort | None = None,
        scan_port: ScanPort | None = None,
        file_audit_repo: FileAuditLogRepositoryPort | None = None,
    ) -> None:
        self.file_repo = file_repo
        self.grant_repo = grant_repo
        self.guest_repo = guest_repo
        self.storage_port = storage_port
        self.audit_repo = audit_repo
        self.scan_port = scan_port
        self.file_audit_repo = file_audit_repo

    async def record_audit_log(
        self,
        file_id: str,
        action: str,
        user_id: str,
        reason_for_change: str,
        detail: dict[str, Any] | None = None,
    ) -> FileAuditLog | None:
        """Record an immutable Part 11 audit log entry for a file action."""
        if not self.file_audit_repo:
            return None
        entry = FileAuditLog(
            id=str(uuid.uuid4()),
            file_record_id=file_id,
            action=action,
            user_id=user_id,
            timestamp=datetime.now(UTC),
            reason_for_change=reason_for_change,
            detail=detail,
        )
        return await self.file_audit_repo.save(entry)

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
        checksum_sha256: str | None = None,
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
            checksum_sha256=checksum_sha256,
            version_index=1,
            uploaded_by=uploader_id,
            uploaded_at=datetime.now(UTC),
            is_on_hold=False,
            hold_reason=None,
            is_deleted=False,
            status=FileStatus.PENDING_SCAN,
            scan_status=ScanStatus.PENDING,
            scan_engine=None,
            scan_completed_at=None,
            scan_threat_name=None,
            transcode_status=TranscodeStatus.NOT_REQUIRED,
            hls_manifest_key=None,
            transcode_error=None,
            created_at=datetime.now(UTC),
            created_by=uploader_id,
            reason_for_change=reason_for_change,
        )
        await self.file_repo.save(record)

        await self.record_audit_log(
            file_id=file_id,
            action="FILE_UPLOADED",
            user_id=uploader_id,
            reason_for_change=reason_for_change,
            detail={"filename": clean_filename, "size_bytes": size_bytes},
        )

        metadata = {
            "file_id": file_id,
            "study_id": study_id,
            "uploader": uploader_id,
            "tenant_id": tenant_id,
        }
        if site_id:
            metadata["site_id"] = site_id
        if checksum_sha256:
            metadata["checksum_sha256"] = checksum_sha256

        required_headers: dict[str, str] = {}
        if checksum_sha256:
            required_headers["x-amz-checksum-sha256"] = checksum_sha256

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
                checksum_sha256=checksum_sha256,
                required_headers=required_headers if required_headers else None,
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
            checksum_sha256=checksum_sha256,
            required_headers=required_headers if required_headers else None,
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

        if (
            file_record.status == FileStatus.QUARANTINED
            or file_record.scan_status == ScanStatus.INFECTED
        ):
            raise FileQuarantinedError(
                f"File '{file_record.filename}' is quarantined due to detected malware.",
                reason="QUARANTINED",
            )

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
                    or (
                        grant.scope == ShareScope.ROLE
                        and grant.granted_to_user_id
                        and grant.granted_to_user_id.strip().lower() in normalized_roles
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
        scope: ShareScope | str,
        permission_level: PermissionLevel | str,
        reason_for_change: str,
        expires_at: datetime | None = None,
    ) -> ShareGrant:
        """Create and persist a new share grant on a file record."""
        file_record = await self.file_repo.get_by_id(file_id)
        if not file_record:
            raise FileNotFoundError(f"File record '{file_id}' not found.")

        if (
            file_record.status == FileStatus.QUARANTINED
            or file_record.scan_status == ScanStatus.INFECTED
        ):
            raise FileQuarantinedError(
                f"Cannot share quarantined file '{file_record.filename}'.",
                reason="QUARANTINED",
            )

        normalized_roles = {r.strip().lower() for r in grantor_roles if r}
        is_admin = bool(normalized_roles.intersection(ADMIN_ROLES))
        is_uploader = file_record.uploaded_by == grantor_user_id

        if not is_admin and not is_uploader:
            user_grant = await self.grant_repo.find_user_grant(file_id, grantor_user_id)
            if not user_grant or not (
                user_grant.permission_level.satisfies(PermissionLevel.ADMIN)
                or user_grant.permission_level.satisfies(PermissionLevel.RESHARE)
            ):
                raise FileSharePermissionDeniedError(
                    "Caller lacks reshare permissions for this file."
                )

        scope_enum = ShareScope.from_str(scope) if isinstance(scope, str) else scope
        perm_enum = (
            PermissionLevel.from_str(permission_level)
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

    async def revoke_share_grant(
        self,
        file_id: str,
        grant_id: str,
        caller_user_id: str,
        caller_roles: list[str],
        reason_for_change: str,
    ) -> ShareGrant:
        """Revoke an existing internal share grant with GxP authorization check."""
        file_record = await self.file_repo.get_by_id(file_id)
        if not file_record:
            raise FileNotFoundError(f"File record '{file_id}' not found.")

        grant = await self.grant_repo.get_by_id(grant_id)
        if not grant or grant.file_record_id != file_id:
            raise FileNotFoundError(
                f"Share grant '{grant_id}' not found for file '{file_id}'."
            )

        normalized_roles = {r.strip().lower() for r in caller_roles if r}
        is_admin = bool(normalized_roles.intersection(ADMIN_ROLES))
        is_uploader = file_record.uploaded_by == caller_user_id
        is_grantor = grant.granted_by_user_id == caller_user_id

        if not is_admin and not is_uploader and not is_grantor:
            user_grant = await self.grant_repo.find_user_grant(file_id, caller_user_id)
            if not user_grant or not (
                user_grant.permission_level.satisfies(PermissionLevel.ADMIN)
                or user_grant.permission_level.satisfies(PermissionLevel.EXPIRE_REVOKE)
            ):
                raise FileSharePermissionDeniedError(
                    "Caller lacks permission to revoke this share grant."
                )

        revoked_grant = ShareGrant(
            id=grant.id,
            file_record_id=grant.file_record_id,
            granted_to_user_id=grant.granted_to_user_id,
            granted_by_user_id=grant.granted_by_user_id,
            scope=grant.scope,
            permission_level=grant.permission_level,
            expires_at=grant.expires_at,
            revoked_at=datetime.now(UTC),
            is_deleted=True,
            created_at=grant.created_at,
            created_by=grant.created_by,
            reason_for_change=reason_for_change,
        )
        return await self.grant_repo.save(revoked_grant)

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

        if (
            file_record.status == FileStatus.QUARANTINED
            or file_record.scan_status == ScanStatus.INFECTED
        ):
            raise FileQuarantinedError(
                f"Cannot generate guest link for quarantined file '{file_record.filename}'.",
                reason="QUARANTINED",
            )

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

    async def access_guest_link(
        self,
        raw_secret_token: str,
    ) -> FileDownloadSession:
        """Resolve an external guest link token and issue a presigned download session."""
        token_hmac = hmac.new(
            os.getenv("GATEWAY_SECRET", "default-guest-secret").encode(),
            raw_secret_token.encode(),
            hashlib.sha256,
        ).hexdigest()

        guest_link = await self.guest_repo.get_by_token_hmac(token_hmac)
        if not guest_link:
            raise FileNotFoundError("Guest link not found or invalid.")

        if guest_link.revoked_at is not None:
            raise FileSharePermissionDeniedError("Guest link has been revoked.")

        if not guest_link.is_valid:
            raise FileSharePermissionDeniedError("Guest link has expired.")

        file_record = await self.file_repo.get_by_id(guest_link.file_record_id)
        if not file_record or file_record.is_deleted:
            raise FileNotFoundError("Associated file record not found.")

        if (
            file_record.status == FileStatus.QUARANTINED
            or file_record.scan_status == ScanStatus.INFECTED
        ):
            raise FileQuarantinedError(
                f"File '{file_record.filename}' is quarantined and cannot be accessed.",
                reason="QUARANTINED",
            )

        # Update download counter and access timestamp
        updated_guest_link = GuestLink(
            id=guest_link.id,
            file_record_id=guest_link.file_record_id,
            token_hmac=guest_link.token_hmac,
            expires_at=guest_link.expires_at,
            created_by=guest_link.created_by,
            last_accessed_at=datetime.now(UTC),
            access_count=guest_link.access_count + 1,
            revoked_at=guest_link.revoked_at,
            created_at=guest_link.created_at,
            reason_for_change="External guest link access",
        )
        await self.guest_repo.save(updated_guest_link)

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
            is_watermarked=False,
            access_count=updated_guest_link.access_count,
        )

    async def revoke_guest_link(
        self,
        file_id: str,
        guest_link_id: str,
        caller_user_id: str,
        caller_roles: list[str],
        reason_for_change: str,
    ) -> GuestLink:
        """Revoke an active external guest link with authorization check."""
        file_record = await self.file_repo.get_by_id(file_id)
        if not file_record:
            raise FileNotFoundError(f"File record '{file_id}' not found.")

        guest_link = await self.guest_repo.get_by_id(guest_link_id)
        if not guest_link or guest_link.file_record_id != file_id:
            raise FileNotFoundError(
                f"Guest link '{guest_link_id}' not found for file '{file_id}'."
            )

        normalized_roles = {r.strip().lower() for r in caller_roles if r}
        is_admin = bool(normalized_roles.intersection(ADMIN_ROLES))
        is_uploader = file_record.uploaded_by == caller_user_id
        is_creator = guest_link.created_by == caller_user_id

        if not is_admin and not is_uploader and not is_creator:
            user_grant = await self.grant_repo.find_user_grant(file_id, caller_user_id)
            if not user_grant or not (
                user_grant.permission_level.satisfies(PermissionLevel.ADMIN)
                or user_grant.permission_level.satisfies(PermissionLevel.EXPIRE_REVOKE)
            ):
                raise FileSharePermissionDeniedError(
                    "Caller lacks permission to revoke this guest link."
                )

        revoked_link = GuestLink(
            id=guest_link.id,
            file_record_id=guest_link.file_record_id,
            token_hmac=guest_link.token_hmac,
            expires_at=guest_link.expires_at,
            created_by=guest_link.created_by,
            last_accessed_at=guest_link.last_accessed_at,
            access_count=guest_link.access_count,
            revoked_at=datetime.now(UTC),
            created_at=guest_link.created_at,
            reason_for_change=reason_for_change,
        )
        return await self.guest_repo.save(revoked_link)

    async def complete_upload(
        self,
        file_id: str,
        caller_user_id: str,
        upload_id: str | None = None,
        parts: list[dict[str, Any]] | None = None,
        checksum_sha256: str | None = None,
        reason_for_change: str = "Completed file upload and virus scan",
    ) -> FileRecord:
        """Completes upload session, finalizes multipart upload if needed, streams payload through ScanPort, and applies quarantine or transcode lifecycle."""
        file_record = await self.file_repo.get_by_id(file_id)
        if not file_record or file_record.is_deleted:
            raise FileNotFoundError(f"File record '{file_id}' not found.")

        # 1. Finalize multipart upload if upload_id and parts provided
        if upload_id and parts:
            await self.storage_port.complete_multipart_upload(
                key=file_record.object_key,
                upload_id=upload_id,
                parts=parts,
            )

        # 2. Stream object bytes through virus scanner with exponential retry on scanner errors
        scan_result = None
        if self.scan_port:

            async def _stream_generator() -> AsyncIterable[bytes]:
                data, _ = await self.storage_port.get_object(file_record.object_key)
                chunk_size = 65536
                for i in range(0, len(data), chunk_size):
                    yield data[i : i + chunk_size]

            for attempt in range(3):
                scan_result = await self.scan_port.scan_stream(_stream_generator())
                if scan_result.status != ScanStatus.SCAN_ERROR:
                    break
                if attempt < 2:
                    await asyncio.sleep(0.05 * (2**attempt))
        else:
            scan_result = ScanResult(
                status=ScanStatus.CLEAN,
                engine="bypass",
                details="No scan port configured",
            )

        if scan_result is None:
            scan_result = ScanResult(
                status=ScanStatus.SCAN_ERROR,
                engine="clamav-1.3",
                details="No scan result returned",
            )

        now = datetime.now(UTC)

        # 3. Handle CLEAN outcome
        if scan_result.status == ScanStatus.CLEAN:
            is_video = is_video_mime_type(file_record.mime_type)
            new_status = FileStatus.PENDING_TRANSCODE if is_video else FileStatus.READY
            new_transcode_status = (
                TranscodeStatus.PENDING if is_video else TranscodeStatus.NOT_REQUIRED
            )

            updated_record = FileRecord(
                id=file_record.id,
                study_id=file_record.study_id,
                site_id=file_record.site_id,
                filename=file_record.filename,
                mime_type=file_record.mime_type,
                size_bytes=file_record.size_bytes,
                object_key=file_record.object_key,
                checksum_sha256=checksum_sha256 or file_record.checksum_sha256,
                version_index=file_record.version_index,
                uploaded_by=file_record.uploaded_by,
                uploaded_at=file_record.uploaded_at,
                is_on_hold=file_record.is_on_hold,
                is_deleted=False,
                status=new_status,
                scan_status=ScanStatus.CLEAN,
                scan_engine=scan_result.engine,
                scan_completed_at=now,
                scan_threat_name=None,
                transcode_status=new_transcode_status,
                hls_manifest_key=file_record.hls_manifest_key,
                transcode_error=file_record.transcode_error,
                created_at=file_record.created_at,
                created_by=file_record.created_by,
                reason_for_change=reason_for_change,
            )
            saved = await self.file_repo.save(updated_record)

            if is_video:
                with contextlib.suppress(Exception):
                    await enqueue_transcode_task(file_id)

            return saved

        # 4. Handle INFECTED outcome
        if scan_result.status == ScanStatus.INFECTED:
            threat_name = scan_result.threat_name or "Malware.Detected"
            quarantine_key = f"quarantine/{file_record.object_key}"

            # Relocate object to quarantine prefix in storage
            with contextlib.suppress(Exception):
                data, sha = await self.storage_port.get_object(file_record.object_key)
                await self.storage_port.put_object(
                    key=quarantine_key,
                    data=data,
                    content_type=file_record.mime_type,
                    metadata={
                        "quarantine_threat": threat_name,
                        "original_key": file_record.object_key,
                    },
                )
                await self.storage_port.delete_object(file_record.object_key)

            updated_record = FileRecord(
                id=file_record.id,
                study_id=file_record.study_id,
                site_id=file_record.site_id,
                filename=file_record.filename,
                mime_type=file_record.mime_type,
                size_bytes=file_record.size_bytes,
                object_key=quarantine_key,
                checksum_sha256=checksum_sha256 or file_record.checksum_sha256,
                version_index=file_record.version_index,
                uploaded_by=file_record.uploaded_by,
                uploaded_at=file_record.uploaded_at,
                is_on_hold=file_record.is_on_hold,
                is_deleted=False,
                status=FileStatus.QUARANTINED,
                scan_status=ScanStatus.INFECTED,
                scan_engine=scan_result.engine,
                scan_completed_at=now,
                scan_threat_name=threat_name,
                transcode_status=TranscodeStatus.FAILED,
                hls_manifest_key=None,
                transcode_error=f"Quarantined malware: {threat_name}",
                created_at=file_record.created_at,
                created_by=file_record.created_by,
                reason_for_change=f"Quarantined due to malware detection: {threat_name}",
            )
            saved = await self.file_repo.save(updated_record)

            # Alert uploader
            with contextlib.suppress(Exception):
                await publish_notification(
                    NotificationEvent(
                        recipient_user_id=file_record.uploaded_by,
                        category=NotificationCategory.ALERTS,
                        priority=NotificationPriority.CRITICAL,
                        channels="IN_APP,EMAIL",
                        message_content=(
                            f"Security Alert: File '{file_record.filename}' uploaded to study "
                            f"'{file_record.study_id}' failed virus scanning and has been quarantined. "
                            f"Threat detected: {threat_name}."
                        ),
                        related_entity_id=file_record.id,
                        related_entity_type="file_record",
                    )
                )

            # Alert super_admin
            with contextlib.suppress(Exception):
                await publish_notification(
                    NotificationEvent(
                        recipient_role="super_admin",
                        category=NotificationCategory.ALERTS,
                        priority=NotificationPriority.CRITICAL,
                        channels="IN_APP,EMAIL",
                        message_content=(
                            f"Malware Detected: User '{file_record.uploaded_by}' uploaded infected file "
                            f"'{file_record.filename}' (ID: {file_record.id}) to study "
                            f"'{file_record.study_id}'. Threat: {threat_name}. File has been quarantined."
                        ),
                        related_entity_id=file_record.id,
                        related_entity_type="file_record",
                    )
                )

            return saved

        # 5. Handle SCAN_ERROR (all retries failed)
        updated_record = FileRecord(
            id=file_record.id,
            study_id=file_record.study_id,
            site_id=file_record.site_id,
            filename=file_record.filename,
            mime_type=file_record.mime_type,
            size_bytes=file_record.size_bytes,
            object_key=file_record.object_key,
            checksum_sha256=checksum_sha256 or file_record.checksum_sha256,
            version_index=file_record.version_index,
            uploaded_by=file_record.uploaded_by,
            uploaded_at=file_record.uploaded_at,
            is_on_hold=file_record.is_on_hold,
            is_deleted=False,
            status=FileStatus.PENDING_SCAN,
            scan_status=ScanStatus.SCAN_ERROR,
            scan_engine=scan_result.engine,
            scan_completed_at=now,
            scan_threat_name=None,
            transcode_status=file_record.transcode_status,
            hls_manifest_key=file_record.hls_manifest_key,
            transcode_error=file_record.transcode_error,
            created_at=file_record.created_at,
            created_by=file_record.created_by,
            reason_for_change="Virus scan failed after retry attempts",
        )
        saved = await self.file_repo.save(updated_record)

        # Alert super_admin regarding engine unavailability
        with contextlib.suppress(Exception):
            await publish_notification(
                NotificationEvent(
                    recipient_role="super_admin",
                    category=NotificationCategory.ALERTS,
                    priority=NotificationPriority.HIGH,
                    channels="IN_APP",
                    message_content=(
                        f"Virus Scan Failure: File '{file_record.filename}' (ID: {file_record.id}) "
                        f"could not be scanned after 3 attempts due to ClamAV engine error. "
                        f"File remains in PENDING_SCAN state."
                    ),
                    related_entity_id=file_record.id,
                    related_entity_type="file_record",
                )
            )

        return saved

    async def abort_upload(
        self,
        file_id: str,
        caller_user_id: str,
        upload_id: str | None = None,
        reason_for_change: str = "Aborted file upload",
    ) -> FileRecord:
        """Aborts upload session, cancels multipart upload if active, and marks record deleted."""
        file_record = await self.file_repo.get_by_id(file_id)
        if not file_record:
            raise FileNotFoundError(f"File record '{file_id}' not found.")

        if upload_id:
            with contextlib.suppress(Exception):
                await self.storage_port.abort_multipart_upload(
                    key=file_record.object_key, upload_id=upload_id
                )

        aborted_record = FileRecord(
            id=file_record.id,
            study_id=file_record.study_id,
            site_id=file_record.site_id,
            filename=file_record.filename,
            mime_type=file_record.mime_type,
            size_bytes=file_record.size_bytes,
            object_key=file_record.object_key,
            checksum_sha256=file_record.checksum_sha256,
            version_index=file_record.version_index,
            uploaded_by=file_record.uploaded_by,
            uploaded_at=file_record.uploaded_at,
            is_on_hold=file_record.is_on_hold,
            is_deleted=True,
            status=file_record.status,
            scan_status=file_record.scan_status,
            scan_engine=file_record.scan_engine,
            scan_completed_at=file_record.scan_completed_at,
            scan_threat_name=file_record.scan_threat_name,
            transcode_status=file_record.transcode_status,
            hls_manifest_key=file_record.hls_manifest_key,
            transcode_error=file_record.transcode_error,
            created_at=file_record.created_at,
            created_by=file_record.created_by,
            reason_for_change=reason_for_change,
        )
        return await self.file_repo.save(aborted_record)

    async def generate_stream_url(
        self,
        file_id: str,
        caller_user_id: str,
        caller_roles: list[str],
        caller_site_id: str | None = None,
    ) -> tuple[FileRecord, str | None]:
        """Verify caller permissions and issue short-lived presigned HLS master manifest stream URL.

        Requirements: PRD-SYS-001, PRD-DOC-001
        """
        file_record = await self.file_repo.get_by_id(file_id)
        if not file_record:
            raise FileNotFoundError(f"File record '{file_id}' not found.")

        if (
            file_record.status == FileStatus.QUARANTINED
            or file_record.scan_status == ScanStatus.INFECTED
        ):
            raise FileQuarantinedError(
                f"File '{file_record.filename}' is quarantined and cannot be streamed.",
                reason="QUARANTINED",
            )

        normalized_roles = {r.strip().lower() for r in caller_roles if r}
        is_admin = bool(normalized_roles.intersection(ADMIN_ROLES))
        is_uploader = file_record.uploaded_by == caller_user_id

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
                    or (
                        grant.scope == ShareScope.ROLE
                        and grant.granted_to_user_id
                        and grant.granted_to_user_id.strip().lower() in normalized_roles
                    )
                ):
                    matching_grants.append(grant)

            if not matching_grants:
                raise FileSharePermissionDeniedError(
                    f"User '{caller_user_id}' does not have permission to stream file '{file_id}'."
                )

        if file_record.transcode_status == TranscodeStatus.READY:
            manifest_key = file_record.hls_manifest_key or f"hls/{file_id}/master.m3u8"
            stream_url = await self.storage_port.generate_presigned_get_url(
                key=manifest_key,
                expires_in=300,
            )
            return file_record, stream_url

        return file_record, None

    async def record_playback_audit(
        self,
        file_id: str,
        event: PlaybackEventType | str,
        source: PlaybackEventSource | str,
        session_id: str,
        user_id: str,
        guest_token: str | None = None,
        reason_for_change: str = "Video playback checkpoint audit event",
    ) -> PlaybackAuditEvent:
        """Records a video playback checkpoint audit event.

        Requirements: PRD-SYS-001, PRD-DOC-001, 21 CFR Part 11
        """
        file_record = await self.file_repo.get_by_id(file_id)
        if not file_record:
            raise FileNotFoundError(f"File record '{file_id}' not found.")

        event_enum = (
            PlaybackEventType.from_str(event) if isinstance(event, str) else event
        )
        source_enum = (
            PlaybackEventSource.from_str(source) if isinstance(source, str) else source
        )

        actor_user_id = user_id
        if source_enum == PlaybackEventSource.GUEST_LINK and guest_token:
            actor_user_id = (
                f"guest_{hashlib.sha256(guest_token.encode()).hexdigest()[:16]}"
            )

        audit_event = PlaybackAuditEvent(
            id=str(uuid.uuid4()),
            file_record_id=file_id,
            event=event_enum,
            source=source_enum,
            session_id=session_id,
            user_id=actor_user_id,
            timestamp=datetime.now(UTC),
            created_at=datetime.now(UTC),
            reason_for_change=reason_for_change,
        )
        if self.audit_repo:
            return await self.audit_repo.save(audit_event)
        return audit_event

    async def set_hold(
        self,
        file_id: str,
        caller_user_id: str,
        caller_roles: list[str],
        reason_for_change: str,
        hold_reason: str | None = None,
    ) -> FileRecord:
        """Place a file on legal/regulatory retention hold.

        Requires super_admin or data_manager role.
        Requirements: PRD-SYS-001, PRD-DOC-001, 21 CFR Part 11
        """
        normalized_roles = {r.strip().lower() for r in caller_roles if r}
        hold_roles = {
            "super_admin",
            "data_manager",
            "sponsor_admin",
            "admin",
            "sysadmin",
        }
        if not normalized_roles.intersection(hold_roles):
            raise FileSharePermissionDeniedError(
                "Only super_admin or data_manager can place a file on retention hold."
            )

        file_record = await self.file_repo.get_by_id(file_id)
        if not file_record or file_record.is_deleted:
            raise FileNotFoundError(f"File record '{file_id}' not found.")

        effective_hold_reason = hold_reason or reason_for_change

        updated = FileRecord(
            id=file_record.id,
            study_id=file_record.study_id,
            site_id=file_record.site_id,
            filename=file_record.filename,
            mime_type=file_record.mime_type,
            size_bytes=file_record.size_bytes,
            object_key=file_record.object_key,
            checksum_sha256=file_record.checksum_sha256,
            version_index=file_record.version_index,
            uploaded_by=file_record.uploaded_by,
            uploaded_at=file_record.uploaded_at,
            is_on_hold=True,
            hold_reason=effective_hold_reason,
            is_deleted=file_record.is_deleted,
            status=file_record.status,
            scan_status=file_record.scan_status,
            scan_engine=file_record.scan_engine,
            scan_completed_at=file_record.scan_completed_at,
            scan_threat_name=file_record.scan_threat_name,
            transcode_status=file_record.transcode_status,
            hls_manifest_key=file_record.hls_manifest_key,
            transcode_error=file_record.transcode_error,
            created_at=file_record.created_at,
            created_by=file_record.created_by,
            reason_for_change=reason_for_change,
        )
        saved = await self.file_repo.save(updated)

        await self.record_audit_log(
            file_id=file_id,
            action="HOLD_SET",
            user_id=caller_user_id,
            reason_for_change=reason_for_change,
            detail={"hold_reason": effective_hold_reason},
        )
        return saved

    async def clear_hold(
        self,
        file_id: str,
        caller_user_id: str,
        caller_roles: list[str],
        reason_for_change: str,
    ) -> FileRecord:
        """Remove legal/regulatory retention hold from a file.

        Requires super_admin or data_manager role.
        Requirements: PRD-SYS-001, PRD-DOC-001, 21 CFR Part 11
        """
        normalized_roles = {r.strip().lower() for r in caller_roles if r}
        hold_roles = {
            "super_admin",
            "data_manager",
            "sponsor_admin",
            "admin",
            "sysadmin",
        }
        if not normalized_roles.intersection(hold_roles):
            raise FileSharePermissionDeniedError(
                "Only super_admin or data_manager can remove retention hold."
            )

        file_record = await self.file_repo.get_by_id(file_id)
        if not file_record or file_record.is_deleted:
            raise FileNotFoundError(f"File record '{file_id}' not found.")

        updated = FileRecord(
            id=file_record.id,
            study_id=file_record.study_id,
            site_id=file_record.site_id,
            filename=file_record.filename,
            mime_type=file_record.mime_type,
            size_bytes=file_record.size_bytes,
            object_key=file_record.object_key,
            checksum_sha256=file_record.checksum_sha256,
            version_index=file_record.version_index,
            uploaded_by=file_record.uploaded_by,
            uploaded_at=file_record.uploaded_at,
            is_on_hold=False,
            hold_reason=None,
            is_deleted=file_record.is_deleted,
            status=file_record.status,
            scan_status=file_record.scan_status,
            scan_engine=file_record.scan_engine,
            scan_completed_at=file_record.scan_completed_at,
            scan_threat_name=file_record.scan_threat_name,
            transcode_status=file_record.transcode_status,
            hls_manifest_key=file_record.hls_manifest_key,
            transcode_error=file_record.transcode_error,
            created_at=file_record.created_at,
            created_by=file_record.created_by,
            reason_for_change=reason_for_change,
        )
        saved = await self.file_repo.save(updated)

        await self.record_audit_log(
            file_id=file_id,
            action="HOLD_CLEARED",
            user_id=caller_user_id,
            reason_for_change=reason_for_change,
        )
        return saved

    async def delete_file(
        self,
        file_id: str,
        caller_user_id: str,
        caller_roles: list[str],
        reason_for_change: str,
    ) -> FileRecord:
        """Perform soft delete on a file record.

        Gated on FileRecord.is_on_hold being False.
        Requirements: PRD-SYS-001, PRD-DOC-001, 21 CFR Part 11
        """
        file_record = await self.file_repo.get_by_id(file_id)
        if not file_record or file_record.is_deleted:
            raise FileNotFoundError(f"File record '{file_id}' not found.")

        if file_record.is_on_hold:
            raise FileOnHoldError("File is under retention hold and cannot be deleted.")

        normalized_roles = {r.strip().lower() for r in caller_roles if r}
        is_admin = bool(normalized_roles.intersection(ADMIN_ROLES))
        is_uploader = file_record.uploaded_by == caller_user_id

        if not is_admin and not is_uploader:
            user_grant = await self.grant_repo.find_user_grant(file_id, caller_user_id)
            if not user_grant or not user_grant.permission_level.satisfies(
                PermissionLevel.ADMIN
            ):
                raise FileSharePermissionDeniedError(
                    "Caller lacks permission to delete this file."
                )

        updated = FileRecord(
            id=file_record.id,
            study_id=file_record.study_id,
            site_id=file_record.site_id,
            filename=file_record.filename,
            mime_type=file_record.mime_type,
            size_bytes=file_record.size_bytes,
            object_key=file_record.object_key,
            checksum_sha256=file_record.checksum_sha256,
            version_index=file_record.version_index,
            uploaded_by=file_record.uploaded_by,
            uploaded_at=file_record.uploaded_at,
            is_on_hold=file_record.is_on_hold,
            hold_reason=file_record.hold_reason,
            is_deleted=True,
            status=file_record.status,
            scan_status=file_record.scan_status,
            scan_engine=file_record.scan_engine,
            scan_completed_at=file_record.scan_completed_at,
            scan_threat_name=file_record.scan_threat_name,
            transcode_status=file_record.transcode_status,
            hls_manifest_key=file_record.hls_manifest_key,
            transcode_error=file_record.transcode_error,
            created_at=file_record.created_at,
            created_by=file_record.created_by,
            reason_for_change=reason_for_change,
        )
        saved = await self.file_repo.save(updated)

        await self.record_audit_log(
            file_id=file_id,
            action="FILE_DELETED",
            user_id=caller_user_id,
            reason_for_change=reason_for_change,
        )
        return saved

    async def list_audit_logs(
        self,
        file_id: str,
        caller_user_id: str,
        caller_roles: list[str],
    ) -> list[FileAuditLog]:
        """List audit log entries for a file record."""
        if not self.file_audit_repo:
            return []

        logs = await self.file_audit_repo.list_by_file_id(file_id)
        if not logs:
            file_record = await self.file_repo.get_by_id(file_id, include_deleted=True)
            if not file_record:
                raise FileNotFoundError(f"File record '{file_id}' not found.")
        return logs
