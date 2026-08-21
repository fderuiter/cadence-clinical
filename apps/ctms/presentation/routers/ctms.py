import os
from datetime import UTC, datetime
from typing import Any

from fastapi import APIRouter, Depends, HTTPException, Request
from pydantic import BaseModel
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.ctms.database import db_manager
from apps.ctms.domain.acl.sync_engine_dto import (
    CTMSSignatureValidationError,
    CTMSSyncMetadataDTO,
    CTMSSyncRecordDTO,
    reconcile_ctms_records,
    verify_ctms_record_signature,
)
from apps.ctms.models import (
    BudgetLineItem,
    CRAAllocation,
    CTMSAuditLog,
    CTMSClinicalQuery,
    CTMSStudy,
    GeneratedLetter,
    InvestigatorGrant,
    InvestigatorPayable,
    MonitoringVisit,
    MonitoringVisitDefeated,
    MonitoringVisitFinding,
    PaymentMilestone,
    RecruitmentRecord,
    SiteMilestone,
    write_audit_log,
)
from apps.ctms.presentation.dtos import (
    BudgetLineItemCreate,
    BudgetLineItemResponse,
    ConflictStrategy,
    CRAAllocationCreate,
    CRAAllocationResponse,
    CRAAllocationUpdate,
    CRAWorkloadItem,
    CTMSAuditLogResponse,
    CTMSStudyCreate,
    CTMSStudyResponse,
    GeneratedLetterResponse,
    InvestigatorGrantCreate,
    InvestigatorGrantResponse,
    InvestigatorGrantUpdate,
    InvestigatorPayableResponse,
    MonitoringVisitComplete,
    MonitoringVisitCreate,
    MonitoringVisitOfflineSync,
    MonitoringVisitResponse,
    PaymentMilestoneCreate,
    PaymentMilestoneResponse,
    RecruitmentRecordCreate,
    RecruitmentRecordResponse,
    SiteMilestoneCreate,
    SiteMilestoneResponse,
    SiteMilestoneUpdate,
)
from apps.ctms.rendering import render_confirmation_letter, render_follow_up_letter
from packages.database import DatabaseSessionDependency
from packages.security.rbac import Principal, get_principal, has_permission

get_db_session = DatabaseSessionDependency(db_manager)

router = APIRouter(prefix="/api/v1/ctms", tags=["CTMS"])


def check_financial_write_roles(principal: Principal) -> None:
    """Enforces that financial writes are restricted to Grants Manager or Sponsor Admin."""
    if not has_permission(principal, "ctms_financial:write"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")


def check_grant_mutable(grant: InvestigatorGrant) -> None:
    """Approved grants are locked from editing."""
    if grant.status == "APPROVED":
        raise HTTPException(
            status_code=400, detail="Approved grants are locked from editing."
        )


async def evaluate_milestones_for_grant(
    session: AsyncSession,
    grant_id: str,
    condition: str,
    user_id: str,
    change_reason: str,
) -> None:
    """Deterministic, idempotent evaluation of payment milestones for a given condition."""
    grant_stmt = select(InvestigatorGrant).where(InvestigatorGrant.id.is_(grant_id))
    grant_res = await session.execute(grant_stmt)
    grant = grant_res.scalars().first()
    if not grant:
        return

    if grant.status != "APPROVED":
        return

    ms_stmt = select(PaymentMilestone).where(
        PaymentMilestone.grant_id.is_(grant_id),
        PaymentMilestone.trigger_condition.is_(condition.upper()),
        PaymentMilestone.is_triggered.is_(False),
    )
    ms_res = await session.execute(ms_stmt)
    milestones = ms_res.scalars().all()

    for ms in milestones:
        p_stmt = select(InvestigatorPayable).where(
            InvestigatorPayable.grant_id.is_(grant_id),
            InvestigatorPayable.milestone_id.is_(ms.id),
        )
        p_res = await session.execute(p_stmt)
        existing_payable = p_res.scalars().first()

        if not existing_payable:
            ms.is_triggered = True
            ms.triggered_at = datetime.now(UTC)
            ms.version_index += 1
            ms.reason_for_change = f"Automated trigger on condition: {condition}"
            session.add(ms)

            payable = InvestigatorPayable(
                grant_id=grant_id,
                milestone_id=ms.id,
                amount=ms.amount,
                payment_status="PENDING",
                created_by=user_id,
                reason_for_change=change_reason,
                version_index=1,
            )
            session.add(payable)

            await write_audit_log(
                session=session,
                user_id=user_id,
                user_role="system",
                action="TRIGGER_MILESTONE",
                details=f"Triggered milestone '{ms.milestone_name}' ({ms.id}) for grant '{grant_id}' due to condition '{condition}'. Created pending payable of {ms.amount} {grant.currency}.",
            )


@router.post("/studies", response_model=CTMSStudyResponse, status_code=201)
async def create_study(
    request: Request,
    payload: CTMSStudyCreate,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> CTMSStudyResponse:
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)
    change_reason = principal.change_reason or "system_operation"

    if not has_permission(principal, "ctms_study:create"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    study = CTMSStudy(
        study_id=payload.study_id,
        name=payload.name,
        status=payload.status or "ACTIVE",
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=1,
    )
    session.add(study)
    await session.flush()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="CREATE_STUDY",
        details=f"Created CTMS study '{payload.study_id}' with name '{payload.name}'. Reason: {change_reason}",
    )

    return CTMSStudyResponse(
        id=study.id,
        study_id=study.study_id,
        name=study.name,
        status=study.status,
        created_at=study.created_at.isoformat(),
        created_by=study.created_by,
        reason_for_change=study.reason_for_change,
        version_index=study.version_index,
    )


@router.get("/studies", response_model=list[CTMSStudyResponse])
async def list_studies(
    request: Request,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> list[CTMSStudyResponse]:
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "ctms_study:read"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    stmt = select(CTMSStudy).order_by(CTMSStudy.created_at.desc())
    result = await session.execute(stmt)
    studies = result.scalars().all()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="LIST_STUDIES",
        details="Listed all CTMS studies.",
    )

    return [
        CTMSStudyResponse(
            id=s.id,
            study_id=s.study_id,
            name=s.name,
            status=s.status,
            created_at=s.created_at.isoformat(),
            created_by=s.created_by,
            reason_for_change=s.reason_for_change,
            version_index=s.version_index,
        )
        for s in studies
    ]


@router.get("/audit-logs", response_model=list[CTMSAuditLogResponse])
async def get_audit_trail(
    request: Request,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> list[CTMSAuditLogResponse]:
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "ctms_audit_logs:read"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="VIEW_AUDIT_LOGS",
        details="Accessed CTMS audit logs.",
    )

    stmt = select(CTMSAuditLog).order_by(CTMSAuditLog.timestamp.desc())
    result = await session.execute(stmt)
    logs = result.scalars().all()

    return [
        CTMSAuditLogResponse(
            id=log.id,
            timestamp=log.timestamp.isoformat(),
            user_id=log.user_id,
            user_role=log.user_role,
            action=log.action,
            details=log.details,
        )
        for log in logs
    ]


@router.post(
    "/monitoring-visits",
    response_model=MonitoringVisitResponse,
    status_code=201,
)
async def schedule_monitoring_visit(
    request: Request,
    payload: MonitoringVisitCreate,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> MonitoringVisitResponse:
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)
    change_reason = principal.change_reason or "system_operation"

    if not has_permission(principal, "ctms_monitoring_visit:create"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    alloc_stmt = select(CRAAllocation).where(
        CRAAllocation.study_id.is_(payload.study_id),
        CRAAllocation.site_id.is_(payload.site_id),
        CRAAllocation.status.is_("ACTIVE"),
    )
    alloc_result = await session.execute(alloc_stmt)
    active_alloc = alloc_result.scalars().first()
    if active_alloc and active_alloc.cra_id != payload.cra_id:
        raise HTTPException(
            status_code=400,
            detail=f"CRA '{payload.cra_id}' is not allocated to site '{payload.site_id}' and study '{payload.study_id}'. Allocated CRA is '{active_alloc.cra_id}'.",
        )

    visit = MonitoringVisit(
        study_id=payload.study_id,
        site_id=payload.site_id,
        cra_id=payload.cra_id,
        visit_type=payload.visit_type,
        scheduled_date=payload.scheduled_date,
        status="SCHEDULED",
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=1,
    )
    session.add(visit)
    await session.flush()

    rendered_content = render_confirmation_letter(
        study_id=visit.study_id,
        site_id=visit.site_id,
        cra_id=visit.cra_id,
        visit_type=visit.visit_type,
        scheduled_date=visit.scheduled_date,
        created_at=visit.created_at,
    )

    letter = GeneratedLetter(
        visit_id=visit.id,
        letter_type="CONFIRMATION",
        rendered_content=rendered_content,
        created_by=user_id,
        reason_for_change="Automated confirmation letter on visit scheduling",
        version_index=1,
    )
    session.add(letter)
    await session.flush()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="CREATE_VISIT",
        details=f"Scheduled monitoring visit '{visit.id}' of type '{visit.visit_type}' for study '{visit.study_id}' at site '{visit.site_id}'.",
    )
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="GENERATE_LETTER",
        details=f"Generated confirmation letter for visit '{visit.id}'.",
    )

    return MonitoringVisitResponse(
        id=visit.id,
        study_id=visit.study_id,
        site_id=visit.site_id,
        cra_id=visit.cra_id,
        visit_type=visit.visit_type,
        scheduled_date=visit.scheduled_date.isoformat(),
        actual_date=None,
        status=visit.status,
        created_at=visit.created_at.isoformat(),
        created_by=visit.created_by,
        reason_for_change=visit.reason_for_change,
        version_index=visit.version_index,
    )


@router.post(
    "/monitoring-visits/{visit_id}/complete",
    response_model=MonitoringVisitResponse,
)
async def complete_monitoring_visit(
    visit_id: str,
    request: Request,
    payload: MonitoringVisitComplete,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> MonitoringVisitResponse:
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)
    change_reason = principal.change_reason or "system_operation"

    if not has_permission(principal, "ctms_monitoring_visit:update"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    stmt = select(MonitoringVisit).where(MonitoringVisit.id.is_(visit_id))
    result = await session.execute(stmt)
    visit = result.scalars().first()

    if not visit:
        raise HTTPException(status_code=404, detail="Monitoring visit not found")

    if visit.status != "SCHEDULED":
        raise HTTPException(
            status_code=400,
            detail=f"Monitoring visit cannot be completed from state: {visit.status}",
        )

    visit.status = "COMPLETED"
    visit.actual_date = payload.actual_date
    visit.version_index += 1
    visit.reason_for_change = change_reason
    session.add(visit)

    finding_objs = []
    for f in payload.findings:
        if f.severity.upper() not in ("MINOR", "MAJOR", "CRITICAL"):
            raise HTTPException(
                status_code=400,
                detail=f"Invalid finding severity: {f.severity}",
            )
        finding = MonitoringVisitFinding(
            visit_id=visit.id,
            text=f.text,
            severity=f.severity.upper(),
            resolution_status=f.resolution_status or "OPEN",
            created_by=user_id,
            reason_for_change=change_reason,
            version_index=1,
        )
        session.add(finding)
        finding_objs.append(finding)

    await session.flush()

    findings_list = [
        {
            "text": f.text,
            "severity": f.severity,
            "resolution_status": f.resolution_status,
        }
        for f in finding_objs
    ]

    rendered_content = render_follow_up_letter(
        study_id=visit.study_id,
        site_id=visit.site_id,
        cra_id=visit.cra_id,
        visit_type=visit.visit_type,
        actual_date=visit.actual_date,
        findings=findings_list,
        created_at=datetime.now(UTC),
    )

    letter = GeneratedLetter(
        visit_id=visit.id,
        letter_type="FOLLOW_UP",
        rendered_content=rendered_content,
        created_by=user_id,
        reason_for_change="Automated follow-up letter on visit completion",
        version_index=1,
    )
    session.add(letter)
    await session.flush()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="COMPLETE_VISIT",
        details=f"Completed monitoring visit '{visit.id}'. Actual date: {visit.actual_date.isoformat()}.",
    )
    for f_obj in finding_objs:
        await write_audit_log(
            session=session,
            user_id=user_id,
            user_role=user_roles,
            action="CREATE_FINDING",
            details=f"Recorded {f_obj.severity} finding for visit '{visit.id}': {f_obj.text}",
        )

    grants_stmt = select(InvestigatorGrant).where(
        InvestigatorGrant.study_id.is_(visit.study_id),
        InvestigatorGrant.site_id.is_(visit.site_id),
        InvestigatorGrant.status.is_("APPROVED"),
    )
    grants_res = await session.execute(grants_stmt)
    matching_grants = grants_res.scalars().all()
    for grant in matching_grants:
        await evaluate_milestones_for_grant(
            session=session,
            grant_id=grant.id,
            condition="VISIT_COMPLETED",
            user_id=user_id,
            change_reason="Automated trigger on Visit Completion milestone",
        )

    return MonitoringVisitResponse(
        id=visit.id,
        study_id=visit.study_id,
        site_id=visit.site_id,
        cra_id=visit.cra_id,
        visit_type=visit.visit_type,
        scheduled_date=visit.scheduled_date.isoformat(),
        actual_date=visit.actual_date.isoformat() if visit.actual_date else None,
        status=visit.status,
        created_at=visit.created_at.isoformat(),
        created_by=visit.created_by,
        reason_for_change=visit.reason_for_change,
        version_index=visit.version_index,
    )


@router.get(
    "/monitoring-visits",
    response_model=list[MonitoringVisitResponse],
)
async def list_monitoring_visits(
    request: Request,
    study_id: str | None = None,
    site_id: str | None = None,
    cra_id: str | None = None,
    status: str | None = None,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> list[MonitoringVisitResponse]:
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "ctms_monitoring_visit:read"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    stmt = select(MonitoringVisit)
    if study_id:
        stmt = stmt.where(MonitoringVisit.study_id.is_(study_id))
    if site_id:
        stmt = stmt.where(MonitoringVisit.site_id.is_(site_id))

    elevated_roles = {
        "Admin",
        "System Admin",
        "Auditor",
        "Sponsor Admin",
        "Supervisor",
        "SYSTEM_ADMIN",
    }
    if cra_id:
        stmt = stmt.where(MonitoringVisit.cra_id.is_(cra_id))
    elif not any(role in elevated_roles for role in principal.raw_roles):
        stmt = stmt.where(MonitoringVisit.cra_id.is_(user_id))

    if status:
        stmt = stmt.where(MonitoringVisit.status.is_(status))

    stmt = stmt.order_by(MonitoringVisit.scheduled_date.desc())
    result = await session.execute(stmt)
    visits = result.scalars().all()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="LIST_VISITS",
        details="Listed CTMS monitoring visits.",
    )

    return [
        MonitoringVisitResponse(
            id=v.id,
            study_id=v.study_id,
            site_id=v.site_id,
            cra_id=v.cra_id,
            visit_type=v.visit_type,
            scheduled_date=v.scheduled_date.isoformat(),
            actual_date=v.actual_date.isoformat() if v.actual_date else None,
            status=v.status,
            created_at=v.created_at.isoformat(),
            created_by=v.created_by,
            reason_for_change=v.reason_for_change,
            version_index=v.version_index,
        )
        for v in visits
    ]


@router.get(
    "/monitoring-visits/{visit_id}/letters",
    response_model=list[GeneratedLetterResponse],
)
async def get_monitoring_visit_letters(
    visit_id: str,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> list[GeneratedLetterResponse]:
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "ctms_monitoring_letter:read"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    stmt = select(GeneratedLetter).where(GeneratedLetter.visit_id.is_(visit_id))
    result = await session.execute(stmt)
    letters = result.scalars().all()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="RETRIEVE_LETTERS",
        details=f"Retrieved letters for monitoring visit '{visit_id}'.",
    )

    return [
        GeneratedLetterResponse(
            id=let.id,
            visit_id=let.visit_id,
            letter_type=let.letter_type,
            rendered_content=let.rendered_content,
            created_at=let.created_at.isoformat(),
            created_by=let.created_by,
            reason_for_change=let.reason_for_change,
            version_index=let.version_index,
        )
        for let in letters
    ]


@router.get(
    "/monitoring-visits/{visit_id}/letters/{letter_type}",
    response_model=GeneratedLetterResponse,
)
async def get_monitoring_visit_letter_by_type(
    visit_id: str,
    letter_type: str,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> GeneratedLetterResponse:
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "ctms_monitoring_letter:read_type"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    stmt = select(GeneratedLetter).where(
        GeneratedLetter.visit_id.is_(visit_id),
        GeneratedLetter.letter_type.is_(letter_type.upper()),
    )
    result = await session.execute(stmt)
    letter = result.scalars().first()

    if not letter:
        raise HTTPException(
            status_code=404,
            detail=f"Generated letter of type '{letter_type}' not found for visit '{visit_id}'",
        )

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="RETRIEVE_LETTER",
        details=f"Retrieved letter of type '{letter_type}' for monitoring visit '{visit_id}'.",
    )

    return GeneratedLetterResponse(
        id=letter.id,
        visit_id=letter.visit_id,
        letter_type=letter.letter_type,
        rendered_content=letter.rendered_content,
        created_at=letter.created_at.isoformat(),
        created_by=letter.created_by,
        reason_for_change=letter.reason_for_change,
        version_index=letter.version_index,
    )


@router.post(
    "/monitoring-visits/{visit_id}/sign-off",
    response_model=MonitoringVisitResponse,
)
async def sign_off_monitoring_visit(
    visit_id: str,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> MonitoringVisitResponse:
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)
    change_reason = principal.change_reason or "system_operation"

    if not has_permission(principal, "ctms_monitoring_visit:sign_off"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    stmt = select(MonitoringVisit).where(MonitoringVisit.id.is_(visit_id))
    result = await session.execute(stmt)
    visit = result.scalars().first()

    if not visit:
        raise HTTPException(status_code=404, detail="Monitoring visit not found")

    if visit.status != "COMPLETED":
        raise HTTPException(
            status_code=400,
            detail="Only completed monitoring visits can be signed off.",
        )

    visit.status = "SIGNED_OFF"
    visit.version_index += 1
    visit.reason_for_change = change_reason
    session.add(visit)
    await session.flush()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="SIGN_OFF_VISIT",
        details=f"Monitor supervisory sign-off recorded for visit '{visit.id}'.",
    )

    return MonitoringVisitResponse(
        id=visit.id,
        study_id=visit.study_id,
        site_id=visit.site_id,
        cra_id=visit.cra_id,
        visit_type=visit.visit_type,
        scheduled_date=visit.scheduled_date.isoformat(),
        actual_date=visit.actual_date.isoformat() if visit.actual_date else None,
        status=visit.status,
        created_at=visit.created_at.isoformat(),
        created_by=visit.created_by,
        reason_for_change=visit.reason_for_change,
        version_index=visit.version_index,
    )


@router.post(
    "/recruitment",
    response_model=RecruitmentRecordResponse,
    status_code=201,
)
async def record_recruitment(
    request: Request,
    payload: RecruitmentRecordCreate,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> RecruitmentRecordResponse:
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)
    change_reason = principal.change_reason or "system_operation"

    if not has_permission(principal, "ctms_recruitment:create"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    as_of = payload.as_of_date or datetime.now(UTC)

    record = RecruitmentRecord(
        site_id=payload.site_id,
        study_id=payload.study_id,
        screened_count=payload.screened_count,
        enrolled_count=payload.enrolled_count,
        target_count=payload.target_count,
        as_of_date=as_of,
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=1,
    )
    session.add(record)
    await session.flush()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="CREATE_RECRUITMENT_RECORD",
        details=f"Recorded recruitment metrics for study '{payload.study_id}' at site '{payload.site_id}': screened={payload.screened_count}, enrolled={payload.enrolled_count}, target={payload.target_count}.",
    )

    return RecruitmentRecordResponse(
        id=record.id,
        site_id=record.site_id,
        study_id=record.study_id,
        screened_count=record.screened_count,
        enrolled_count=record.enrolled_count,
        target_count=record.target_count,
        as_of_date=record.as_of_date.isoformat(),
        created_at=record.created_at.isoformat(),
        created_by=record.created_by,
        reason_for_change=record.reason_for_change,
        version_index=record.version_index,
    )


@router.get(
    "/recruitment",
    response_model=list[RecruitmentRecordResponse],
)
async def list_recruitment_records(
    request: Request,
    study_id: str | None = None,
    site_id: str | None = None,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> list[RecruitmentRecordResponse]:
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "ctms_recruitment:read"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    stmt = select(RecruitmentRecord)
    if study_id:
        stmt = stmt.where(RecruitmentRecord.study_id.is_(study_id))
    if site_id:
        stmt = stmt.where(RecruitmentRecord.site_id.is_(site_id))

    stmt = stmt.order_by(RecruitmentRecord.as_of_date.desc())
    result = await session.execute(stmt)
    records = result.scalars().all()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="LIST_RECRUITMENT_RECORDS",
        details="Listed recruitment records.",
    )

    return [
        RecruitmentRecordResponse(
            id=r.id,
            site_id=r.site_id,
            study_id=r.study_id,
            screened_count=r.screened_count,
            enrolled_count=r.enrolled_count,
            target_count=r.target_count,
            as_of_date=r.as_of_date.isoformat(),
            created_at=r.created_at.isoformat(),
            created_by=r.created_by,
            reason_for_change=r.reason_for_change,
            version_index=r.version_index,
        )
        for r in records
    ]


@router.post(
    "/site-milestones",
    response_model=SiteMilestoneResponse,
    status_code=201,
)
async def create_site_milestone(
    request: Request,
    payload: SiteMilestoneCreate,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> SiteMilestoneResponse:
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)
    change_reason = principal.change_reason or "system_operation"

    if not has_permission(principal, "ctms_site_milestone:create"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    milestone = SiteMilestone(
        site_id=payload.site_id,
        study_id=payload.study_id,
        milestone_type=payload.milestone_type,
        planned_date=payload.planned_date,
        actual_date=payload.actual_date,
        status=payload.status or "PLANNED",
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=1,
    )
    session.add(milestone)
    await session.flush()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="CREATE_MILESTONE",
        details=f"Created milestone '{payload.milestone_type}' for site '{payload.site_id}' in study '{payload.study_id}'.",
    )

    return SiteMilestoneResponse(
        id=milestone.id,
        site_id=milestone.site_id,
        study_id=milestone.study_id,
        milestone_type=milestone.milestone_type,
        planned_date=(
            milestone.planned_date.isoformat() if milestone.planned_date else None
        ),
        actual_date=(
            milestone.actual_date.isoformat() if milestone.actual_date else None
        ),
        status=milestone.status,
        created_at=milestone.created_at.isoformat(),
        created_by=milestone.created_by,
        reason_for_change=milestone.reason_for_change,
        version_index=milestone.version_index,
    )


@router.put(
    "/site-milestones/{milestone_id}",
    response_model=SiteMilestoneResponse,
)
async def update_site_milestone(
    milestone_id: str,
    request: Request,
    payload: SiteMilestoneUpdate,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> SiteMilestoneResponse:
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)
    change_reason = principal.change_reason or "system_operation"

    if not has_permission(principal, "ctms_site_milestone:update"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    stmt = select(SiteMilestone).where(SiteMilestone.id.is_(milestone_id))
    result = await session.execute(stmt)
    milestone = result.scalars().first()

    if not milestone:
        raise HTTPException(status_code=404, detail="Site milestone not found")

    if payload.planned_date is not None:
        milestone.planned_date = payload.planned_date
    if payload.actual_date is not None:
        milestone.actual_date = payload.actual_date
    if payload.status is not None:
        milestone.status = payload.status

    milestone.version_index += 1
    milestone.reason_for_change = change_reason
    session.add(milestone)
    await session.flush()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="UPDATE_MILESTONE",
        details=f"Updated site milestone '{milestone_id}' (type '{milestone.milestone_type}'). Status: '{milestone.status}'.",
    )

    return SiteMilestoneResponse(
        id=milestone.id,
        site_id=milestone.site_id,
        study_id=milestone.study_id,
        milestone_type=milestone.milestone_type,
        planned_date=(
            milestone.planned_date.isoformat() if milestone.planned_date else None
        ),
        actual_date=(
            milestone.actual_date.isoformat() if milestone.actual_date else None
        ),
        status=milestone.status,
        created_at=milestone.created_at.isoformat(),
        created_by=milestone.created_by,
        reason_for_change=milestone.reason_for_change,
        version_index=milestone.version_index,
    )


@router.get(
    "/site-milestones",
    response_model=list[SiteMilestoneResponse],
)
async def list_site_milestones(
    request: Request,
    study_id: str | None = None,
    site_id: str | None = None,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> list[SiteMilestoneResponse]:
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "ctms_site_milestone:read"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    stmt = select(SiteMilestone)
    if study_id:
        stmt = stmt.where(SiteMilestone.study_id.is_(study_id))
    if site_id:
        stmt = stmt.where(SiteMilestone.site_id.is_(site_id))

    stmt = stmt.order_by(SiteMilestone.created_at.desc())
    result = await session.execute(stmt)
    milestones = result.scalars().all()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="LIST_SITE_MILESTONES",
        details="Listed site milestones.",
    )

    return [
        SiteMilestoneResponse(
            id=m.id,
            site_id=m.site_id,
            study_id=m.study_id,
            milestone_type=m.milestone_type,
            planned_date=m.planned_date.isoformat() if m.planned_date else None,
            actual_date=m.actual_date.isoformat() if m.actual_date else None,
            status=m.status,
            created_at=m.created_at.isoformat(),
            created_by=m.created_by,
            reason_for_change=m.reason_for_change,
            version_index=m.version_index,
        )
        for m in milestones
    ]


@router.post(
    "/cra-allocations",
    response_model=CRAAllocationResponse,
    status_code=201,
)
async def allocate_cra(
    request: Request,
    payload: CRAAllocationCreate,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> CRAAllocationResponse:
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)
    change_reason = principal.change_reason or "system_operation"

    if not has_permission(principal, "ctms_cra_allocation:create"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    stmt = select(CRAAllocation).where(
        CRAAllocation.study_id.is_(payload.study_id),
        CRAAllocation.site_id.is_(payload.site_id),
        CRAAllocation.status.is_("ACTIVE"),
    )
    result = await session.execute(stmt)
    existing_active = result.scalars().all()

    start_date = payload.effective_start_date or datetime.now(UTC)

    for old_alloc in existing_active:
        old_alloc.status = "INACTIVE"
        old_alloc.effective_end_date = start_date
        old_alloc.version_index += 1
        old_alloc.reason_for_change = f"Reassigned CRA to {payload.cra_id}"
        session.add(old_alloc)
        await write_audit_log(
            session=session,
            user_id=user_id,
            user_role=user_roles,
            action="DEACTIVATE_CRA_ALLOCATION",
            details=f"Deactivated active allocation '{old_alloc.id}' for CRA '{old_alloc.cra_id}' at site '{payload.site_id}' in study '{payload.study_id}' due to reassignment.",
        )

    allocation = CRAAllocation(
        cra_id=payload.cra_id,
        site_id=payload.site_id,
        study_id=payload.study_id,
        status=payload.status or "ACTIVE",
        effective_start_date=start_date,
        effective_end_date=payload.effective_end_date,
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=1,
    )
    session.add(allocation)
    await session.flush()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="CREATE_CRA_ALLOCATION",
        details=f"Allocated CRA '{payload.cra_id}' to site '{payload.site_id}' in study '{payload.study_id}'. Status: '{allocation.status}'.",
    )

    return CRAAllocationResponse(
        id=allocation.id,
        cra_id=allocation.cra_id,
        site_id=allocation.site_id,
        study_id=allocation.study_id,
        status=allocation.status,
        effective_start_date=allocation.effective_start_date.isoformat(),
        effective_end_date=(
            allocation.effective_end_date.isoformat()
            if allocation.effective_end_date
            else None
        ),
        created_at=allocation.created_at.isoformat(),
        created_by=allocation.created_by,
        reason_for_change=allocation.reason_for_change,
        version_index=allocation.version_index,
    )


@router.put(
    "/cra-allocations/{allocation_id}",
    response_model=CRAAllocationResponse,
)
async def update_cra_allocation(
    allocation_id: str,
    request: Request,
    payload: CRAAllocationUpdate,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> CRAAllocationResponse:
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)
    change_reason = principal.change_reason or "system_operation"

    if not has_permission(principal, "ctms_cra_allocation:update"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    stmt = select(CRAAllocation).where(CRAAllocation.id.is_(allocation_id))
    result = await session.execute(stmt)
    allocation = result.scalars().first()

    if not allocation:
        raise HTTPException(status_code=404, detail="CRA Allocation not found")

    if payload.cra_id is not None:
        allocation.cra_id = payload.cra_id
    if payload.status is not None:
        allocation.status = payload.status
    if payload.effective_start_date is not None:
        allocation.effective_start_date = payload.effective_start_date
    if payload.effective_end_date is not None:
        allocation.effective_end_date = payload.effective_end_date

    allocation.version_index += 1
    allocation.reason_for_change = change_reason
    session.add(allocation)
    await session.flush()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="UPDATE_CRA_ALLOCATION",
        details=f"Updated CRA Allocation '{allocation_id}'. CRA: '{allocation.cra_id}', Status: '{allocation.status}'.",
    )

    return CRAAllocationResponse(
        id=allocation.id,
        cra_id=allocation.cra_id,
        site_id=allocation.site_id,
        study_id=allocation.study_id,
        status=allocation.status,
        effective_start_date=allocation.effective_start_date.isoformat(),
        effective_end_date=(
            allocation.effective_end_date.isoformat()
            if allocation.effective_end_date
            else None
        ),
        created_at=allocation.created_at.isoformat(),
        created_by=allocation.created_by,
        reason_for_change=allocation.reason_for_change,
        version_index=allocation.version_index,
    )


@router.get(
    "/cra-allocations",
    response_model=list[CRAAllocationResponse],
)
async def list_cra_allocations(
    request: Request,
    study_id: str | None = None,
    site_id: str | None = None,
    cra_id: str | None = None,
    status: str | None = None,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> list[CRAAllocationResponse]:
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "ctms_cra_allocation:read"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    stmt = select(CRAAllocation)
    if study_id:
        stmt = stmt.where(CRAAllocation.study_id.is_(study_id))
    if site_id:
        stmt = stmt.where(CRAAllocation.site_id.is_(site_id))
    if cra_id:
        stmt = stmt.where(CRAAllocation.cra_id.is_(cra_id))
    if status:
        stmt = stmt.where(CRAAllocation.status.is_(status))

    stmt = stmt.order_by(CRAAllocation.created_at.desc())
    result = await session.execute(stmt)
    allocations = result.scalars().all()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="LIST_CRA_ALLOCATIONS",
        details="Listed CRA allocations.",
    )

    return [
        CRAAllocationResponse(
            id=a.id,
            cra_id=a.cra_id,
            site_id=a.site_id,
            study_id=a.study_id,
            status=a.status,
            effective_start_date=a.effective_start_date.isoformat(),
            effective_end_date=(
                a.effective_end_date.isoformat() if a.effective_end_date else None
            ),
            created_at=a.created_at.isoformat(),
            created_by=a.created_by,
            reason_for_change=a.reason_for_change,
            version_index=a.version_index,
        )
        for a in allocations
    ]


@router.get(
    "/cra-allocations/workload",
    response_model=list[CRAWorkloadItem],
)
async def retrieve_workload_summaries(
    request: Request,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> list[CRAWorkloadItem]:
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "ctms_cra_workload:read"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    stmt = select(CRAAllocation).where(CRAAllocation.status.is_("ACTIVE"))
    result = await session.execute(stmt)
    active_allocations = result.scalars().all()

    cra_workload_map: dict[str, dict[str, Any]] = {}
    for alloc in active_allocations:
        cra_id = alloc.cra_id
        if cra_id not in cra_workload_map:
            cra_workload_map[cra_id] = {
                "active_allocations_count": 0,
                "allocated_sites": set(),
                "allocated_studies": set(),
            }

        cra_workload_map[cra_id]["active_allocations_count"] += 1
        cra_workload_map[cra_id]["allocated_sites"].add(alloc.site_id)
        cra_workload_map[cra_id]["allocated_studies"].add(alloc.study_id)

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="VIEW_WORKLOAD_SUMMARY",
        details="Accessed CRA workload summaries.",
    )

    return [
        CRAWorkloadItem(
            cra_id=cra_id,
            active_allocations_count=info["active_allocations_count"],
            allocated_sites=list(info["allocated_sites"]),
            allocated_studies=list(info["allocated_studies"]),
        )
        for cra_id, info in cra_workload_map.items()
    ]


@router.post(
    "/grants",
    response_model=InvestigatorGrantResponse,
    status_code=201,
)
async def create_grant(
    request: Request,
    payload: InvestigatorGrantCreate,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> InvestigatorGrantResponse:
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)
    change_reason = principal.change_reason or "system_operation"

    check_financial_write_roles(principal)

    grant = InvestigatorGrant(
        study_id=payload.study_id,
        site_id=payload.site_id,
        total_budget=payload.total_budget,
        currency=payload.currency or "USD",
        status="DRAFT",
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=1,
    )
    session.add(grant)
    await session.flush()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="CREATE_GRANT",
        details=f"Created grant for study '{payload.study_id}' site '{payload.site_id}' with budget {payload.total_budget} {grant.currency}.",
    )

    return InvestigatorGrantResponse(
        id=grant.id,
        study_id=grant.study_id,
        site_id=grant.site_id,
        total_budget=grant.total_budget,
        currency=grant.currency,
        status=grant.status,
        created_at=grant.created_at.isoformat(),
        created_by=grant.created_by,
        reason_for_change=grant.reason_for_change,
        version_index=grant.version_index,
    )


@router.get(
    "/grants",
    response_model=list[InvestigatorGrantResponse],
)
async def list_grants(
    request: Request,
    study_id: str | None = None,
    site_id: str | None = None,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> list[InvestigatorGrantResponse]:
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "ctms_financial:read"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    stmt = select(InvestigatorGrant)
    if study_id:
        stmt = stmt.where(InvestigatorGrant.study_id.is_(study_id))
    if site_id:
        stmt = stmt.where(InvestigatorGrant.site_id.is_(site_id))

    stmt = stmt.order_by(InvestigatorGrant.created_at.desc())
    result = await session.execute(stmt)
    grants = result.scalars().all()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="LIST_GRANTS",
        details="Listed investigator grants.",
    )

    return [
        InvestigatorGrantResponse(
            id=g.id,
            study_id=g.study_id,
            site_id=g.site_id,
            total_budget=g.total_budget,
            currency=g.currency,
            status=g.status,
            created_at=g.created_at.isoformat(),
            created_by=g.created_by,
            reason_for_change=g.reason_for_change,
            version_index=g.version_index,
        )
        for g in grants
    ]


@router.get(
    "/grants/{grant_id}",
    response_model=InvestigatorGrantResponse,
)
async def get_grant(
    grant_id: str,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> InvestigatorGrantResponse:
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "ctms_financial:read"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    stmt = select(InvestigatorGrant).where(InvestigatorGrant.id.is_(grant_id))
    result = await session.execute(stmt)
    grant = result.scalars().first()

    if not grant:
        raise HTTPException(status_code=404, detail="Investigator grant not found")

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="GET_GRANT",
        details=f"Retrieved grant '{grant_id}'.",
    )

    return InvestigatorGrantResponse(
        id=grant.id,
        study_id=grant.study_id,
        site_id=grant.site_id,
        total_budget=grant.total_budget,
        currency=grant.currency,
        status=grant.status,
        created_at=grant.created_at.isoformat(),
        created_by=grant.created_by,
        reason_for_change=grant.reason_for_change,
        version_index=grant.version_index,
    )


@router.put(
    "/grants/{grant_id}",
    response_model=InvestigatorGrantResponse,
)
async def update_grant(
    grant_id: str,
    request: Request,
    payload: InvestigatorGrantUpdate,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> InvestigatorGrantResponse:
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)
    change_reason = principal.change_reason or "system_operation"

    check_financial_write_roles(principal)

    stmt = select(InvestigatorGrant).where(InvestigatorGrant.id.is_(grant_id))
    result = await session.execute(stmt)
    grant = result.scalars().first()

    if not grant:
        raise HTTPException(status_code=404, detail="Investigator grant not found")

    check_grant_mutable(grant)

    trigger_approval = False
    if payload.total_budget is not None:
        grant.total_budget = payload.total_budget
    if payload.currency is not None:
        grant.currency = payload.currency
    if payload.status is not None:
        if payload.status.upper() == "APPROVED" and grant.status != "APPROVED":
            from packages.security.middleware import (
                downstream_replay_cache,
                verify_sig_token,
            )
            from packages.security.regulated_actions import SemanticAction

            sig_token = request.headers.get("X-Sig-Token") or request.headers.get(
                "x-sig-token"
            )
            secret = os.getenv(
                "GATEWAY_SECRET", "internal-gateway-secret-12345"
            ).encode()  # pragma: allowlist secret

            success, result_auth = verify_sig_token(
                sig_token=sig_token,
                user_id=principal.user_id,
                request_path=request.url.path,
                secret=secret,
                replay_cache=downstream_replay_cache,
                expected_semantic_action=SemanticAction.GRANT_APPROVE,
                check_replay=False,
            )
            if not success:
                raise HTTPException(status_code=401, detail="REAUTHENTICATION_REQUIRED")
            grant.status = "APPROVED"
            trigger_approval = True
        else:
            grant.status = payload.status

    grant.version_index += 1
    grant.reason_for_change = change_reason
    session.add(grant)
    await session.flush()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="UPDATE_GRANT",
        details=f"Updated grant '{grant_id}'. New Status: '{grant.status}'. Budget: {grant.total_budget}.",
    )

    if trigger_approval:
        await evaluate_milestones_for_grant(
            session=session,
            grant_id=grant.id,
            condition="STUDY_APPROVED",
            user_id=user_id,
            change_reason="Automated trigger on Study Approved milestone",
        )

    return InvestigatorGrantResponse(
        id=grant.id,
        study_id=grant.study_id,
        site_id=grant.site_id,
        total_budget=grant.total_budget,
        currency=grant.currency,
        status=grant.status,
        created_at=grant.created_at.isoformat(),
        created_by=grant.created_by,
        reason_for_change=grant.reason_for_change,
        version_index=grant.version_index,
    )


@router.post(
    "/grants/{grant_id}/budget-items",
    response_model=BudgetLineItemResponse,
    status_code=201,
)
async def create_budget_line_item(
    grant_id: str,
    request: Request,
    payload: BudgetLineItemCreate,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> BudgetLineItemResponse:
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)
    change_reason = principal.change_reason or "system_operation"

    check_financial_write_roles(principal)

    g_stmt = select(InvestigatorGrant).where(InvestigatorGrant.id.is_(grant_id))
    g_res = await session.execute(g_stmt)
    grant = g_res.scalars().first()

    if not grant:
        raise HTTPException(status_code=404, detail="Investigator grant not found")

    check_grant_mutable(grant)

    item = BudgetLineItem(
        grant_id=grant_id,
        category=payload.category,
        description=payload.description,
        amount=payload.amount,
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=1,
    )
    session.add(item)
    await session.flush()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="CREATE_BUDGET_ITEM",
        details=f"Created budget line item '{payload.category}' for grant '{grant_id}' with amount {payload.amount}.",
    )

    return BudgetLineItemResponse(
        id=item.id,
        grant_id=item.grant_id,
        category=item.category,
        description=item.description,
        amount=item.amount,
        created_at=item.created_at.isoformat(),
        created_by=item.created_by,
        reason_for_change=item.reason_for_change,
        version_index=item.version_index,
    )


@router.get(
    "/grants/{grant_id}/budget-items",
    response_model=list[BudgetLineItemResponse],
)
async def list_budget_line_items(
    grant_id: str,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> list[BudgetLineItemResponse]:
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "ctms_financial_budget:read"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    g_stmt = select(InvestigatorGrant).where(InvestigatorGrant.id.is_(grant_id))
    g_res = await session.execute(g_stmt)
    if not g_res.scalars().first():
        raise HTTPException(status_code=404, detail="Investigator grant not found")

    stmt = (
        select(BudgetLineItem)
        .where(BudgetLineItem.grant_id.is_(grant_id))
        .order_by(BudgetLineItem.created_at.desc())
    )
    result = await session.execute(stmt)
    items = result.scalars().all()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="LIST_BUDGET_ITEMS",
        details=f"Listed budget items for grant '{grant_id}'.",
    )

    return [
        BudgetLineItemResponse(
            id=item.id,
            grant_id=item.grant_id,
            category=item.category,
            description=item.description,
            amount=item.amount,
            created_at=item.created_at.isoformat(),
            created_by=item.created_by,
            reason_for_change=item.reason_for_change,
            version_index=item.version_index,
        )
        for item in items
    ]


@router.post(
    "/grants/{grant_id}/milestones",
    response_model=PaymentMilestoneResponse,
    status_code=201,
)
async def create_payment_milestone(
    grant_id: str,
    request: Request,
    payload: PaymentMilestoneCreate,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> PaymentMilestoneResponse:
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)
    change_reason = principal.change_reason or "system_operation"

    check_financial_write_roles(principal)

    g_stmt = select(InvestigatorGrant).where(InvestigatorGrant.id.is_(grant_id))
    g_res = await session.execute(g_stmt)
    grant = g_res.scalars().first()

    if not grant:
        raise HTTPException(status_code=404, detail="Investigator grant not found")

    check_grant_mutable(grant)

    tc = payload.trigger_condition.upper()
    if tc not in ("VISIT_COMPLETED", "STUDY_APPROVED", "MANUAL"):
        raise HTTPException(
            status_code=400,
            detail=f"Invalid trigger condition: {payload.trigger_condition}",
        )

    ms = PaymentMilestone(
        grant_id=grant_id,
        milestone_name=payload.milestone_name,
        trigger_condition=tc,
        amount=payload.amount,
        is_triggered=False,
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=1,
    )
    session.add(ms)
    await session.flush()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="CREATE_PAYMENT_MILESTONE",
        details=f"Created payment milestone '{payload.milestone_name}' for grant '{grant_id}' with trigger condition '{tc}' and amount {payload.amount}.",
    )

    return PaymentMilestoneResponse(
        id=ms.id,
        grant_id=ms.grant_id,
        milestone_name=ms.milestone_name,
        trigger_condition=ms.trigger_condition,
        amount=ms.amount,
        is_triggered=ms.is_triggered,
        triggered_at=ms.triggered_at.isoformat() if ms.triggered_at else None,
        created_at=ms.created_at.isoformat(),
        created_by=ms.created_by,
        reason_for_change=ms.reason_for_change,
        version_index=ms.version_index,
    )


@router.get(
    "/grants/{grant_id}/milestones",
    response_model=list[PaymentMilestoneResponse],
)
async def list_payment_milestones(
    grant_id: str,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> list[PaymentMilestoneResponse]:
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "ctms_financial_milestone:read"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    g_stmt = select(InvestigatorGrant).where(InvestigatorGrant.id.is_(grant_id))
    g_res = await session.execute(g_stmt)
    if not g_res.scalars().first():
        raise HTTPException(status_code=404, detail="Investigator grant not found")

    stmt = (
        select(PaymentMilestone)
        .where(PaymentMilestone.grant_id.is_(grant_id))
        .order_by(PaymentMilestone.created_at.desc())
    )
    result = await session.execute(stmt)
    milestones = result.scalars().all()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="LIST_PAYMENT_MILESTONES",
        details=f"Listed payment milestones for grant '{grant_id}'.",
    )

    return [
        PaymentMilestoneResponse(
            id=ms.id,
            grant_id=ms.grant_id,
            milestone_name=ms.milestone_name,
            trigger_condition=ms.trigger_condition,
            amount=ms.amount,
            is_triggered=ms.is_triggered,
            triggered_at=ms.triggered_at.isoformat() if ms.triggered_at else None,
            created_at=ms.created_at.isoformat(),
            created_by=ms.created_by,
            reason_for_change=ms.reason_for_change,
            version_index=ms.version_index,
        )
        for ms in milestones
    ]


@router.post(
    "/grants/{grant_id}/milestones/{milestone_id}/trigger",
    response_model=PaymentMilestoneResponse,
)
async def trigger_manual_milestone(
    grant_id: str,
    milestone_id: str,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> PaymentMilestoneResponse:
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)
    change_reason = principal.change_reason or "system_operation"

    check_financial_write_roles(principal)

    g_stmt = select(InvestigatorGrant).where(InvestigatorGrant.id.is_(grant_id))
    g_res = await session.execute(g_stmt)
    grant = g_res.scalars().first()

    if not grant:
        raise HTTPException(status_code=404, detail="Investigator grant not found")

    stmt = select(PaymentMilestone).where(
        PaymentMilestone.id.is_(milestone_id), PaymentMilestone.grant_id.is_(grant_id)
    )
    result = await session.execute(stmt)
    ms = result.scalars().first()

    if not ms:
        raise HTTPException(status_code=404, detail="Payment milestone not found")

    if ms.trigger_condition != "MANUAL":
        raise HTTPException(
            status_code=400,
            detail=f"Only MANUAL milestones can be manually triggered via this endpoint. Milestone has condition: {ms.trigger_condition}",
        )

    if not ms.is_triggered:
        if grant.status != "APPROVED":
            raise HTTPException(
                status_code=400,
                detail="Milestones can only be triggered on approved grants.",
            )

        ms.is_triggered = True
        ms.triggered_at = datetime.now(UTC)
        ms.version_index += 1
        ms.reason_for_change = change_reason
        session.add(ms)

        p_stmt = select(InvestigatorPayable).where(
            InvestigatorPayable.grant_id.is_(grant_id),
            InvestigatorPayable.milestone_id.is_(ms.id),
        )
        p_res = await session.execute(p_stmt)
        existing_payable = p_res.scalars().first()

        if not existing_payable:
            payable = InvestigatorPayable(
                grant_id=grant_id,
                milestone_id=ms.id,
                amount=ms.amount,
                payment_status="PENDING",
                created_by=user_id,
                reason_for_change=change_reason,
                version_index=1,
            )
            session.add(payable)

            await write_audit_log(
                session=session,
                user_id=user_id,
                user_role=user_roles,
                action="MANUAL_TRIGGER_MILESTONE",
                details=f"Manually triggered milestone '{ms.milestone_name}' ({ms.id}) for grant '{grant_id}'. Created pending payable of {ms.amount} {grant.currency}.",
            )

        await session.flush()

    return PaymentMilestoneResponse(
        id=ms.id,
        grant_id=ms.grant_id,
        milestone_name=ms.milestone_name,
        trigger_condition=ms.trigger_condition,
        amount=ms.amount,
        is_triggered=ms.is_triggered,
        triggered_at=ms.triggered_at.isoformat() if ms.triggered_at else None,
        created_at=ms.created_at.isoformat(),
        created_by=ms.created_by,
        reason_for_change=ms.reason_for_change,
        version_index=ms.version_index,
    )


@router.post(
    "/grants/{grant_id}/evaluate",
    status_code=200,
)
async def evaluate_grant_milestones(
    grant_id: str,
    request: Request,
    condition: str = "STUDY_APPROVED",
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> dict:
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)
    change_reason = principal.change_reason or "system_operation"

    check_financial_write_roles(principal)

    g_stmt = select(InvestigatorGrant).where(InvestigatorGrant.id.is_(grant_id))
    g_res = await session.execute(g_stmt)
    grant = g_res.scalars().first()

    if not grant:
        raise HTTPException(status_code=404, detail="Investigator grant not found")

    await evaluate_milestones_for_grant(
        session=session,
        grant_id=grant_id,
        condition=condition,
        user_id=user_id,
        change_reason=change_reason,
    )

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="EVALUATE_MILESTONES",
        details=f"Evaluated milestones for grant '{grant_id}' under condition '{condition}'.",
    )

    return {
        "status": "success",
        "message": f"Milestone evaluation executed for condition: {condition}",
    }


@router.get(
    "/grants/{grant_id}/payables",
    response_model=list[InvestigatorPayableResponse],
)
async def list_investigator_payables(
    grant_id: str,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> list[InvestigatorPayableResponse]:
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "ctms_financial_payable:read"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    g_stmt = select(InvestigatorGrant).where(InvestigatorGrant.id.is_(grant_id))
    g_res = await session.execute(g_stmt)
    if not g_res.scalars().first():
        raise HTTPException(status_code=404, detail="Investigator grant not found")

    stmt = (
        select(InvestigatorPayable)
        .where(InvestigatorPayable.grant_id.is_(grant_id))
        .order_by(InvestigatorPayable.created_at.desc())
    )
    result = await session.execute(stmt)
    payables = result.scalars().all()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="LIST_PAYABLES",
        details=f"Listed payables for grant '{grant_id}'.",
    )

    return [
        InvestigatorPayableResponse(
            id=p.id,
            grant_id=p.grant_id,
            milestone_id=p.milestone_id,
            amount=p.amount,
            payment_status=p.payment_status,
            due_date=p.due_date.isoformat() if p.due_date else None,
            paid_at=p.paid_at.isoformat() if p.paid_at else None,
            created_at=p.created_at.isoformat(),
            created_by=p.created_by,
            reason_for_change=p.reason_for_change,
            version_index=p.version_index,
        )
        for p in payables
    ]


async def process_visit_sync(
    session: AsyncSession, payload: MonitoringVisitOfflineSync, principal: Principal
) -> dict[str, Any]:
    visit_stmt = select(MonitoringVisit).where(MonitoringVisit.id.is_(payload.visit_id))
    visit_res = await session.execute(visit_stmt)
    visit = visit_res.scalars().first()

    if not visit:
        query = CTMSClinicalQuery(
            study_id=payload.study_id or "UNKNOWN",
            site_id=payload.site_id or "UNKNOWN",
            visit_id=payload.visit_id,
            status="OPEN",
            explanation=f"Structural conflict: target record (MonitoringVisit) is missing or deleted for Visit {payload.visit_id}.",
            created_by=principal.user_id,
            reason_for_change="SYSTEM SYNC EXCEPTION TRIGGERED",
            version_index=1,
        )
        session.add(query)

        defeated = MonitoringVisitDefeated(
            visit_id=payload.visit_id,
            actual_date=payload.actual_date,
            findings={"findings": [f.model_dump() for f in payload.findings]},
            device_timestamp=payload.device_timestamp,
            offline_sync_markers=payload.offline_sync_markers.model_dump(mode="json"),
            status="Defeated by online-merge conflict resolution",
        )
        session.add(defeated)
        await session.flush()

        await write_audit_log(
            session=session,
            user_id=principal.user_id,
            user_role=",".join(principal.raw_roles),
            action="MONITORING_VISIT_STRUCTURAL_CONFLICT",
            details=f"Structural conflict on Visit '{payload.visit_id}': Target record missing or deleted. Reason: SYSTEM SYNC EXCEPTION TRIGGERED",
        )

        return {
            "status": "STRUCTURAL_CONFLICT",
            "query": {
                "id": query.id,
                "study_id": query.study_id,
                "site_id": query.site_id,
                "visit_id": query.visit_id,
                "status": query.status,
                "explanation": query.explanation,
            },
            "signature_validation": {
                "status": "SKIPPED",
                "detail": None,
            },
            "reconciliation_result": {
                "status": "STRUCTURAL_CONFLICT",
                "metadata": None,
            },
            "audit_details": {
                "action": "MONITORING_VISIT_STRUCTURAL_CONFLICT",
                "details": f"Structural conflict on Visit '{payload.visit_id}'.",
            },
        }

    from packages.security.rbac import can_access_site

    if not (
        principal.user_id == visit.cra_id or can_access_site(principal, visit.site_id)
    ):
        raise HTTPException(
            status_code=403,
            detail=f"Forbidden: Submitting principal '{principal.user_id}' does not match CRA '{visit.cra_id}' or Site '{visit.site_id}' allocation.",
        )

    if visit.offline_sync_markers:
        v_markers = visit.offline_sync_markers
        if (
            v_markers.get("client_id") == payload.offline_sync_markers.client_id
            and v_markers.get("sequence_number")
            == payload.offline_sync_markers.sequence_number
        ):
            return {
                "status": "DUPLICATE_IGNORED",
                "id": visit.id,
                "actual_date": visit.actual_date.isoformat()
                if visit.actual_date
                else None,
                "sync_status": visit.sync_status,
                "version_index": visit.version_index,
                "signature_validation": {
                    "status": "SKIPPED",
                    "detail": None,
                },
                "reconciliation_result": {
                    "status": "DUPLICATE_IGNORED",
                    "metadata": None,
                },
                "audit_details": {
                    "action": "MONITORING_VISIT_RECONCILE",
                    "details": "Exact duplicate sync payload ignored",
                },
            }

    incoming_timestamps = payload.offline_sync_markers.timestamps or {}
    timestamps = {}
    for k in ["actual_date"]:
        t_val = incoming_timestamps.get(k)
        if t_val:
            if isinstance(t_val, str):
                timestamps[k] = datetime.fromisoformat(t_val)
            else:
                timestamps[k] = t_val
        else:
            timestamps[k] = payload.device_timestamp

    metadata = CTMSSyncMetadataDTO(
        timestamps=timestamps,
        modified_by=payload.offline_sync_markers.client_id,
        signature=payload.offline_sync_markers.signature,
    )

    study_id = visit.study_id
    site_id = visit.site_id
    dedup_key = f"{study_id}:{site_id}:{payload.visit_id}"

    incoming_record = CTMSSyncRecordDTO(
        deduplication_key=dedup_key,
        data={"actual_date": payload.actual_date.isoformat()},
        metadata=metadata,
    )

    gateway_secret_str = os.getenv(
        "GATEWAY_SECRET", default="internal-gateway-secret-12345"
    )
    secret_bytes = gateway_secret_str.encode("utf-8")

    signature_status = "SKIPPED"
    signature_detail = None

    if payload.offline_sync_markers.signature is not None:
        try:
            if not verify_ctms_record_signature(incoming_record, secret_bytes):
                raise CTMSSignatureValidationError(
                    "Invalid signature on the incoming record."
                )
            signature_status = "VALID"
        except CTMSSignatureValidationError as e:
            signature_status = "FAILED"
            signature_detail = str(e)
            raise HTTPException(status_code=400, detail=str(e))

    if visit.actual_date:
        existing_markers = visit.offline_sync_markers or {}
        existing_ts_raw = existing_markers.get("timestamps") or {}
        existing_timestamps = {}
        for k in ["actual_date"]:
            t_val = existing_ts_raw.get(k)
            if t_val:
                if isinstance(t_val, str):
                    existing_timestamps[k] = datetime.fromisoformat(t_val)
                else:
                    existing_timestamps[k] = t_val
            else:
                existing_timestamps[k] = visit.actual_date or visit.created_at

        existing_metadata = CTMSSyncMetadataDTO(
            timestamps=existing_timestamps,
            modified_by=existing_markers.get("client_id", "server"),
            signature=existing_markers.get("signature"),
        )
        existing_data = {"actual_date": visit.actual_date.isoformat()}
    else:
        existing_data = {}
        existing_metadata = None

    strategy = payload.offline_sync_markers.conflict_strategy
    if isinstance(strategy, ConflictStrategy):
        strategy = strategy.value
    strategy = str(strategy).upper()

    res = reconcile_ctms_records(
        existing_data=existing_data,
        existing_metadata=existing_metadata,
        incoming_record=incoming_record,
        strategy=strategy,
        secret=secret_bytes,
        require_signature=False,
    )

    status = res.status
    reconciled_metadata = res.metadata

    markers_dict = payload.offline_sync_markers.model_dump(mode="json")
    markers_dict["timestamps"] = {
        k: v.isoformat() if isinstance(v, datetime) else str(v)
        for k, v in reconciled_metadata.timestamps.items()
    }
    if reconciled_metadata.signature:
        markers_dict["signature"] = reconciled_metadata.signature

    change_reason = principal.change_reason or "SYSTEM OFFLINE SYNC"

    if status in ("UPDATED_CLIENT_WINS", "MERGED", "CREATED"):
        if status in ("UPDATED_CLIENT_WINS", "MERGED"):
            existing_findings_stmt = select(MonitoringVisitFinding).where(
                MonitoringVisitFinding.visit_id.is_(visit.id)
            )
            existing_findings_res = await session.execute(existing_findings_stmt)
            existing_findings = existing_findings_res.scalars().all()
            findings_list = [
                {
                    "text": f.text,
                    "severity": f.severity,
                    "resolution_status": f.resolution_status,
                    "offline_sync_markers": f.offline_sync_markers,
                }
                for f in existing_findings
            ]

            defeated = MonitoringVisitDefeated(
                visit_id=visit.id,
                actual_date=visit.actual_date,
                findings={"findings": findings_list},
                device_timestamp=visit.actual_date or visit.created_at,
                offline_sync_markers=visit.offline_sync_markers or {},
                status="Defeated by online-merge conflict resolution",
            )
            session.add(defeated)

        winning_date_str = res.data["actual_date"]
        visit.actual_date = datetime.fromisoformat(winning_date_str)
        visit.status = "COMPLETED"
        visit.offline_sync_markers = markers_dict
        visit.sync_status = "RESOLVED"
        visit.version_index += 1
        visit.reason_for_change = change_reason
        session.add(visit)

        for f in payload.findings:
            f_stmt = select(MonitoringVisitFinding).where(
                MonitoringVisitFinding.visit_id.is_(visit.id),
                MonitoringVisitFinding.text.is_(f.text),
            )
            f_res = await session.execute(f_stmt)
            existing_f = f_res.scalars().first()

            if not existing_f:
                new_finding = MonitoringVisitFinding(
                    visit_id=visit.id,
                    text=f.text,
                    severity=f.severity.upper(),
                    resolution_status=f.resolution_status or "OPEN",
                    created_by=principal.user_id,
                    reason_for_change=change_reason,
                    version_index=1,
                    offline_sync_markers=markers_dict,
                    sync_status="RESOLVED",
                )
                session.add(new_finding)

        await session.flush()

        audit_details = f"Decision: {status}. Strategy applied: {strategy}. Version incremented to {visit.version_index}. Reason: {change_reason}"
        await write_audit_log(
            session=session,
            user_id=principal.user_id,
            user_role=",".join(principal.raw_roles),
            action="MONITORING_VISIT_RECONCILE",
            details=audit_details,
        )

    elif status == "IGNORED_SERVER_WINS":
        defeated = MonitoringVisitDefeated(
            visit_id=payload.visit_id,
            actual_date=payload.actual_date,
            findings={"findings": [f.model_dump() for f in payload.findings]},
            device_timestamp=payload.device_timestamp,
            offline_sync_markers=payload.offline_sync_markers.model_dump(mode="json"),
            status="Defeated by online-merge conflict resolution",
        )
        session.add(defeated)
        await session.flush()

        audit_details = f"Decision: SERVER_WINS. Strategy applied: {strategy}. Version index is {visit.version_index}. Reason: {change_reason}"
        await write_audit_log(
            session=session,
            user_id=principal.user_id,
            user_role=",".join(principal.raw_roles),
            action="MONITORING_VISIT_RECONCILE",
            details=audit_details,
        )

    return {
        "status": status,
        "id": visit.id,
        "actual_date": visit.actual_date.isoformat() if visit.actual_date else None,
        "sync_status": visit.sync_status,
        "version_index": visit.version_index,
        "signature_validation": {
            "status": signature_status,
            "detail": signature_detail,
        },
        "reconciliation_result": {
            "status": status,
            "metadata": reconciled_metadata.model_dump(mode="json"),
        },
        "audit_details": {
            "action": "MONITORING_VISIT_RECONCILE",
            "details": audit_details,
        },
    }


@router.post("/monitoring-visits/sync", status_code=200)
async def sync_monitoring_visit(
    request: Request,
    payload: MonitoringVisitOfflineSync,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> dict[str, Any]:
    if not has_permission(principal, "ctms_monitoring_visit:sync"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    return await process_visit_sync(session, payload, principal)


@router.post("/monitoring-visits/bulk-sync", status_code=200)
async def bulk_sync_monitoring_visits(
    request: Request,
    payloads: list[MonitoringVisitOfflineSync],
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> dict[str, Any]:
    if not has_permission(principal, "ctms_monitoring_visit:sync"):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    results = []
    for payload in payloads:
        res = await process_visit_sync(session, payload, principal)
        results.append(res)

    return {
        "status": "success",
        "processed_count": len(payloads),
        "results": results,
    }


class CTMSResupplyApprovalRequest(BaseModel):
    change_justification: str


class CTMSResupplyEventResponse(BaseModel):
    id: str
    study_id: str
    site_id: str
    kit_id: str
    requested_qty: int
    status: str
    triggered_at: datetime


@router.get("/resupply-events", response_model=list[CTMSResupplyEventResponse])
async def list_ctms_resupply_events(
    request: Request,
    study_id: str | None = None,
    site_id: str | None = None,
    status: str | None = None,
    principal: Principal = Depends(get_principal),
) -> list[CTMSResupplyEventResponse]:
    """List pending/all resupply events from downstream execution, applying site boundaries and manager-level role checks."""
    import httpx

    from packages.security.gateway_client import GatewayBaseClient
    from packages.security.rbac import can_access_site, can_access_study

    # Verification of manager-level roles
    def is_manager(p: Principal) -> bool:
        manager_roles = {"sponsor_dm", "admin", "sysadmin", "cra", "monitor"}
        if any(r in manager_roles for r in p.roles):
            return True
        raw_manager_roles = {
            "cra",
            "monitor",
            "clinical_research_associate",
            "clinicalresearchassociate",
            "sponsor_admin",
            "sponsoradmin",
            "admin",
            "sysadmin",
            "system_admin",
            "systemadmin",
        }
        for r in p.raw_roles:
            norm_r = r.strip().lower().replace(" ", "_")
            if norm_r in raw_manager_roles:
                return True
        return False

    if not is_manager(principal):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: User does not have manager-level permissions.",
        )

    if site_id and not can_access_site(principal, site_id):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied to site.")
    if study_id and not can_access_study(principal, study_id):
        raise HTTPException(
            status_code=403, detail="Forbidden: Access denied to study."
        )

    params = {}
    if study_id:
        params["study_id"] = study_id
    if site_id:
        params["site_id"] = site_id
    if status:
        params["status"] = status

    try:
        client = GatewayBaseClient(
            base_url=os.getenv("EXECUTION_URL") or "http://localhost:8002", timeout=5.0
        )
        response = await client.request(
            method="GET",
            path="/api/v1/execution/rtsm/resupply-events",
            user_id=principal.user_id,
            roles=",".join(principal.roles),
            change_reason="Query resupply events",
            params=params,
        )
    except (httpx.RequestError, httpx.TimeoutException) as e:
        raise HTTPException(
            status_code=502,
            detail=f"Failed to communicate with downstream execution service: {str(e)}",
        )

    if response.status_code != 200:
        raise HTTPException(
            status_code=response.status_code,
            detail=f"Downstream service returned error: {response.text}",
        )

    return response.json()


@router.post(
    "/resupply-events/{event_id}/confirm", response_model=CTMSResupplyEventResponse
)
async def approve_ctms_resupply_event(
    event_id: str,
    payload: CTMSResupplyApprovalRequest,
    principal: Principal = Depends(get_principal),
) -> CTMSResupplyEventResponse:
    """Approve resupply event, securely signing and delegating downstream."""
    import httpx

    from packages.security.gateway_client import GatewayBaseClient
    from packages.security.rbac import can_access_site, can_access_study

    change_justification = payload.change_justification
    if not change_justification or not change_justification.strip():
        raise HTTPException(
            status_code=400, detail="Change justification must not be empty."
        )

    # 1. Fetch resupply events to check permissions
    try:
        client = GatewayBaseClient(
            base_url=os.getenv("EXECUTION_URL") or "http://localhost:8002", timeout=5.0
        )
        response = await client.request(
            method="GET",
            path="/api/v1/execution/rtsm/resupply-events",
            user_id=principal.user_id,
            roles=",".join(principal.roles),
            change_reason="Fetch events for permission check",
        )
    except (httpx.RequestError, httpx.TimeoutException) as e:
        raise HTTPException(
            status_code=502,
            detail=f"Failed to communicate with downstream execution service: {str(e)}",
        )

    if response.status_code != 200:
        raise HTTPException(
            status_code=response.status_code,
            detail=f"Downstream service returned error: {response.text}",
        )

    events = response.json()
    event = next((ev for ev in events if ev["id"] == event_id), None)
    if not event:
        raise HTTPException(status_code=404, detail="Resupply event not found")

    site_id = event["site_id"]
    study_id = event["study_id"]

    def is_manager(p: Principal) -> bool:
        manager_roles = {"sponsor_dm", "admin", "sysadmin", "cra", "monitor"}
        if any(r in manager_roles for r in p.roles):
            return True
        raw_manager_roles = {
            "cra",
            "monitor",
            "clinical_research_associate",
            "clinicalresearchassociate",
            "sponsor_admin",
            "sponsoradmin",
            "admin",
            "sysadmin",
            "system_admin",
            "systemadmin",
        }
        for r in p.raw_roles:
            norm_r = r.strip().lower().replace(" ", "_")
            if norm_r in raw_manager_roles:
                return True
        return False

    if not is_manager(principal):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: User does not have manager-level permissions.",
        )

    if not can_access_site(principal, site_id):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied to site.")
    if not can_access_study(principal, study_id):
        raise HTTPException(
            status_code=403, detail="Forbidden: Access denied to study."
        )

    # 2. Delegate approval downstream
    try:
        response_approve = await client.request(
            method="POST",
            path=f"/api/v1/execution/rtsm/resupply-events/{event_id}/confirm",
            user_id=principal.user_id,
            roles=",".join(principal.roles),
            change_reason=change_justification,
            json={"change_justification": change_justification},
        )
    except (httpx.RequestError, httpx.TimeoutException) as e:
        raise HTTPException(
            status_code=502,
            detail=f"Failed to communicate with downstream execution service: {str(e)}",
        )

    if response_approve.status_code != 200:
        try:
            err_detail = response_approve.json().get("detail", response_approve.text)
        except Exception:
            err_detail = response_approve.text
        raise HTTPException(
            status_code=response_approve.status_code,
            detail=f"Downstream service returned error: {err_detail}",
        )

    return response_approve.json()


@router.post(
    "/resupply-events/{event_id}/reject", response_model=CTMSResupplyEventResponse
)
async def reject_ctms_resupply_event(
    event_id: str,
    payload: CTMSResupplyApprovalRequest,
    principal: Principal = Depends(get_principal),
) -> CTMSResupplyEventResponse:
    """Reject resupply event, securely signing and delegating downstream."""
    import httpx

    from packages.security.gateway_client import GatewayBaseClient
    from packages.security.rbac import can_access_site, can_access_study

    change_justification = payload.change_justification
    if not change_justification or not change_justification.strip():
        raise HTTPException(
            status_code=400, detail="Change justification must not be empty."
        )

    # 1. Fetch resupply events to check permissions
    try:
        client = GatewayBaseClient(
            base_url=os.getenv("EXECUTION_URL") or "http://localhost:8002", timeout=5.0
        )
        response = await client.request(
            method="GET",
            path="/api/v1/execution/rtsm/resupply-events",
            user_id=principal.user_id,
            roles=",".join(principal.roles),
            change_reason="Fetch events for permission check",
        )
    except (httpx.RequestError, httpx.TimeoutException) as e:
        raise HTTPException(
            status_code=502,
            detail=f"Failed to communicate with downstream execution service: {str(e)}",
        )

    if response.status_code != 200:
        raise HTTPException(
            status_code=response.status_code,
            detail=f"Downstream service returned error: {response.text}",
        )

    events = response.json()
    event = next((ev for ev in events if ev["id"] == event_id), None)
    if not event:
        raise HTTPException(status_code=404, detail="Resupply event not found")

    site_id = event["site_id"]
    study_id = event["study_id"]

    def is_manager(p: Principal) -> bool:
        manager_roles = {"sponsor_dm", "admin", "sysadmin", "cra", "monitor"}
        if any(r in manager_roles for r in p.roles):
            return True
        raw_manager_roles = {
            "cra",
            "monitor",
            "clinical_research_associate",
            "clinicalresearchassociate",
            "sponsor_admin",
            "sponsoradmin",
            "admin",
            "sysadmin",
            "system_admin",
            "systemadmin",
        }
        for r in p.raw_roles:
            norm_r = r.strip().lower().replace(" ", "_")
            if norm_r in raw_manager_roles:
                return True
        return False

    if not is_manager(principal):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: User does not have manager-level permissions.",
        )

    if not can_access_site(principal, site_id):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied to site.")
    if not can_access_study(principal, study_id):
        raise HTTPException(
            status_code=403, detail="Forbidden: Access denied to study."
        )

    # 2. Delegate rejection downstream
    try:
        response_reject = await client.request(
            method="POST",
            path=f"/api/v1/execution/rtsm/resupply-events/{event_id}/reject",
            user_id=principal.user_id,
            roles=",".join(principal.roles),
            change_reason=change_justification,
            json={"change_justification": change_justification},
        )
    except (httpx.RequestError, httpx.TimeoutException) as e:
        raise HTTPException(
            status_code=502,
            detail=f"Failed to communicate with downstream execution service: {str(e)}",
        )

    if response_reject.status_code != 200:
        try:
            err_detail = response_reject.json().get("detail", response_reject.text)
        except Exception:
            err_detail = response_reject.text
        raise HTTPException(
            status_code=response_reject.status_code,
            detail=f"Downstream service returned error: {err_detail}",
        )

    return response_reject.json()
