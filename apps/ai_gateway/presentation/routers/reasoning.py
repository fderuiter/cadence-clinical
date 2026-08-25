"""
FastAPI presentation router for Hybrid Multi-Step Clinical Reasoning Core.

Provides endpoints for executing multi-step clinical trial planning loops,
enforcing in-flight surrogate PHI masking, protocol citation grounding gates,
and staging generated outputs into draft state for human e-signature review.
"""

from __future__ import annotations

import logging
from typing import Annotated

from fastapi import APIRouter, Depends, HTTPException, Request, status

from apps.ai_gateway.adapters.deid_adapter import DeidentifiedAIEngineAdapter
from apps.ai_gateway.adapters.litellm_adapter import LiteLLMAdapter
from apps.ai_gateway.application.reasoning_service import ClinicalReasoningService
from apps.ai_gateway.domain.ports import AIEnginePort
from apps.ai_gateway.domain.reasoning import (
    ClinicalReasoningRequest,
    ClinicalReasoningResponse,
)

logger = logging.getLogger("ai-reasoning-router")

router = APIRouter(prefix="/api/v1/ai/reasoning", tags=["Clinical Reasoning"])


def get_ai_engine() -> AIEnginePort:
    """Dependency provider factory for AI Engine adapter wrapped in de-identification air-gap."""
    return DeidentifiedAIEngineAdapter(LiteLLMAdapter())


def get_reasoning_service(
    engine: Annotated[AIEnginePort, Depends(get_ai_engine)],
) -> ClinicalReasoningService:
    """Dependency provider factory for ClinicalReasoningService."""
    return ClinicalReasoningService(ai_engine=engine)


ReasoningServiceDep = Annotated[
    ClinicalReasoningService, Depends(get_reasoning_service)
]


@router.post(
    "/multi-step",
    response_model=ClinicalReasoningResponse,
    status_code=status.HTTP_200_OK,
    summary="Execute multi-step clinical reasoning planning loop with grounded citations",
)
async def execute_multi_step_reasoning(
    request: Request,
    payload: ClinicalReasoningRequest,
    service: ReasoningServiceDep,
) -> ClinicalReasoningResponse:
    """Executes a multi-step clinical reasoning workflow for protocol synthesis and patient data.

    Enforces:
    - Multi-step planning loops breaking down trial queries into discrete steps
    - In-flight PHI masking using temporary surrogate tokens for intermediate steps
    - Evaluation of protocol citations and 85% confidence threshold gate (fail-closed)
    - Stateless persistence with caller-managed execution state context
    - Staging generated clinical records into draft state requiring human e-signature
    - Mandatory tenant and audit context validation
    """
    # Validate tenant authentication context and audit headers before execution
    header_tenant = request.headers.get("X-Tenant-Id") or getattr(
        request.state, "tenant_id", None
    )
    header_user = request.headers.get("X-User-Id") or getattr(
        request.state, "user_id", None
    )

    resolved_tenant = payload.tenant_id or header_tenant
    if not resolved_tenant or resolved_tenant.strip() in ("", "None"):
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="TENANT_CONTEXT_REQUIRED: Valid tenant identification is required before execution.",
        )

    # Ensure payload uses resolved tenant ID and preserves user audit context
    payload.tenant_id = resolved_tenant.strip()
    if header_user and not payload.esignature_context:
        payload.esignature_context = {"user_id": header_user}

    try:
        response = await service.execute_multi_step_reasoning(payload)
    except ValueError as exc:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST, detail=str(exc)
        ) from exc

    # Enforce 85% citation gate rejection behavior (Requirement 2 & AC 3)
    if (
        response.status == "REJECTED_LOW_CONFIDENCE"
        or response.overall_faithfulness_score < payload.min_citation_confidence
    ):
        logger.warning(
            "Rejecting reasoning output due to low citation confidence: %.2f < %.2f",
            response.overall_faithfulness_score,
            payload.min_citation_confidence,
        )
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_CONTENT,
            detail=(
                f"CITATION_CONFIDENCE_BELOW_THRESHOLD: Citation confidence score "
                f"({response.overall_faithfulness_score:.2f}) is below the required "
                f"threshold ({payload.min_citation_confidence:.2f}). Output rejected and draft creation suppressed."
            ),
        )

    return response


@router.post(
    "/plan",
    response_model=ClinicalReasoningResponse,
    status_code=status.HTTP_200_OK,
    summary="Initialize or advance a caller-managed multi-step clinical reasoning plan",
)
async def plan_clinical_reasoning(
    request: Request,
    payload: ClinicalReasoningRequest,
    service: ReasoningServiceDep,
) -> ClinicalReasoningResponse:
    """Initialize or advance a multi-step clinical reasoning loop statelessly."""
    return await execute_multi_step_reasoning(
        request=request, payload=payload, service=service
    )


__all__ = ["execute_multi_step_reasoning", "plan_clinical_reasoning", "router"]
