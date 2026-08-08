"""FastAPI router for Principal Investigator (PI) batch eSignature execution API.

Requirements: PRD-SYS-001
"""

import uuid
from datetime import UTC, datetime

from fastapi import APIRouter, Depends, HTTPException, Request, status

import packages  # noqa: F401
from apps.execution.domain.signature_transport_models import (
    BatchSignatureRequest,
    BatchSignatureResponse,
)
from packages.security.middleware import get_current_user
from packages.security.sig_token_verifier import verify_and_consume_sig_token
from packages.security.signature_builder import CryptographicSignatureBuilder

router = APIRouter(prefix="/api/v1/execution/signatures", tags=["Signatures"])


@router.post(
    "/batch-sign-off",
    response_model=BatchSignatureResponse,
    status_code=status.HTTP_201_CREATED,
)
async def batch_signature_sign_off_endpoint(
    request: Request,
    payload: BatchSignatureRequest,
    current_user: dict = Depends(get_current_user),
) -> BatchSignatureResponse:
    """Execute 21 CFR Part 11 batch electronic signature casebook sign-off.

    Requirements: PRD-SYS-001
    """
    sig_token = request.headers.get("X-Sig-Token")
    verify_and_consume_sig_token(sig_token, current_user["sub"])

    if not payload.target_form_ids:
        raise HTTPException(
            status_code=400,
            detail="At least one target eCRF form ID must be provided for batch sign-off.",
        )

    if not payload.password or not payload.password.strip():
        raise HTTPException(
            status_code=400,
            detail="Re-authentication password is required for 21 CFR Part 11 sign-off.",
        )

    builder = CryptographicSignatureBuilder()
    content_digest = builder.compute_content_digest(payload.target_form_ids)

    sig_id = f"sig_{uuid.uuid4().hex[:8]}"
    audit_tx = f"tx_{uuid.uuid4().hex[:12]}"
    now_iso = datetime.now(UTC).isoformat()

    return BatchSignatureResponse(
        signature_id=sig_id,
        study_id=payload.study_id,
        subject_id=payload.subject_id,
        signed_forms_count=len(payload.target_form_ids),
        content_digest=content_digest,
        timestamp_utc=now_iso,
        audit_tx=audit_tx,
    )
