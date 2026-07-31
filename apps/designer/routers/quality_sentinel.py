"""FastAPI router for Protocol Quality Sentinel evaluation API.

Requirements: PRD-SYS-001
"""

from typing import Any, Dict

from cdisc.sentinel_models import ProtocolQualityScore
from fastapi import APIRouter, Depends

import packages  # noqa: F401
from apps.designer.services.quality_sentinel import ProtocolQualitySentinel
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
    payload: Dict[str, Any],
    principal: Principal = Depends(get_principal),
) -> ProtocolQualityScore:
    """Evaluate authored protocol specification payload against quality and burden rules.

    Requirements: PRD-SYS-001
    """
    sentinel = ProtocolQualitySentinel()
    report = sentinel.evaluate_protocol_quality(payload)

    # Apply masking for blinded users
    masked_report = mask_payload(report, principal)
    return masked_report
