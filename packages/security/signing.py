import base64
import hashlib
import hmac
import json
from typing import Any, Dict, Optional

from cryptography import x509
from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.asymmetric import ec, padding, rsa
from cryptography.hazmat.primitives.serialization import (
    load_pem_private_key,
    load_pem_public_key,
)


def generate_gateway_signature(
    user_id: str,
    roles: str,
    timestamp: str,
    secret: bytes,
    change_reason: Optional[str] = None,
    site_id: Optional[str] = None,
    sponsor_id: Optional[str] = None,
    unblinded_access: bool = False,
    tenant_id: Optional[str] = None,
) -> str:
    """Generates an HMAC-SHA256 signature for API Gateway identity and scope headers."""
    payload = {
        "change_reason": change_reason if change_reason is not None else "",
        "roles": roles,
        "timestamp": timestamp,
        "user_id": user_id,
        "site_id": site_id if site_id is not None else "",
        "sponsor_id": sponsor_id if sponsor_id is not None else "",
        "unblinded_access": unblinded_access,
        "tenant_id": tenant_id if tenant_id is not None else "",
    }
    serialized = json.dumps(payload, sort_keys=True, separators=(",", ":"))
    return hmac.new(secret, serialized.encode("utf-8"), hashlib.sha256).hexdigest()


def verify_gateway_signature(
    user_id: str,
    roles: str,
    timestamp: str,
    signature: str,
    secret: bytes,
    change_reason: Optional[str] = None,
    site_id: Optional[str] = None,
    sponsor_id: Optional[str] = None,
    unblinded_access: bool = False,
    tenant_id: Optional[str] = None,
) -> bool:
    """Verifies an HMAC-SHA256 signature for API Gateway identity and scope headers."""
    # 1. Verify with the full 8-field scope-aware payload (includes tenant_id)
    expected = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=secret,
        change_reason=change_reason,
        site_id=site_id,
        sponsor_id=sponsor_id,
        unblinded_access=unblinded_access,
        tenant_id=tenant_id,
    )
    if hmac.compare_digest(expected, signature):
        return True

    # Fallback 1: Try verifying with tenant_id=None as compatibility for requests
    # signed before tenant propagation was introduced.
    if tenant_id:
        fallback_tenant_expected = generate_gateway_signature(
            user_id=user_id,
            roles=roles,
            timestamp=timestamp,
            secret=secret,
            change_reason=change_reason,
            site_id=site_id,
            sponsor_id=sponsor_id,
            unblinded_access=unblinded_access,
            tenant_id=None,
        )
        if hmac.compare_digest(fallback_tenant_expected, signature):
            return True

    # Fallback 2: The sender generated the signature but did not pass site_id/sponsor_id/unblinded_access/tenant_id to the generator.
    if site_id or sponsor_id or unblinded_access or tenant_id:
        fallback_expected = generate_gateway_signature(
            user_id=user_id,
            roles=roles,
            timestamp=timestamp,
            secret=secret,
            change_reason=change_reason,
            site_id=None,
            sponsor_id=None,
            unblinded_access=False,
            tenant_id=None,
        )
        if hmac.compare_digest(fallback_expected, signature):
            return True

    # 2. Compatibility check: Fallbacks are ONLY permitted if no scope fields are present/active.
    # If any scope values are present, they are scope-bearing requests and must verify using the payload.
    has_scopes = bool(
        site_id
        or sponsor_id
        or unblinded_access
        or (tenant_id and tenant_id != "tenant_default")
    )
    if not has_scopes:
        # Fallback 1: Verify as if scope fields were omitted from the signature generation
        no_scope_expected = generate_gateway_signature(
            user_id=user_id,
            roles=roles,
            timestamp=timestamp,
            secret=secret,
            change_reason=change_reason,
            site_id=None,
            sponsor_id=None,
            unblinded_access=False,
            tenant_id=None,
        )
        if hmac.compare_digest(no_scope_expected, signature):
            return True

        # Fallback 2: Verify with legacy 4-field payload for backward compatibility
        legacy_payload = {
            "change_reason": change_reason if change_reason is not None else "",
            "roles": roles,
            "timestamp": timestamp,
            "user_id": user_id,
        }
        legacy_serialized = json.dumps(
            legacy_payload, sort_keys=True, separators=(",", ":")
        )
        legacy_expected = hmac.new(
            secret, legacy_serialized.encode("utf-8"), hashlib.sha256
        ).hexdigest()
        if hmac.compare_digest(legacy_expected, signature):
            return True

    return False


def canonical_serialize(payload: Dict[str, Any]) -> bytes:
    """Serializes a dictionary into a key-sorted, whitespace-stripped UTF-8 JSON byte string."""
    return json.dumps(payload, sort_keys=True, separators=(",", ":")).encode("utf-8")


def generate_canonical_signature(payload: Dict[str, Any], secret: bytes) -> str:
    """Generates an HMAC-SHA256 signature of a canonically serialized JSON payload.

    Used to guarantee cryptographic integrity for study versions and protocol locks
    before persistence.
    """
    serialized = canonical_serialize(payload)
    return hmac.new(secret, serialized, hashlib.sha256).hexdigest()


def verify_canonical_signature(
    payload: Dict[str, Any], signature: str, secret: bytes
) -> bool:
    """Verifies that the provided HMAC-SHA256 signature matches the canonically serialized JSON payload.

    Used to validate cryptographic integrity for study versions and protocol locks
    before loading or processing.
    """
    expected_sig = generate_canonical_signature(payload, secret)
    return hmac.compare_digest(expected_sig, signature)


def compute_sha256_hash(data: bytes | str) -> str:
    """Computes the hex-encoded SHA-256 hash of a string or byte string."""
    if isinstance(data, str):
        data = data.encode("utf-8")
    return hashlib.sha256(data).hexdigest()


def asymmetric_sign(
    data: bytes, private_key_pem: str, password: Optional[bytes] = None
) -> str:
    """Signs data using a PEM-encoded private key (RSA or Elliptic Curve) and returns a Base64-encoded signature.

    Used to guarantee certificate-bound identity signatures for GxP operations.
    """
    private_key = load_pem_private_key(
        private_key_pem.encode("utf-8"), password=password
    )
    if isinstance(private_key, rsa.RSAPrivateKey):
        signature_bytes = private_key.sign(data, padding.PKCS1v15(), hashes.SHA256())
    elif isinstance(private_key, ec.EllipticCurvePrivateKey):
        signature_bytes = private_key.sign(data, ec.ECDSA(hashes.SHA256()))
    else:
        raise ValueError("Unsupported private key type for asymmetric signing.")
    return base64.b64encode(signature_bytes).decode("utf-8")


def asymmetric_verify(
    data: bytes, signature_b64: str, public_key_pem_or_cert_pem: str
) -> bool:
    """Verifies a Base64-encoded asymmetric signature of data using a public key or X.509 certificate (RSA or EC)."""
    try:
        # Attempt to load as X.509 certificate first
        try:
            cert = x509.load_pem_x509_certificate(
                public_key_pem_or_cert_pem.encode("utf-8")
            )
            public_key = cert.public_key()
        except Exception:
            # If not a certificate, load directly as a public key
            public_key = load_pem_public_key(public_key_pem_or_cert_pem.encode("utf-8"))

        signature_bytes = base64.b64decode(signature_b64.encode("utf-8"))

        if isinstance(public_key, rsa.RSAPublicKey):
            public_key.verify(
                signature_bytes, data, padding.PKCS1v15(), hashes.SHA256()
            )
        elif isinstance(public_key, ec.EllipticCurvePublicKey):
            public_key.verify(signature_bytes, data, ec.ECDSA(hashes.SHA256()))
        else:
            return False
        return True
    except Exception:
        return False


def capture_certificate_identifiers(cert_pem: str) -> Dict[str, str]:
    """Captures key and certificate identifiers (serial_number, sha256_fingerprint, subject_key_identifier)

    from a PEM-encoded X.509 certificate.
    """
    cert = x509.load_pem_x509_certificate(cert_pem.encode("utf-8"))
    serial_number = str(cert.serial_number)
    sha256_fingerprint = cert.fingerprint(hashes.SHA256()).hex()

    ski = None
    try:
        ski_ext = cert.extensions.get_extension_for_class(x509.SubjectKeyIdentifier)
        ski = ski_ext.value.digest.hex()
    except Exception:
        pass

    return {
        "serial_number": serial_number,
        "sha256_fingerprint": sha256_fingerprint,
        "subject_key_identifier": ski or sha256_fingerprint,
    }


def clean_json_val(val: Any) -> str:
    """
    Ensure consistent, deterministic serialization of JSON values for hashing.
    Parses and formats dictionaries/lists to remove any whitespace or key order differences.
    """
    if val is None:
        return "null"
    if isinstance(val, (dict, list)):
        return json.dumps(val, sort_keys=True)
    if isinstance(val, str):
        try:
            parsed = json.loads(val)
            return json.dumps(parsed, sort_keys=True)
        except Exception:
            return json.dumps(val)
    return json.dumps(val)


def compute_merkle_root(record_hashes: list[str]) -> str:
    """
    Computes the Merkle Root hash from a list of record hashes.
    """
    combined_records_payload = "".join(record_hashes).encode("utf-8")
    return hashlib.sha256(combined_records_payload).hexdigest()


def compute_block_hash(previous_hash: str, merkle_root: str) -> str:
    """
    Computes a sequential block-level chaining hash using the previous block hash and the current Merkle root.
    """
    block_input = (previous_hash + merkle_root).encode("utf-8")
    return hashlib.sha256(block_input).hexdigest()
