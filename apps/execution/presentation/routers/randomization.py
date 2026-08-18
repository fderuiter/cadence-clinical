"""FastAPI router for subject randomization.

Requirements: PRD-SYS-005
"""

from fastapi import APIRouter, Depends, HTTPException, Request
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.adapters.repositories import get_execution_db_session
from apps.execution.database.models import ClinicalSubject
from apps.execution.dependencies import verify_change_justification
from apps.execution.presentation.routers.randomization_schemas import (
    SubjectRandomizationResponse,
)
from apps.execution.subject_lifecycle import InvalidStateTransitionError
from packages.security import (
    ROLE_CRC,
    ROLE_INVESTIGATOR,
    ROLE_SITE_INVESTIGATOR,
    Principal,
    get_principal,
    require_roles,
)
from packages.security.rbac import mask_payload

router = APIRouter(prefix="/api/v1/execution", tags=["Randomization"])


@router.post(
    "/subjects/{subject_id}/randomize",
    response_model=SubjectRandomizationResponse,
)
async def randomize_subject_endpoint(
    subject_id: str,
    request: Request,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(
            ROLE_SITE_INVESTIGATOR, ROLE_INVESTIGATOR, ROLE_CRC, "investigator"
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> SubjectRandomizationResponse:
    """Execute GxP compliant subject randomization allocation and block-index advancement."""
    # Ensure change justification headers are present and valid
    verify_change_justification(request)
    change_reason = request.headers.get("X-Change-Reason")

    # Fetch subject to resolve study_id
    stmt = select(ClinicalSubject).where(ClinicalSubject.subject_id == subject_id)
    result = await session.execute(stmt)
    subject = result.scalars().first()
    if not subject:
        raise HTTPException(status_code=404, detail="Subject not found")
    study_id = subject.study_id

    # Execute randomization via service
    from apps.execution.cryptography import AllocationKeyManager
    from apps.execution.randomization_service import randomize_subject

    try:
        assignment = await randomize_subject(
            study_id=study_id,
            subject_id=subject_id,
            change_reason=change_reason,
            user_id=principal.user_id,
        )
    except InvalidStateTransitionError as e:
        raise HTTPException(status_code=400, detail=str(e))
    except ValueError as e:
        msg = str(e)
        if "not found" in msg.lower():
            raise HTTPException(status_code=404, detail=msg)
        raise HTTPException(status_code=400, detail=msg)
    except HTTPException:
        raise
    except Exception as e:
        raise HTTPException(status_code=400, detail=str(e))

    # Decrypt allocation plaintext for response (which will then be masked/blinded)
    key_mgr = AllocationKeyManager()
    await key_mgr.load_from_db(session)
    decrypted = key_mgr.decrypt(assignment.encrypted_allocation)
    allocated_arm = decrypted.get("allocation")

    response_dict = {
        "subject_id": assignment.subject_id,
        "status": "RANDOMIZED",
        "stratum_key": assignment.stratum_key,
        "randomized_at": assignment.randomized_at,
        "kit_reference": assignment.kit_reference,
        "treatment_arm": allocated_arm,
    }

    masked_response = mask_payload(response_dict, principal)
    return SubjectRandomizationResponse(**masked_response)
