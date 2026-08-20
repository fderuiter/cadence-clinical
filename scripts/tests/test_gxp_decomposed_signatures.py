"""Unit and integration tests for Decomposed GxP Modules & Identity-Bound Electronic Signatures.

Requirements: PRD-SYS-001, 21 CFR Part 11
"""

import os
import subprocess
import sys
import time
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent.parent

from packages.compliance.services.esignature_verifier import ESignatureVerifier
from packages.compliance.services.gxp_signer import (
    generate_gxp_signing_credentials,
    sign_gxp_markdown,
)


def test_sign_and_verify_rsa_and_ecdsa():
    """Verify asymmetric signing (RSA-PSS and ECDSA) and signature verification.

    Requirements: PRD-SYS-001
    """
    verifier = ESignatureVerifier()
    doc_body = "# GxP Test Qualification Report\n\nExecution Pass Rate: 100%\n"

    # 1. RSA-PSS
    rsa_priv, rsa_cert = generate_gxp_signing_credentials(
        signer_id="test-rsa-engineer", key_type="RSA"
    )
    rsa_signed = sign_gxp_markdown(
        content=doc_body,
        signer_id="test-rsa-engineer",
        signing_reason="RSA Validation Sign-off",
        private_key_pem=rsa_priv,
        cert_pem=rsa_cert,
    )
    assert "-----BEGIN CERTIFICATE-----" in rsa_signed
    assert "-----BEGIN SIGNATURE-----" in rsa_signed
    assert "Signer Identity:** test-rsa-engineer" in rsa_signed

    rsa_res = verifier.verify_markdown(rsa_signed)
    assert rsa_res.is_valid is True
    assert rsa_res.status == "VALID"

    # 2. ECDSA
    ec_priv, ec_cert = generate_gxp_signing_credentials(
        signer_id="test-ec-engineer", key_type="ECDSA"
    )
    ec_signed = sign_gxp_markdown(
        content=doc_body,
        signer_id="test-ec-engineer",
        signing_reason="ECDSA Validation Sign-off",
        private_key_pem=ec_priv,
        cert_pem=ec_cert,
    )
    assert "-----BEGIN CERTIFICATE-----" in ec_signed
    assert "-----BEGIN SIGNATURE-----" in ec_signed
    assert "Signer Identity:** test-ec-engineer" in ec_signed

    ec_res = verifier.verify_markdown(ec_signed)
    assert ec_res.is_valid is True
    assert ec_res.status == "VALID"


def test_tamper_detection_body_modification():
    """Verify verifier flags unauthorized body modifications post-signature.

    Requirements: PRD-SYS-001
    """
    verifier = ESignatureVerifier()
    doc_body = "# GxP Qualification Report\n\nTotal Tests: 100\nPassed: 100\n"

    signed_doc = sign_gxp_markdown(content=doc_body, signer_id="qa-lead")
    assert verifier.verify_markdown(signed_doc).is_valid is True

    # Tamper with body text
    tampered_doc = signed_doc.replace("Passed: 100", "Passed: 90")
    res = verifier.verify_markdown(tampered_doc)
    assert res.is_valid is False
    assert (
        "TAMPER DETECTED" in res.failure_reason or res.status == "TAMPERED_INVALID_HASH"
    )


def test_tamper_detection_metadata_modification():
    """Verify verifier flags modifications to signature footer metadata.

    Requirements: PRD-SYS-001
    """
    verifier = ESignatureVerifier()
    doc_body = "# GxP Qualification Report\n\nStatus: PASSED\n"

    signed_doc = sign_gxp_markdown(content=doc_body, signer_id="qa-lead-orig")
    assert verifier.verify_markdown(signed_doc).is_valid is True

    # Tamper with signer identity in footer
    tampered_doc = signed_doc.replace("qa-lead-orig", "unauthorized-user")
    res = verifier.verify_markdown(tampered_doc)
    assert res.is_valid is False
    assert (
        "TAMPER DETECTED" in res.failure_reason or res.status == "TAMPERED_INVALID_HASH"
    )


def test_tamper_detection_signature_bytes_modification():
    """Verify verifier flags corrupted signature bytes.

    Requirements: PRD-SYS-001
    """
    verifier = ESignatureVerifier()
    doc_body = "# GxP Qualification Report\n\nStatus: PASSED\n"

    signed_doc = sign_gxp_markdown(content=doc_body, signer_id="qa-lead")

    # Corrupt signature string
    parts = signed_doc.split("-----BEGIN SIGNATURE-----")
    corrupted_sig_block = "AAAA" + parts[1][4:]
    tampered_doc = parts[0] + "-----BEGIN SIGNATURE-----" + corrupted_sig_block

    res = verifier.verify_markdown(tampered_doc)
    assert res.is_valid is False


def test_gxp_generation_and_runs_splitting(tmp_path):
    """Verify generate_rtm.py creates stable RTM mapping and dynamic signed run files.

    Requirements: PRD-SYS-001
    """
    output_dir = tmp_path / "SDLC"

    cmd = [
        sys.executable,
        "scripts/generate_rtm.py",
        "--output-dir",
        str(output_dir),
        "--draft",
    ]

    env = os.environ.copy()
    env["AUDIT_LOG_SECRET_KEY"] = "test-secret-key"  # pragma: allowlist secret
    env["INBOUND_EMAIL_HMAC_SECRET"] = "test-email-key"  # pragma: allowlist secret

    res = subprocess.run(
        cmd, capture_output=True, text=True, cwd=str(REPO_ROOT), env=env
    )
    assert res.returncode == 0, f"generate_rtm.py failed: {res.stderr}"

    rtm_file = output_dir / "Requirements_Traceability_Matrix.md"
    qual_file = output_dir / "IQ_OQ_PQ_Execution_Report.md"
    runs_dir = output_dir / "runs"

    assert rtm_file.is_file()
    assert qual_file.is_file()
    assert runs_dir.is_dir()

    run_files = list(runs_dir.glob("*.md"))
    assert len(run_files) >= 1

    # Check that qualification report and dynamic run files are signed
    verifier = ESignatureVerifier()
    qual_res = verifier.verify_markdown(qual_file.read_bytes())
    assert qual_res.is_valid is True

    run_res = verifier.verify_markdown(run_files[0].read_bytes())
    assert run_res.is_valid is True


def test_verification_performance_under_five_seconds():
    """Verify that signature verification across all docs completes in under 5 seconds.

    Requirements: PRD-SYS-001
    """
    cmd = [sys.executable, "scripts/verify_gxp_signatures.py", "docs/SDLC"]
    env = os.environ.copy()
    env["AUDIT_LOG_SECRET_KEY"] = "test-secret-key"  # pragma: allowlist secret
    env["INBOUND_EMAIL_HMAC_SECRET"] = "test-email-key"  # pragma: allowlist secret

    start_time = time.perf_counter()
    res = subprocess.run(
        cmd, capture_output=True, text=True, cwd=str(REPO_ROOT), env=env
    )
    elapsed = time.perf_counter() - start_time

    assert res.returncode == 0, f"verify_gxp_signatures.py failed: {res.stderr}"
    assert elapsed < 5.0, (
        f"Signature verification took {elapsed:.2f}s (exceeded 5.0s budget)"
    )
