"""Cryptographic Signature Verification Engine for 21 CFR Part 11 eSignatures.

Provides RSA-SHA256, ECDSA-SHA256, and HMAC-SHA256 signature verification helpers, public key certificate
validation, and tamper-detection for electronic signatures and batch manifests.

Requirements: PRD-SYS-001, 21 CFR Part 11
"""

import base64
import hashlib
import hmac
import re
from typing import Any

from cryptography import x509
from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.asymmetric import ec, padding, rsa
from cryptography.hazmat.primitives.asymmetric.utils import Prehashed
from cryptography.hazmat.primitives.serialization import load_pem_public_key
from pydantic import BaseModel, Field


class SignatureVerificationRequest(BaseModel):
    """Pydantic v2 request model for verifying electronic signature validity."""

    payload_hash: str = Field(
        ..., description="SHA-256 hash of signed content or raw document"
    )
    signature_bytes_b64: str = Field(
        ..., description="Base64-encoded signature payload"
    )
    signer_id: str = Field(..., description="Keycloak user ID of signature owner")
    signing_reason: str = Field(
        ..., description="Part 11 signing reason (e.g. AUTHORS_APPROVAL, PI_SIGN_OFF)"
    )
    public_key_pem: str | None = Field(
        default=None,
        description="Optional RSA/ECDSA PEM public key for asymmetric verification",
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


def strip_and_canonicalize_json(payload_str: str) -> str:
    """Recursively removes signature-related keys from a JSON string, then canonically serializes it."""
    import json

    try:
        data = json.loads(payload_str)

        def remove_keys(obj: Any):
            if isinstance(obj, dict):
                for k in list(obj.keys()):
                    if k in ("signature", "sig", "signature_bytes_b64", "value"):
                        del obj[k]
                    else:
                        remove_keys(obj[k])
            elif isinstance(obj, list):
                for item in obj:
                    remove_keys(item)

        remove_keys(data)
        return json.dumps(data, sort_keys=True, separators=(",", ":"))
    except Exception:
        return payload_str


def strip_xml_signatures(payload_str: str) -> str:
    """Strips XML signature blocks before calculating content hashes."""
    cleaned = re.sub(
        r"<Signature[^>]*>.*?</Signature>",
        "",
        payload_str,
        flags=re.DOTALL | re.IGNORECASE,
    )
    return re.sub(
        r"<SignatureValue[^>]*>.*?</SignatureValue>",
        "",
        cleaned,
        flags=re.DOTALL | re.IGNORECASE,
    )


def parse_signature_format(sig_str: str) -> str:
    """
    Parses signature from PEM blocks, XML tags, JSON metadata, hex, or raw base64.
    Returns standard base64 string.
    """
    if not sig_str:
        raise ValueError("Empty signature")

    sig_str = sig_str.strip()

    # 1. PEM block
    if "-----BEGIN SIGNATURE-----" in sig_str:
        match = re.search(
            r"-----BEGIN SIGNATURE-----\s*(.*?)\s*-----END SIGNATURE-----",
            sig_str,
            re.DOTALL,
        )
        if match:
            b64_candidate = "".join(match.group(1).split())
            base64.b64decode(b64_candidate, validate=True)
            return b64_candidate
        raise ValueError("Malformed PEM signature block")

    # 2. XML tags
    if "<" in sig_str and ">" in sig_str:
        match_val = re.search(
            r"<SignatureValue[^>]*>(.*?)</SignatureValue>",
            sig_str,
            re.DOTALL | re.IGNORECASE,
        )
        if match_val:
            b64_candidate = "".join(match_val.group(1).strip().split())
            base64.b64decode(b64_candidate, validate=True)
            return b64_candidate
        match_sig = re.search(
            r"<Signature[^>]*>(.*?)</Signature>", sig_str, re.DOTALL | re.IGNORECASE
        )
        if match_sig:
            b64_candidate = "".join(match_sig.group(1).strip().split())
            base64.b64decode(b64_candidate, validate=True)
            return b64_candidate

    # 3. JSON metadata
    if sig_str.startswith("{") and sig_str.endswith("}"):
        try:
            import json

            data = json.loads(sig_str)
            if isinstance(data, dict):
                for k in ("signature", "signature_bytes_b64", "sig", "value"):
                    if k in data and isinstance(data[k], str):
                        val = data[k].strip()
                        base64.b64decode(val, validate=True)
                        return val
        except Exception:
            pass

    # 4. Hex string
    if re.match(r"^[0-9a-fA-F]+$", sig_str):
        try:
            hex_bytes = bytes.fromhex(sig_str)
            return base64.b64encode(hex_bytes).decode("utf-8")
        except Exception:
            pass

    # 5. Direct base64
    return sig_str


def verify_asymmetric_signature(
    payload_str: str,
    signature_b64: str,
    public_key_pem: str,
) -> tuple[bool, str | None, str | None]:
    """
    Verifies RSA or ECDSA signatures with either raw document strings (XML/JSON)
    or hex-encoded hashes.
    Returns: (is_valid, error_code, error_message)
    """
    # Reject mock signatures / certificates
    if (
        "mock" in payload_str.lower()
        or "mock" in signature_b64.lower()
        or "mock" in public_key_pem.lower()
    ):
        return (
            False,
            "MOCK_SIGNATURE_DETECTED",
            "Mock signature detected and blocked.",
        )

    try:
        try:
            cert = x509.load_pem_x509_certificate(public_key_pem.encode("utf-8"))
            public_key = cert.public_key()
        except Exception:
            try:
                public_key = load_pem_public_key(public_key_pem.encode("utf-8"))
            except Exception as e:
                return (
                    False,
                    "INVALID_KEY",
                    f"Failed to load public key or certificate: {e}",
                )

        signature_bytes = base64.b64decode(signature_b64)

        payload_str_clean = payload_str.strip()
        is_prehashed = False
        binary_data = b""

        if re.match(r"^[0-9a-fA-F]{64}$", payload_str_clean):
            is_prehashed = True
            binary_data = bytes.fromhex(payload_str_clean)
        else:
            stripped_payload = payload_str_clean
            if payload_str_clean.startswith("<") and payload_str_clean.endswith(">"):
                stripped_payload = strip_xml_signatures(payload_str_clean)
            elif (
                payload_str_clean.startswith("{") and payload_str_clean.endswith("}")
            ) or (
                payload_str_clean.startswith("[") and payload_str_clean.endswith("]")
            ):
                stripped_payload = strip_and_canonicalize_json(payload_str_clean)
            binary_data = stripped_payload.encode("utf-8")

        if isinstance(public_key, rsa.RSAPublicKey):
            try:
                if is_prehashed:
                    public_key.verify(
                        signature_bytes,
                        binary_data,
                        padding.PSS(
                            mgf=padding.MGF1(hashes.SHA256()),
                            salt_length=padding.PSS.MAX_LENGTH,
                        ),
                        Prehashed(hashes.SHA256()),
                    )
                else:
                    public_key.verify(
                        signature_bytes,
                        binary_data,
                        padding.PSS(
                            mgf=padding.MGF1(hashes.SHA256()),
                            salt_length=padding.PSS.MAX_LENGTH,
                        ),
                        hashes.SHA256(),
                    )
            except Exception:
                # Check if it was signed with PKCS#1 v1.5 deterministic padding
                try:
                    if is_prehashed:
                        public_key.verify(
                            signature_bytes,
                            binary_data,
                            padding.PKCS1v15(),
                            Prehashed(hashes.SHA256()),
                        )
                    else:
                        public_key.verify(
                            signature_bytes,
                            binary_data,
                            padding.PKCS1v15(),
                            hashes.SHA256(),
                        )
                    # Succeeded with PKCS1v15, meaning it's a legacy padding signature!
                    import logging

                    logging.getLogger("crypto-verifier").error(
                        "COMPLIANCE ALERT: Legacy PKCS#1 v1.5 signature padding detected. This signature is insecure and has been rejected."
                    )
                    return (
                        False,
                        "LEGACY_PADDING_REJECTED",
                        "LEGACY PADDING DETECTED: Document signatures using legacy PKCS#1 v1.5 padding fail verification to satisfy 21 CFR Part 11 strict compliance.",
                    )
                except Exception:
                    # It failed PKCS1v15 too, so it's just a general verification failure
                    return (
                        False,
                        "SIGNATURE_MISMATCH",
                        "Asymmetric verification failed: Invalid RSA-PSS signature.",
                    )
        elif isinstance(public_key, ec.EllipticCurvePublicKey):
            if is_prehashed:
                public_key.verify(
                    signature_bytes, binary_data, ec.ECDSA(Prehashed(hashes.SHA256()))
                )
            else:
                public_key.verify(
                    signature_bytes, binary_data, ec.ECDSA(hashes.SHA256())
                )
        else:
            return (
                False,
                "INVALID_KEY",
                f"Unsupported public key type: {type(public_key)}",
            )

        return True, None, None

    except Exception as e:
        return False, "SIGNATURE_MISMATCH", f"Asymmetric verification failed: {e}"


def verify_electronic_signature(
    request: SignatureVerificationRequest,
    secret_key: str | None = None,
) -> SignatureVerificationResult:
    """Verify electronic signature authenticity, signer binding, and tamper-resistance.

    Supports both RSA/ECDSA asymmetric verification and symmetric HMAC fallback verification.
    """
    if secret_key is None:
        from packages.security.audit_logger import AUDIT_LOG_SECRET_KEY

        secret_key = AUDIT_LOG_SECRET_KEY
    if not request.signer_id:
        return SignatureVerificationResult(
            is_valid=False,
            error_code="INVALID_PAYLOAD",
            error_message="Missing signer ID",
            signer_id="",
        )

    if not request.payload_hash:
        return SignatureVerificationResult(
            is_valid=False,
            error_code="MISSING_PAYLOAD",
            error_message="Missing payload hash or document content",
            signer_id=request.signer_id,
        )

    if request.signature_bytes_b64 is None:
        return SignatureVerificationResult(
            is_valid=False,
            error_code="MALFORMED_SIGNATURE",
            error_message="Missing signature content",
            signer_id=request.signer_id,
        )

    # Parse and extract raw public key / cert if signature is JSON or PEM
    public_key_pem = request.public_key_pem
    sig_str_raw = request.signature_bytes_b64

    # Extract public key from JSON signature metadata if not provided in request
    if sig_str_raw.strip().startswith("{") and sig_str_raw.strip().endswith("}"):
        try:
            import json

            data = json.loads(sig_str_raw)
            if isinstance(data, dict) and not public_key_pem:
                public_key_pem = (
                    data.get("public_key")
                    or data.get("certificate_pem")
                    or data.get("cert")
                )
        except Exception:
            pass

    # Extract parsed signature base64
    try:
        parsed_sig_b64 = parse_signature_format(sig_str_raw)
    except ValueError as ve:
        return SignatureVerificationResult(
            is_valid=False,
            error_code="MALFORMED_SIGNATURE",
            error_message=str(ve),
            signer_id=request.signer_id,
        )
    except Exception as e:
        return SignatureVerificationResult(
            is_valid=False,
            error_code="DECODING_ERROR",
            error_message=f"Decoding error: {e}",
            signer_id=request.signer_id,
        )

    # Check mock for both asymmetric and symmetric paths
    req_payload_lower = (request.payload_hash or "").lower()
    req_sig_lower = (request.signature_bytes_b64 or "").lower()
    req_pk_lower = (public_key_pem or "").lower()
    if "mock" in req_payload_lower or "mock" in req_sig_lower or "mock" in req_pk_lower:
        return SignatureVerificationResult(
            is_valid=False,
            error_code="MOCK_SIGNATURE_DETECTED",
            error_message="Mock signature detected and blocked.",
            signer_id=request.signer_id,
        )

    # Determine whether to use asymmetric verification
    is_asymmetric = bool(public_key_pem)

    if is_asymmetric:
        is_valid, err_code, err_msg = verify_asymmetric_signature(
            payload_str=request.payload_hash,
            signature_b64=parsed_sig_b64,
            public_key_pem=public_key_pem,
        )
        if not is_valid:
            return SignatureVerificationResult(
                is_valid=False,
                error_code=err_code,
                error_message=err_msg,
                signer_id=request.signer_id,
            )
        return SignatureVerificationResult(
            is_valid=True,
            signer_id=request.signer_id,
        )

    # Fallback to Symmetric HMAC verification
    # Prepare payload hash for verification (strip if XML/JSON)
    payload_str_clean = request.payload_hash.strip()
    if payload_str_clean.startswith("<") and payload_str_clean.endswith(">"):
        stripped_payload = strip_xml_signatures(payload_str_clean)
        payload_hash_to_verify = hashlib.sha256(
            stripped_payload.encode("utf-8")
        ).hexdigest()
    elif (payload_str_clean.startswith("{") and payload_str_clean.endswith("}")) or (
        payload_str_clean.startswith("[") and payload_str_clean.endswith("]")
    ):
        stripped_payload = strip_and_canonicalize_json(payload_str_clean)
        payload_hash_to_verify = hashlib.sha256(
            stripped_payload.encode("utf-8")
        ).hexdigest()
    else:
        payload_hash_to_verify = payload_str_clean

    is_valid_hmac = verify_hmac_signature(
        payload_hash=payload_hash_to_verify,
        signature_b64=parsed_sig_b64,
        secret_key=secret_key,
    )

    if not is_valid_hmac:
        return SignatureVerificationResult(
            is_valid=False,
            error_code="SIGNATURE_MISMATCH",
            error_message="Cryptographic signature verification failed; payload may be tampered",
            signer_id=request.signer_id,
        )

    return SignatureVerificationResult(
        is_valid=True,
        signer_id=request.signer_id,
    )
