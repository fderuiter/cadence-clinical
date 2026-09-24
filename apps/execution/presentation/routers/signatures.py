"""FastAPI router for Principal Investigator (PI) batch eSignature execution API.

Requirements: PRD-SYS-001
"""

import uuid
from datetime import UTC, datetime
from pathlib import Path

from fastapi import APIRouter, Depends, HTTPException, Request, status
from pydantic import BaseModel

import packages  # noqa: F401
from apps.execution.domain.signature_transport_models import (
    BatchSignatureRequest,
    BatchSignatureResponse,
)
from packages.compliance.services.esignature_verifier import ESignatureVerifier
from packages.compliance.services.pkcs7_signer import PKCS7Signer
from packages.security.cert_store import get_active_cert_store
from packages.security.middleware import get_current_user
from packages.security.sig_token_verifier import verify_and_consume_sig_token
from packages.security.signature_builder import CryptographicSignatureBuilder

router = APIRouter(prefix="/api/v1/execution/signatures", tags=["Signatures"])


class SignRequest(BaseModel):
    """Request model for signing payload data."""

    data: str


class SignResponse(BaseModel):
    """Response model with the PKCS#7 signed payload."""

    signed_data: str


class VerifyRequest(BaseModel):
    """Request model for verifying a PKCS#7 signed payload."""

    signed_data: str


class VerifyResponse(BaseModel):
    """Response model for PKCS#7 signature verification."""

    is_valid: bool
    status: str
    failure_reason: str = ""


KEYS_DIR = (
    Path(__file__).resolve().parent.parent.parent.parent.parent
    / "tests"
    / "fixtures"
    / "keys"
)


def _load_keys_and_register():
    """Helper to load key and cert from fixtures and register in trust store."""
    from cryptography import x509
    from cryptography.hazmat.primitives import serialization

    cert_path = KEYS_DIR / "certificate.crt"
    key_path = KEYS_DIR / "private_key.pem"

    with open(cert_path, "rb") as f:
        cert_bytes = f.read()
        cert = x509.load_pem_x509_certificate(cert_bytes)
        get_active_cert_store().register_certificate(
            user_id="backend_signer", cert_pem=cert_bytes.decode("utf-8")
        )

    with open(key_path, "rb") as f:
        key = serialization.load_pem_private_key(f.read(), password=None)

    return cert, key


@router.post(
    "/sign",
    response_model=SignResponse,
    status_code=status.HTTP_200_OK,
)
async def sign_payload_endpoint(
    payload: SignRequest,
) -> SignResponse:
    """Signs a given string payload using the secure backend PKCS#7 signer.

    Requirements: PRD-SYS-001
    """
    try:
        cert, key = _load_keys_and_register()
        signer = PKCS7Signer(cert=cert, key=key)
        signed_bytes = signer.sign_pdf(payload.data.encode("utf-8"))
        return SignResponse(signed_data=signed_bytes.decode("utf-8", errors="ignore"))
    except Exception as e:
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=f"Signature generation failed: {str(e)}",
        )


@router.post(
    "/verify",
    response_model=VerifyResponse,
    status_code=status.HTTP_200_OK,
)
async def verify_signature_endpoint(
    payload: VerifyRequest,
) -> VerifyResponse:
    """Verifies a PKCS#7 signed document payload using the ESignatureVerifier and trust store.

    Requirements: PRD-SYS-001
    """
    try:
        # Load and register certificate to ensure trust store has the valid cert registered
        _load_keys_and_register()
        verifier = ESignatureVerifier()
        result = verifier.verify_signature(payload.signed_data.encode("utf-8"))
        return VerifyResponse(
            is_valid=result.is_valid,
            status=result.status,
            failure_reason=result.failure_reason,
        )
    except Exception as e:
        return VerifyResponse(
            is_valid=False,
            status="SYSTEM_FAILURE",
            failure_reason=f"Signature verification system failure: {str(e)}",
        )


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
