"""Unit tests for the remove_jules_labels script.

Requirements: PRD-SYS-042
"""

from unittest.mock import patch

from scripts.remove_jules_labels import (
    fetch_all_issues,
    is_jules_label,
    remove_label,
)


def test_is_jules_label():
    """Verify is_jules_label matching logic.

    Requirements: PRD-SYS-042
    """
    assert is_jules_label("jules") is True
    assert is_jules_label("Jules") is True
    assert is_jules_label("JULES") is True
    assert is_jules_label("jules:in-progress") is True
    assert is_jules_label("jules-auto") is True
    assert is_jules_label("jules/task") is True
    assert is_jules_label("bug") is False
    assert is_jules_label("enhancement") is False


def test_remove_label_dry_run():
    """Verify remove_label in dry-run mode.

    Requirements: PRD-SYS-042
    """
    assert remove_label("owner/repo", 123, "jules", dry_run=True) is True


@patch("scripts.remove_jules_labels.run_gh_cmd")
def test_remove_label_success(mock_run):
    """Verify remove_label success handling.

    Requirements: PRD-SYS-042
    """
    mock_run.return_value = (0, "", "")
    assert remove_label("owner/repo", 123, "jules", dry_run=False) is True
    mock_run.assert_called_once()


@patch("scripts.remove_jules_labels.run_gh_cmd")
def test_fetch_all_issues(mock_run):
    """Verify fetch_all_issues pagination.

    Requirements: PRD-SYS-042
    """
    mock_run.return_value = (0, '[{"number": 1, "labels": [{"name": "jules"}]}]', "")
    issues = fetch_all_issues("owner/repo")
    assert len(issues) == 1
    assert issues[0]["number"] == 1
