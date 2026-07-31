"""Integration test suite qualifying Part 11 audit manifest generation for signed casebooks.

Requirements: PRD-SYS-001
"""

from datetime import datetime, timezone

import packages  # noqa: F401
from apps.execution.services.audit_manifest import AuditManifestGenerator


def test_generate_casebook_manifest_structure() -> None:
    """Validate Part 11 audit manifest structure and master root digest calculation.

    Requirements: PRD-SYS-001
    """
    generator = AuditManifestGenerator()
    now_iso = datetime.now(timezone.utc).isoformat()

    form_digests = {
        "form_vs_01": "a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90",  # pragma: allowlist secret
        "form_lb_01": "90f8e7d6c5b4a39281706f5e4d3c2b1a90f8e7d6c5b4a39281706f5e4d3c2b1a",  # pragma: allowlist secret
    }

    manifest = generator.generate_casebook_manifest(
        study_id="study_manifest_01",
        subject_id="sub_manifest_101",
        signature_id="sig_m_001",
        signer_name="Dr. Jane Doe, MD",
        signer_user_id="pi_user_200",
        signing_reason="PI Casebook Sign-Off",
        form_digests=form_digests,
        timestamp_utc=now_iso,
    )

    assert manifest["manifest_version"] == "1.0"
    assert manifest["study_id"] == "study_manifest_01"
    assert manifest["subject_id"] == "sub_manifest_101"
    assert manifest["signer_name"] == "Dr. Jane Doe, MD"
    assert "master_root_digest" in manifest
    assert len(manifest["master_root_digest"]) == 64
    assert (
        "21 CFR Part 11 ELECTRONIC SIGNATURE MANIFEST" in manifest["printable_summary"]
    )


def test_master_root_digest_tamper_sensitivity() -> None:
    """Validate master root digest changes if any form digest is tampered with.

    Requirements: PRD-SYS-001
    """
    generator = AuditManifestGenerator()
    now_iso = datetime.now(timezone.utc).isoformat()

    original_digests = {
        "form_vs_01": "1111111111111111111111111111111111111111111111111111111111111111",
        "form_lb_01": "2222222222222222222222222222222222222222222222222222222222222222",
    }

    tampered_digests = {
        "form_vs_01": "1111111111111111111111111111111111111111111111111111111111111111",
        "form_lb_01": "9999999999999999999999999999999999999999999999999999999999999999",
    }

    manifest_orig = generator.generate_casebook_manifest(
        study_id="study_01",
        subject_id="sub_01",
        signature_id="sig_01",
        signer_name="Dr. Smith",
        signer_user_id="pi_01",
        signing_reason="Sign-off",
        form_digests=original_digests,
        timestamp_utc=now_iso,
    )

    manifest_tamp = generator.generate_casebook_manifest(
        study_id="study_01",
        subject_id="sub_01",
        signature_id="sig_01",
        signer_name="Dr. Smith",
        signer_user_id="pi_01",
        signing_reason="Sign-off",
        form_digests=tampered_digests,
        timestamp_utc=now_iso,
    )

    assert manifest_orig["master_root_digest"] != manifest_tamp["master_root_digest"]
