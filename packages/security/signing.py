import hashlib
import hmac
import json
from typing import Any, Dict


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
