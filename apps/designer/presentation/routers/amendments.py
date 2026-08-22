"""FastAPI presentation router for Designer Protocol Amendments and USDM Branching.

Enables zero-downtime protocol amendments, immutable graph branching,
semantic multi-layer diffing, ripple-effect analysis, and operational ticket dispatch.
Requirements: PRD-SYS-001, PRD-SUB-007, PRD-SYS-051
"""

import logging

from fastapi import APIRouter, HTTPException, Request, status

from apps.designer.adapters.tickets_client import DesignerTicketsClient
from apps.designer.application.services.branch_manager import ProtocolBranchManager
from apps.designer.application.services.ripple_analyzer import (
    ProtocolAmendmentRippleAnalyzer,
)
from apps.designer.delta import ImmutabilityViolationError
from apps.designer.dependencies import get_neo4j_driver
from apps.designer.domain.amendment_service import create_protocol_amendment
from apps.designer.domain.cdisc.branch_models import (
    AmendmentImpactSummary,
    BranchAmendmentRequest,
    BranchAmendmentResponse,
    SemanticDiffRequest,
    SemanticDiffResponse,
)
from apps.designer.domain.cdisc.ripple_models import (
    ProtocolImpactAssessment,
    RippleAnalysisRequest,
    TicketDispatchRequest,
    TicketDispatchResponse,
)

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/v1/designer/amendments", tags=["DesignerAmendments"])


@router.post(
    "/branch",
    response_model=BranchAmendmentResponse,
    status_code=status.HTTP_201_CREATED,
)
async def branch_protocol_amendment_endpoint(
    payload: BranchAmendmentRequest,
    request: Request,
) -> BranchAmendmentResponse:
    """Create an isolated, immutable protocol amendment working branch from an approved baseline.

    Requirements: PRD-SYS-001, PRD-SUB-007
    """
    user_id = (
        getattr(request.state, "user_id", None)
        or request.headers.get("X-User-Id")
        or "sponsor_designer_01"
    )
    change_reason = (
        getattr(request.state, "change_reason", None)
        or request.headers.get("X-Change-Reason")
        or payload.change_reason
    )

    if not change_reason or not change_reason.strip():
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Missing change justification reason for protocol amendment branch",
        )

    driver = await get_neo4j_driver(request) if request else None

    try:
        result = await create_protocol_amendment(
            driver=driver,
            study_id=payload.study_id,
            base_version_tag=payload.base_version_tag,
            amendment_type=payload.amendment_type,
            requires_reconsent=payload.requires_reconsent,
            change_reason=change_reason.strip(),
            user_id=user_id,
            branch_name=payload.branch_name,
        )

        return BranchAmendmentResponse(
            study_id=result["study_id"],
            branch_id=result["branch_id"],
            branch_name=result["branch_name"],
            base_version_tag=result["base_version_tag"],
            new_version_tag=result["new_version_tag"],
            version_id=result["version_id"],
            status=result["status"],
            requires_reconsent=result["requires_reconsent"],
            created_by=result["created_by"],
            created_at=result["created_at"],
        )
    except ImmutabilityViolationError as e:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail=f"IMMUTABILITY_VIOLATION: {e}",
        ) from e
    except Exception as e:
        logger.error(f"Failed to create protocol amendment branch: {e}")
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=f"Failed to create protocol amendment branch: {e}",
        ) from e


@router.post(
    "/diff",
    response_model=SemanticDiffResponse,
    status_code=status.HTTP_200_OK,
)
async def compute_semantic_diff_endpoint(
    payload: SemanticDiffRequest,
    request: Request,
) -> SemanticDiffResponse:
    """Compute multi-layer visual and semantic protocol diff across USDM Graph, SoA, Eligibility, and eCRF forms.

    Requirements: PRD-SYS-001, PRD-SUB-007
    """
    branch_manager = ProtocolBranchManager()

    return branch_manager.compute_semantic_diff(
        study_id=payload.study_id,
        base_version_tag=payload.base_version_tag,
        amended_version_tag=payload.amended_version_tag,
        base_payload=payload.base_payload,
        draft_payload=payload.draft_payload,
    )


@router.get(
    "/{study_id}/diff",
    response_model=SemanticDiffResponse,
    status_code=status.HTTP_200_OK,
)
async def get_study_semantic_diff_endpoint(
    study_id: str,
    base_version: str = "1.0.0",
    amended_version: str = "2.0.0",
    request: Request = None,
) -> SemanticDiffResponse:
    """Get multi-layer semantic diff for a study between two version tags.

    Requirements: PRD-SYS-001, PRD-SUB-007
    """
    branch_manager = ProtocolBranchManager()
    return branch_manager.compute_semantic_diff(
        study_id=study_id,
        base_version_tag=base_version,
        amended_version_tag=amended_version,
    )


@router.post(
    "/impact",
    response_model=AmendmentImpactSummary,
    status_code=status.HTTP_200_OK,
)
async def compute_amendment_impact_endpoint(
    payload: SemanticDiffRequest,
    request: Request,
) -> AmendmentImpactSummary:
    """Compute quantitative Amendment Impact Summary (burden delta, affected visits, schema revisions).

    Requirements: PRD-SYS-001, PRD-SUB-007
    """
    branch_manager = ProtocolBranchManager()
    diff_response = branch_manager.compute_semantic_diff(
        study_id=payload.study_id,
        base_version_tag=payload.base_version_tag,
        amended_version_tag=payload.amended_version_tag,
        base_payload=payload.base_payload,
        draft_payload=payload.draft_payload,
    )
    return diff_response.impact_summary


@router.post(
    "/analyze-ripple",
    response_model=ProtocolImpactAssessment,
    status_code=status.HTTP_200_OK,
)
async def analyze_ripple_effects_endpoint(
    payload: RippleAnalysisRequest,
    request: Request,
) -> ProtocolImpactAssessment:
    """Analyze ripple-effect deltas across USDM graph, SoA matrix, and narrative to generate ProtocolImpactAssessment.

    Requirements: PRD-SYS-001, PRD-SUB-007, PRD-SYS-051
    """
    analyzer = ProtocolAmendmentRippleAnalyzer()
    return analyzer.analyze_amendment_impact(
        study_id=payload.study_id,
        base_version_tag=payload.base_version_tag,
        amended_version_tag=payload.amended_version_tag,
        amendment_type=payload.amendment_type,
        requires_reconsent_override=payload.requires_reconsent,
        base_payload=payload.base_payload,
        draft_payload=payload.draft_payload,
        active_subject_ids=payload.active_subject_ids,
    )


@router.post(
    "/dispatch-tickets",
    response_model=TicketDispatchResponse,
    status_code=status.HTTP_200_OK,
)
async def dispatch_operational_tickets_endpoint(
    payload: TicketDispatchRequest,
    request: Request,
) -> TicketDispatchResponse:
    """Generate and dispatch multi-domain operational tickets for a protocol amendment to apps/tickets.

    Requirements: PRD-SYS-001, PRD-SUB-007, PRD-SYS-051
    """
    user_id = (
        getattr(request.state, "user_id", None)
        or request.headers.get("X-User-Id")
        or "sponsor_designer_01"
    )
    change_reason = (
        getattr(request.state, "change_reason", None)
        or request.headers.get("X-Change-Reason")
        or "Dispatching operational tickets for protocol amendment"
    )

    assessment = payload.impact_assessment
    if assessment is None:
        analyzer = ProtocolAmendmentRippleAnalyzer()
        assessment = analyzer.analyze_amendment_impact(
            study_id=payload.study_id,
            base_version_tag=payload.base_version_tag or "1.0.0",
            amended_version_tag=payload.amended_version_tag or "2.0.0",
            base_payload=payload.base_payload,
            draft_payload=payload.draft_payload,
        )

    tickets_client = DesignerTicketsClient()
    dispatched = await tickets_client.dispatch_batch(
        blueprints=assessment.operational_tickets,
        study_id=payload.study_id,
        user_id=user_id,
        change_reason=change_reason,
        selected_queues=payload.selected_domain_queues,
    )

    return TicketDispatchResponse(
        study_id=payload.study_id,
        assessment_id=assessment.assessment_id,
        total_dispatched=len(dispatched),
        dispatched_tickets=dispatched,
        message=f"Successfully dispatched {len(dispatched)} operational ticket(s) across domain queues.",
    )
