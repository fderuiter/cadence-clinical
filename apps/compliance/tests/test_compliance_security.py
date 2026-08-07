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


def test_security_audit_targeted_files(tmp_path):
    """Verify automated security audit handles targeted files and identifies secrets.

    Requirements: PRD-SYS-001, 21 CFR Part 11
    """
    # Create a temporary file with a secret
    secret_file = tmp_path / "secret_file.txt"
    secret_file.write_text(
        "aws_secret_access_key = '0123456789012345678901234567890123456789'",  # pragma: allowlist secret
        encoding="utf-8",
    )

    # Create a clean temporary file
    clean_file = tmp_path / "clean_file.txt"
    clean_file.write_text("This is a clean file without any secrets.", encoding="utf-8")

    # Scanning only the clean file should pass
    success_clean = run_security_audit(files=[str(clean_file)])
    assert success_clean is True

    # Scanning the file with secret should fail
    success_secret = run_security_audit(files=[str(secret_file)])
    assert success_secret is False


def test_security_audit_exclusions(tmp_path):
    """Verify automated security audit skips excluded paths and directories.

    Requirements: PRD-SYS-001, 21 CFR Part 11
    """
    # Create an excluded directory inside the temp path
    node_modules_dir = tmp_path / "node_modules"
    node_modules_dir.mkdir()

    # Create a file inside that directory containing a secret
    secret_file = node_modules_dir / "secret_key.txt"
    secret_file.write_text(
        "aws_secret_access_key = '0123456789012345678901234567890123456789'",  # pragma: allowlist secret
        encoding="utf-8",
    )

    # Running scan on this file should skip it because it's inside an excluded path (node_modules)
    success = run_security_audit(files=[str(secret_file)])
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


def test_security_audit_scanner_detection_and_bypass():
    """Verify that the security scanner detects hardcoded environment fallbacks and honors inline bypass comments."""
    import os
    import tempfile

    from scripts.audit_security import scan_file_for_secrets

    # Case 1: Line has a hardcoded environment fallback
    with tempfile.NamedTemporaryFile(suffix=".py", mode="w+", delete=False) as f:
        f.write(
            "GATEWAY_SECRET = os.get"
            + 'env("GATEWAY_SECRET", "internal-gateway-secret-12345")\n'
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


def test_audit_logger_raises_runtime_error_if_secret_missing(monkeypatch):
    """Verify that the security module raises a RuntimeError if AUDIT_LOG_SECRET_KEY is missing or empty."""
    import importlib
    import sys

    import pytest

    # Store original modules
    orig_audit = sys.modules.get("packages.security.audit_logger")
    orig_security = sys.modules.get("packages.security")

    monkeypatch.delenv("AUDIT_LOG_SECRET_KEY", raising=False)
    sys.modules.pop("packages.security.audit_logger", None)
    sys.modules.pop("packages.security", None)

    try:
        with pytest.raises(RuntimeError) as exc_info:
            importlib.import_module("packages.security.audit_logger")
        assert "AUDIT_LOG_SECRET_KEY environment variable is missing or empty" in str(
            exc_info.value
        )
    finally:
        if orig_audit is not None:
            sys.modules["packages.security.audit_logger"] = orig_audit
        else:
            sys.modules.pop("packages.security.audit_logger", None)
        if orig_security is not None:
            sys.modules["packages.security"] = orig_security


def test_signing_raises_runtime_error_if_email_secret_missing(monkeypatch):
    """Verify that the signing module raises a RuntimeError if INBOUND_EMAIL_HMAC_SECRET is missing or empty."""
    import importlib
    import sys

    import pytest

    orig_signing = sys.modules.get("packages.security.signing")
    orig_security = sys.modules.get("packages.security")

    monkeypatch.delenv("INBOUND_EMAIL_HMAC_SECRET", raising=False)
    sys.modules.pop("packages.security.signing", None)
    sys.modules.pop("packages.security", None)

    try:
        with pytest.raises(RuntimeError) as exc_info:
            importlib.import_module("packages.security.signing")
        assert (
            "INBOUND_EMAIL_HMAC_SECRET environment variable is missing or empty"
            in str(exc_info.value)
        )
    finally:
        if orig_signing is not None:
            sys.modules["packages.security.signing"] = orig_signing
        else:
            sys.modules.pop("packages.security.signing", None)
        if orig_security is not None:
            sys.modules["packages.security"] = orig_security


def test_assert_secure_secrets_validation(monkeypatch):
    """Verify that assert_secure_secrets raises RuntimeError on insecure fallbacks in production/staging but permits them in dev."""
    import pytest

    from packages.security.fail_fast import assert_secure_secrets

    # Case 1: Non-dev env with insecure fallback should raise RuntimeError
    monkeypatch.setenv("APP_ENV", "production")
    with pytest.raises(RuntimeError) as exc_info:
        assert_secure_secrets(
            "test-service",
            {
                "GATEWAY_SECRET": "internal-gateway-secret-12345"  # pragma: allowlist secret
            },
        )
    assert "GATEWAY_SECRET" in str(exc_info.value)
    assert "Uses insecure fallback value" in str(exc_info.value)

    # Case 2: Non-dev env with missing/empty secret should raise RuntimeError
    with pytest.raises(RuntimeError) as exc_info:
        assert_secure_secrets("test-service", {"GATEWAY_SECRET": ""})
    assert "GATEWAY_SECRET" in str(exc_info.value)
    assert "Missing override" in str(exc_info.value)

    # Case 3: Dev env should permit insecure fallback value without raising error
    monkeypatch.setenv("APP_ENV", "development")
    assert_secure_secrets(
        "test-service",
        {"GATEWAY_SECRET": "internal-gateway-secret-12345"},  # pragma: allowlist secret
    )

    # Case 4: Missing/unset APP_ENV should permit fallback (local developer mode)
    monkeypatch.delenv("APP_ENV", raising=False)
    assert_secure_secrets(
        "test-service",
        {"GATEWAY_SECRET": "internal-gateway-secret-12345"},  # pragma: allowlist secret
    )


def test_global_scanner_with_opt_out(tmp_path):
    """Verify static security scanner runs globally but respects explicit opt-out via .scannerignore or .nosec."""
    from scripts.audit_security import run_security_audit

    # Create dir_a (no opt-out)
    dir_a = tmp_path / "dir_a"
    dir_a.mkdir()
    file_a = dir_a / "service.py"
    file_a.write_text(
        "GATEWAY_SECRET = os.getenv('GATEWAY_SECRET', 'internal-gateway-secret-12345')",  # pragma: allowlist secret
        encoding="utf-8",
    )

    # Create dir_b (opted out via .scannerignore)
    dir_b = tmp_path / "dir_b"
    dir_b.mkdir()
    (dir_b / ".scannerignore").write_text("", encoding="utf-8")
    file_b = dir_b / "test_mock.py"
    file_b.write_text(
        "GATEWAY_SECRET = os.getenv('GATEWAY_SECRET', 'internal-gateway-secret-12345')",  # pragma: allowlist secret
        encoding="utf-8",
    )

    # Run scan on file_a (must fail/find secret fallback)
    success_a = run_security_audit(files=[str(file_a)])
    assert success_a is False

    # Run scan on file_b (must pass because parent has .scannerignore)
    success_b = run_security_audit(files=[str(file_b)])
    assert success_b is True
