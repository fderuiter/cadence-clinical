"""Unit and integration test suite for Compliance and Security Hub.

Validates 21 CFR Part 11 AuditLoggerEngine, SHA-256 digest chain tamper-resistance,
and cryptographic signature verification helper functions.

Requirements: PRD-SYS-001, 21 CFR Part 11
"""

from packages.security.audit_logger import (
    AuditLoggerEngine,
    AuditLogPayload,
)
from packages.security.crypto_verifier import (
    SignatureVerificationRequest,
    verify_electronic_signature,
)
from scripts.audit_security import run_security_audit


def test_audit_logger_creates_valid_record():
    """Verify AuditLoggerEngine appends valid AuditLogRecord with SHA-256 digest.

    Requirements: PRD-SYS-001, 21 CFR Part 11
    """
    engine = AuditLoggerEngine(secret_key="test-secret-key")
    payload = AuditLogPayload(
        service_name="apps/execution",
        action_type="UPDATE",
        entity_name="ClinicalObservation",
        entity_id="obs-101",
        user_id="user_crc_99",
        tenant_id="tenant_alpha",
        reason_for_change="Corrected temperature entry typo",
    )

    record = engine.log_event(payload)

    assert record.event_id is not None
    assert record.service_name == "apps/execution"
    assert record.reason_for_change == "Corrected temperature entry typo"
    assert record.sha256_digest is not None
    assert len(record.sha256_digest) == 64
    assert engine.verify_chain_integrity() is True


def test_audit_logger_detects_chain_tampering():
    """Verify AuditLoggerEngine flags tampered audit payload or broken digest chain.

    Requirements: PRD-SYS-001, 21 CFR Part 11
    """
    engine = AuditLoggerEngine(secret_key="test-secret-key")

    # Event 1
    engine.log_event(
        AuditLogPayload(
            service_name="apps/execution",
            action_type="CREATE",
            entity_name="FormSubmission",
            entity_id="form-1",
            user_id="user_crc_1",
            reason_for_change="Initial submission",
        )
    )

    # Event 2
    engine.log_event(
        AuditLogPayload(
            service_name="apps/execution",
            action_type="LOCK",
            entity_name="FormSubmission",
            entity_id="form-1",
            user_id="user_dm_1",
            reason_for_change="Data lock applied",
        )
    )

    # Assert unbroken chain before tampering
    assert engine.verify_chain_integrity() is True

    # Tamper with Event 1 reason_for_change in place
    engine._chain[0].reason_for_change = "Tampered unauthorized edit"

    # Assert integrity verification fails
    assert engine.verify_chain_integrity() is False


def test_crypto_verifier_valid_signature():
    """Verify cryptographic signature verifier approves valid HMAC signature.

    Requirements: PRD-SYS-001, 21 CFR Part 11
    """
    secret = "secret-key-123"  # pragma: allowlist secret
    payload_hash = "a591a6d40bf420404a011733cfb7b190d62c65bf0bcda32b57b277d9ad9f146e"  # pragma: allowlist secret

    import base64
    import hashlib
    import hmac

    sig_bytes = hmac.new(
        secret.encode("utf-8"), payload_hash.encode("utf-8"), hashlib.sha256
    ).digest()
    sig_b64 = base64.b64encode(sig_bytes).decode("utf-8")

    req = SignatureVerificationRequest(
        payload_hash=payload_hash,
        signature_bytes_b64=sig_b64,
        signer_id="user_pi_1",
        signing_reason="AUTHORS_APPROVAL",
    )

    result = verify_electronic_signature(req, secret_key=secret)
    assert result.is_valid is True
    assert result.error_code is None


def test_crypto_verifier_invalid_signature():
    """Verify cryptographic signature verifier rejects invalid signature.

    Requirements: PRD-SYS-001, 21 CFR Part 11
    """
    req = SignatureVerificationRequest(
        payload_hash="a591a6d40bf420404a011733cfb7b190d62c65bf0bcda32b57b277d9ad9f146e",  # pragma: allowlist secret
        signature_bytes_b64="invalid_b64_signature==",
        signer_id="user_pi_1",
        signing_reason="AUTHORS_APPROVAL",
    )

    result = verify_electronic_signature(req, secret_key="secret-key-123")
    assert result.is_valid is False
    assert result.error_code == "SIGNATURE_MISMATCH"


def test_security_audit_script():
    """Verify automated security audit script runs and reports clean codebase.

    Requirements: PRD-SYS-001, 21 CFR Part 11
    """
    success = run_security_audit(root_dir="packages/security")
    assert success is True


def test_gateway_raises_runtime_error_if_secret_missing(monkeypatch):
    """Verify that the gateway service raises a RuntimeError on initialization if GATEWAY_SECRET is missing.

    Requirements: PRD-SYS-001
    """
    import importlib
    import sys

    import pytest

    # Store original module to prevent desynchronizing other tests in the same process
    original_module = sys.modules.get("apps.gateway.main")

    monkeypatch.delenv("GATEWAY_SECRET", raising=False)
    sys.modules.pop("apps.gateway.main", None)
    try:
        with pytest.raises(RuntimeError) as exc_info:
            importlib.import_module("apps.gateway.main")
        assert "GATEWAY_SECRET environment variable is missing" in str(exc_info.value)
    finally:
        if original_module is not None:
            sys.modules["apps.gateway.main"] = original_module
        else:
            sys.modules.pop("apps.gateway.main", None)


def test_designer_signing_raises_runtime_error_if_secret_missing(monkeypatch):
    """Verify that the study designer signing module raises a RuntimeError when generating or verifying if SIGNING_SECRET is missing.

    Requirements: PRD-SYS-001
    """
    import pytest

    from apps.designer.delta import verify_version_signature

    monkeypatch.delenv("SIGNING_SECRET", raising=False)
    with pytest.raises(RuntimeError) as exc_info:
        verify_version_signature({"signature": "some_signature"})
    assert "SIGNING_SECRET environment variable is missing" in str(exc_info.value)


def test_security_audit_scanner_detection_and_bypass():
    """Verify that the security scanner detects hardcoded environment fallbacks and honors inline bypass comments."""
    import os
    import tempfile

    from scripts.audit_security import scan_file_for_secrets

    # Case 1: Line has a hardcoded environment fallback
    with tempfile.NamedTemporaryFile(suffix=".py", mode="w+", delete=False) as f:
        f.write(
            'GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")\n'
        )
        f.flush()
        try:
            findings = scan_file_for_secrets(f.name)
            assert len(findings) == 1
            assert "Hardcoded Environment Fallback" in findings[0]
        finally:
            os.unlink(f.name)

    # Case 2: Line has a hardcoded environment fallback but with an explicit inline bypass annotation
    with tempfile.NamedTemporaryFile(suffix=".py", mode="w+", delete=False) as f:
        f.write(
            'GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")  # pragma: allowlist secret\n'
        )
        f.flush()
        try:
            findings = scan_file_for_secrets(f.name)
            assert len(findings) == 0
        finally:
            os.unlink(f.name)
