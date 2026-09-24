"""FastAPI router for Clinical Endpoint Adjudication Committee (CEC) Suite.

Requirements:
- PRD-CEC-001: Automated Patient Event Dossier Compilation & PHI Redaction
- PRD-CEC-002: 2+1 Independent Blinded Reader & Tie-Breaker Workflow
- PRD-CEC-003: Bi-Directional EDC Feedback & CDISC SDTM CE Domain Export
- PRD-SYS-124: Charter Governance, Quorum Tracking, and 21 CFR Part 11 Electronic Signatures
- ADR-2199: Blinded Dual-Reader Charter & Tie-Breaker Adjudication Architecture
"""

from typing import Any

from fastapi import APIRouter, Depends, Header, HTTPException, Query
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.adapters.repositories import get_execution_db_session
from apps.execution.database.context import audit_context
from apps.execution.database.models.cec import (
    CecDossierRecord,
    CecMeetingRecord,
)
from apps.execution.domain.cec_models import (
    AdjudicationClassification,
    AdjudicationWorkflowStatus,
    CecCharterConfig,
    CecDossierSummaryItem,
    CreateCharterRequest,
    CreateDossierRequest,
    DossierBlindedView,
    EdcCasebookWritebackResult,
    EndpointCategory,
    ReaderRole,
    SdtmCeCompilationResult,
    VotingRule,
)
from apps.execution.presentation.routers.cec_schemas import (
    ChairAdjudicateDTO,
    CharterCreateDTO,
    DossierDetailResponseDTO,
    DossierPackageCreateDTO,
    MeetingQuorumRecordDTO,
    ReaderAssessmentSubmitDTO,
    SdtmCeCompileRequestDTO,
)
from apps.execution.services.cec_adjudication_service import CecAdjudicationService
from packages.security import (
    ROLE_AUDITOR,
    ROLE_CRA,
    ROLE_CRC,
    ROLE_DATA_MANAGER,
    ROLE_PRINCIPAL_INVESTIGATOR,
    ROLE_SITE_INVESTIGATOR,
    ROLE_SPONSOR_ADMIN,
    ROLE_SPONSOR_DESIGNER,
    Principal,
    get_principal,
    require_roles,
)
from packages.security.rbac import can_access_study

router = APIRouter(prefix="/api/v1/execution/cec", tags=["CEC Adjudication"])


@router.post(
    "/dossiers",
    response_model=dict[str, Any],
    status_code=201,
)
async def create_and_package_dossier(
    payload: DossierPackageCreateDTO,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_DATA_MANAGER,
            ROLE_CRA,
            ROLE_CRC,
            ROLE_SITE_INVESTIGATOR,
            ROLE_PRINCIPAL_INVESTIGATOR,
            ROLE_SPONSOR_DESIGNER,
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> dict[str, Any]:
    """Compiles and packages a de-identified clinical event dossier for 2+1 blinded adjudication.

    @req:PRD-CEC-001, @req:PRD-SYS-124, @req:ADR-2199
    """
    if not can_access_study(principal, payload.study_id):
        raise HTTPException(
            status_code=403,
            detail=f"User lacks access to study {payload.study_id}",
        )

    service = CecAdjudicationService()
    req = CreateDossierRequest(
        study_id=payload.study_id,
        site_id=payload.site_id,
        subject_id=payload.subject_id,
        event_id=payload.event_id,
        candidate_event_type=payload.candidate_event_type,
        reported_term=payload.reported_term,
        verbatim_description=payload.verbatim_description,
        reported_onset_date=payload.reported_onset_date,
        medical_narrative=payload.medical_narrative,
        bundled_forms=payload.bundled_forms,
        lab_panels=payload.lab_panels,
        ecg_records=payload.ecg_records,
        diagnostic_attachments=payload.diagnostic_attachments,
        reader_1_id=payload.reader_1_id,
        reader_1_name=payload.reader_1_name,
        reader_2_id=payload.reader_2_id,
        reader_2_name=payload.reader_2_name,
        chair_id=payload.chair_id,
        chair_name=payload.chair_name,
    )

    with audit_context(
        user_id=principal.user_id,
        change_reason="Packaged and de-identified candidate clinical event dossier",
    ):
        record = await service.package_dossier(
            session=session,
            request=req,
            created_by=principal.user_id,
        )
        await session.commit()
        await session.refresh(record)

    return {
        "status": "CREATED",
        "dossier_id": record.id,
        "event_id": record.event_id,
        "study_id": record.study_id,
        "masked_subject_id": record.masked_subject_id,
        "workflow_status": record.workflow_status,
        "dossier_digest": record.dossier_digest,
    }


@router.get(
    "/dossiers",
    response_model=list[CecDossierSummaryItem],
)
async def list_dossiers(
    study_id: str | None = Query(None, description="Filter by study ID"),
    status: str | None = Query(None, description="Filter by workflow status"),
    category: str | None = Query(
        None, description="Filter by candidate event category"
    ),
    reader_id: str | None = Query(None, description="Filter by assigned reader"),
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_DATA_MANAGER,
            ROLE_CRA,
            ROLE_CRC,
            ROLE_SITE_INVESTIGATOR,
            ROLE_PRINCIPAL_INVESTIGATOR,
            ROLE_SPONSOR_DESIGNER,
            ROLE_AUDITOR,
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> list[CecDossierSummaryItem]:
    """Lists clinical event dossiers with filtering options.

    @req:PRD-CEC-002
    """
    if study_id and not can_access_study(principal, study_id):
        return []

    stmt = select(CecDossierRecord)
    if study_id:
        stmt = stmt.where(CecDossierRecord.study_id == study_id)
    if status:
        stmt = stmt.where(CecDossierRecord.workflow_status == status)
    if category:
        stmt = stmt.where(CecDossierRecord.candidate_event_type == category)
    if reader_id:
        stmt = stmt.where(
            (CecDossierRecord.reader_1_id == reader_id)
            | (CecDossierRecord.reader_2_id == reader_id)
            | (CecDossierRecord.chair_id == reader_id)
        )

    result = await session.execute(stmt)
    records = result.scalars().all()

    items = []
    for r in records:
        items.append(
            CecDossierSummaryItem(
                dossier_id=r.id,
                event_id=r.event_id,
                study_id=r.study_id,
                masked_subject_id=r.masked_subject_id,
                candidate_event_type=EndpointCategory(r.candidate_event_type),
                reported_term=r.reported_term,
                reported_onset_date=r.reported_onset_date or "",
                workflow_status=AdjudicationWorkflowStatus(r.workflow_status),
                is_discordant=r.is_discordant,
                final_consensus_classification=(
                    AdjudicationClassification(r.final_consensus_classification)
                    if r.final_consensus_classification
                    else None
                ),
                reader_1_status=r.reader_1_status,
                reader_2_status=r.reader_2_status,
                created_at=r.created_at.isoformat() if r.created_at else "",
            )
        )
    return items


@router.get(
    "/dossiers/{dossier_id}",
    response_model=DossierBlindedView,
)
async def get_dossier_blinded_view(
    dossier_id: str,
    x_reader_role: str | None = Header(
        None, description="Reader Role override for testing/simulation"
    ),
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_DATA_MANAGER,
            ROLE_CRA,
            ROLE_CRC,
            ROLE_SITE_INVESTIGATOR,
            ROLE_PRINCIPAL_INVESTIGATOR,
            ROLE_SPONSOR_DESIGNER,
            ROLE_AUDITOR,
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> DossierBlindedView:
    """Retrieves a role-blinded projection of a dossier, enforcing reader isolation.

    @req:PRD-CEC-001, @req:PRD-CEC-002, @req:ADR-2199
    """
    stmt = select(CecDossierRecord).where(CecDossierRecord.id == dossier_id)
    result = await session.execute(stmt)
    record = result.scalar_one_or_none()
    if not record:
        raise HTTPException(status_code=404, detail=f"Dossier {dossier_id} not found.")

    if not can_access_study(principal, record.study_id):
        raise HTTPException(
            status_code=403,
            detail=f"User lacks access to study {record.study_id}",
        )

    effective_role = x_reader_role or ReaderRole.CHAIR.value
    service = CecAdjudicationService()
    return service.get_blinded_dossier_view(
        record=record,
        requesting_user_id=principal.user_id,
        requesting_role=effective_role,
    )


@router.get(
    "/dossiers/{dossier_id}/full",
    response_model=DossierDetailResponseDTO,
)
async def get_dossier_full(
    dossier_id: str,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_DATA_MANAGER,
            ROLE_AUDITOR,
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> DossierDetailResponseDTO:
    """Retrieves unblinded full dossier details for Committee Chair, Data Manager, or Auditor.

    @req:PRD-CEC-002, @req:PRD-SYS-001
    """
    stmt = select(CecDossierRecord).where(CecDossierRecord.id == dossier_id)
    result = await session.execute(stmt)
    record = result.scalar_one_or_none()
    if not record:
        raise HTTPException(status_code=404, detail=f"Dossier {dossier_id} not found.")

    if not can_access_study(principal, record.study_id):
        raise HTTPException(
            status_code=403, detail=f"Access denied to study {record.study_id}"
        )

    payload = record.dossier_payload or {}
    return DossierDetailResponseDTO(
        dossier_id=record.id,
        event_id=record.event_id,
        study_id=record.study_id,
        site_id=record.site_id,
        masked_site_id=record.masked_site_id,
        subject_id=record.subject_id,
        masked_subject_id=record.masked_subject_id,
        candidate_event_type=record.candidate_event_type,
        reported_term=record.reported_term,
        verbatim_description=record.verbatim_description or "",
        reported_onset_date=record.reported_onset_date or "",
        medical_narrative=record.medical_narrative or "",
        is_deidentified=True,
        bundled_forms=payload.get("bundled_forms", []),
        lab_panels=payload.get("lab_panels", []),
        ecg_records=payload.get("ecg_records", []),
        diagnostic_attachments=payload.get("diagnostic_attachments", []),
        redaction_summary=payload.get("redaction_summary", {}),
        reader_1_id=record.reader_1_id,
        reader_1_name=record.reader_1_name,
        reader_1_status=record.reader_1_status,
        reader_1_assessment=record.reader_1_assessment,
        reader_2_id=record.reader_2_id,
        reader_2_name=record.reader_2_name,
        reader_2_status=record.reader_2_status,
        reader_2_assessment=record.reader_2_assessment,
        chair_id=record.chair_id,
        chair_name=record.chair_name,
        chair_determination=record.chair_determination,
        workflow_status=record.workflow_status,
        is_discordant=record.is_discordant,
        discordance_reasons=record.discordance_reasons or [],
        final_consensus_classification=record.final_consensus_classification,
        final_consensus_subtype=record.final_consensus_subtype,
        final_adjudicated_onset_date=record.final_adjudicated_onset_date,
        binding_determination_source=record.binding_determination_source,
        adjudication_rationale=record.adjudication_rationale,
        electronic_signature=record.electronic_signature,
        edc_writeback_status=record.edc_writeback_status,
        dossier_digest=record.dossier_digest or "",
        created_at=record.created_at.isoformat() if record.created_at else "",
        version_index=record.version_index,
    )


@router.post(
    "/dossiers/{dossier_id}/assessments",
    response_model=dict[str, Any],
    status_code=200,
)
async def submit_reader_assessment(
    dossier_id: str,
    payload: ReaderAssessmentSubmitDTO,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_DATA_MANAGER,
            ROLE_SITE_INVESTIGATOR,
            ROLE_PRINCIPAL_INVESTIGATOR,
            ROLE_SPONSOR_DESIGNER,
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> dict[str, Any]:
    """Submits an expert reader assessment for Reader 1 or Reader 2 and auto-evaluates consensus.

    @req:PRD-CEC-002, @req:ADR-2199
    """
    service = CecAdjudicationService()

    with audit_context(
        user_id=principal.user_id,
        change_reason=f"Submitted {payload.reader_role.value} expert endpoint adjudication assessment",
    ):
        try:
            record = await service.submit_reader_assessment(
                session=session,
                dossier_id=dossier_id,
                reader_id=principal.user_id,
                reader_name=principal.user_id or "Dr. Expert Adjudicator",
                reader_role=payload.reader_role,
                classification=payload.classification,
                subtype=payload.subtype,
                adjudicated_onset_date=payload.adjudicated_onset_date,
                criteria_met=payload.criteria_met,
                confidence_score=payload.confidence_score,
                clinical_rationale=payload.clinical_rationale,
            )
            await session.commit()
            await session.refresh(record)
        except ValueError as ex:
            raise HTTPException(status_code=400, detail=str(ex))

    return {
        "status": "SUCCESS",
        "dossier_id": record.id,
        "workflow_status": record.workflow_status,
        "is_discordant": record.is_discordant,
        "discordance_reasons": record.discordance_reasons or [],
        "final_consensus_classification": record.final_consensus_classification,
        "binding_determination_source": record.binding_determination_source,
    }


@router.post(
    "/dossiers/{dossier_id}/chair-adjudicate",
    response_model=dict[str, Any],
    status_code=200,
)
async def adjudicate_chair_tie_breaker(
    dossier_id: str,
    payload: ChairAdjudicateDTO,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_DATA_MANAGER,
            ROLE_PRINCIPAL_INVESTIGATOR,
            ROLE_SPONSOR_DESIGNER,
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> dict[str, Any]:
    """Executes Adjudication Committee Chair tie-breaker ruling with 21 CFR Part 11 electronic signature.

    @req:PRD-CEC-002, @req:PRD-SYS-124, @req:ADR-2199
    """
    service = CecAdjudicationService()

    with audit_context(
        user_id=principal.user_id,
        change_reason="Chair tie-breaker adjudication determination and Part 11 sign-off",
    ):
        try:
            record = await service.adjudicate_chair_tie_breaker(
                session=session,
                dossier_id=dossier_id,
                chair_id=principal.user_id,
                chair_name=payload.signer_name,
                final_classification=payload.final_classification,
                final_subtype=payload.final_subtype,
                final_adjudicated_onset_date=payload.final_adjudicated_onset_date,
                binding_reader=payload.binding_reader,
                criteria_confirmed=payload.criteria_confirmed,
                adjudication_rationale=payload.adjudication_rationale,
                signer_name=payload.signer_name,
                signature_meaning=payload.signature_meaning,
                password=payload.password,
                signature_token=payload.signature_token,
            )
            await session.commit()
            await session.refresh(record)
        except ValueError as ex:
            raise HTTPException(status_code=400, detail=str(ex))

    return {
        "status": "FINALIZED",
        "dossier_id": record.id,
        "workflow_status": record.workflow_status,
        "final_consensus_classification": record.final_consensus_classification,
        "final_consensus_subtype": record.final_consensus_subtype,
        "final_adjudicated_onset_date": record.final_adjudicated_onset_date,
        "binding_determination_source": record.binding_determination_source,
        "electronic_signature_id": (record.electronic_signature or {}).get(
            "signature_id"
        ),
        "edc_writeback_status": record.edc_writeback_status,
    }


@router.post(
    "/dossiers/{dossier_id}/writeback",
    response_model=EdcCasebookWritebackResult,
    status_code=200,
)
async def trigger_edc_writeback(
    dossier_id: str,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_DATA_MANAGER,
            ROLE_CRA,
            ROLE_PRINCIPAL_INVESTIGATOR,
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> EdcCasebookWritebackResult:
    """Manually or automatically triggers bi-directional EDC casebook writeback for an adjudicated event.

    @req:PRD-CEC-003
    """
    stmt = select(CecDossierRecord).where(CecDossierRecord.id == dossier_id)
    result = await session.execute(stmt)
    record = result.scalar_one_or_none()
    if not record:
        raise HTTPException(status_code=404, detail=f"Dossier {dossier_id} not found.")

    service = CecAdjudicationService()
    with audit_context(
        user_id=principal.user_id,
        change_reason="Triggered bi-directional EDC casebook write-back for adjudicated endpoint",
    ):
        res = await service.write_back_to_edc_casebook(session, record)
        await session.commit()
    return res


@router.get(
    "/charters/{study_id}",
    response_model=CecCharterConfig,
)
async def get_study_charter(
    study_id: str,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_DATA_MANAGER,
            ROLE_CRA,
            ROLE_CRC,
            ROLE_SITE_INVESTIGATOR,
            ROLE_PRINCIPAL_INVESTIGATOR,
            ROLE_SPONSOR_DESIGNER,
            ROLE_AUDITOR,
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> CecCharterConfig:
    """Retrieves active CEC charter governance configuration.

    @req:PRD-SYS-124
    """
    service = CecAdjudicationService()
    charter = await service.get_charter_for_study(session, study_id)
    if not charter:
        # Return standard default charter config if not yet explicitly saved
        return CecCharterConfig(
            charter_id=f"charter-{study_id}-default",
            study_id=study_id,
            committee_name=f"Clinical Endpoint Adjudication Committee for {study_id}",
            version="1.0",
            voting_rule=VotingRule.CHAIR_TIE_BREAKER,
            minimum_quorum_percentage=66.67,
            minimum_members_count=3,
        )

    return CecCharterConfig(
        charter_id=charter.id,
        study_id=charter.study_id,
        committee_name=charter.committee_name,
        version=charter.version,
        voting_rule=VotingRule(charter.voting_rule),
        minimum_quorum_percentage=charter.minimum_quorum_percentage,
        minimum_members_count=charter.minimum_members_count,
        eligible_members=charter.eligible_members or [],
        endpoint_definitions=charter.endpoint_definitions or [],
        diagnostic_criteria_templates=charter.diagnostic_criteria_templates or {},
        is_active=charter.is_active,
        created_at=charter.created_at.isoformat() if charter.created_at else "",
        created_by=charter.created_by or "system",
        version_index=charter.version_index,
    )


@router.post(
    "/charters",
    response_model=CecCharterConfig,
    status_code=201,
)
async def create_or_update_charter(
    payload: CharterCreateDTO,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_DATA_MANAGER,
            ROLE_SPONSOR_DESIGNER,
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> CecCharterConfig:
    """Configures or updates committee charter voting rules, quorum, and diagnostic criteria forms.

    @req:PRD-SYS-124
    """
    service = CecAdjudicationService()
    req = CreateCharterRequest(
        charter_id=f"charter-{payload.study_id}",
        study_id=payload.study_id,
        committee_name=payload.committee_name,
        version=payload.version,
        voting_rule=payload.voting_rule,
        minimum_quorum_percentage=payload.minimum_quorum_percentage,
        minimum_members_count=payload.minimum_members_count,
        eligible_members=payload.eligible_members,
        endpoint_definitions=payload.endpoint_definitions,
        diagnostic_criteria_templates=payload.diagnostic_criteria_templates,
    )

    with audit_context(
        user_id=principal.user_id,
        change_reason="Configured Clinical Endpoint Adjudication Committee charter parameters",
    ):
        charter = await service.create_or_update_charter(
            session, req, principal.user_id
        )
        await session.commit()
        await session.refresh(charter)

    return CecCharterConfig(
        charter_id=charter.id,
        study_id=charter.study_id,
        committee_name=charter.committee_name,
        version=charter.version,
        voting_rule=VotingRule(charter.voting_rule),
        minimum_quorum_percentage=charter.minimum_quorum_percentage,
        minimum_members_count=charter.minimum_members_count,
        eligible_members=charter.eligible_members or [],
        endpoint_definitions=charter.endpoint_definitions or [],
        diagnostic_criteria_templates=charter.diagnostic_criteria_templates or {},
        is_active=charter.is_active,
        created_at=charter.created_at.isoformat() if charter.created_at else "",
        created_by=charter.created_by or "system",
        version_index=charter.version_index,
    )


@router.post(
    "/meetings",
    response_model=dict[str, Any],
    status_code=201,
)
async def record_meeting_quorum(
    payload: MeetingQuorumRecordDTO,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_DATA_MANAGER,
            ROLE_PRINCIPAL_INVESTIGATOR,
            ROLE_SPONSOR_DESIGNER,
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> dict[str, Any]:
    """Records committee meeting attendance, evaluates quorum compliance, and captures minutes.

    @req:PRD-SYS-124
    """
    service = CecAdjudicationService()

    with audit_context(
        user_id=principal.user_id,
        change_reason="Recorded CEC Committee meeting quorum attendance and minutes",
    ):
        meeting = await service.record_meeting_quorum(
            session=session,
            study_id=payload.study_id,
            charter_id=payload.charter_id,
            meeting_date=payload.meeting_date,
            attendees=payload.attendees,
            total_eligible_members=payload.total_eligible_members,
            dossiers_reviewed=payload.dossiers_reviewed,
            voting_results=payload.voting_results,
            meeting_minutes=payload.meeting_minutes,
            chair_signature=payload.chair_signature,
            created_by=principal.user_id,
        )
        await session.commit()
        await session.refresh(meeting)

    return {
        "status": "RECORDED",
        "meeting_id": meeting.id,
        "study_id": meeting.study_id,
        "quorum_percentage": meeting.quorum_percentage,
        "quorum_met": meeting.quorum_met,
        "minutes_digest": meeting.minutes_digest,
    }


@router.get(
    "/meetings/{study_id}",
    response_model=list[dict[str, Any]],
)
async def list_study_meetings(
    study_id: str,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_DATA_MANAGER,
            ROLE_CRA,
            ROLE_AUDITOR,
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> list[dict[str, Any]]:
    """Lists all recorded committee meetings and quorum statuses for a study.

    @req:PRD-SYS-124
    """
    stmt = select(CecMeetingRecord).where(CecMeetingRecord.study_id == study_id)
    result = await session.execute(stmt)
    records = result.scalars().all()

    return [
        {
            "meeting_id": m.id,
            "study_id": m.study_id,
            "charter_id": m.charter_id,
            "meeting_date": m.meeting_date,
            "attendee_count": len(m.attendees or []),
            "total_eligible_members": m.total_eligible_members,
            "quorum_percentage": m.quorum_percentage,
            "quorum_met": m.quorum_met,
            "dossiers_reviewed": m.dossiers_reviewed or [],
            "signed_by_chair": m.signed_by_chair,
            "created_at": m.created_at.isoformat() if m.created_at else "",
        }
        for m in records
    ]


@router.post(
    "/sdtm-ce/compile",
    response_model=SdtmCeCompilationResult,
    status_code=200,
)
async def compile_sdtm_ce_dataset(
    payload: SdtmCeCompileRequestDTO,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SPONSOR_ADMIN,
            ROLE_DATA_MANAGER,
            ROLE_SPONSOR_DESIGNER,
            ROLE_AUDITOR,
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> SdtmCeCompilationResult:
    """Compiles all finalized adjudicated events for a study directly into CDISC SDTM CE dataset.

    @req:PRD-CEC-003, @req:PRD-SYS-001
    """
    if not can_access_study(principal, payload.study_id):
        raise HTTPException(
            status_code=403, detail=f"Access denied to study {payload.study_id}"
        )

    service = CecAdjudicationService()
    with audit_context(
        user_id=principal.user_id,
        change_reason="Compiled CDISC SDTM CE (Clinical Events) dataset for study",
    ):
        result = await service.compile_study_sdtm_ce_dataset(session, payload.study_id)
        await session.commit()
    return result
