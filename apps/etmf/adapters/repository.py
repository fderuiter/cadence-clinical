from typing import Sequence, Any
from sqlalchemy import select, func
from ..ports.repository import ETMFRepositoryPort
from ..models import TMFDocument, ExpectedDocument, TMFAuditLog, DocumentQCTransition
from ..database import get_session


class SQLETMFRepository(ETMFRepositoryPort):
    @property
    def session(self) -> Any:
        return get_session()

    async def get_document_by_id(self, doc_id: str) -> TMFDocument | None:
        session = get_session()
        stmt = select(TMFDocument).where(TMFDocument.id == doc_id)
        result = await session.execute(stmt)
        return result.scalars().first()

    async def get_documents_by_study(self, study_id: str) -> Sequence[TMFDocument]:
        session = get_session()
        stmt = select(TMFDocument).where(TMFDocument.study_id == study_id)
        result = await session.execute(stmt)
        return result.scalars().all()

    async def save_document(self, doc: TMFDocument) -> TMFDocument:
        session = get_session()
        session.add(doc)
        await session.flush()
        return doc

    async def delete_document(self, doc: TMFDocument) -> None:
        session = get_session()
        await session.delete(doc)
        await session.flush()

    async def get_expected_document_by_id(self, edl_id: str) -> ExpectedDocument | None:
        session = get_session()
        stmt = select(ExpectedDocument).where(ExpectedDocument.id == edl_id)
        result = await session.execute(stmt)
        return result.scalars().first()

    async def get_expected_documents_by_study(self, study_id: str) -> Sequence[ExpectedDocument]:
        session = get_session()
        stmt = select(ExpectedDocument).where(ExpectedDocument.study_id == study_id)
        result = await session.execute(stmt)
        return result.scalars().all()

    async def get_expected_documents_by_study_and_site(
        self, study_id: str, site_id: str | None
    ) -> Sequence[ExpectedDocument]:
        session = get_session()
        stmt = select(ExpectedDocument).where(ExpectedDocument.study_id == study_id)
        if site_id:
            stmt = stmt.where((ExpectedDocument.site_id == site_id) | ExpectedDocument.site_id.is_(None))
        else:
            stmt = stmt.where(ExpectedDocument.site_id.is_(None))
        result = await session.execute(stmt)
        return result.scalars().all()

    async def save_expected_document(self, edl: ExpectedDocument) -> ExpectedDocument:
        session = get_session()
        session.add(edl)
        await session.flush()
        return edl

    async def get_audit_logs(self, skip: int, limit: int) -> Sequence[TMFAuditLog]:
        session = get_session()
        stmt = select(TMFAuditLog).order_by(TMFAuditLog.created_at.desc()).offset(skip).limit(limit)
        result = await session.execute(stmt)
        return result.scalars().all()

    async def get_audit_logs_count(self) -> int:
        session = get_session()
        stmt = select(func.count()).select_from(TMFAuditLog)
        result = await session.execute(stmt)
        return result.scalar() or 0

    async def save_audit_log(self, log: TMFAuditLog) -> TMFAuditLog:
        session = get_session()
        session.add(log)
        await session.flush()
        return log

    async def get_qc_transitions_by_document_id(
        self, doc_id: str
    ) -> Sequence[DocumentQCTransition]:
        session = get_session()
        stmt = (
            select(DocumentQCTransition)
            .where(DocumentQCTransition.document_id == doc_id)
            .order_by(DocumentQCTransition.created_at.desc())
        )
        result = await session.execute(stmt)
        return result.scalars().all()

    async def get_qc_transitions_by_document_id_asc(
        self, doc_id: str
    ) -> Sequence[DocumentQCTransition]:
        session = get_session()
        stmt = (
            select(DocumentQCTransition)
            .where(DocumentQCTransition.document_id == doc_id)
            .order_by(DocumentQCTransition.timestamp.asc())
        )
        result = await session.execute(stmt)
        return result.scalars().all()

    async def save_qc_transition(
        self, transition: DocumentQCTransition
    ) -> DocumentQCTransition:
        session = get_session()
        session.add(transition)
        await session.flush()
        return transition

    async def get_documents_filtered(
        self,
        study_id: str | None,
        zone: int | None,
        search: str | None,
        status: str | None,
        principal: Any,
    ) -> Sequence[TMFDocument]:
        from apps.etmf.lifecycle import apply_document_query_filter
        session = get_session()
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
        result = await session.execute(stmt)
        return result.scalars().all()

    async def get_max_version_index(
        self, study_id: str, site_id: str | None, artifact_code: str
    ) -> int:
        session = get_session()
        stmt = (
            select(func.max(TMFDocument.version_index))
            .where(
                TMFDocument.study_id == study_id,
                TMFDocument.site_id == site_id,
                TMFDocument.artifact_code == artifact_code,
            )
        )
        result = await session.execute(stmt)
        return result.scalar() or 0

    async def get_redacted_successor(self, doc_id: str) -> TMFDocument | None:
        session = get_session()
        stmt = select(TMFDocument).where(TMFDocument.redaction_source_id == doc_id)
        result = await session.execute(stmt)
        return result.scalars().first()

    async def get_document_lineage(
        self, study_id: str, artifact_code: str
    ) -> Sequence[TMFDocument]:
        session = get_session()
        stmt = (
            select(TMFDocument)
            .where(
                TMFDocument.study_id == study_id,
                TMFDocument.artifact_code == artifact_code,
            )
            .order_by(TMFDocument.version_index.asc())
        )
        result = await session.execute(stmt)
        return result.scalars().all()

    async def get_expected_document_by_study_milestone_and_artifact(
        self, study_id: str, milestone: str, artifact_type: str
    ) -> ExpectedDocument | None:
        session = get_session()
        stmt = select(ExpectedDocument).where(
            ExpectedDocument.study_id == study_id,
            ExpectedDocument.milestone == milestone,
            ExpectedDocument.artifact_type == artifact_type,
            ExpectedDocument.site_id.is_(None),
        )
        result = await session.execute(stmt)
        return result.scalars().first()

    async def get_documents_by_study_and_status(
        self, study_id: str, status: str
    ) -> Sequence[TMFDocument]:
        session = get_session()
        stmt = select(TMFDocument).where(
            TMFDocument.study_id == study_id,
            TMFDocument.status == status,
        )
        result = await session.execute(stmt)
        return result.scalars().all()

    async def get_unsealed_audit_logs(self) -> Sequence[TMFAuditLog]:
        session = get_session()
        stmt = (
            select(TMFAuditLog)
            .where(TMFAuditLog.sealed_block_id.is_(None))
            .order_by(TMFAuditLog.created_at.asc())
        )
        result = await session.execute(stmt)
        return result.scalars().all()

    async def get_audit_log_by_id(self, log_id: str) -> TMFAuditLog | None:
        session = get_session()
        stmt = select(TMFAuditLog).where(TMFAuditLog.id == log_id)
        result = await session.execute(stmt)
        return result.scalars().first()

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
        session = get_session()
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
        total_res = await session.execute(count_stmt)
        total_count = total_res.scalar_one()

        stmt = select(TMFAuditLog)
        if filters:
            stmt = stmt.where(*filters)
        stmt = stmt.order_by(TMFAuditLog.timestamp.desc(), TMFAuditLog.id.desc())
        stmt = stmt.offset(offset).limit(limit)
        result = await session.execute(stmt)
        logs = result.scalars().all()
        return total_count, logs

    async def get_expected_documents_filtered(
        self,
        study_id: str,
        site_id: str | None,
        milestone: str | None,
    ) -> Sequence[ExpectedDocument]:
        from apps.etmf.main import normalize_milestone
        session = get_session()
        stmt = select(ExpectedDocument).where(ExpectedDocument.study_id == study_id)
        if site_id:
            stmt = stmt.where(ExpectedDocument.site_id == site_id)
        if milestone:
            stmt = stmt.where(ExpectedDocument.milestone == normalize_milestone(milestone))
        result = await session.execute(stmt)
        return result.scalars().all()

    async def get_document_history(
        self, study_id: str, artifact_type: str, canonical_name: str, principal: Any = None
    ) -> Sequence[TMFDocument]:
        from apps.etmf.lifecycle import apply_document_query_filter
        session = get_session()
        stmt = select(TMFDocument).where(
            TMFDocument.study_id == study_id,
            (TMFDocument.artifact_type == canonical_name)
            | (TMFDocument.artifact_type == artifact_type),
        ).order_by(TMFDocument.version_index.asc())
        if principal:
            stmt = apply_document_query_filter(stmt, principal)
        result = await session.execute(stmt)
        return result.scalars().all()

    async def get_document_by_message_id(self, message_id: str) -> TMFDocument | None:
        session = get_session()
        stmt = select(TMFDocument).where(
            TMFDocument.metadata_json["message_id"].as_string() == message_id
        )
        result = await session.execute(stmt)
        return result.scalars().first()
