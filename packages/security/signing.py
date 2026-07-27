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
) -> bool:
    """Verifies an HMAC-SHA256 signature for API Gateway identity and scope headers."""
    expected = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=secret,
        change_reason=change_reason,
        site_id=site_id,
        sponsor_id=sponsor_id,
        unblinded_access=unblinded_access,
    )
    return hmac.compare_digest(expected, signature)


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
