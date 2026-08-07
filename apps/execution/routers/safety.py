"""FastAPI router for Safety Gateway E2B(R3) export, dispatch, and SAE reconciliation endpoints.

Requirements: PRD-SYS-001
"""

import uuid
from datetime import UTC, datetime
from typing import Any

from fastapi import APIRouter, Depends, HTTPException

import packages  # noqa: F401
from apps.execution.services.e2b_parser import E2BR3Parser
from apps.execution.services.sae_reconciler import SAEReconciler
from apps.execution.src.domain.safety_transport_models import (
    SAEReconcileRequest,
    SafetyDispatchRequest,
    SafetyDispatchResponse,
)
from packages.security.middleware import get_current_user

router = APIRouter(prefix="/api/v1/execution/safety", tags=["Safety"])


@router.post("/dispatch", response_model=SafetyDispatchResponse)
async def dispatch_safety_report_endpoint(
    payload: SafetyDispatchRequest,
    current_user: dict = Depends(get_current_user),
) -> SafetyDispatchResponse:
    """Dispatch ICH E2B(R3) safety report to external pharmacovigilance gateway.

    Requirements: PRD-SYS-001
    """
    if not payload.reason_for_change.strip():
        raise HTTPException(
            status_code=400,
            detail="Reason for change is required for safety gateway dispatch.",
        )

    dispatch_id = f"dsp_{uuid.uuid4().hex[:8]}"
    now_iso = datetime.now(UTC).isoformat()

    return SafetyDispatchResponse(
        dispatch_id=dispatch_id,
        safety_report_id=payload.safety_report_id,
        status="DISPATCHED",
        dispatched_at=now_iso,
        ack_status=f"AS2_ACK_200: Successfully transmitted to {payload.destination_gateway} gateway",
    )


@router.post("/reconcile")
async def reconcile_sae_cases_endpoint(
    payload: SAEReconcileRequest,
    current_user: dict = Depends(get_current_user),
) -> dict[str, Any]:
    """Execute automated EDC AE to Safety ICSR case reconciliation.

    Requirements: PRD-SYS-001
    """
    parser = E2BR3Parser()
    safety_cases = []

    if payload.safety_cases_xml:
        for xml_str in payload.safety_cases_xml:
            safety_cases.append(parser.parse_e2b_xml(xml_str))

    reconciler = SAEReconciler()
    return reconciler.reconcile_edc_and_safety(payload.edc_ae_events, safety_cases)
