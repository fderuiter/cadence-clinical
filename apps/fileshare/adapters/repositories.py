"""SQLAlchemy repository implementations for Fileshare domain entities.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002, PRD-DOC-003
"""

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.fileshare.domain.models import FileRecord, GuestLink, ShareGrant
from apps.fileshare.infrastructure.models import (
    FileRecordModel,
    GuestLinkModel,
    ShareGrantModel,
)
from apps.fileshare.ports.repository_port import (
    FileRecordRepositoryPort,
    GuestLinkRepositoryPort,
    ShareGrantRepositoryPort,
)


class SqlAlchemyFileRecordRepository(FileRecordRepositoryPort):
    """SQLAlchemy implementation of FileRecordRepositoryPort."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    async def get_by_id(self, entity_id: str) -> FileRecord | None:
        stmt = select(FileRecordModel).where(
            FileRecordModel.id == entity_id,
            FileRecordModel.is_deleted.is_(False),
        )
        res = await self.session.execute(stmt)
        model = res.scalar_one_or_none()
        return model.to_domain() if model else None

    async def get_by_object_key(self, object_key: str) -> FileRecord | None:
        stmt = select(FileRecordModel).where(
            FileRecordModel.object_key == object_key,
            FileRecordModel.is_deleted.is_(False),
        )
        res = await self.session.execute(stmt)
        model = res.scalar_one_or_none()
        return model.to_domain() if model else None

    async def list_by_study(
        self,
        study_id: str,
        site_id: str | None = None,
        include_deleted: bool = False,
    ) -> list[FileRecord]:
        stmt = select(FileRecordModel).where(FileRecordModel.study_id == study_id)
        if not include_deleted:
            stmt = stmt.where(FileRecordModel.is_deleted.is_(False))
        if site_id is not None:
            stmt = stmt.where(FileRecordModel.site_id == site_id)

        res = await self.session.execute(stmt)
        return [m.to_domain() for m in res.scalars().all()]

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
                is_deleted=entity.is_deleted,
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
            existing.is_deleted = entity.is_deleted
            existing.reason_for_change = entity.reason_for_change

        await self.session.flush()
        return entity

    async def delete(self, entity_id: str) -> bool:
        stmt = select(FileRecordModel).where(FileRecordModel.id == entity_id)
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
        stmt = select(GuestLinkModel).where(
            GuestLinkModel.token_hmac == token_hmac
        )
        res = await self.session.execute(stmt)
        model = res.scalar_one_or_none()
        return model.to_domain() if model else None

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

