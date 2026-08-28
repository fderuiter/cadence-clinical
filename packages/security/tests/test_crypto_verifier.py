"""Unit test suite for Cryptographic Verification Engine and Security Middleware.

Requirements: PRD-SYS-053, Trace-14, Trace-15
"""

import base64
import importlib
import logging
import sys

import pytest
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import ec, padding, rsa
from cryptography.hazmat.primitives.asymmetric.utils import Prehashed

from packages.security.crypto_verifier import (
    verify_asymmetric_signature,
    verify_hmac_signature,
)
from packages.security.middleware import downstream_replay_cache, verify_sig_token


def test_signing_module_import_safety_unset_secrets(monkeypatch):
    """Verify that packages.security.signing imports safely when environment secrets are unset.

    @req:PRD-SYS-053
    @req:Trace-14
    """
    monkeypatch.delenv("INBOUND_EMAIL_HMAC_SECRET", raising=False)
    if "packages.security.signing" in sys.modules:
        importlib.reload(sys.modules["packages.security.signing"])
    import packages.security.signing as signing

    # Top-level import succeeds
    assert signing is not None

    # Calling getter without env set raises RuntimeError
    with pytest.raises(
        RuntimeError,
        match="INBOUND_EMAIL_HMAC_SECRET environment variable is missing or empty",
    ):
        signing.get_inbound_email_hmac_secret()


def test_audit_logger_module_import_safety_unset_secrets(monkeypatch):
    """Verify that packages.security.audit_logger imports safely when secrets are unset.

    @req:PRD-SYS-053
    @req:Trace-14
    """
    monkeypatch.delenv("AUDIT_LOG_SECRET_KEY", raising=False)
    if "packages.security.audit_logger" in sys.modules:
        importlib.reload(sys.modules["packages.security.audit_logger"])
    import packages.security.audit_logger as audit_logger

    assert audit_logger is not None

    with pytest.raises(
        RuntimeError,
        match="AUDIT_LOG_SECRET_KEY environment variable is missing or empty",
    ):
        audit_logger.get_audit_log_secret_key()


def test_crypto_verifier_asymmetric_rsa_pss():
    """Verify that RSA-PSS signatures are verified properly.

    @req:PRD-SYS-053
    @req:Trace-14
    """
    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    pub_key_pem = (
        key.public_key()
        .public_bytes(
            serialization.Encoding.PEM,
            serialization.PublicFormat.SubjectPublicKeyInfo,
        )
        .decode("utf-8")
    )

    data = b"GxP Audit Record Submission 2026-08-28"
    hasher = hashes.Hash(hashes.SHA256())
    hasher.update(data)
    digest = hasher.finalize()

    # Prehashed signing
    sig = key.sign(
        digest,
        padding.PSS(
            mgf=padding.MGF1(hashes.SHA256()),
            salt_length=padding.PSS.MAX_LENGTH,
        ),
        Prehashed(hashes.SHA256()),
    )
    sig_b64 = base64.b64encode(sig).decode("utf-8")

    is_valid, err_code, err_msg = verify_asymmetric_signature(
        payload_str=digest.hex(),
        signature_b64=sig_b64,
        public_key_pem=pub_key_pem,
    )
    assert is_valid is True
    assert err_code is None


def test_crypto_verifier_legacy_padding_rejected():
    """Verify that insecure legacy PKCS#1 v1.5 padding is rejected by RSA-PSS verifier.

    @req:PRD-SYS-053
    @req:Trace-14
    """
    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    pub_key_pem = (
        key.public_key()
        .public_bytes(
            serialization.Encoding.PEM,
            serialization.PublicFormat.SubjectPublicKeyInfo,
        )
        .decode("utf-8")
    )

    data = b"Legacy Payload"
    hasher = hashes.Hash(hashes.SHA256())
    hasher.update(data)
    digest = hasher.finalize()

    # Sign using legacy PKCS1v15 padding
    sig = key.sign(digest, padding.PKCS1v15(), Prehashed(hashes.SHA256()))
    sig_b64 = base64.b64encode(sig).decode("utf-8")

    is_valid, err_code, err_msg = verify_asymmetric_signature(
        payload_str=digest.hex(),
        signature_b64=sig_b64,
        public_key_pem=pub_key_pem,
    )
    assert is_valid is False
    assert err_code == "LEGACY_PADDING_REJECTED"


def test_crypto_verifier_ecdsa():
    """Verify ECDSA SHA-256 signature verification.

    @req:PRD-SYS-053
    @req:Trace-14
    """
    key = ec.generate_private_key(ec.SECP256R1())
    pub_key_pem = (
        key.public_key()
        .public_bytes(
            serialization.Encoding.PEM,
            serialization.PublicFormat.SubjectPublicKeyInfo,
        )
        .decode("utf-8")
    )

    data = b"Clinical Study Consent Record"
    hasher = hashes.Hash(hashes.SHA256())
    hasher.update(data)
    digest = hasher.finalize()

    sig = key.sign(digest, ec.ECDSA(Prehashed(hashes.SHA256())))
    sig_b64 = base64.b64encode(sig).decode("utf-8")

    is_valid, err_code, err_msg = verify_asymmetric_signature(
        payload_str=digest.hex(),
        signature_b64=sig_b64,
        public_key_pem=pub_key_pem,
    )
    assert is_valid is True
    assert err_code is None


def test_crypto_verifier_hmac():
    """Verify HMAC symmetric signature verification.

    @req:PRD-SYS-053
    @req:Trace-14
    """
    import hmac

    secret = "production-hmac-shared-secret-12345"
    data = b"Inbound Webhook Payload"
    hasher = hashes.Hash(hashes.SHA256())
    hasher.update(data)
    digest = hasher.finalize()

    sig = hmac.new(
        secret.encode("utf-8"), digest.hex().encode("utf-8"), "sha256"
    ).digest()
    sig_b64 = base64.b64encode(sig).decode("utf-8")

    assert verify_hmac_signature(digest.hex(), sig_b64, secret) is True
    assert verify_hmac_signature(digest.hex(), "invalid_b64", secret) is False


def test_middleware_masked_logging(caplog):
    """Verify that verify_sig_token logs structured debug logs and masks sensitive tokens.

    @req:PRD-SYS-053
    @req:Trace-14
    """
    with caplog.at_level(logging.DEBUG):
        verify_sig_token(
            sig_token="eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.sensitive_payload.xyz123",
            user_id="site_crc_user",
            request_path="/api/v1/execution/submit",
            secret=b"test-secret-123",
            replay_cache=downstream_replay_cache,
        )

    log_text = caplog.text
    assert "VERIFY_SIG_TOKEN" in log_text
    assert "sensitive_payload" not in log_text
    assert "eyJh...z123" in log_text
