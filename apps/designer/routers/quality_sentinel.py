"""FastAPI router for Protocol Quality Sentinel evaluation API.

Requirements: PRD-SYS-001
"""

from typing import Any

from fastapi import APIRouter, Depends

from apps.designer.dependencies import get_quality_sentinel
from apps.designer.services.quality_sentinel import ProtocolQualitySentinel
from apps.designer.src.domain.cdisc.sentinel_models import ProtocolQualityScore
from packages.security.rbac import (
    Principal,
    get_principal,
    mask_payload,
    require_permission,
)

router = APIRouter(prefix="/api/v1/designer/sentinel", tags=["QualitySentinel"])


@router.post(
    "/evaluate",
    response_model=ProtocolQualityScore,
    dependencies=[Depends(require_permission("study_design:read"))],
)
async def evaluate_protocol_quality_endpoint(
    payload: dict[str, Any],
    principal: Principal = Depends(get_principal),
    sentinel: ProtocolQualitySentinel = Depends(get_quality_sentinel),
) -> ProtocolQualityScore:
    """Evaluate authored protocol specification payload against quality and burden rules.

    Requirements: PRD-SYS-001
    """
    report = sentinel.evaluate_protocol_quality(payload)

    # Apply masking for blinded users
    return mask_payload(report, principal)
