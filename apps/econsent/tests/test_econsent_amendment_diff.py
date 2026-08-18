"""Tests for Template Amendment Diff Engine and Substantive Change Detection.

Verifies version comparison, word-level diffing, and automated re-consent triggering.
"""

from apps.econsent.domain.diff_engine import (
    compare_templates,
    compute_text_diff,
    is_text_change_substantive,
)


def test_text_diff_computation():
    """Verify unified diff formatting between old and new text blocks."""
    old_text = "The dose is 10mg once daily."
    new_text = "The dose is 20mg twice daily with meals."
    diff = compute_text_diff(old_text, new_text)

    assert "--- Base Version" in diff
    assert "+++ Amended Version" in diff
    assert "-The dose is 10mg once daily." in diff
    assert "+The dose is 20mg twice daily with meals." in diff


def test_substantive_change_detection():
    """Verify identification of safety, risk, dose, and procedure keyword shifts."""
    # Substantive: addition of safety risk
    assert is_text_change_substantive(
        "Treatment is administered weekly.",
        "Treatment is administered weekly. Known adverse safety risk of cardiac arrhythmia.",
    )

    # Substantive: change in genetic or biobanking scope
    assert is_text_change_substantive(
        "Blood samples will be analyzed for routine safety biomarkers.",
        "Blood samples will be stored in a biobank for future genetic DNA analysis.",
    )

    # Non-substantive: simple grammatical or contact phone typo fix
    assert not is_text_change_substantive(
        "Please contact the site at 555-0100 for queries.",
        "Please contact the site at 555-0101 for queries.",
    )


def test_compare_templates_delta_report():
    """Verify full comparison between template v1 and template v2."""
    base_clauses = [
        {
            "clause_id": "c1",
            "title": "Introduction",
            "text": "Study introduction text.",
        },
        {"clause_id": "c2", "title": "Risks", "text": "Mild nausea may occur."},
        {
            "clause_id": "c3",
            "title": "Confidentiality",
            "text": "Data is protected under HIPAA.",
        },
    ]

    target_clauses = [
        {
            "clause_id": "c1",
            "title": "Introduction",
            "text": "Study introduction text.",
        },  # UNCHANGED
        {
            "clause_id": "c2",
            "title": "Risks & Adverse Events",
            "text": "Mild nausea and severe risk of thrombocytopenia may occur.",
        },  # MODIFIED (substantive)
        # c3 REMOVED
        {
            "clause_id": "c4",
            "title": "Genetic Testing",
            "text": "Optional pharmacogenomics.",
        },  # ADDED
    ]

    report = compare_templates(
        template_id="tpl-diff-100",
        base_version_index=1,
        target_version_index=2,
        base_clauses=base_clauses,
        target_clauses=target_clauses,
    )

    assert report.template_id == "tpl-diff-100"
    assert report.base_version_index == 1
    assert report.target_version_index == 2
    assert report.total_unchanged == 1
    assert report.total_modified == 1
    assert report.total_added == 1
    assert report.total_removed == 1
    assert report.requires_reconsent is True
    assert len(report.substantive_summary) >= 3
