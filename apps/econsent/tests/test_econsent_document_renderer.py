"""Tests for Verifiable HTML Document Renderer."""

from apps.econsent.adapters.document_renderer import (
    render_verifiable_consent_html,
)


def test_render_verifiable_consent_html():
    """Verify HTML generation contains 21 CFR Part 11 seals, clauses, granular choices, and signatures."""
    clauses = [
        {
            "title": "Purpose of Study",
            "text": "This is a phase 3 study evaluating drug efficacy.",
        },
        {
            "title": "Risks & Discomforts",
            "text": "Common risks include dizziness and fatigue.",
        },
    ]
    signatures = [
        {
            "role": "SUBJECT",
            "signer_name": "John Doe",
            "signed_at": "2026-08-15 12:00:00 UTC",
            "meaning": "I consent to study procedures",
            "digest_sha256": "112233445566778899aabbccddeeff00112233445566778899aabbccddeeff00",
        },
        {
            "role": "LAR",
            "signer_name": "Mary Doe",
            "lar_relationship": "Spouse / Healthcare Proxy",
            "signed_at": "2026-08-15 12:05:00 UTC",
            "meaning": "I consent as Legally Authorized Representative",
            "digest_sha256": "aabbccddeeff00112233445566778899aabbccddeeff00112233445566778899",
        },
    ]
    granular = [
        {"option_code": "OPT_BIOBANK", "title": "Tissue Biobanking", "selected": True},
        {"option_code": "OPT_GENETICS", "title": "DNA Analysis", "selected": False},
    ]
    audit_logs = [
        {
            "timestamp": "2026-08-15 12:00:00 UTC",
            "actor_id": "john.doe",
            "actor_role": "patient",
            "action": "CAPTURE_CONSENT",
            "reason_for_change": "Signed eConsent Form",
        }
    ]

    html_out = render_verifiable_consent_html(
        study_id="STUDY-RENDER-01",
        site_id="SITE-101",
        subject_pseudonym="SUBJ-JD-001",
        template_id="tpl-render-01",
        template_name="Cardiology Consent Form",
        protocol_version="v2.0",
        version_index=1,
        clauses=clauses,
        signatures=signatures,
        granular_selections=granular,
        audit_logs=audit_logs,
        source_content_identity="source-hash-12345",
    )

    assert "<!DOCTYPE html>" in html_out
    assert "Cardiology Consent Form" in html_out
    assert "STUDY-RENDER-01" in html_out
    assert "SUBJ-JD-001" in html_out
    assert "Purpose of Study" in html_out
    assert "Risks &amp; Discomforts" in html_out
    assert "Tissue Biobanking (OPT_BIOBANK)" in html_out
    assert "CONSENTED / OPTED-IN" in html_out
    assert "DECLINED / OPTED-OUT" in html_out
    assert "John Doe" in html_out
    assert "Mary Doe" in html_out
    assert "Spouse / Healthcare Proxy" in html_out
    assert (
        "112233445566778899aabbccddeeff00112233445566778899aabbccddeeff00" in html_out
    )
    assert "Append-Only Audit Trail Summary" in html_out
