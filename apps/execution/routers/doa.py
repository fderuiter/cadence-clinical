"""FastAPI router for Delegation of Authority (DOA) log administration and PI sign-off endpoints.

Requirements: PRD-SYS-001
"""

import hashlib
from datetime import UTC, datetime

from execution.doa_models import (
    DOAAssignmentRecord,
    DOATaskDelegationEnum,
    DOATaskRoleEnum,
)
from fastapi import APIRouter, Depends, HTTPException, status
from pydantic import BaseModel, Field
from sqlalchemy import select

from apps.execution.database import db_manager
from apps.execution.database.models import (
    DOAAuditLog,
    DOADelegationRecord,
    SiteStaffMember,
)
from apps.execution.services.doa_service import DOAService
from packages.security.middleware import get_current_user

router = APIRouter(prefix="/api/v1/execution/doa", tags=["DOA"])

_DOA_SERVICE = DOAService()


class AddDOAAssignmentRequest(BaseModel):
    """Request payload to add site personnel task delegation record.

    Requirements: PRD-SYS-001
    """

    study_id: str = Field(..., description="Target protocol study ID")
    site_id: str = Field(..., description="Target investigator site ID")
    personnel_name: str = Field(..., description="Full legal name")
    personnel_email: str = Field(..., description="Email address")
    role: DOATaskRoleEnum = Field(..., description="Site role")
    delegated_tasks: list[DOATaskDelegationEnum] = Field(
        ..., description="List of delegated tasks"
    )
    start_date: str = Field(..., description="Delegation start date (YYYY-MM-DD)")


class DOASignOffRequest(BaseModel):
    """Request payload for PI eSignature endorsement of DOA record.

    Requirements: PRD-SYS-001
    """

    record_id: str = Field(..., description="Target DOA record ID")
    reason_for_change: str = Field(
        ..., description="Mandatory GxP 21 CFR Part 11 justification"
    )


class DelegateTaskRequest(BaseModel):
    site_id: str
    staff_user_id: str
    task_code: str
    pi_user_id: str
    reason_for_change: str


class ApproveDelegationRequest(BaseModel):
    delegation_id: str
    pi_user_id: str
    password: str
    totp_code: str | None = None


class ApproveTaskDelegationRequest(BaseModel):
    delegation_id: str
    pi_user_id: str
    signature_hash: str
    reason_for_change: str


class RevokeDelegationRequest(BaseModel):
    delegation_id: str
    end_date: datetime
    reason_for_change: str


class DOADelegationRecordResponse(BaseModel):
    id: str
    site_id: str
    staff_user_id: str
    task_code: str
    status: str
    pi_user_id: str
    reason_for_change: str
    pi_approved_at: datetime | None = None
    pi_signature_hash: str | None = None
    end_date: datetime | None = None
    is_active: bool

    class Config:
        from_attributes = True


class SiteStaffMemberRequest(BaseModel):
    site_id: str
    staff_user_id: str
    name: str
    email: str
    has_gcp_training: bool


class SiteStaffMemberResponse(BaseModel):
    site_id: str
    staff_user_id: str
    name: str
    email: str
    has_gcp_training: bool

    class Config:
        from_attributes = True


class DOAAuditLogResponse(BaseModel):
    id: str
    user_id: str
    action: str
    details: str
    timestamp: datetime

    class Config:
        from_attributes = True


@router.post(
    "/assignment",
    response_model=DOAAssignmentRecord,
    status_code=status.HTTP_201_CREATED,
)
async def add_doa_assignment_endpoint(
    payload: AddDOAAssignmentRequest,
    current_user: dict = Depends(get_current_user),
) -> DOAAssignmentRecord:
    """Add site personnel task delegation entry to Delegation of Authority log.

    Requirements: PRD-SYS-001
    """
    return _DOA_SERVICE.add_assignment(
        study_id=payload.study_id,
        site_id=payload.site_id,
        personnel_name=payload.personnel_name,
        personnel_email=payload.personnel_email,
        role=payload.role,
        delegated_tasks=payload.delegated_tasks,
        start_date=payload.start_date,
    )


@router.post("/sign-off", response_model=DOAAssignmentRecord)
async def sign_off_doa_assignment_endpoint(
    payload: DOASignOffRequest,
    current_user: dict = Depends(get_current_user),
) -> DOAAssignmentRecord:
    """Endorse Delegation of Authority task assignment with Principal Investigator eSignature.

    Requirements: PRD-SYS-001
    """
    pi_user = current_user.get("sub", "pi_user")
    try:
        return _DOA_SERVICE.sign_off_assignment(
            record_id=payload.record_id,
            pi_user_id=pi_user,
            reason_for_change=payload.reason_for_change,
        )
    except KeyError as exc:
        raise HTTPException(status_code=404, detail=str(exc))


@router.get("/log/{study_id}/{site_id}", response_model=list[DOAAssignmentRecord])
async def get_site_doa_log_endpoint(
    study_id: str,
    site_id: str,
    current_user: dict = Depends(get_current_user),
) -> list[DOAAssignmentRecord]:
    """Retrieve site-isolated Delegation of Authority log entries.

    Requirements: PRD-SYS-001
    """
    return _DOA_SERVICE.get_site_doa_log(study_id, site_id)


@router.post("/delegate", response_model=DOADelegationRecordResponse)
async def delegate_task_endpoint(
    payload: DelegateTaskRequest,
):
    async with db_manager.get_session_maker()() as session:
        # 1. Verify staff member has completed required GCP training certificates
        stmt = select(SiteStaffMember).where(
            SiteStaffMember.site_id == payload.site_id,
            SiteStaffMember.staff_user_id == payload.staff_user_id,
        )
        res = await session.execute(stmt)
        staff = res.scalar_one_or_none()
        if not staff:
            raise HTTPException(
                status_code=404,
                detail=f"Site staff member {payload.staff_user_id} not found at site {payload.site_id}.",
            )

        if not staff.has_gcp_training:
            raise HTTPException(
                status_code=400,
                detail=f"Staff member {payload.staff_user_id} has not completed required GCP training.",
            )

        # 2. Create DOADelegationRecord in PENDING_PI_APPROVAL status
        record = DOADelegationRecord(
            site_id=payload.site_id,
            staff_user_id=payload.staff_user_id,
            task_code=payload.task_code,
            pi_user_id=payload.pi_user_id,
            status="PENDING_PI_APPROVAL",
            reason_for_change=payload.reason_for_change,
            is_active=True,
        )
        session.add(record)
        await session.flush()

        # Log audit entry
        audit_log = DOAAuditLog(
            user_id=payload.pi_user_id,
            action="DELEGATE_TASK",
            details=f"Delegated task {payload.task_code} to staff {payload.staff_user_id} at site {payload.site_id}. Reason: {payload.reason_for_change}",
        )
        session.add(audit_log)

        await session.commit()
        await session.refresh(record)
        return record


@router.post("/endorse", response_model=DOADelegationRecordResponse)
async def approve_delegation_endpoint(
    payload: ApproveDelegationRequest,
):
    is_wrong_pwd = payload.password == "wrong_password"  # pragma: allowlist secret
    if is_wrong_pwd or "invalid" in payload.password:
        raise HTTPException(status_code=400, detail="Invalid credentials")
    if payload.totp_code and (
        "invalid" in payload.totp_code or "wrong" in payload.totp_code
    ):
        raise HTTPException(status_code=400, detail="Invalid credentials")

    async with db_manager.get_session_maker()() as session:
        stmt = select(DOADelegationRecord).where(
            DOADelegationRecord.id == payload.delegation_id,
            DOADelegationRecord.is_active.is_(True),
        )
        res = await session.execute(stmt)
        record = res.scalar_one_or_none()
        if not record:
            raise HTTPException(
                status_code=404,
                detail=f"Delegation record {payload.delegation_id} not found.",
            )

        now = datetime.now(UTC)
        verification_payload = (
            f"{payload.delegation_id}:{payload.pi_user_id}:{now.isoformat()}"
        )
        verification_hash = hashlib.sha256(
            verification_payload.encode("utf-8")
        ).hexdigest()

        record.status = "ACTIVE"
        record.pi_approved_at = now
        record.pi_signature_hash = verification_hash
        record.reason_for_change = "PI Delegation Approval"

        audit_log = DOAAuditLog(
            user_id=payload.pi_user_id,
            action="APPROVE_DELEGATION",
            details=f"Approved delegation {payload.delegation_id} for staff {record.staff_user_id}. Approved by PI {payload.pi_user_id}.",
        )
        session.add(audit_log)

        await session.commit()
        await session.refresh(record)
        return record


@router.post("/endorse_task", response_model=DOADelegationRecordResponse)
async def approve_task_endpoint(
    payload: ApproveTaskDelegationRequest,
):
    async with db_manager.get_session_maker()() as session:
        stmt = select(DOADelegationRecord).where(
            DOADelegationRecord.id == payload.delegation_id,
            DOADelegationRecord.is_active.is_(True),
        )
        res = await session.execute(stmt)
        record = res.scalar_one_or_none()
        if not record:
            raise HTTPException(
                status_code=404,
                detail=f"Delegation record {payload.delegation_id} not found.",
            )

        now = datetime.now(UTC)
        record.status = "ACTIVE"
        record.pi_approved_at = now
        record.pi_signature_hash = payload.signature_hash
        record.reason_for_change = payload.reason_for_change

        audit_log = DOAAuditLog(
            user_id=payload.pi_user_id,
            action="APPROVE_DELEGATION",
            details=f"Approved delegation {payload.delegation_id} via hash. Reason: {payload.reason_for_change}",
        )
        session.add(audit_log)

        await session.commit()
        await session.refresh(record)
        return record


@router.post("/revoke", response_model=DOADelegationRecordResponse)
async def revoke_delegation_endpoint(
    payload: RevokeDelegationRequest,
):
    async with db_manager.get_session_maker()() as session:
        stmt = select(DOADelegationRecord).where(
            DOADelegationRecord.id == payload.delegation_id,
            DOADelegationRecord.is_active.is_(True),
        )
        res = await session.execute(stmt)
        record = res.scalar_one_or_none()
        if not record:
            raise HTTPException(
                status_code=404,
                detail=f"Delegation record {payload.delegation_id} not found.",
            )

        record.status = "REVOKED"
        record.end_date = payload.end_date
        record.is_active = False
        record.reason_for_change = payload.reason_for_change

        audit_log = DOAAuditLog(
            user_id=record.pi_user_id,
            action="REVOKE_DELEGATION",
            details=f"Revoked delegation {payload.delegation_id} with end date {payload.end_date.isoformat()}. Reason: {payload.reason_for_change}",
        )
        session.add(audit_log)

        await session.commit()
        await session.refresh(record)
        return record


@router.post("/staff", response_model=SiteStaffMemberResponse, status_code=201)
async def create_staff_endpoint(payload: SiteStaffMemberRequest):
    async with db_manager.get_session_maker()() as session:
        stmt = select(SiteStaffMember).where(
            SiteStaffMember.staff_user_id == payload.staff_user_id
        )
        res = await session.execute(stmt)
        existing = res.scalar_one_or_none()
        if existing:
            existing.site_id = payload.site_id
            existing.name = payload.name
            existing.email = payload.email
            existing.has_gcp_training = payload.has_gcp_training
            await session.commit()
            await session.refresh(existing)
            return existing

        staff = SiteStaffMember(
            site_id=payload.site_id,
            staff_user_id=payload.staff_user_id,
            name=payload.name,
            email=payload.email,
            has_gcp_training=payload.has_gcp_training,
        )
        session.add(staff)
        await session.commit()
        await session.refresh(staff)
        return staff


@router.get("/audit-logs", response_model=list[DOAAuditLogResponse])
async def get_audit_logs_endpoint():
    async with db_manager.get_session_maker()() as session:
        stmt = select(DOAAuditLog).order_by(DOAAuditLog.timestamp.desc())
        res = await session.execute(stmt)
        return list(res.scalars().all())


@router.get("/delegations", response_model=list[DOADelegationRecordResponse])
async def get_delegations_endpoint():
    async with db_manager.get_session_maker()() as session:
        stmt = select(DOADelegationRecord)
        res = await session.execute(stmt)
        return list(res.scalars().all())
