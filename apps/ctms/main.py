import os
from datetime import datetime
from typing import List, Optional

from fastapi import Depends, FastAPI, HTTPException, Request
from pydantic import BaseModel, Field
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.ctms.database import db_manager
from apps.ctms.models import (
    Base,
    BudgetLineItem,
    CRAAllocation,
    CTMSAuditLog,
    CTMSStudy,
    GeneratedLetter,
    InvestigatorGrant,
    InvestigatorPayable,
    MonitoringVisit,
    MonitoringVisitFinding,
    PaymentMilestone,
    RecruitmentRecord,
    SiteMilestone,
    write_audit_log,
)
from apps.ctms.rendering import render_confirmation_letter, render_follow_up_letter
from packages.database import DatabaseSessionDependency, get_relational_db_lifespan
from packages.security.middleware import GatewayAuthMiddleware

DATABASE_URL = os.getenv("CTMS_DATABASE_URL", "sqlite+aiosqlite:///:memory:")


app = FastAPI(
    title="Cadence Clinical - CTMS",
    version="0.1.0",
    lifespan=get_relational_db_lifespan(
        db_manager=db_manager,
        database_url=DATABASE_URL,
        base_metadata=Base.metadata,
    ),
)

# Enforce secure gateway authentication middleware
app.add_middleware(GatewayAuthMiddleware)


# Dependable to obtain database session
get_db_session = DatabaseSessionDependency(db_manager)


# Helper to check roles case-insensitively
def check_roles(roles_str: str, allowed_roles: List[str]) -> None:
    roles_list = [r.strip().lower() for r in roles_str.split(",") if r.strip()]
    if not any(r in allowed_roles for r in roles_list):
        raise HTTPException(
            status_code=403,
            detail=f"Forbidden: Access denied for roles: {roles_str}.",
        )


# Pydantic models for CTMS Study
class CTMSStudyCreate(BaseModel):
    study_id: str = Field(..., description="Unique clinical study ID")
    name: str = Field(..., description="Descriptive name of the clinical study")
    status: Optional[str] = Field("ACTIVE", description="Initial status of the study")


class CTMSStudyResponse(BaseModel):
    id: str
    study_id: str
    name: str
    status: str
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


class CTMSAuditLogResponse(BaseModel):
    id: str
    timestamp: str
    user_id: str
    user_role: str
    action: str
    details: str


# Pydantic models for CTMS Monitoring Visits
class MonitoringVisitCreate(BaseModel):
    study_id: str = Field(..., description="Study ID associated with the visit")
    site_id: str = Field(..., description="Site ID where the monitoring visit occurs")
    cra_id: str = Field(..., description="CRA performing the monitoring visit")
    visit_type: str = Field(
        ..., description="Type of monitoring visit (e.g. SIV, IMV, COV)"
    )
    scheduled_date: datetime = Field(
        ..., description="Scheduled date/time of the visit"
    )


class FindingCreate(BaseModel):
    text: str = Field(..., description="The observation or action item text")
    severity: str = Field(..., description="Finding severity (MINOR, MAJOR, CRITICAL)")
    resolution_status: Optional[str] = Field("OPEN", description="Resolution status")


class MonitoringVisitComplete(BaseModel):
    actual_date: datetime = Field(
        ..., description="Actual date/time when the visit was conducted"
    )
    findings: List[FindingCreate] = Field(
        default=[], description="List of recorded findings"
    )


class MonitoringVisitResponse(BaseModel):
    id: str
    study_id: str
    site_id: str
    cra_id: str
    visit_type: str
    scheduled_date: str
    actual_date: Optional[str]
    status: str
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


class MonitoringVisitFindingResponse(BaseModel):
    id: str
    visit_id: str
    text: str
    severity: str
    resolution_status: str
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


class GeneratedLetterResponse(BaseModel):
    id: str
    visit_id: str
    letter_type: str
    rendered_content: str
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


# Pydantic models for CTMS Recruitment Records
class RecruitmentRecordCreate(BaseModel):
    site_id: str = Field(..., description="Site ID being tracked")
    study_id: str = Field(..., description="Study ID associated with the site")
    screened_count: int = Field(0, description="Total number of screened subjects")
    enrolled_count: int = Field(0, description="Total number of enrolled subjects")
    target_count: int = Field(0, description="Target enrollment count")
    as_of_date: Optional[datetime] = Field(
        None, description="The date/time as of which metrics apply"
    )


class RecruitmentRecordResponse(BaseModel):
    id: str
    site_id: str
    study_id: str
    screened_count: int
    enrolled_count: int
    target_count: int
    as_of_date: str
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


# Pydantic models for CTMS Site Milestones
class SiteMilestoneCreate(BaseModel):
    site_id: str = Field(..., description="Site ID")
    study_id: str = Field(..., description="Study ID")
    milestone_type: str = Field(..., description="The type of milestone")
    planned_date: Optional[datetime] = Field(None, description="Planned milestone date")
    actual_date: Optional[datetime] = Field(None, description="Actual milestone date")
    status: Optional[str] = Field("PLANNED", description="Status of the milestone")


class SiteMilestoneUpdate(BaseModel):
    planned_date: Optional[datetime] = Field(None, description="Planned milestone date")
    actual_date: Optional[datetime] = Field(None, description="Actual milestone date")
    status: Optional[str] = Field(None, description="Status of the milestone")


class SiteMilestoneResponse(BaseModel):
    id: str
    site_id: str
    study_id: str
    milestone_type: str
    planned_date: Optional[str]
    actual_date: Optional[str]
    status: str
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


# Pydantic models for CTMS CRA Allocation & Workload
class CRAAllocationCreate(BaseModel):
    cra_id: str = Field(..., description="CRA ID being allocated")
    site_id: str = Field(..., description="Site ID")
    study_id: str = Field(..., description="Study ID")
    status: Optional[str] = Field("ACTIVE", description="Allocation status")
    effective_start_date: Optional[datetime] = Field(
        None, description="Effective start date"
    )
    effective_end_date: Optional[datetime] = Field(
        None, description="Effective end date"
    )


class CRAAllocationUpdate(BaseModel):
    cra_id: Optional[str] = Field(None, description="CRA ID being allocated")
    status: Optional[str] = Field(None, description="Allocation status")
    effective_start_date: Optional[datetime] = Field(
        None, description="Effective start date"
    )
    effective_end_date: Optional[datetime] = Field(
        None, description="Effective end date"
    )


class CRAAllocationResponse(BaseModel):
    id: str
    cra_id: str
    site_id: str
    study_id: str
    status: str
    effective_start_date: str
    effective_end_date: Optional[str]
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


class CRAWorkloadItem(BaseModel):
    cra_id: str
    active_allocations_count: int
    allocated_sites: List[str]
    allocated_studies: List[str]


# --- Investigator Grant & Financial Schemas ---
class BudgetLineItemCreate(BaseModel):
    category: str = Field(
        ..., description="Category of budget item (VISIT_COST, EQUIPMENT, etc.)"
    )
    description: str = Field(..., description="Description of budget line item")
    amount: float = Field(..., description="Budget item cost")


class BudgetLineItemResponse(BaseModel):
    id: str
    grant_id: str
    category: str
    description: str
    amount: float
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


class PaymentMilestoneCreate(BaseModel):
    milestone_name: str = Field(
        ..., description="Descriptive name of the payment milestone"
    )
    trigger_condition: str = Field(
        ..., description="Trigger condition (VISIT_COMPLETED, STUDY_APPROVED, MANUAL)"
    )
    amount: float = Field(
        ..., description="Payment amount associated with the milestone"
    )


class PaymentMilestoneResponse(BaseModel):
    id: str
    grant_id: str
    milestone_name: str
    trigger_condition: str
    amount: float
    is_triggered: bool
    triggered_at: Optional[str]
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


class InvestigatorGrantCreate(BaseModel):
    study_id: str = Field(..., description="Clinical study ID")
    site_id: str = Field(..., description="Site ID")
    total_budget: float = Field(
        0.0, description="Overall budget allocated for the site"
    )
    currency: Optional[str] = Field("USD", description="Currency code")


class InvestigatorGrantUpdate(BaseModel):
    total_budget: Optional[float] = Field(None, description="Updated budget")
    currency: Optional[str] = Field(None, description="Updated currency")
    status: Optional[str] = Field(None, description="Updated status (DRAFT, APPROVED)")


class InvestigatorGrantResponse(BaseModel):
    id: str
    study_id: str
    site_id: str
    total_budget: float
    currency: str
    status: str
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


class InvestigatorPayableResponse(BaseModel):
    id: str
    grant_id: str
    milestone_id: Optional[str]
    amount: float
    payment_status: str
    due_date: Optional[str]
    paid_at: Optional[str]
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


def check_financial_write_roles(roles_str: str) -> None:
    """Enforces that financial writes are restricted to Grants Manager or Sponsor Admin."""
    check_roles(roles_str, ["grants manager", "sponsor admin", "system"])


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
    """
    Deterministic, idempotent evaluation of payment milestones for a given condition.
    Triggers matching milestones and creates single InvestigatorPayable records.
    """
    # 1. Fetch the grant
    grant_stmt = select(InvestigatorGrant).where(InvestigatorGrant.id == grant_id)
    grant_res = await session.execute(grant_stmt)
    grant = grant_res.scalars().first()
    if not grant:
        return

    # Only approved grants can trigger payments!
    if grant.status != "APPROVED":
        return

    # 2. Fetch untriggered milestones with matching trigger_condition
    ms_stmt = select(PaymentMilestone).where(
        PaymentMilestone.grant_id == grant_id,
        PaymentMilestone.trigger_condition == condition.upper(),
        PaymentMilestone.is_triggered.is_(False),
    )
    ms_res = await session.execute(ms_stmt)
    milestones = ms_res.scalars().all()

    for ms in milestones:
        # Check if a payable already exists for this milestone to ensure idempotence
        p_stmt = select(InvestigatorPayable).where(
            InvestigatorPayable.grant_id == grant_id,
            InvestigatorPayable.milestone_id == ms.id,
        )
        p_res = await session.execute(p_stmt)
        existing_payable = p_res.scalars().first()

        if not existing_payable:
            # Trigger the milestone
            ms.is_triggered = True
            ms.triggered_at = datetime.utcnow()
            ms.version_index += 1
            ms.reason_for_change = f"Automated trigger on condition: {condition}"
            session.add(ms)

            # Create the payable
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


@app.get("/health")
async def health_check() -> dict[str, str]:
    """
    Service health check endpoint.
    """
    return {"status": "ok", "service": "ctms"}


@app.post("/api/v1/ctms/studies", response_model=CTMSStudyResponse, status_code=201)
async def create_study(
    request: Request,
    payload: CTMSStudyCreate,
    session: AsyncSession = Depends(get_db_session),
) -> CTMSStudyResponse:
    user_id = getattr(request.state, "user_id", "system")
    user_roles = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    # RBAC: Only write-privileged roles can manage CTMS studies
    check_roles(
        user_roles,
        ["monitor", "grants manager", "cra", "sponsor admin", "admin", "system"],
    )

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


@app.get("/api/v1/ctms/studies", response_model=List[CTMSStudyResponse])
async def list_studies(
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> List[CTMSStudyResponse]:
    user_id = getattr(request.state, "user_id", "anonymous")
    user_roles = getattr(request.state, "roles", "anonymous")

    check_roles(
        user_roles,
        [
            "monitor",
            "grants manager",
            "cra",
            "site investigator",
            "sponsor admin",
            "admin",
            "auditor",
            "anonymous",
            "system",
        ],
    )

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


@app.get("/api/v1/ctms/audit-logs", response_model=List[CTMSAuditLogResponse])
async def get_audit_trail(
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> List[CTMSAuditLogResponse]:
    user_id = getattr(request.state, "user_id", "anonymous")
    user_roles = getattr(request.state, "roles", "anonymous")

    check_roles(
        user_roles,
        ["auditor", "sponsor admin", "admin", "monitor", "grants manager", "system"],
    )

    # Log view action first
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


# --- Monitoring Visits Endpoints ---


@app.post(
    "/api/v1/ctms/monitoring-visits",
    response_model=MonitoringVisitResponse,
    status_code=201,
)
async def schedule_monitoring_visit(
    request: Request,
    payload: MonitoringVisitCreate,
    session: AsyncSession = Depends(get_db_session),
) -> MonitoringVisitResponse:
    """
    Schedules a clinical site monitoring visit and automatically generates/persists
    a corresponding confirmation letter.
    """
    user_id = getattr(request.state, "user_id", "system")
    user_roles = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    # RBAC: Only CRA (or admin/sponsor admin/system) can schedule/create visits
    check_roles(
        user_roles,
        ["cra", "admin", "sponsor admin", "system"],
    )

    # 1. Validate active CRA allocation for study and site if one exists
    alloc_stmt = select(CRAAllocation).where(
        CRAAllocation.study_id == payload.study_id,
        CRAAllocation.site_id == payload.site_id,
        CRAAllocation.status == "ACTIVE",
    )
    alloc_result = await session.execute(alloc_stmt)
    active_alloc = alloc_result.scalars().first()
    if active_alloc and active_alloc.cra_id != payload.cra_id:
        raise HTTPException(
            status_code=400,
            detail=f"CRA '{payload.cra_id}' is not allocated to site '{payload.site_id}' and study '{payload.study_id}'. Allocated CRA is '{active_alloc.cra_id}'.",
        )

    # 2. Persist the Monitoring Visit
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

    # 2. Render and Persist the Confirmation Letter
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

    # 3. Write state-changing CTMS audit logs
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


@app.post(
    "/api/v1/ctms/monitoring-visits/{visit_id}/complete",
    response_model=MonitoringVisitResponse,
)
async def complete_monitoring_visit(
    visit_id: str,
    request: Request,
    payload: MonitoringVisitComplete,
    session: AsyncSession = Depends(get_db_session),
) -> MonitoringVisitResponse:
    """
    Completes a scheduled monitoring visit, records findings and action items,
    and automatically generates/persists a follow-up letter.
    """
    user_id = getattr(request.state, "user_id", "system")
    user_roles = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    # RBAC: Only CRA (or admin/sponsor admin/system) can record/complete visit content
    check_roles(
        user_roles,
        ["cra", "admin", "sponsor admin", "system"],
    )

    # 1. Retrieve the scheduled visit
    stmt = select(MonitoringVisit).where(MonitoringVisit.id == visit_id)
    result = await session.execute(stmt)
    visit = result.scalars().first()

    if not visit:
        raise HTTPException(status_code=404, detail="Monitoring visit not found")

    if visit.status != "SCHEDULED":
        raise HTTPException(
            status_code=400,
            detail=f"Monitoring visit cannot be completed from state: {visit.status}",
        )

    # 2. Complete the visit
    visit.status = "COMPLETED"
    visit.actual_date = payload.actual_date
    visit.version_index += 1
    visit.reason_for_change = change_reason
    session.add(visit)

    # 3. Create and persist findings
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

    # 4. Render and persist the Follow-up Letter
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
        created_at=datetime.utcnow(),
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

    # 5. Write state-changing CTMS audit logs
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
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="GENERATE_LETTER",
        details=f"Generated follow-up letter for visit '{visit.id}'.",
    )

    # 6. Trigger automatic milestone evaluation for any approved grants matching study_id and site_id
    grants_stmt = select(InvestigatorGrant).where(
        InvestigatorGrant.study_id == visit.study_id,
        InvestigatorGrant.site_id == visit.site_id,
        InvestigatorGrant.status == "APPROVED",
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


@app.get(
    "/api/v1/ctms/monitoring-visits",
    response_model=List[MonitoringVisitResponse],
)
async def list_monitoring_visits(
    request: Request,
    study_id: Optional[str] = None,
    site_id: Optional[str] = None,
    cra_id: Optional[str] = None,
    status: Optional[str] = None,
    session: AsyncSession = Depends(get_db_session),
) -> List[MonitoringVisitResponse]:
    """
    Lists and filters clinical trial site monitoring visits.
    """
    user_id = getattr(request.state, "user_id", "anonymous")
    user_roles = getattr(request.state, "roles", "anonymous")

    # RBAC: Allow CRA, Monitor, Auditor, Admin, Sponsor Admin, System to list visits
    check_roles(
        user_roles,
        ["monitor", "cra", "admin", "sponsor admin", "auditor", "system"],
    )

    stmt = select(MonitoringVisit)
    if study_id:
        stmt = stmt.where(MonitoringVisit.study_id == study_id)
    if site_id:
        stmt = stmt.where(MonitoringVisit.site_id == site_id)
    if cra_id:
        stmt = stmt.where(MonitoringVisit.cra_id == cra_id)
    if status:
        stmt = stmt.where(MonitoringVisit.status == status)

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


@app.get(
    "/api/v1/ctms/monitoring-visits/{visit_id}/letters",
    response_model=List[GeneratedLetterResponse],
)
async def get_monitoring_visit_letters(
    visit_id: str,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> List[GeneratedLetterResponse]:
    """
    Retrieves all generated letters associated with a specific monitoring visit.
    Guarantees no re-rendering of previously issued letters by returning stored content.
    """
    user_id = getattr(request.state, "user_id", "anonymous")
    user_roles = getattr(request.state, "roles", "anonymous")

    check_roles(
        user_roles,
        ["monitor", "cra", "admin", "sponsor admin", "auditor", "system"],
    )

    stmt = select(GeneratedLetter).where(GeneratedLetter.visit_id == visit_id)
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


@app.get(
    "/api/v1/ctms/monitoring-visits/{visit_id}/letters/{letter_type}",
    response_model=GeneratedLetterResponse,
)
async def get_monitoring_visit_letter_by_type(
    visit_id: str,
    letter_type: str,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> GeneratedLetterResponse:
    """
    Retrieves a specific letter (e.g. CONFIRMATION or FOLLOW_UP) associated with a monitoring visit.
    Guarantees no re-rendering of previously issued letters by returning stored content.
    """
    user_id = getattr(request.state, "user_id", "anonymous")
    user_roles = getattr(request.state, "roles", "anonymous")

    check_roles(
        user_roles,
        ["monitor", "cra", "admin", "sponsor admin", "auditor", "system"],
    )

    stmt = select(GeneratedLetter).where(
        GeneratedLetter.visit_id == visit_id,
        GeneratedLetter.letter_type == letter_type.upper(),
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


@app.post(
    "/api/v1/ctms/monitoring-visits/{visit_id}/sign-off",
    response_model=MonitoringVisitResponse,
)
async def sign_off_monitoring_visit(
    visit_id: str,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> MonitoringVisitResponse:
    """
    Allows a clinical Monitor to perform a supervisory sign-off on a completed monitoring visit.
    """
    user_id = getattr(request.state, "user_id", "system")
    user_roles = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    # RBAC: Only supervisory roles (Monitor, Admin, Sponsor Admin, System) can perform sign-off
    check_roles(
        user_roles,
        ["monitor", "admin", "sponsor admin", "system"],
    )

    # 1. Retrieve the visit
    stmt = select(MonitoringVisit).where(MonitoringVisit.id == visit_id)
    result = await session.execute(stmt)
    visit = result.scalars().first()

    if not visit:
        raise HTTPException(status_code=404, detail="Monitoring visit not found")

    if visit.status != "COMPLETED":
        raise HTTPException(
            status_code=400,
            detail="Only completed monitoring visits can be signed off.",
        )

    # 2. Update visit status to SIGNED_OFF
    visit.status = "SIGNED_OFF"
    visit.version_index += 1
    visit.reason_for_change = change_reason
    session.add(visit)
    await session.flush()

    # 3. Write state-changing CTMS audit log
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


# --- Recruitment Records Endpoints ---


@app.post(
    "/api/v1/ctms/recruitment",
    response_model=RecruitmentRecordResponse,
    status_code=201,
)
async def record_recruitment(
    request: Request,
    payload: RecruitmentRecordCreate,
    session: AsyncSession = Depends(get_db_session),
) -> RecruitmentRecordResponse:
    """
    Record or update recruitment metrics for a site and study.
    """
    user_id = getattr(request.state, "user_id", "system")
    user_roles = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    check_roles(
        user_roles,
        ["cra", "monitor", "admin", "sponsor admin", "system"],
    )

    as_of = payload.as_of_date or datetime.utcnow()

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


@app.get(
    "/api/v1/ctms/recruitment",
    response_model=List[RecruitmentRecordResponse],
)
async def list_recruitment_records(
    request: Request,
    study_id: Optional[str] = None,
    site_id: Optional[str] = None,
    session: AsyncSession = Depends(get_db_session),
) -> List[RecruitmentRecordResponse]:
    """
    List recorded recruitment metrics, optionally filtered by site and/or study.
    """
    user_id = getattr(request.state, "user_id", "anonymous")
    user_roles = getattr(request.state, "roles", "anonymous")

    check_roles(
        user_roles,
        ["monitor", "cra", "admin", "sponsor admin", "auditor", "system", "anonymous"],
    )

    stmt = select(RecruitmentRecord)
    if study_id:
        stmt = stmt.where(RecruitmentRecord.study_id == study_id)
    if site_id:
        stmt = stmt.where(RecruitmentRecord.site_id == site_id)

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


# --- Site Milestones Endpoints ---


@app.post(
    "/api/v1/ctms/site-milestones",
    response_model=SiteMilestoneResponse,
    status_code=201,
)
async def create_site_milestone(
    request: Request,
    payload: SiteMilestoneCreate,
    session: AsyncSession = Depends(get_db_session),
) -> SiteMilestoneResponse:
    """
    Create a new site lifecycle milestone.
    """
    user_id = getattr(request.state, "user_id", "system")
    user_roles = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    check_roles(
        user_roles,
        ["cra", "monitor", "admin", "sponsor admin", "system"],
    )

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


@app.put(
    "/api/v1/ctms/site-milestones/{milestone_id}",
    response_model=SiteMilestoneResponse,
)
async def update_site_milestone(
    milestone_id: str,
    request: Request,
    payload: SiteMilestoneUpdate,
    session: AsyncSession = Depends(get_db_session),
) -> SiteMilestoneResponse:
    """
    Update site lifecycle milestones.
    """
    user_id = getattr(request.state, "user_id", "system")
    user_roles = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    check_roles(
        user_roles,
        ["cra", "monitor", "admin", "sponsor admin", "system"],
    )

    stmt = select(SiteMilestone).where(SiteMilestone.id == milestone_id)
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


@app.get(
    "/api/v1/ctms/site-milestones",
    response_model=List[SiteMilestoneResponse],
)
async def list_site_milestones(
    request: Request,
    study_id: Optional[str] = None,
    site_id: Optional[str] = None,
    session: AsyncSession = Depends(get_db_session),
) -> List[SiteMilestoneResponse]:
    """
    List site milestones, optionally filtered by site and/or study.
    """
    user_id = getattr(request.state, "user_id", "anonymous")
    user_roles = getattr(request.state, "roles", "anonymous")

    check_roles(
        user_roles,
        ["monitor", "cra", "admin", "sponsor admin", "auditor", "system", "anonymous"],
    )

    stmt = select(SiteMilestone)
    if study_id:
        stmt = stmt.where(SiteMilestone.study_id == study_id)
    if site_id:
        stmt = stmt.where(SiteMilestone.site_id == site_id)

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


# --- CRA Allocations Endpoints ---


@app.post(
    "/api/v1/ctms/cra-allocations",
    response_model=CRAAllocationResponse,
    status_code=201,
)
async def allocate_cra(
    request: Request,
    payload: CRAAllocationCreate,
    session: AsyncSession = Depends(get_db_session),
) -> CRAAllocationResponse:
    """
    Allocate or reallocate a CRA to a site and study.
    Restricted to Sponsor Admin.
    """
    user_id = getattr(request.state, "user_id", "system")
    user_roles = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    # Restrict CRA allocation writes strictly to Sponsor Admin
    check_roles(user_roles, ["sponsor admin"])

    # Reassignment logic: deactivate any existing active allocations for this study and site
    stmt = select(CRAAllocation).where(
        CRAAllocation.study_id == payload.study_id,
        CRAAllocation.site_id == payload.site_id,
        CRAAllocation.status == "ACTIVE",
    )
    result = await session.execute(stmt)
    existing_active = result.scalars().all()

    start_date = payload.effective_start_date or datetime.utcnow()

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


@app.put(
    "/api/v1/ctms/cra-allocations/{allocation_id}",
    response_model=CRAAllocationResponse,
)
async def update_cra_allocation(
    allocation_id: str,
    request: Request,
    payload: CRAAllocationUpdate,
    session: AsyncSession = Depends(get_db_session),
) -> CRAAllocationResponse:
    """
    Update or reassign an existing CRA allocation.
    Restricted to Sponsor Admin.
    """
    user_id = getattr(request.state, "user_id", "system")
    user_roles = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    check_roles(user_roles, ["sponsor admin"])

    stmt = select(CRAAllocation).where(CRAAllocation.id == allocation_id)
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


@app.get(
    "/api/v1/ctms/cra-allocations",
    response_model=List[CRAAllocationResponse],
)
async def list_cra_allocations(
    request: Request,
    study_id: Optional[str] = None,
    site_id: Optional[str] = None,
    cra_id: Optional[str] = None,
    status: Optional[str] = None,
    session: AsyncSession = Depends(get_db_session),
) -> List[CRAAllocationResponse]:
    """
    List CRA allocations, optionally filtered.
    """
    user_id = getattr(request.state, "user_id", "anonymous")
    user_roles = getattr(request.state, "roles", "anonymous")

    check_roles(
        user_roles,
        ["monitor", "cra", "admin", "sponsor admin", "auditor", "system", "anonymous"],
    )

    stmt = select(CRAAllocation)
    if study_id:
        stmt = stmt.where(CRAAllocation.study_id == study_id)
    if site_id:
        stmt = stmt.where(CRAAllocation.site_id == site_id)
    if cra_id:
        stmt = stmt.where(CRAAllocation.cra_id == cra_id)
    if status:
        stmt = stmt.where(CRAAllocation.status == status)

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


@app.get(
    "/api/v1/ctms/cra-allocations/workload",
    response_model=List[CRAWorkloadItem],
)
async def retrieve_workload_summaries(
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> List[CRAWorkloadItem]:
    """
    Retrieve workload summaries reflecting active CRA allocations.
    """
    user_id = getattr(request.state, "user_id", "anonymous")
    user_roles = getattr(request.state, "roles", "anonymous")

    check_roles(
        user_roles,
        ["monitor", "cra", "admin", "sponsor admin", "auditor", "system", "anonymous"],
    )

    # Fetch active allocations
    stmt = select(CRAAllocation).where(CRAAllocation.status == "ACTIVE")
    result = await session.execute(stmt)
    active_allocations = result.scalars().all()

    cra_workload_map = {}
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

    # Log action
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


# --- Investigator Grants Endpoints ---


@app.post(
    "/api/v1/ctms/grants",
    response_model=InvestigatorGrantResponse,
    status_code=201,
)
async def create_grant(
    request: Request,
    payload: InvestigatorGrantCreate,
    session: AsyncSession = Depends(get_db_session),
) -> InvestigatorGrantResponse:
    user_id = getattr(request.state, "user_id", "system")
    user_roles = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    check_financial_write_roles(user_roles)

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


@app.get(
    "/api/v1/ctms/grants",
    response_model=List[InvestigatorGrantResponse],
)
async def list_grants(
    request: Request,
    study_id: Optional[str] = None,
    site_id: Optional[str] = None,
    session: AsyncSession = Depends(get_db_session),
) -> List[InvestigatorGrantResponse]:
    user_id = getattr(request.state, "user_id", "anonymous")
    user_roles = getattr(request.state, "roles", "anonymous")

    check_roles(
        user_roles,
        [
            "monitor",
            "cra",
            "admin",
            "sponsor admin",
            "grants manager",
            "auditor",
            "system",
            "anonymous",
        ],
    )

    stmt = select(InvestigatorGrant)
    if study_id:
        stmt = stmt.where(InvestigatorGrant.study_id == study_id)
    if site_id:
        stmt = stmt.where(InvestigatorGrant.site_id == site_id)

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


@app.get(
    "/api/v1/ctms/grants/{grant_id}",
    response_model=InvestigatorGrantResponse,
)
async def get_grant(
    grant_id: str,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> InvestigatorGrantResponse:
    user_id = getattr(request.state, "user_id", "anonymous")
    user_roles = getattr(request.state, "roles", "anonymous")

    check_roles(
        user_roles,
        [
            "monitor",
            "cra",
            "admin",
            "sponsor admin",
            "grants manager",
            "auditor",
            "system",
            "anonymous",
        ],
    )

    stmt = select(InvestigatorGrant).where(InvestigatorGrant.id == grant_id)
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


@app.put(
    "/api/v1/ctms/grants/{grant_id}",
    response_model=InvestigatorGrantResponse,
)
async def update_grant(
    grant_id: str,
    request: Request,
    payload: InvestigatorGrantUpdate,
    session: AsyncSession = Depends(get_db_session),
) -> InvestigatorGrantResponse:
    user_id = getattr(request.state, "user_id", "system")
    user_roles = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    check_financial_write_roles(user_roles)

    stmt = select(InvestigatorGrant).where(InvestigatorGrant.id == grant_id)
    result = await session.execute(stmt)
    grant = result.scalars().first()

    if not grant:
        raise HTTPException(status_code=404, detail="Investigator grant not found")

    check_grant_mutable(grant)

    # Apply changes
    trigger_approval = False
    if payload.total_budget is not None:
        grant.total_budget = payload.total_budget
    if payload.currency is not None:
        grant.currency = payload.currency
    if payload.status is not None:
        if payload.status.upper() == "APPROVED" and grant.status != "APPROVED":
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


# --- Budget Line Items Endpoints ---


@app.post(
    "/api/v1/ctms/grants/{grant_id}/budget-items",
    response_model=BudgetLineItemResponse,
    status_code=201,
)
async def create_budget_line_item(
    grant_id: str,
    request: Request,
    payload: BudgetLineItemCreate,
    session: AsyncSession = Depends(get_db_session),
) -> BudgetLineItemResponse:
    user_id = getattr(request.state, "user_id", "system")
    user_roles = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    check_financial_write_roles(user_roles)

    # Fetch grant
    g_stmt = select(InvestigatorGrant).where(InvestigatorGrant.id == grant_id)
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


@app.get(
    "/api/v1/ctms/grants/{grant_id}/budget-items",
    response_model=List[BudgetLineItemResponse],
)
async def list_budget_line_items(
    grant_id: str,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> List[BudgetLineItemResponse]:
    user_id = getattr(request.state, "user_id", "anonymous")
    user_roles = getattr(request.state, "roles", "anonymous")

    check_roles(
        user_roles,
        [
            "monitor",
            "cra",
            "admin",
            "sponsor admin",
            "grants manager",
            "auditor",
            "system",
            "anonymous",
        ],
    )

    # Confirm grant exists
    g_stmt = select(InvestigatorGrant).where(InvestigatorGrant.id == grant_id)
    g_res = await session.execute(g_stmt)
    if not g_res.scalars().first():
        raise HTTPException(status_code=404, detail="Investigator grant not found")

    stmt = (
        select(BudgetLineItem)
        .where(BudgetLineItem.grant_id == grant_id)
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


# --- Payment Milestones Endpoints ---


@app.post(
    "/api/v1/ctms/grants/{grant_id}/milestones",
    response_model=PaymentMilestoneResponse,
    status_code=201,
)
async def create_payment_milestone(
    grant_id: str,
    request: Request,
    payload: PaymentMilestoneCreate,
    session: AsyncSession = Depends(get_db_session),
) -> PaymentMilestoneResponse:
    user_id = getattr(request.state, "user_id", "system")
    user_roles = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    check_financial_write_roles(user_roles)

    # Fetch grant
    g_stmt = select(InvestigatorGrant).where(InvestigatorGrant.id == grant_id)
    g_res = await session.execute(g_stmt)
    grant = g_res.scalars().first()

    if not grant:
        raise HTTPException(status_code=404, detail="Investigator grant not found")

    check_grant_mutable(grant)

    # Validate trigger condition
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


@app.get(
    "/api/v1/ctms/grants/{grant_id}/milestones",
    response_model=List[PaymentMilestoneResponse],
)
async def list_payment_milestones(
    grant_id: str,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> List[PaymentMilestoneResponse]:
    user_id = getattr(request.state, "user_id", "anonymous")
    user_roles = getattr(request.state, "roles", "anonymous")

    check_roles(
        user_roles,
        [
            "monitor",
            "cra",
            "admin",
            "sponsor admin",
            "grants manager",
            "auditor",
            "system",
            "anonymous",
        ],
    )

    # Confirm grant exists
    g_stmt = select(InvestigatorGrant).where(InvestigatorGrant.id == grant_id)
    g_res = await session.execute(g_stmt)
    if not g_res.scalars().first():
        raise HTTPException(status_code=404, detail="Investigator grant not found")

    stmt = (
        select(PaymentMilestone)
        .where(PaymentMilestone.grant_id == grant_id)
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


@app.post(
    "/api/v1/ctms/grants/{grant_id}/milestones/{milestone_id}/trigger",
    response_model=PaymentMilestoneResponse,
)
async def trigger_manual_milestone(
    grant_id: str,
    milestone_id: str,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> PaymentMilestoneResponse:
    user_id = getattr(request.state, "user_id", "system")
    user_roles = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    check_financial_write_roles(user_roles)

    # Fetch grant
    g_stmt = select(InvestigatorGrant).where(InvestigatorGrant.id == grant_id)
    g_res = await session.execute(g_stmt)
    grant = g_res.scalars().first()

    if not grant:
        raise HTTPException(status_code=404, detail="Investigator grant not found")

    # Fetch milestone
    stmt = select(PaymentMilestone).where(
        PaymentMilestone.id == milestone_id, PaymentMilestone.grant_id == grant_id
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

    # Trigger manually
    if not ms.is_triggered:
        if grant.status != "APPROVED":
            raise HTTPException(
                status_code=400,
                detail="Milestones can only be triggered on approved grants.",
            )

        ms.is_triggered = True
        ms.triggered_at = datetime.utcnow()
        ms.version_index += 1
        ms.reason_for_change = change_reason
        session.add(ms)

        # Check if payable already exists to make it idempotent
        p_stmt = select(InvestigatorPayable).where(
            InvestigatorPayable.grant_id == grant_id,
            InvestigatorPayable.milestone_id == ms.id,
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


@app.post(
    "/api/v1/ctms/grants/{grant_id}/evaluate",
    status_code=200,
)
async def evaluate_grant_milestones(
    grant_id: str,
    request: Request,
    condition: str = "STUDY_APPROVED",
    session: AsyncSession = Depends(get_db_session),
) -> dict:
    """Manually run the milestone evaluation engine for a specific condition."""
    user_id = getattr(request.state, "user_id", "system")
    user_roles = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    check_financial_write_roles(user_roles)

    g_stmt = select(InvestigatorGrant).where(InvestigatorGrant.id == grant_id)
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


# --- Investigator Payables Endpoints ---


@app.get(
    "/api/v1/ctms/grants/{grant_id}/payables",
    response_model=List[InvestigatorPayableResponse],
)
async def list_investigator_payables(
    grant_id: str,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> List[InvestigatorPayableResponse]:
    user_id = getattr(request.state, "user_id", "anonymous")
    user_roles = getattr(request.state, "roles", "anonymous")

    check_roles(
        user_roles,
        [
            "monitor",
            "cra",
            "admin",
            "sponsor admin",
            "grants manager",
            "auditor",
            "system",
            "anonymous",
        ],
    )

    # Confirm grant exists
    g_stmt = select(InvestigatorGrant).where(InvestigatorGrant.id == grant_id)
    g_res = await session.execute(g_stmt)
    if not g_res.scalars().first():
        raise HTTPException(status_code=404, detail="Investigator grant not found")

    stmt = (
        select(InvestigatorPayable)
        .where(InvestigatorPayable.grant_id == grant_id)
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
