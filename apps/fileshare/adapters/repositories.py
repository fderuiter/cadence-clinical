"""SQLAlchemy repository implementations for Fileshare domain entities.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002, PRD-DOC-003
"""

from datetime import datetime

from sqlalchemy import func, select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.fileshare.adapters.models import (
    FileAuditLogModel,
    FileRecordModel,
    GuestLinkModel,
    PlaybackAuditLogModel,
    ShareGrantModel,
)
from apps.fileshare.domain.models import (
    FileAuditLog,
    FileRecord,
    GuestLink,
    PlaybackAuditEvent,
    ShareGrant,
)
from apps.fileshare.ports.repository_port import (
    FileAuditLogRepositoryPort,
    FileRecordRepositoryPort,
    GuestLinkRepositoryPort,
    PlaybackAuditRepositoryPort,
    ShareGrantRepositoryPort,
)
from packages.database import map_database_exceptions


class SqlAlchemyFileRecordRepository(FileRecordRepositoryPort):
    """SQLAlchemy implementation of FileRecordRepositoryPort."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    @map_database_exceptions
    async def get_by_id(
        self, entity_id: str, include_deleted: bool = False
    ) -> FileRecord | None:
        stmt = select(FileRecordModel).where(FileRecordModel.id == entity_id)
        if not include_deleted:
            stmt = stmt.where(FileRecordModel.is_deleted.is_(False))
        res = await self.session.execute(stmt)
        model = res.scalar_one_or_none()
        return model.to_domain() if model else None

    @map_database_exceptions
    async def get_by_object_key(self, object_key: str) -> FileRecord | None:
        stmt = select(FileRecordModel).where(
            FileRecordModel.object_key == object_key,
            FileRecordModel.is_deleted.is_(False),
        )
        res = await self.session.execute(stmt)
        model = res.scalar_one_or_none()
        return model.to_domain() if model else None

    @map_database_exceptions
    async def list_by_study(
        self,
        study_id: str | None = None,
        site_id: str | None = None,
        include_deleted: bool = False,
    ) -> list[FileRecord]:
        stmt = select(FileRecordModel)
        if study_id is not None:
            stmt = stmt.where(FileRecordModel.study_id == study_id)
        if not include_deleted:
            stmt = stmt.where(FileRecordModel.is_deleted.is_(False))
        if site_id is not None:
            stmt = stmt.where(FileRecordModel.site_id == site_id)

        res = await self.session.execute(stmt)
        return [m.to_domain() for m in res.scalars().all()]

    async def list_pending_transcodes(self) -> list[FileRecord]:
        stmt = select(FileRecordModel).where(
            FileRecordModel.transcode_status == "PENDING",
            FileRecordModel.is_deleted.is_(False),
        )
        res = await self.session.execute(stmt)
        return [m.to_domain() for m in res.scalars().all()]

    async def list_pending_conversions(self) -> list[FileRecord]:
        stmt = select(FileRecordModel).where(
            FileRecordModel.conversion_status == "PENDING",
            FileRecordModel.is_deleted.is_(False),
        )
        res = await self.session.execute(stmt)
        return [m.to_domain() for m in res.scalars().all()]

    @map_database_exceptions
    async def save(self, entity: FileRecord) -> FileRecord:
        stmt = select(FileRecordModel).where(FileRecordModel.id == entity.id)
        res = await self.session.execute(stmt)
        existing = res.scalar_one_or_none()

        if existing is None:
            model = FileRecordModel(
                id=entity.id,
                study_id=entity.study_id,
                site_id=entity.site_id,
                filename=entity.filename,
                mime_type=entity.mime_type,
                size_bytes=entity.size_bytes,
                object_key=entity.object_key,
                checksum_sha256=entity.checksum_sha256,
                version_index=entity.version_index,
                uploaded_by=entity.uploaded_by,
                uploaded_at=entity.uploaded_at,
                is_on_hold=entity.is_on_hold,
                hold_reason=entity.hold_reason,
                is_deleted=entity.is_deleted,
                status=entity.status.value,
                scan_status=entity.scan_status.value,
                scan_engine=entity.scan_engine,
                scan_completed_at=entity.scan_completed_at,
                scan_threat_name=entity.scan_threat_name,
                transcode_status=entity.transcode_status.value,
                hls_manifest_key=entity.hls_manifest_key,
                transcode_error=entity.transcode_error,
                pdf_rendition_key=entity.pdf_rendition_key,
                conversion_status=entity.conversion_status.value
                if hasattr(entity.conversion_status, "value")
                else str(entity.conversion_status),
                conversion_error=entity.conversion_error,
                approval_status=entity.approval_status.value
                if hasattr(entity.approval_status, "value")
                else str(entity.approval_status),
                signature_manifestation=entity.signature_manifestation,
                approved_by=entity.approved_by,
                approved_at=entity.approved_at,
                approval_reason=entity.approval_reason,
                created_at=entity.created_at,
                created_by=entity.created_by,
                reason_for_change=entity.reason_for_change,
            )
            self.session.add(model)
        else:
            existing.filename = entity.filename
            existing.mime_type = entity.mime_type
            existing.size_bytes = entity.size_bytes
            existing.object_key = entity.object_key
            existing.checksum_sha256 = entity.checksum_sha256
            existing.version_index = entity.version_index
            existing.is_on_hold = entity.is_on_hold
            existing.hold_reason = entity.hold_reason
            existing.is_deleted = entity.is_deleted
            existing.status = entity.status.value
            existing.scan_status = entity.scan_status.value
            existing.scan_engine = entity.scan_engine
            existing.scan_completed_at = entity.scan_completed_at
            existing.scan_threat_name = entity.scan_threat_name
            existing.transcode_status = entity.transcode_status.value
            existing.hls_manifest_key = entity.hls_manifest_key
            existing.transcode_error = entity.transcode_error
            existing.pdf_rendition_key = entity.pdf_rendition_key
            existing.conversion_status = (
                entity.conversion_status.value
                if hasattr(entity.conversion_status, "value")
                else str(entity.conversion_status)
            )
            existing.conversion_error = entity.conversion_error
            existing.approval_status = (
                entity.approval_status.value
                if hasattr(entity.approval_status, "value")
                else str(entity.approval_status)
            )
            existing.signature_manifestation = entity.signature_manifestation
            existing.approved_by = entity.approved_by
            existing.approved_at = entity.approved_at
            existing.approval_reason = entity.approval_reason
            existing.reason_for_change = entity.reason_for_change

        await self.session.flush()
        return entity

    async def delete(self, entity_id: str) -> bool:
        stmt = select(FileRecordModel).where(
            FileRecordModel.id == entity_id,
            FileRecordModel.is_on_hold.is_(False),
        )
        res = await self.session.execute(stmt)
        existing = res.scalar_one_or_none()
        if existing:
            existing.is_deleted = True
            await self.session.flush()
            return True
        return False


class SqlAlchemyShareGrantRepository(ShareGrantRepositoryPort):
    """SQLAlchemy implementation of ShareGrantRepositoryPort."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    async def get_by_id(self, entity_id: str) -> ShareGrant | None:
        stmt = select(ShareGrantModel).where(
            ShareGrantModel.id == entity_id,
            ShareGrantModel.is_deleted.is_(False),
        )
        res = await self.session.execute(stmt)
        model = res.scalar_one_or_none()
        return model.to_domain() if model else None

    async def list_by_file_id(
        self, file_record_id: str, active_only: bool = True
    ) -> list[ShareGrant]:
        stmt = select(ShareGrantModel).where(
            ShareGrantModel.file_record_id == file_record_id
        )
        if active_only:
            stmt = stmt.where(
                ShareGrantModel.is_deleted.is_(False),
                ShareGrantModel.revoked_at.is_(None),
            )
        res = await self.session.execute(stmt)
        return [m.to_domain() for m in res.scalars().all()]

    async def find_user_grant(
        self, file_record_id: str, user_id: str
    ) -> ShareGrant | None:
        stmt = select(ShareGrantModel).where(
            ShareGrantModel.file_record_id == file_record_id,
            ShareGrantModel.granted_to_user_id == user_id,
            ShareGrantModel.is_deleted.is_(False),
            ShareGrantModel.revoked_at.is_(None),
        )
        res = await self.session.execute(stmt)
        model = res.scalar_one_or_none()
        return model.to_domain() if model else None

    async def save(self, entity: ShareGrant) -> ShareGrant:
        stmt = select(ShareGrantModel).where(ShareGrantModel.id == entity.id)
        res = await self.session.execute(stmt)
        existing = res.scalar_one_or_none()

        if existing is None:
            model = ShareGrantModel(
                id=entity.id,
                file_record_id=entity.file_record_id,
                granted_to_user_id=entity.granted_to_user_id,
                granted_by_user_id=entity.granted_by_user_id,
                scope=entity.scope.value,
                permission_level=entity.permission_level.value,
                expires_at=entity.expires_at,
                revoked_at=entity.revoked_at,
                is_deleted=entity.is_deleted,
                created_at=entity.created_at,
                created_by=entity.created_by,
                reason_for_change=entity.reason_for_change,
            )
            self.session.add(model)
        else:
            existing.permission_level = entity.permission_level.value
            existing.scope = entity.scope.value
            existing.expires_at = entity.expires_at
            existing.revoked_at = entity.revoked_at
            existing.is_deleted = entity.is_deleted
            existing.reason_for_change = entity.reason_for_change

        await self.session.flush()
        return entity

    async def delete(self, entity_id: str) -> bool:
        stmt = select(ShareGrantModel).where(ShareGrantModel.id == entity_id)
        res = await self.session.execute(stmt)
        existing = res.scalar_one_or_none()
        if existing:
            existing.is_deleted = True
            await self.session.flush()
            return True
        return False


class SqlAlchemyGuestLinkRepository(GuestLinkRepositoryPort):
    """SQLAlchemy implementation of GuestLinkRepositoryPort."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    async def get_by_id(self, entity_id: str) -> GuestLink | None:
        stmt = select(GuestLinkModel).where(GuestLinkModel.id == entity_id)
        res = await self.session.execute(stmt)
        model = res.scalar_one_or_none()
        return model.to_domain() if model else None

    async def get_by_token_hmac(self, token_hmac: str) -> GuestLink | None:
        stmt = select(GuestLinkModel).where(GuestLinkModel.token_hmac == token_hmac)
        res = await self.session.execute(stmt)
        model = res.scalar_one_or_none()
        return model.to_domain() if model else None

    async def list_by_file_id(
        self, file_record_id: str, active_only: bool = True
    ) -> list[GuestLink]:
        stmt = select(GuestLinkModel).where(
            GuestLinkModel.file_record_id == file_record_id
        )
        if active_only:
            stmt = stmt.where(GuestLinkModel.revoked_at.is_(None))
        res = await self.session.execute(stmt)
        return [m.to_domain() for m in res.scalars().all()]

    async def save(self, entity: GuestLink) -> GuestLink:
        stmt = select(GuestLinkModel).where(GuestLinkModel.id == entity.id)
        res = await self.session.execute(stmt)
        existing = res.scalar_one_or_none()

        if existing is None:
            model = GuestLinkModel(
                id=entity.id,
                file_record_id=entity.file_record_id,
                token_hmac=entity.token_hmac,
                expires_at=entity.expires_at,
                created_by=entity.created_by,
                last_accessed_at=entity.last_accessed_at,
                access_count=entity.access_count,
                revoked_at=entity.revoked_at,
                created_at=entity.created_at,
                reason_for_change=entity.reason_for_change,
            )
            self.session.add(model)
        else:
            existing.last_accessed_at = entity.last_accessed_at
            existing.access_count = entity.access_count
            existing.revoked_at = entity.revoked_at
            existing.reason_for_change = entity.reason_for_change

        await self.session.flush()
        return entity

    async def delete(self, entity_id: str) -> bool:
        stmt = select(GuestLinkModel).where(GuestLinkModel.id == entity_id)
        res = await self.session.execute(stmt)
        existing = res.scalar_one_or_none()
        if existing:
            await self.session.delete(existing)
            await self.session.flush()
            return True
        return False


class SqlAlchemyPlaybackAuditRepository(PlaybackAuditRepositoryPort):
    """SQLAlchemy implementation of PlaybackAuditRepositoryPort."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    async def get_by_id(self, entity_id: str) -> PlaybackAuditEvent | None:
        stmt = select(PlaybackAuditLogModel).where(
            PlaybackAuditLogModel.id == entity_id
        )
        res = await self.session.execute(stmt)
        model = res.scalar_one_or_none()
        return model.to_domain() if model else None

    async def list_by_file_id(self, file_record_id: str) -> list[PlaybackAuditEvent]:
        stmt = (
            select(PlaybackAuditLogModel)
            .where(PlaybackAuditLogModel.file_record_id == file_record_id)
            .order_by(PlaybackAuditLogModel.timestamp.desc())
        )
        res = await self.session.execute(stmt)
        return [m.to_domain() for m in res.scalars().all()]

    async def list_by_session_id(self, session_id: str) -> list[PlaybackAuditEvent]:
        stmt = (
            select(PlaybackAuditLogModel)
            .where(PlaybackAuditLogModel.session_id == session_id)
            .order_by(PlaybackAuditLogModel.timestamp.asc())
        )
        res = await self.session.execute(stmt)
        return [m.to_domain() for m in res.scalars().all()]

    async def save(self, entity: PlaybackAuditEvent) -> PlaybackAuditEvent:
        model = PlaybackAuditLogModel(
            id=entity.id,
            file_record_id=entity.file_record_id,
            event=entity.event.value,
            source=entity.source.value,
            session_id=entity.session_id,
            user_id=entity.user_id,
            timestamp=entity.timestamp,
            created_at=entity.created_at,
            reason_for_change=entity.reason_for_change,
        )
        self.session.add(model)
        await self.session.flush()
        return entity

    async def delete(self, entity_id: str) -> bool:
        stmt = select(PlaybackAuditLogModel).where(
            PlaybackAuditLogModel.id == entity_id
        )
        res = await self.session.execute(stmt)
        existing = res.scalar_one_or_none()
        if existing:
            await self.session.delete(existing)
            await self.session.flush()
            return True
        return False


class SqlAlchemyFileAuditLogRepository(FileAuditLogRepositoryPort):
    """SQLAlchemy implementation of FileAuditLogRepositoryPort."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    async def get_by_id(self, entity_id: str) -> FileAuditLog | None:
        stmt = select(FileAuditLogModel).where(FileAuditLogModel.id == entity_id)
        res = await self.session.execute(stmt)
        model = res.scalar_one_or_none()
        return model.to_domain() if model else None

    async def list_by_file_id(self, file_record_id: str) -> list[FileAuditLog]:
        stmt = (
            select(FileAuditLogModel)
            .where(FileAuditLogModel.file_record_id == file_record_id)
            .order_by(FileAuditLogModel.timestamp.desc())
        )
        res = await self.session.execute(stmt)
        return [m.to_domain() for m in res.scalars().all()]

    async def get_activity_feed(
        self,
        file_record_id: str,
        event_type: str | None = None,
        from_date: datetime | None = None,
        to_date: datetime | None = None,
        page: int = 1,
        page_size: int = 50,
    ) -> tuple[list[FileAuditLog], int]:
        base_stmt = select(FileAuditLogModel).where(
            FileAuditLogModel.file_record_id == file_record_id
        )
        count_stmt = select(func.count(FileAuditLogModel.id)).where(
            FileAuditLogModel.file_record_id == file_record_id
        )

        if event_type and event_type.strip():
            norm_event = event_type.strip().upper()
            base_stmt = base_stmt.where(
                func.upper(FileAuditLogModel.action) == norm_event
            )
            count_stmt = count_stmt.where(
                func.upper(FileAuditLogModel.action) == norm_event
            )

        if from_date is not None:
            base_stmt = base_stmt.where(FileAuditLogModel.timestamp >= from_date)
            count_stmt = count_stmt.where(FileAuditLogModel.timestamp >= from_date)

        if to_date is not None:
            base_stmt = base_stmt.where(FileAuditLogModel.timestamp <= to_date)
            count_stmt = count_stmt.where(FileAuditLogModel.timestamp <= to_date)

        total_res = await self.session.execute(count_stmt)
        total = total_res.scalar() or 0

        offset = max(0, (page - 1) * page_size)
        paginated_stmt = (
            base_stmt.order_by(FileAuditLogModel.timestamp.desc())
            .offset(offset)
            .limit(page_size)
        )
        res = await self.session.execute(paginated_stmt)
        items = [m.to_domain() for m in res.scalars().all()]
        return items, total

    async def save(self, entity: FileAuditLog) -> FileAuditLog:
        stmt = select(FileAuditLogModel).where(FileAuditLogModel.id == entity.id)
        res = await self.session.execute(stmt)
        existing = res.scalar_one_or_none()

        if existing is None:
            model = FileAuditLogModel(
                id=entity.id,
                file_record_id=entity.file_record_id,
                action=entity.action,
                user_id=entity.user_id,
                timestamp=entity.timestamp,
                reason_for_change=entity.reason_for_change,
                detail=entity.detail,
            )
            self.session.add(model)
        else:
            existing.action = entity.action
            existing.user_id = entity.user_id
            existing.timestamp = entity.timestamp
            existing.reason_for_change = entity.reason_for_change
            existing.detail = entity.detail

        await self.session.flush()
        return entity

    async def delete(self, entity_id: str) -> bool:
        stmt = select(FileAuditLogModel).where(FileAuditLogModel.id == entity_id)
        res = await self.session.execute(stmt)
        existing = res.scalar_one_or_none()
        if existing:
            await self.session.delete(existing)
            await self.session.flush()
            return True
        return False
