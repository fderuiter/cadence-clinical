from typing import Sequence
from sqlalchemy import select
from ..ports.repository import EISFRepositoryPort
from ..models import ISFDocument, ISFAuditLog
from ..database import get_session, db_manager


class SQLEISFRepository(EISFRepositoryPort):
    async def get_documents_by_site(self, site_id: str) -> Sequence[ISFDocument]:
        session = get_session()
        stmt = select(ISFDocument).where(ISFDocument.site_id == site_id)
        result = await session.execute(stmt)
        return result.scalars().all()

    async def get_all_documents(self) -> Sequence[ISFDocument]:
        session = get_session()
        stmt = select(ISFDocument)
        result = await session.execute(stmt)
        return result.scalars().all()

    async def get_document_by_id(self, doc_id: str) -> ISFDocument | None:
        session = get_session()
        stmt = select(ISFDocument).where(ISFDocument.id == doc_id)
        result = await session.execute(stmt)
        return result.scalars().first()

    async def save_document(self, doc: ISFDocument) -> ISFDocument:
        session = get_session()
        session.add(doc)
        await session.flush()
        return doc

    async def delete_document(self, doc: ISFDocument) -> None:
        session = get_session()
        await session.delete(doc)
        await session.flush()

    async def save_audit_log(self, log: ISFAuditLog) -> ISFAuditLog:
        session = get_session()
        session.add(log)
        await session.flush()
        return log

    async def save_security_alert_out_of_band(self, alert: ISFAuditLog) -> None:
        async with db_manager.get_session_maker()() as audit_session:
            audit_session.add(alert)
            await audit_session.commit()

    async def get_documents_by_study(self, study_id: str) -> Sequence[ISFDocument]:
        session = get_session()
        stmt = select(ISFDocument).where(ISFDocument.study_id == study_id)
        result = await session.execute(stmt)
        return result.scalars().all()

    async def get_latest_document(
        self, study_id: str, site_id: str, section_code: str
    ) -> ISFDocument | None:
        session = get_session()
        stmt = (
            select(ISFDocument)
            .where(
                ISFDocument.study_id == study_id,
                ISFDocument.site_id == site_id,
                ISFDocument.binder_classification == section_code,
            )
            .order_by(ISFDocument.version_index.desc())
        )
        result = await session.execute(stmt)
        return result.scalars().first()

    async def get_documents_by_correlation_or_logical_fields(
        self,
        correlation_key: str | None,
        study_id: str,
        site_id: str,
        binder_classification: str,
    ) -> Sequence[ISFDocument]:
        session = get_session()
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
        result = await session.execute(stmt)
        return result.scalars().all()

    async def list_documents_filtered(
        self,
        site_ids: str | list[str] | None,
        study_id: str | None,
        binder_section: str | None,
        binder_classification: str | None,
    ) -> Sequence[ISFDocument]:
        session = get_session()
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
            stmt = stmt.where(ISFDocument.binder_classification == binder_classification)
        result = await session.execute(stmt)
        return result.scalars().all()

