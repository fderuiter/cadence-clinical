import base64
import hashlib
import hmac
import json
import os
from typing import Any

from cryptography import x509
from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.asymmetric import ec, padding, rsa
from cryptography.hazmat.primitives.serialization import (
    load_pem_private_key,
    load_pem_public_key,
)


def normalize_scope_values(
    site_id_input: Any,
    sponsor_id_input: Any,
    unblinded_access_input: Any,
) -> tuple[str | None, str | None, bool]:
    """Normalizes site_id, sponsor_id, and unblinded_access inputs into their canonical types

    expected by generate_gateway_signature and verify_gateway_signature.
    """
    # 1. Normalize site_id
    if site_id_input is None:
        site_id_val = None
    elif isinstance(site_id_input, list):
        site_id_val = ",".join(str(s).strip() for s in site_id_input if str(s).strip())
        if not site_id_val:
            site_id_val = None
    else:
        site_id_str = str(site_id_input).strip()
        site_id_val = site_id_str if site_id_str else None

    # 2. Normalize sponsor_id
    if sponsor_id_input is None:
        sponsor_id_val = None
    elif isinstance(sponsor_id_input, list):
        sponsor_id_val = ",".join(
            str(s).strip() for s in sponsor_id_input if str(s).strip()
        )
        if not sponsor_id_val:
            sponsor_id_val = None
    else:
        sponsor_id_str = str(sponsor_id_input).strip()
        sponsor_id_val = sponsor_id_str if sponsor_id_str else None

    # 3. Normalize unblinded_access
    unblinded_access_val = False
    if isinstance(unblinded_access_input, bool):
        unblinded_access_val = unblinded_access_input
    elif unblinded_access_input is not None:
        val_str = str(unblinded_access_input).strip().lower()
        if val_str in ("true", "1", "yes"):
            unblinded_access_val = True

    return site_id_val, sponsor_id_val, unblinded_access_val


def generate_gateway_signature(
    user_id: str,
    roles: str,
    timestamp: str,
    secret: bytes,
    change_reason: str | None = None,
    site_id: str | None = None,
    sponsor_id: str | None = None,
    unblinded_access: bool = False,
    tenant_id: str | None = None,
    sig_token: str | None = None,
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
    if sig_token is not None:
        payload["sig_token"] = sig_token
    serialized = json.dumps(payload, sort_keys=True, separators=(",", ":"))
    return hmac.new(secret, serialized.encode("utf-8"), hashlib.sha256).hexdigest()


def verify_gateway_signature(
    user_id: str,
    roles: str,
    timestamp: str,
    signature: str,
    secret: bytes,
    change_reason: str | None = None,
    site_id: str | None = None,
    sponsor_id: str | None = None,
    unblinded_access: bool = False,
    tenant_id: str | None = None,
    sig_token: str | None = None,
) -> bool:
    """Verifies an HMAC-SHA256 signature for API Gateway identity and scope headers.

    The verification flow ensures that:
    1. Scope-bearing requests must validate either against the canonical 8-field scope-aware payload (including tenant_id)
       or the 7-field compatibility fallback (tenant_id=None) preserving all scope bindings.
    2. Fallback verification paths (re-serialization with cleared scopes or legacy 4-field payload)
       are strictly restricted to scope-free requests only, consistent with ADR-86.
    """
    # 1. Verify with the canonical 9-field scope-aware payload (includes tenant_id and sig_token)
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
        sig_token=sig_token,
    )
    if hmac.compare_digest(expected, signature):
        return True

    # Fallback 0: Try verifying with canonical 8-field scope-aware payload (no sig_token)
    if sig_token:
        fallback_8_field = generate_gateway_signature(
            user_id=user_id,
            roles=roles,
            timestamp=timestamp,
            secret=secret,
            change_reason=change_reason,
            site_id=site_id,
            sponsor_id=sponsor_id,
            unblinded_access=unblinded_access,
            tenant_id=tenant_id,
            sig_token=None,
        )
        if hmac.compare_digest(fallback_8_field, signature):
            return True

    # Fallback 1: Try verifying with compatibility fallback (tenant_id=None)
    # for requests signed before tenant propagation was introduced.
    # Note: This is safe because scope-level variables (site_id, sponsor_id, unblinded_access)
    # are kept completely intact in the payload serialization.
    has_other_scopes = bool(site_id or sponsor_id or unblinded_access)
    if tenant_id and has_other_scopes:
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
            sig_token=sig_token,
        )
        if hmac.compare_digest(fallback_tenant_expected, signature):
            return True

        if sig_token:
            fallback_tenant_no_sig = generate_gateway_signature(
                user_id=user_id,
                roles=roles,
                timestamp=timestamp,
                secret=secret,
                change_reason=change_reason,
                site_id=site_id,
                sponsor_id=sponsor_id,
                unblinded_access=unblinded_access,
                tenant_id=None,
                sig_token=None,
            )
            if hmac.compare_digest(fallback_tenant_no_sig, signature):
                return True

    # 2. Scope-free fallbacks: Only permitted if no scope fields are present/active.
    # If any scope values are present, they are scope-bearing requests and must not fall back to cleared scopes.
    has_scopes = bool(
        site_id
        or sponsor_id
        or unblinded_access
        or (tenant_id and tenant_id != "tenant_default")
    )
    if not has_scopes:
        # Fallback 2: Verify using the scope-free payload serialization with all scopes cleared/defaulted.
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
            sig_token=sig_token,
        )
        if hmac.compare_digest(no_scope_expected, signature):
            return True

        if sig_token:
            no_scope_no_sig = generate_gateway_signature(
                user_id=user_id,
                roles=roles,
                timestamp=timestamp,
                secret=secret,
                change_reason=change_reason,
                site_id=None,
                sponsor_id=None,
                unblinded_access=False,
                tenant_id=None,
                sig_token=None,
            )
            if hmac.compare_digest(no_scope_no_sig, signature):
                return True

        # Fallback 3: Verify using the legacy 4-field V2 payload (backward compatibility for identity-only signatures).
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


def canonical_serialize(payload: dict[str, Any]) -> bytes:
    """Serializes a dictionary into a key-sorted, whitespace-stripped UTF-8 JSON byte string."""
    return json.dumps(payload, sort_keys=True, separators=(",", ":")).encode("utf-8")


def generate_canonical_signature(payload: dict[str, Any], secret: bytes) -> str:
    """Generates an HMAC-SHA256 signature of a canonically serialized JSON payload.

    Used to guarantee cryptographic integrity for study versions and protocol locks
    before persistence.
    """
    serialized = canonical_serialize(payload)
    return hmac.new(secret, serialized, hashlib.sha256).hexdigest()


def verify_canonical_signature(
    payload: dict[str, Any], signature: str, secret: bytes
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
    data: bytes, private_key_pem: str, password: bytes | None = None
) -> str:
    """Signs data using a PEM-encoded private key (RSA or Elliptic Curve) and returns a Base64-encoded signature.

    Used to guarantee certificate-bound identity signatures for GxP operations.
    """
    private_key = load_pem_private_key(
        private_key_pem.encode("utf-8"), password=password
    )
    if isinstance(private_key, rsa.RSAPrivateKey):
        signature_bytes = private_key.sign(
            data,
            padding.PSS(
                mgf=padding.MGF1(hashes.SHA256()),
                salt_length=padding.PSS.MAX_LENGTH,
            ),
            hashes.SHA256(),
        )
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
                signature_bytes,
                data,
                padding.PSS(
                    mgf=padding.MGF1(hashes.SHA256()),
                    salt_length=padding.PSS.MAX_LENGTH,
                ),
                hashes.SHA256(),
            )
        elif isinstance(public_key, ec.EllipticCurvePublicKey):
            public_key.verify(signature_bytes, data, ec.ECDSA(hashes.SHA256()))
        else:
            return False
        return True
    except Exception:
        return False


def capture_certificate_identifiers(cert_pem: str) -> dict[str, str]:
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


class InboundEmailReplayCache:
    def __init__(self) -> None:
        self.used_keys: dict[str, float] = {}

    def is_replayed(self, key: str, ttl: float = 300) -> bool:
        import time

        now = time.time()
        # Prune expired keys
        self.used_keys = {k: exp for k, exp in self.used_keys.items() if exp > now}
        if key in self.used_keys:
            return True
        self.used_keys[key] = now + ttl
        return False


def get_inbound_email_hmac_secret() -> str:
    """Retrieve the inbound email HMAC secret from settings, failing if unset."""
    from packages.config import get_common_settings

    secret = (get_common_settings().inbound_email_hmac_secret or "").strip()
    if not secret:
        raise RuntimeError(
            "INBOUND_EMAIL_HMAC_SECRET environment variable is missing or empty"
        )
    return secret


inbound_email_replay_cache = InboundEmailReplayCache()


def verify_inbound_email_signature(
    timestamp: str,
    token: str,
    signature: str,
    message_id: str | None = None,
) -> bool:
    """Verifies that the inbound email HMAC signature is correct, fresh, and not replayed."""
    import time

    secret = get_inbound_email_hmac_secret()

    # 1. Timestamp Freshness Check (300-second drift window)
    try:
        ts = float(timestamp)
        if abs(time.time() - ts) > 300:
            return False
    except (ValueError, TypeError):  # fmt: skip
        return False

    # 2. Replay Protection
    replay_key = token or message_id
    if not replay_key:
        return False

    if inbound_email_replay_cache.is_replayed(replay_key):
        return False

    # 3. Signature Verification
    expected_sig = hmac.new(
        secret.encode("utf-8") if isinstance(secret, str) else secret,
        f"{timestamp}{token}".encode(),
        hashlib.sha256,
    ).hexdigest()

    return hmac.compare_digest(expected_sig, signature)


def generate_greenlight_token(
    site_id: str,
    study_id: str,
    country_code: str,
    certified_by: str,
    expires_in_seconds: int = 86400 * 365,
    secret: bytes | None = None,
    is_overridden: bool = False,
) -> str:
    """Generates an HMAC-SHA256 signed cryptographic Site Greenlight Activation Token.

    Compliant with PRD-SSU-003, ADR-2202, ICH GCP E6(R2) Section 8, and 21 CFR Part 11.
    """
    import time

    if secret is None:
        from packages.config import get_common_settings

        raw_secret = get_common_settings().gateway_secret
        secret = (
            raw_secret.encode("utf-8") if isinstance(raw_secret, str) else raw_secret
        )

    now = int(time.time())
    payload = {
        "site_id": site_id,
        "study_id": study_id,
        "country_code": country_code.upper(),
        "certified_by": certified_by,
        "token_type": "SITE_GREENLIGHT_ACTIVATION",
        "token_version": "1",
        "is_overridden": is_overridden,
        "issued_at": now,
        "expires_at": now + expires_in_seconds,
    }
    serialized = canonical_serialize(payload)
    b64_payload = base64.urlsafe_b64encode(serialized).decode("utf-8").rstrip("=")
    sig = hmac.new(secret, b64_payload.encode("utf-8"), hashlib.sha256).hexdigest()
    return f"{b64_payload}.{sig}"


def verify_greenlight_token(
    token: str,
    site_id: str | None = None,
    study_id: str | None = None,
    secret: bytes | None = None,
) -> dict[str, Any] | None:
    """Verifies HMAC-SHA256 signature, expiry, and site/study binding of a Greenlight Activation Token.

    Returns the decoded token payload dictionary if valid, or None if invalid/expired/tampered.
    """
    import time

    if not token or "." not in token:
        return None

    if secret is None:
        from packages.config import get_common_settings

        raw_secret = get_common_settings().gateway_secret
        secret = (
            raw_secret.encode("utf-8") if isinstance(raw_secret, str) else raw_secret
        )

    try:
        parts = token.split(".")
        if len(parts) != 2:
            return None
        b64_payload, signature = parts[0], parts[1]

        # Verify signature
        expected_sig = hmac.new(
            secret, b64_payload.encode("utf-8"), hashlib.sha256
        ).hexdigest()
        if not hmac.compare_digest(expected_sig, signature):
            return None

        # Decode payload (pad base64url string if needed)
        padding_needed = len(b64_payload) % 4
        if padding_needed:
            b64_payload += "=" * (4 - padding_needed)
        payload_bytes = base64.urlsafe_b64decode(b64_payload.encode("utf-8"))
        payload = json.loads(payload_bytes.decode("utf-8"))

        if payload.get("token_type") != "SITE_GREENLIGHT_ACTIVATION":
            return None

        if site_id and payload.get("site_id") != site_id:
            return None

        if study_id and payload.get("study_id") != study_id:
            return None

        # Expiry check
        expires_at = payload.get("expires_at")
        if expires_at and expires_at < time.time():
            return None

        return payload
    except Exception:
        return None
