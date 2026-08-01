"""FastAPI router for Delegation of Authority (DOA) log administration and PI sign-off endpoints.

Requirements: PRD-SYS-001
"""

from execution.doa_models import (
    DOAAssignmentRecord,
    DOATaskDelegationEnum,
    DOATaskRoleEnum,
)
from fastapi import APIRouter, Depends, HTTPException, status
from pydantic import BaseModel, Field

import packages  # noqa: F401
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
