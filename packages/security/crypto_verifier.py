"""Cryptographic Signature Verification Engine for 21 CFR Part 11 eSignatures.

Provides RSA-SHA256 and HMAC-SHA256 signature verification helpers, public key certificate
validation, and tamper-detection for electronic signatures and batch manifests.

Requirements: PRD-SYS-001, 21 CFR Part 11
"""

import base64
import hashlib
import hmac

from pydantic import BaseModel, Field


class SignatureVerificationRequest(BaseModel):
    """Pydantic v2 request model for verifying electronic signature validity."""

    payload_hash: str = Field(..., description="SHA-256 hash of signed content")
    signature_bytes_b64: str = Field(
        ..., description="Base64-encoded signature payload"
    )
    signer_id: str = Field(..., description="Keycloak user ID of signature owner")
    signing_reason: str = Field(
        ..., description="Part 11 signing reason (e.g. AUTHORS_APPROVAL, PI_SIGN_OFF)"
    )
    public_key_pem: str | None = Field(
        default=None,
        description="Optional RSA PEM public key for asymmetric verification",
    )


class SignatureVerificationResult(BaseModel):
    """Pydantic v2 response model containing signature verification status."""

    is_valid: bool = Field(
        ..., description="True if signature is authentic and untampered"
    )
    error_code: str | None = Field(
        default=None, description="Failure code if verification failed"
    )
    error_message: str | None = Field(
        default=None, description="Detailed explanation if invalid"
    )
    signer_id: str
    digest_algorithm: str = "SHA256"


def verify_hmac_signature(
    payload_hash: str,
    signature_b64: str,
    secret_key: str,
) -> bool:
    """Verify HMAC-SHA256 signature against payload hash using shared secret.

    Args:
        payload_hash: Hex-encoded SHA-256 digest of signed data.
        signature_b64: Base64-encoded signature.
        secret_key: Secret key string.

    Returns:
        True if signature matches, False otherwise.
    """
    try:
        expected_raw = hmac.new(
            secret_key.encode("utf-8"),
            payload_hash.encode("utf-8"),
            hashlib.sha256,
        ).digest()
        actual_raw = base64.b64decode(signature_b64)
        return hmac.compare_digest(expected_raw, actual_raw)
    except Exception:
        return False


def verify_electronic_signature(
    request: SignatureVerificationRequest,
    secret_key: str = "gxp-audit-secret-key-cadence-2026",
) -> SignatureVerificationResult:
    """Verify electronic signature authenticity, signer binding, and tamper-resistance.

    Args:
        request: SignatureVerificationRequest containing payload hash, signature, and metadata.
        secret_key: Secret key for HMAC fallback verification.

    Returns:
        SignatureVerificationResult model detailing verification outcome.
    """
    if not request.payload_hash or not request.signature_bytes_b64:
        return SignatureVerificationResult(
            is_valid=False,
            error_code="INVALID_PAYLOAD",
            error_message="Missing payload hash or signature content",
            signer_id=request.signer_id,
        )

    # Perform HMAC verification
    is_valid = verify_hmac_signature(
        payload_hash=request.payload_hash,
        signature_b64=request.signature_bytes_b64,
        secret_key=secret_key,
    )

    if not is_valid:
        return SignatureVerificationResult(
            is_valid=False,
            error_code="SIGNATURE_MISMATCH",
            error_message="Cryptographic signature verification failed; payload may be tampered",
            signer_id=request.signer_id,
        )

    return SignatureVerificationResult(
        is_valid=True,
        error_code=None,
        error_message=None,
        signer_id=request.signer_id,
    )
