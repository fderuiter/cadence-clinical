from collections.abc import Sequence
from typing import Any

from sqlalchemy import func, select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.etmf.domain.ports import ETMFRepositoryPort
from apps.etmf.infrastructure.database import get_session
from apps.etmf.infrastructure.models import (
    DocumentQCTransition,
    ExpectedDocument,
    TMFAuditLog,
    TMFDocument,
)
from packages.database import map_database_exceptions


class SQLETMFRepository(ETMFRepositoryPort):
    """SQLAlchemy implementation of ETMFRepositoryPort."""

    def __init__(self, session: AsyncSession | None = None) -> None:
        self._session = session

    def _deduplicate_rules(
        self, docs: Sequence[ExpectedDocument]
    ) -> list[ExpectedDocument]:
        seen = set()
        deduped = []
        for doc in docs:
            site_key = doc.site_id if doc.site_id else None
            key = (doc.artifact_type, doc.milestone, site_key)
            if key not in seen:
                seen.add(key)
                deduped.append(doc)
        return deduped

    @property
    def session(self) -> AsyncSession:
        if self._session is not None:
            return self._session
        return get_session()

    @map_database_exceptions
    async def get_by_id(self, entity_id: str) -> TMFDocument | None:
        return await self.get_document_by_id(entity_id)

    @map_database_exceptions
    async def save(self, entity: TMFDocument) -> TMFDocument:
        return await self.save_document(entity)

    @map_database_exceptions
    async def get_document_by_id(self, doc_id: str) -> TMFDocument | None:
        stmt = select(TMFDocument).where(TMFDocument.id == doc_id)
        result = await self.session.execute(stmt)
        return result.scalars().first()

    @map_database_exceptions
    async def get_documents_by_study(self, study_id: str) -> Sequence[TMFDocument]:
        stmt = select(TMFDocument).where(TMFDocument.study_id == study_id)
        result = await self.session.execute(stmt)
        return result.scalars().all()

    @map_database_exceptions
    async def save_document(self, doc: TMFDocument) -> TMFDocument:
        self.session.add(doc)
        await self.session.flush()
        return doc

    @map_database_exceptions
    async def delete_document(self, doc: TMFDocument) -> None:
        await self.session.delete(doc)
        await self.session.flush()

    @map_database_exceptions
    async def get_expected_document_by_id(self, edl_id: str) -> ExpectedDocument | None:
        stmt = select(ExpectedDocument).where(ExpectedDocument.id == edl_id)
        result = await self.session.execute(stmt)
        return result.scalars().first()

    @map_database_exceptions
    async def get_expected_documents_by_study(
        self, study_id: str
    ) -> Sequence[ExpectedDocument]:
        stmt = (
            select(ExpectedDocument)
            .where(ExpectedDocument.study_id == study_id)
            .order_by(
                ExpectedDocument.artifact_type.asc(),
                ExpectedDocument.milestone.asc(),
                ExpectedDocument.site_id.asc(),
                ExpectedDocument.version_index.desc(),
                ExpectedDocument.created_at.desc(),
            )
        )
        result = await self.session.execute(stmt)
        raw_docs = result.scalars().all()
        return self._deduplicate_rules(raw_docs)

    @map_database_exceptions
    async def get_expected_documents_by_study_and_site(
        self, study_id: str, site_id: str | None
    ) -> Sequence[ExpectedDocument]:
        stmt = select(ExpectedDocument).where(ExpectedDocument.study_id == study_id)
        if site_id:
            stmt = stmt.where(
                (ExpectedDocument.site_id == site_id)
                | ExpectedDocument.site_id.is_(None)
            )
        else:
            stmt = stmt.where(ExpectedDocument.site_id.is_(None))
        stmt = stmt.order_by(
            ExpectedDocument.artifact_type.asc(),
            ExpectedDocument.milestone.asc(),
            ExpectedDocument.site_id.asc(),
            ExpectedDocument.version_index.desc(),
            ExpectedDocument.created_at.desc(),
        )
        result = await self.session.execute(stmt)
        raw_docs = result.scalars().all()
        return self._deduplicate_rules(raw_docs)

    @map_database_exceptions
    async def save_expected_document(self, edl: ExpectedDocument) -> ExpectedDocument:
        self.session.add(edl)
        await self.session.flush()
        return edl

    @map_database_exceptions
    async def get_audit_logs(self, skip: int, limit: int) -> Sequence[TMFAuditLog]:
        stmt = (
            select(TMFAuditLog)
            .order_by(TMFAuditLog.timestamp.desc())
            .offset(skip)
            .limit(limit)
        )
        result = await self.session.execute(stmt)
        return result.scalars().all()

    @map_database_exceptions
    async def get_audit_logs_count(self) -> int:
        stmt = select(func.count()).select_from(TMFAuditLog)
        result = await self.session.execute(stmt)
        return result.scalar() or 0

    @map_database_exceptions
    async def save_audit_log(self, log: TMFAuditLog) -> TMFAuditLog:
        self.session.add(log)
        await self.session.flush()
        return log

    @map_database_exceptions
    async def get_qc_transitions_by_document_id(
        self, doc_id: str
    ) -> Sequence[DocumentQCTransition]:
        stmt = (
            select(DocumentQCTransition)
            .where(DocumentQCTransition.document_id == doc_id)
            .order_by(DocumentQCTransition.timestamp.desc())
        )
        result = await self.session.execute(stmt)
        return result.scalars().all()

    @map_database_exceptions
    async def get_qc_transitions_by_document_id_asc(
        self, doc_id: str
    ) -> Sequence[DocumentQCTransition]:
        stmt = (
            select(DocumentQCTransition)
            .where(DocumentQCTransition.document_id == doc_id)
            .order_by(DocumentQCTransition.timestamp.asc())
        )
        result = await self.session.execute(stmt)
        return result.scalars().all()

    @map_database_exceptions
    async def save_qc_transition(
        self, transition: DocumentQCTransition
    ) -> DocumentQCTransition:
        self.session.add(transition)
        await self.session.flush()
        return transition

    @map_database_exceptions
    async def get_documents_filtered(
        self,
        study_id: str | None,
        zone: int | None,
        search: str | None,
        status: str | None,
        principal: Any,
    ) -> Sequence[TMFDocument]:
        from apps.etmf.lifecycle import apply_document_query_filter

        stmt = select(TMFDocument)
        if study_id:
            stmt = stmt.where(TMFDocument.study_id == study_id)
        if zone:
            stmt = stmt.where(TMFDocument.zone == zone)
        if search:
            stmt = stmt.where(TMFDocument._content.contains(search))
        if status:
            stmt = stmt.where(TMFDocument.status == status)
        stmt = apply_document_query_filter(stmt, principal)
        result = await self.session.execute(stmt)
        return result.scalars().all()

    @map_database_exceptions
    async def get_max_version_index(
        self, study_id: str, site_id: str | None, artifact_code: str
    ) -> int:
        stmt = select(func.max(TMFDocument.version_index)).where(
            TMFDocument.study_id == study_id,
            TMFDocument.site_id == site_id,
            TMFDocument.artifact_code == artifact_code,
        )
        result = await self.session.execute(stmt)
        return result.scalar() or 0

    @map_database_exceptions
    async def get_redacted_successor(self, doc_id: str) -> TMFDocument | None:
        stmt = select(TMFDocument).where(TMFDocument.redaction_source_id == doc_id)
        result = await self.session.execute(stmt)
        return result.scalars().first()

    @map_database_exceptions
    async def get_document_lineage(
        self, study_id: str, artifact_code: str
    ) -> Sequence[TMFDocument]:
        stmt = (
            select(TMFDocument)
            .where(
                TMFDocument.study_id == study_id,
                TMFDocument.artifact_code == artifact_code,
            )
            .order_by(TMFDocument.version_index.asc())
        )
        result = await self.session.execute(stmt)
        return result.scalars().all()

    @map_database_exceptions
    async def get_expected_document_by_study_milestone_and_artifact(
        self, study_id: str, milestone: str, artifact_type: str
    ) -> ExpectedDocument | None:
        stmt = select(ExpectedDocument).where(
            ExpectedDocument.study_id == study_id,
            ExpectedDocument.milestone == milestone,
            ExpectedDocument.artifact_type == artifact_type,
            ExpectedDocument.site_id.is_(None),
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    @map_database_exceptions
    async def get_documents_by_study_and_status(
        self, study_id: str, status: str
    ) -> Sequence[TMFDocument]:
        stmt = select(TMFDocument).where(
            TMFDocument.study_id == study_id,
            TMFDocument.status == status,
        )
        result = await self.session.execute(stmt)
        return result.scalars().all()

    @map_database_exceptions
    async def get_unsealed_audit_logs(self) -> Sequence[TMFAuditLog]:
        stmt = (
            select(TMFAuditLog)
            .where(TMFAuditLog.cryptographic_seal.is_(None))
            .order_by(TMFAuditLog.timestamp.asc())
        )
        result = await self.session.execute(stmt)
        return result.scalars().all()

    @map_database_exceptions
    async def get_audit_log_by_id(self, log_id: str) -> TMFAuditLog | None:
        stmt = select(TMFAuditLog).where(TMFAuditLog.id == log_id)
        result = await self.session.execute(stmt)
        return result.scalars().first()

    @map_database_exceptions
    async def get_audit_logs_paginated(
        self,
        user_id: str | None,
        action: str | None,
        document_id: str | None,
        start_time: Any,
        end_time: Any,
        offset: int,
        limit: int,
    ) -> tuple[int, Sequence[TMFAuditLog]]:
        filters = []
        if user_id:
            filters.append(TMFAuditLog.user_id == user_id)
        if action:
            filters.append(TMFAuditLog.action == action)
        if document_id:
            filters.append(TMFAuditLog.document_id == document_id)
        if start_time:
            filters.append(TMFAuditLog.timestamp >= start_time)
        if end_time:
            filters.append(TMFAuditLog.timestamp <= end_time)

        count_stmt = select(func.count()).select_from(TMFAuditLog)
        if filters:
            count_stmt = count_stmt.where(*filters)
        total_res = await self.session.execute(count_stmt)
        total_count = total_res.scalar_one()

        stmt = select(TMFAuditLog)
        if filters:
            stmt = stmt.where(*filters)
        stmt = stmt.order_by(TMFAuditLog.timestamp.desc(), TMFAuditLog.id.desc())
        stmt = stmt.offset(offset).limit(limit)
        result = await self.session.execute(stmt)
        logs = result.scalars().all()
        return total_count, logs

    @map_database_exceptions
    async def get_expected_documents_filtered(
        self,
        study_id: str,
        site_id: str | None,
        milestone: str | None,
    ) -> Sequence[ExpectedDocument]:
        from apps.etmf.main import normalize_milestone

        stmt = select(ExpectedDocument).where(ExpectedDocument.study_id == study_id)
        if site_id:
            stmt = stmt.where(ExpectedDocument.site_id == site_id)
        if milestone:
            stmt = stmt.where(
                ExpectedDocument.milestone == normalize_milestone(milestone)
            )
        stmt = stmt.order_by(
            ExpectedDocument.artifact_type.asc(),
            ExpectedDocument.milestone.asc(),
            ExpectedDocument.site_id.asc(),
            ExpectedDocument.version_index.desc(),
            ExpectedDocument.created_at.desc(),
        )
        result = await self.session.execute(stmt)
        raw_docs = result.scalars().all()
        return self._deduplicate_rules(raw_docs)

    @map_database_exceptions
    async def get_document_history(
        self,
        study_id: str,
        artifact_type: str,
        canonical_name: str,
        principal: Any = None,
    ) -> Sequence[TMFDocument]:
        from apps.etmf.lifecycle import apply_document_query_filter

        stmt = (
            select(TMFDocument)
            .where(
                TMFDocument.study_id == study_id,
                (TMFDocument.artifact_type == canonical_name)
                | (TMFDocument.artifact_type == artifact_type),
            )
            .order_by(TMFDocument.version_index.asc())
        )
        if principal:
            stmt = apply_document_query_filter(stmt, principal)
        result = await self.session.execute(stmt)
        return result.scalars().all()

    @map_database_exceptions
    async def get_document_by_message_id(self, message_id: str) -> TMFDocument | None:
        stmt = select(TMFDocument).where(
            TMFDocument.metadata_json["message_id"].as_string() == message_id
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()
