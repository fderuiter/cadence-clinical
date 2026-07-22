import os
import sys

sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

from scripts.post_pr_comment import (
    build_comment_body,
    get_status_emoji,
    merge_outcomes,
    parse_existing_outcomes,
)


def test_get_status_emoji():
    assert get_status_emoji("success") == "✅ Passed"
    assert get_status_emoji("passed") == "✅ Passed"
    assert get_status_emoji("failure") == "❌ Failed"
    assert get_status_emoji("failed") == "❌ Failed"
    assert get_status_emoji("skipped") == "⚪ Skipped"
    assert get_status_emoji("warning") == "⚠️ Warning"
    assert get_status_emoji(None) == "⚪ Skip/Unknown"
    assert get_status_emoji("random") == "⚪ Random"


def test_parse_existing_outcomes():
    sample_comment = """<!-- ID: CADENCE_PR_QUALITY_GATE_CHECKLIST -->
### ⚠️ Quality Gate Alerts & Review Checklist Required

| Quality Gate / Check | Status |
| :--- | :--- |
| **Linting & Formatting** (Ruff) | ❌ Failed |
| **Backend Tests & Coverage** (pytest) | ✅ Passed |
| **Frontend Checks** (pnpm check) | ⚪ Skipped |
| **ADR Validation** (validate_adrs.py) | ⚠️ Warning |
| **Dependency & Static Audit** (pip-audit/bandit) | ⚪ Skip/Unknown |
| **Git Merge Conflicts** | ❌ Conflicts Detected |
"""
    outcomes = parse_existing_outcomes(sample_comment)
    assert outcomes.get("lint") == "failure"
    assert outcomes.get("test") == "success"
    assert outcomes.get("frontend") == "skipped"
    assert outcomes.get("adr") == "warning"
    assert outcomes.get("audit") == "skipped"
    assert outcomes.get("conflict") == "failure"


def test_merge_outcomes():
    existing = {
        "lint": "failure",
        "test": "success",
        "frontend": "skipped",
        "adr": "warning",
        "audit": "skipped",
        "conflict": "success",
    }
    # If we run conflict check and get no conflicts, but we didn't run the others (they are empty/skipped)
    new_runs = {
        "conflict": "success",
        "lint": "",  # omitted/not run
        "test": "skipped",  # explicitly skipped
    }
    merged = merge_outcomes(new_runs, existing)
    assert merged.get("conflict") == "success"
    assert merged.get("lint") == "failure"  # preserved!
    assert merged.get("test") == "success"  # preserved!
    assert merged.get("frontend") == "skipped"  # preserved!
    assert merged.get("adr") == "warning"  # preserved!


def test_combined_audit_logic():
    # We can test how environment variables are combined
    # Let's mock the environment and check
    import os

    os.environ["AUDIT_OUTCOME"] = "failure"
    os.environ["STATIC_OUTCOME"] = "success"

    audit_outcome = os.environ.get("AUDIT_OUTCOME", "").lower()
    static_outcome = os.environ.get("STATIC_OUTCOME", "").lower()
    combined_audit = ""
    if "failure" in (audit_outcome, static_outcome):
        combined_audit = "failure"
    elif audit_outcome == "success" and static_outcome == "success":
        combined_audit = "success"
    else:
        combined_audit = audit_outcome or static_outcome

    assert combined_audit == "failure"


def test_build_comment_body():
    outcomes = {
        "lint": "success",
        "test": "success",
        "frontend": "success",
        "adr": "success",
        "audit": "success",
        "conflict": "success",
    }
    body = build_comment_body(outcomes, has_failures=False)
    assert "### ✅ All Quality Gates Passed Successfully" in body
    assert "✅ Passed" in body
    assert "✅ No Conflicts" in body

    body_fail = build_comment_body(outcomes, has_failures=True)
    assert "### ⚠️ Quality Gate Alerts & Review Checklist Required" in body_fail
