"""Database adapter and repository implementations for the Medical Imaging microservice."""

import uuid
from datetime import UTC, datetime
from typing import Any

from fastapi import Depends
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.imaging.adapters.models import (
    ImagingAdjudicationSessionModel,
    ImagingAuditLogModel,
    TumorResponseAssessmentModel,
)
from apps.imaging.domain.models import (
    AdjudicationWorkflowStatus,
    DualReaderAdjudicationSession,
    OncologicCriteria,
    ReaderAssessment,
)
from apps.imaging.ports.repository_port import ImagingAdjudicationRepositoryPort
from apps.imaging.services.adjudication import AdjudicationWorkflow
from packages.database import (
    DatabaseSessionDependency,
    RelationalDatabaseManager,
    map_database_exceptions,
)

db_manager = RelationalDatabaseManager("imaging")
get_db_session = DatabaseSessionDependency(db_manager)


def _model_to_domain(
    m: ImagingAdjudicationSessionModel,
) -> DualReaderAdjudicationSession:
    """Helper to convert ORM model to domain session entity."""
    r1_assessment = (
        ReaderAssessment(**m.reader_1_assessment) if m.reader_1_assessment else None
    )
    r2_assessment = (
        ReaderAssessment(**m.reader_2_assessment) if m.reader_2_assessment else None
    )
    adj_assessment = (
        ReaderAssessment(**m.adjudicator_assessment)
        if m.adjudicator_assessment
        else None
    )

    criteria_val = m.assessment_criteria or "RECIST_1_1"
    wf_status_val = m.workflow_status or "PENDING_PRIMARY_READS"

    return DualReaderAdjudicationSession(
        id=m.id,
        session_id=m.id,
        study_envelope_id=m.study_envelope_id,
        study_id=m.study_id,
        site_id=m.site_id or "SITE-DEFAULT",
        subject_id=m.subject_id,
        timepoint_name=m.timepoint_name,
        assessment_criteria=OncologicCriteria(criteria_val),
        reader_1_id=m.reader_1_id,
        reader_1_name=m.reader_1_name or "Reader 1",
        reader_1_status=m.reader_1_status or "PENDING",
        reader_1_assessment=r1_assessment,
        reader_2_id=m.reader_2_id,
        reader_2_name=m.reader_2_name or "Reader 2",
        reader_2_status=m.reader_2_status or "PENDING",
        reader_2_assessment=r2_assessment,
        adjudicator_id=m.adjudicator_id,
        adjudicator_name=m.adjudicator_name or "Adjudication Committee Chair",
        adjudicator_assessment=adj_assessment,
        sld_discordance_delta=(
            m.sld_discordance_delta if m.sld_discordance_delta is not None else 0.0
        ),
        is_response_discordant=(
            m.is_response_discordant if m.is_response_discordant is not None else False
        ),
        discordance_reasons=m.discordance_reasons or [],
        workflow_status=AdjudicationWorkflowStatus(wf_status_val),
        final_consensus_response=m.final_consensus_response,
        final_consensus_sld_mm=m.final_consensus_sld_mm,
        binding_reader=m.binding_reader,
        adjudication_rationale=m.adjudication_rationale,
        electronic_signature=m.electronic_signature,
        locked_at=m.locked_at.isoformat() if m.locked_at else None,
        locked_by=m.locked_by,
        session_digest=m.session_digest or "",
        tenant_id=m.tenant_id or "tenant_default",
        created_at=m.created_at.isoformat() if m.created_at else None,
        created_by=m.created_by or "system",
        updated_at=m.updated_at.isoformat() if m.updated_at else None,
        reason_for_change=m.reason_for_change,
        version_index=m.version_index if m.version_index is not None else 1,
    )


class SqlImagingAdjudicationRepository(ImagingAdjudicationRepositoryPort):
    """SQLAlchemy implementation of the ImagingAdjudicationRepositoryPort."""

    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    @map_database_exceptions
    async def get_by_id(
        self, entity_id: str, *args: Any, **kwargs: Any
    ) -> DualReaderAdjudicationSession | None:
        """Retrieve adjudication session by ID."""
        stmt = select(ImagingAdjudicationSessionModel).where(
            ImagingAdjudicationSessionModel.id == entity_id
        )
        res = await self._session.execute(stmt)
        model = res.scalar_one_or_none()
        if not model:
            return None
        return _model_to_domain(model)

    @map_database_exceptions
    async def save(
        self, entity: DualReaderAdjudicationSession, *args: Any, **kwargs: Any
    ) -> DualReaderAdjudicationSession:
        """Persist or update adjudication session state."""
        session_id = entity.session_id or entity.id or str(uuid.uuid4())
        stmt = select(ImagingAdjudicationSessionModel).where(
            ImagingAdjudicationSessionModel.id == session_id
        )
        res = await self._session.execute(stmt)
        model = res.scalar_one_or_none()

        r1_dump = (
            entity.reader_1_assessment.model_dump()
            if entity.reader_1_assessment
            else None
        )
        r2_dump = (
            entity.reader_2_assessment.model_dump()
            if entity.reader_2_assessment
            else None
        )
        adj_dump = (
            entity.adjudicator_assessment.model_dump()
            if entity.adjudicator_assessment
            else None
        )
        criteria_str = (
            entity.assessment_criteria.value
            if hasattr(entity.assessment_criteria, "value")
            else str(entity.assessment_criteria)
        )
        wf_str = (
            entity.workflow_status.value
            if hasattr(entity.workflow_status, "value")
            else str(entity.workflow_status)
        )
        final_resp = (
            getattr(
                entity.final_consensus_response,
                "value",
                entity.final_consensus_response,
            )
            if entity.final_consensus_response is not None
            else None
        )
        locked_dt = (
            datetime.fromisoformat(entity.locked_at) if entity.locked_at else None
        )

        if not model:
            model = ImagingAdjudicationSessionModel(
                id=session_id,
                study_envelope_id=entity.study_envelope_id,
                study_id=entity.study_id,
                site_id=entity.site_id,
                subject_id=entity.subject_id,
                timepoint_name=entity.timepoint_name,
                assessment_criteria=criteria_str,
                reader_1_id=entity.reader_1_id,
                reader_1_name=entity.reader_1_name,
                reader_1_status=entity.reader_1_status,
                reader_1_assessment=r1_dump,
                reader_2_id=entity.reader_2_id,
                reader_2_name=entity.reader_2_name,
                reader_2_status=entity.reader_2_status,
                reader_2_assessment=r2_dump,
                adjudicator_id=entity.adjudicator_id,
                adjudicator_name=entity.adjudicator_name,
                adjudicator_assessment=adj_dump,
                sld_discordance_delta=entity.sld_discordance_delta,
                is_response_discordant=entity.is_response_discordant,
                discordance_reasons=entity.discordance_reasons,
                workflow_status=wf_str,
                final_consensus_response=final_resp,
                final_consensus_sld_mm=entity.final_consensus_sld_mm,
                binding_reader=entity.binding_reader,
                adjudication_rationale=entity.adjudication_rationale,
                electronic_signature=entity.electronic_signature,
                locked_at=locked_dt,
                locked_by=entity.locked_by,
                session_digest=entity.session_digest,
                tenant_id=entity.tenant_id,
                created_by=entity.created_by,
                reason_for_change=entity.reason_for_change,
                version_index=entity.version_index,
            )
            self._session.add(model)
        else:
            model.study_envelope_id = entity.study_envelope_id
            model.study_id = entity.study_id
            model.site_id = entity.site_id
            model.subject_id = entity.subject_id
            model.timepoint_name = entity.timepoint_name
            model.assessment_criteria = criteria_str
            model.reader_1_id = entity.reader_1_id
            model.reader_1_name = entity.reader_1_name
            model.reader_1_status = entity.reader_1_status
            model.reader_1_assessment = r1_dump
            model.reader_2_id = entity.reader_2_id
            model.reader_2_name = entity.reader_2_name
            model.reader_2_status = entity.reader_2_status
            model.reader_2_assessment = r2_dump
            model.adjudicator_id = entity.adjudicator_id
            model.adjudicator_name = entity.adjudicator_name
            model.adjudicator_assessment = adj_dump
            model.sld_discordance_delta = entity.sld_discordance_delta
            model.is_response_discordant = entity.is_response_discordant
            model.discordance_reasons = entity.discordance_reasons
            model.workflow_status = wf_str
            model.final_consensus_response = final_resp
            model.final_consensus_sld_mm = entity.final_consensus_sld_mm
            model.binding_reader = entity.binding_reader
            model.adjudication_rationale = entity.adjudication_rationale
            model.electronic_signature = entity.electronic_signature
            model.locked_at = locked_dt
            model.locked_by = entity.locked_by
            model.session_digest = entity.session_digest
            model.tenant_id = entity.tenant_id
            model.version_index = entity.version_index
            model.reason_for_change = entity.reason_for_change

        await self._session.commit()
        await self._session.refresh(model)
        return _model_to_domain(model)

    @map_database_exceptions
    async def list_by_filters(
        self,
        study_id: str | None = None,
        subject_id: str | None = None,
        timepoint_name: str | None = None,
        workflow_status: str | None = None,
        skip: int = 0,
        limit: int = 50,
    ) -> list[DualReaderAdjudicationSession]:
        """List adjudication sessions matching query filters."""
        stmt = (
            select(ImagingAdjudicationSessionModel)
            .order_by(ImagingAdjudicationSessionModel.created_at.desc())
            .offset(skip)
            .limit(limit)
        )
        if study_id:
            stmt = stmt.where(ImagingAdjudicationSessionModel.study_id == study_id)
        if subject_id:
            stmt = stmt.where(ImagingAdjudicationSessionModel.subject_id == subject_id)
        if timepoint_name:
            stmt = stmt.where(
                ImagingAdjudicationSessionModel.timepoint_name == timepoint_name
            )
        if workflow_status:
            stmt = stmt.where(
                ImagingAdjudicationSessionModel.workflow_status == workflow_status
            )

        res = await self._session.execute(stmt)
        models = res.scalars().all()
        return [_model_to_domain(m) for m in models]

    @map_database_exceptions
    async def record_tumor_response_assessment(
        self,
        session: DualReaderAdjudicationSession,
        signature_digest: str,
        actor_id: str,
        rationale: str,
    ) -> None:
        """Persist TumorResponseAssessmentModel for subject trajectory synchronization."""
        locked_dt = (
            datetime.fromisoformat(session.locked_at)
            if session.locked_at
            else datetime.now(UTC)
        )
        criteria_str = (
            session.assessment_criteria.value
            if hasattr(session.assessment_criteria, "value")
            else str(session.assessment_criteria)
        )
        resp_val = (
            getattr(
                session.final_consensus_response,
                "value",
                str(session.final_consensus_response),
            )
            if session.final_consensus_response is not None
            else "SD"
        )
        assessment_model = TumorResponseAssessmentModel(
            id=str(uuid.uuid4()),
            study_id=session.study_id,
            site_id=session.site_id,
            subject_id=session.subject_id,
            timepoint_name=session.timepoint_name,
            assessment_criteria=criteria_str,
            current_sld_mm=session.final_consensus_sld_mm,
            target_response=resp_val,
            non_target_status="NON_CR_NON_PD",
            new_lesions_present=False,
            overall_response=resp_val,
            is_locked=True,
            locked_at=locked_dt,
            locked_by=session.locked_by or actor_id,
            reader_id=session.locked_by or actor_id,
            manifest_sha256=session.session_digest,
            signature=f"SIG-ADJ-{signature_digest[:16]}",
            created_by=actor_id,
            reason_for_change="Adjudication Tie-Breaker Resolution",
        )
        self._session.add(assessment_model)
        await self._session.commit()

    @map_database_exceptions
    async def record_audit_log(
        self,
        action: str,
        actor_id: str,
        reason_for_change: str | None = None,
        details: dict[str, Any] | None = None,
    ) -> None:
        """Record 21 CFR Part 11 compliant audit log."""
        entry = ImagingAuditLogModel(
            action=action,
            actor_id=actor_id,
            reason_for_change=reason_for_change,
            details=details or {},
        )
        self._session.add(entry)
        await self._session.commit()


def get_adjudication_repository(
    session: AsyncSession = Depends(get_db_session),
) -> ImagingAdjudicationRepositoryPort:
    """FastAPI dependency yielding ImagingAdjudicationRepositoryPort."""
    return SqlImagingAdjudicationRepository(session)


def get_adjudication_workflow(
    repository: ImagingAdjudicationRepositoryPort = Depends(
        get_adjudication_repository
    ),
) -> AdjudicationWorkflow:
    """FastAPI dependency yielding AdjudicationWorkflow service."""
    return AdjudicationWorkflow(repository)
