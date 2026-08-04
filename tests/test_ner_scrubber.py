"""Unit test suite for PHI Named Entity Recognition (NER) scrubber.

Requirements: PRD-SYS-001
"""

import packages  # noqa: F401
from packages.security.ner_scrubber import PHINameEntityScrubber


def test_detect_phi_patterns() -> None:
    """Validate detect_phi identifies SSN, email, phone number, and MRN tokens.

    Requirements: PRD-SYS-001
    """
    scrubber = PHINameEntityScrubber()
    sample_text = (
        "Patient John Doe (MRN:#12345678) DOB:1980-05-12. "
        "Contact: john.doe@example.com or 555-123-4567. "
        "SSN: 000-12-3456."
    )

    entities = scrubber.detect_phi(sample_text)
    assert len(entities) >= 4

    types = [e["entity_type"] for e in entities]
    assert "SSN" in types
    assert "EMAIL" in types
    assert "PHONE" in types
    assert "MRN" in types
    assert "DOB" in types


def test_scrub_phi_redaction() -> None:
    """Validate scrub_phi replaces PHI tokens with redaction tags.

    Requirements: PRD-SYS-001
    """
    scrubber = PHINameEntityScrubber()
    sample_text = "Patient SSN is 123-45-6789 and email is patient@hospital.org"

    scrubbed = scrubber.scrub_phi(sample_text)

    assert "123-45-6789" not in scrubbed
    assert "patient@hospital.org" not in scrubbed
    assert "[REDACTED_SSN]" in scrubbed
    assert "[REDACTED_EMAIL]" in scrubbed


def test_custom_terms_and_overlap_resolution() -> None:
    """Validate custom terms matching and deterministic overlap resolution.

    Requirements: PRD-SYS-001
    """
    scrubber = PHINameEntityScrubber()
    # "123-45-6789" is an SSN, which spans (15, 26).
    # Custom terms: "123-45" (15, 21), "123-45-6789" (15, 26), "John Doe"
    sample_text = "Patient name is John Doe, and SSN is 123-45-6789."

    entities = scrubber.detect_phi(
        sample_text, custom_terms=["John Doe", "123-45", "123-45-6789"]
    )

    # Overlaps between SSN and "123-45", "123-45-6789" must be resolved.
    # The SSN pattern and "123-45-6789" are identical spans, but one of them gets priority and the other is discarded.
    # The shorter "123-45" is a nested overlap and must be completely discarded.
    # So we should only have matches for "John Doe" (CUSTOM) and the SSN (SSN or CUSTOM)
    # Let's verify that we have no overlapping spans returned.
    for i in range(len(entities)):
        for j in range(i + 1, len(entities)):
            e1, e2 = entities[i], entities[j]
            assert max(e1["start_char"], e2["start_char"]) >= min(
                e1["end_char"], e2["end_char"]
            )

    scrubbed = scrubber.scrub_phi(
        sample_text,
        custom_terms=["John Doe", "123-45", "123-45-6789"],
        custom_replacement="[REDACTED]",
    )

    assert "John Doe" not in scrubbed
    assert "123-45-6789" not in scrubbed
    assert "[REDACTED]" in scrubbed
    # The SSN was also redacted
    assert "123-45" not in scrubbed


def test_word_boundaries_custom_terms() -> None:
    """Validate custom terms are only matched on word boundaries if alphanumeric.

    Requirements: PRD-SYS-001
    """
    scrubber = PHINameEntityScrubber()
    sample_text = "Do not match JohnDoe, but match John Doe. Match $100."
    entities = scrubber.detect_phi(sample_text, custom_terms=["John Doe", "$100"])

    matched_texts = [e["text"] for e in entities]
    assert "John Doe" in matched_texts
    assert "$100" in matched_texts
    assert "JohnDoe" not in matched_texts
