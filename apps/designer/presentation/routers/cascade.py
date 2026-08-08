"""FastAPI router for downstream artifact cascade propagation API endpoints.

Requirements: PRD-SYS-001
"""

from typing import Any

from fastapi import APIRouter, Depends

from apps.designer.dependencies import get_cascade_engine
from apps.designer.domain.cdisc.cascade_models import CascadeSummaryReport
from apps.designer.services.artifact_cascade import ArtifactCascadeEngine
from packages.security.middleware import get_current_user

router = APIRouter(prefix="/api/v1/designer/cascade", tags=["ArtifactCascade"])


@router.post("/propagate", response_model=CascadeSummaryReport)
async def propagate_cascade_endpoint(
    payload: dict[str, Any],
    amendment_version: int = 1,
    current_user: dict = Depends(get_current_user),
    engine: ArtifactCascadeEngine = Depends(get_cascade_engine),
) -> CascadeSummaryReport:
    """Cascade authored USDM protocol specification changes to downstream eCRFs and SoA matrices.

    Requirements: PRD-SYS-001
    """
    return engine.cascade_protocol_to_downstream(
        study_payload=payload, amendment_version=amendment_version
    )
