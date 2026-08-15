from collections.abc import Sequence

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.eisf.domain.ports import EISFRepositoryPort
from apps.eisf.infrastructure.database import db_manager, get_session
from apps.eisf.infrastructure.models import ISFAuditLog, ISFDocument
from packages.database import map_database_exceptions


class SQLEISFRepository(EISFRepositoryPort):
    """SQLAlchemy implementation of EISFRepositoryPort."""

    def __init__(self, session: AsyncSession | None = None) -> None:
        self._session = session

    @property
    def session(self) -> AsyncSession:
        if self._session is not None:
            return self._session
        return get_session()

    @map_database_exceptions
    async def get_by_id(self, entity_id: str) -> ISFDocument | None:
        return await self.get_document_by_id(entity_id)

    @map_database_exceptions
    async def save(self, entity: ISFDocument) -> ISFDocument:
        return await self.save_document(entity)

    @map_database_exceptions
    async def get_documents_by_site(self, site_id: str) -> Sequence[ISFDocument]:
        stmt = select(ISFDocument).where(ISFDocument.site_id == site_id)
        result = await self.session.execute(stmt)
        return result.scalars().all()

    @map_database_exceptions
    async def get_all_documents(self) -> Sequence[ISFDocument]:
        stmt = select(ISFDocument)
        result = await self.session.execute(stmt)
        return result.scalars().all()

    @map_database_exceptions
    async def get_document_by_id(self, doc_id: str) -> ISFDocument | None:
        stmt = select(ISFDocument).where(ISFDocument.id == doc_id)
        result = await self.session.execute(stmt)
        return result.scalars().first()

    @map_database_exceptions
    async def save_document(self, doc: ISFDocument) -> ISFDocument:
        self.session.add(doc)
        await self.session.flush()
        return doc

    @map_database_exceptions
    async def delete_document(self, doc: ISFDocument) -> None:
        await self.session.delete(doc)
        await self.session.flush()

    @map_database_exceptions
    async def save_audit_log(self, log: ISFAuditLog) -> ISFAuditLog:
        self.session.add(log)
        await self.session.flush()
        return log

    @map_database_exceptions
    async def save_security_alert_out_of_band(self, alert: ISFAuditLog) -> None:
        async with db_manager.get_session_maker()() as audit_session:
            audit_session.add(alert)
            await audit_session.commit()

    @map_database_exceptions
    async def get_documents_by_study(self, study_id: str) -> Sequence[ISFDocument]:
        stmt = select(ISFDocument).where(ISFDocument.study_id == study_id)
        result = await self.session.execute(stmt)
        return result.scalars().all()

    @map_database_exceptions
    async def get_latest_document(
        self, study_id: str, site_id: str, section_code: str
    ) -> ISFDocument | None:
        stmt = (
            select(ISFDocument)
            .where(
                ISFDocument.study_id == study_id,
                ISFDocument.site_id == site_id,
                ISFDocument.binder_classification == section_code,
            )
            .order_by(ISFDocument.version_index.desc())
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    @map_database_exceptions
    async def get_documents_by_correlation_or_logical_fields(
        self,
        correlation_key: str | None,
        study_id: str,
        site_id: str,
        binder_classification: str,
    ) -> Sequence[ISFDocument]:
        stmt = (
            select(ISFDocument)
            .where(
                (ISFDocument.correlation_key == correlation_key)
                | (
                    ISFDocument.correlation_key.is_(None)
                    & (ISFDocument.study_id == study_id)
                    & (ISFDocument.site_id == site_id)
                    & (ISFDocument.binder_classification == binder_classification)
                )
            )
            .order_by(ISFDocument.version_index.desc())
        )
        result = await self.session.execute(stmt)
        return result.scalars().all()

    @map_database_exceptions
    async def list_documents_filtered(
        self,
        site_ids: str | list[str] | None,
        study_id: str | None,
        binder_section: str | None,
        binder_classification: str | None,
    ) -> Sequence[ISFDocument]:
        stmt = select(ISFDocument)
        if isinstance(site_ids, list):
            stmt = stmt.where(ISFDocument.site_id.in_(site_ids))
        elif site_ids:
            stmt = stmt.where(ISFDocument.site_id == site_ids)
        if study_id:
            stmt = stmt.where(ISFDocument.study_id == study_id)
        if binder_section:
            stmt = stmt.where(ISFDocument.binder_classification == binder_section)
        if binder_classification:
            stmt = stmt.where(
                ISFDocument.binder_classification == binder_classification
            )
        result = await self.session.execute(stmt)
        return result.scalars().all()
