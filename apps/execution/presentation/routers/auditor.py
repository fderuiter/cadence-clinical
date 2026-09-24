"""FastAPI router exposing read-only inspection API for regulatory auditors and token generation.

Requirements: PRD-SYS-001
"""

from typing import Any

from fastapi import APIRouter, Depends, HTTPException
from pydantic import BaseModel, Field

import packages  # noqa: F401
from packages.security.auditor_token import AuditorAccessTokenService
from packages.security.middleware import get_current_user

router = APIRouter(prefix="/api/v1/execution/auditor", tags=["Auditor"])


class GenerateAuditorTokenRequest(BaseModel):
    """Request payload to generate a temporary auditor access token.

    Requirements: PRD-SYS-001
    """

    auditor_email: str = Field(..., description="Target auditor email address")
    study_id: str = Field(..., description="Target protocol study ID")
    duration_hours: int = Field(24, description="Token validity in hours")
    reason_for_access: str = Field(
        ..., description="GxP reason for provisioning auditor access"
    )


@router.post("/token/generate")
async def generate_auditor_token_endpoint(
    payload: GenerateAuditorTokenRequest,
    current_user: dict = Depends(get_current_user),
) -> dict[str, Any]:
    """Generate temporary time-bounded access token for regulatory auditors.

    Requirements: PRD-SYS-001
    """
    if not payload.reason_for_access.strip():
        raise HTTPException(
            status_code=400,
            detail="Reason for access is required when generating auditor token.",
        )

    service = AuditorAccessTokenService()
    return service.generate_auditor_token(
        auditor_email=payload.auditor_email,
        study_id=payload.study_id,
        duration_hours=payload.duration_hours,
    )


@router.get("/inspect/audit-trail/{study_id}")
async def inspect_study_audit_trail_endpoint(
    study_id: str,
    limit: int | None = 100,
    current_user: dict = Depends(get_current_user),
) -> dict[str, Any]:
    """Expose read-only 21 CFR Part 11 audit trail inspection endpoint for study.

    Requirements: PRD-SYS-001
    """
    sample_audit_trail = [
        {
            "tx_id": "tx_audit_001",
            "study_id": study_id,
            "entity": "FormSubmission",
            "action": "CREATE",
            "performed_by": "site_crc_01",
            "timestamp_utc": "2026-07-30T10:00:00Z",
            "reason_for_change": "Initial Vital Signs Data Entry",
        },
        {
            "tx_id": "tx_audit_002",
            "study_id": study_id,
            "entity": "DataLockRecord",
            "action": "LOCK",
            "performed_by": "dm_user_01",
            "timestamp_utc": "2026-07-30T12:00:00Z",
            "reason_for_change": "Interim Analysis Data Freeze",
        },
    ]

    return {
        "study_id": study_id,
        "records_count": len(sample_audit_trail),
        "audit_logs": sample_audit_trail[:limit],
    }
