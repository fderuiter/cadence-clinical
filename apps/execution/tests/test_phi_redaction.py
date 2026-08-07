"""Integration test suite verifying zero PHI leakage in redacted PDF document output.

Requirements: PRD-SYS-001
"""

import packages  # noqa: F401
from apps.execution.services.pdf_redactor import PDFRedactorService
from packages.security.ner_scrubber import PHINameEntityScrubber


def test_zero_phi_leak_in_redacted_pdf() -> None:
    """Validate PDF redactor eliminates all HIPAA 18 PHI tokens leaving zero leakage.

    Requirements: PRD-SYS-001
    """
    raw_document = (
        b"CONFIDENTIAL MEDICAL REPORT\n"
        b"Subject: John Patient Doe\n"
        b"SSN: 333-22-1111\n"
        b"DOB: 1975-10-20\n"
        b"MRN:#88776655\n"
        b"Email: john.patient@clinicalsite.org\n"
        b"Phone: 800-555-0199\n"
        b"Clinical Diagnosis: Acute Appendicitis."
    )

    redactor = PDFRedactorService()
    result = redactor.apply_redaction_overlay(
        raw_document,
        target_snippets=["John Patient Doe"],
    )

    assert result["is_clean"] is True
    assert result["redacted_entities_count"] >= 5

    redacted_content = result["redacted_content"].decode("utf-8")
    scrubber = PHINameEntityScrubber()
    remaining = scrubber.detect_phi(redacted_content)

    assert len(remaining) == 0
    assert "333-22-1111" not in redacted_content
    assert "john.patient@clinicalsite.org" not in redacted_content
    assert "800-555-0199" not in redacted_content
    assert "88776655" not in redacted_content
