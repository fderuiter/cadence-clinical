import os
import sys
from unittest.mock import patch

import pytest

sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

from scripts.self_heal import is_safe_file, main


def test_is_safe_file():
    # 1. Regulated compliance files (forbidden)
    assert not is_safe_file("docs/SDLC/compliance.md")
    assert not is_safe_file("docs/sdlc/process.md")
    assert not is_safe_file("apps/execution/docs/SDLC/policy.md")

    # 2. Our self-healing script and tests (allowed)
    assert is_safe_file("scripts/self_heal.py")
    assert is_safe_file("tests/test_self_heal.py")

    # 3. Lockfiles (allowed)
    assert is_safe_file("uv.lock")
    assert is_safe_file("pnpm-lock.yaml")

    # 4. Standard documentation (allowed)
    assert is_safe_file("README.md")
    assert is_safe_file("docs/architecture.md")

    # 5. Test files (allowed)
    assert is_safe_file("tests/conftest.py")
    assert is_safe_file("apps/execution/tests/test_router.py")
    assert is_safe_file("packages/security/tests/test_signing.py")

    # 6. Core app/model files (forbidden)
    assert not is_safe_file("apps/execution/main.py")
    assert not is_safe_file("packages/core-models/cdisc/usdm_models.py")
    assert not is_safe_file("packages/security/gating.py")
    assert not is_safe_file("pyproject.toml")
    assert not is_safe_file("package.json")


@patch("scripts.self_heal.run_command")
@patch("scripts.self_heal.update_pr_comment")
def test_main_skipped_if_no_safe_change_label(mock_update_comment, mock_run_cmd):
    # Mock gh pr view return to not have "safe-change" label
    mock_run_cmd.side_effect = [
        (
            '{"labels": [{"name": "documentation"}], "headRefName": "feat-docs", "baseRefName": "main", "mergeable": "CONFLICTING"}',
            "",
        )
    ]

    with patch.dict(
        os.environ, {"GITHUB_REPOSITORY": "owner/repo", "PR_NUMBER": "123"}
    ):
        with patch("sys.exit", side_effect=SystemExit) as mock_exit:
            with pytest.raises(SystemExit):
                main()
            mock_exit.assert_called_once_with(0)
            mock_update_comment.assert_called_once_with("failure")


@patch("scripts.self_heal.run_command")
@patch("scripts.self_heal.update_pr_comment")
def test_main_blocked_on_non_safe_files(mock_update_comment, mock_run_cmd):
    # Mock PR with safe-change label, but contains a non-safe file
    mock_run_cmd.side_effect = [
        # gh pr view
        (
            '{"labels": [{"name": "safe-change"}], "headRefName": "feat-docs", "baseRefName": "main", "mergeable": "CONFLICTING"}',
            "",
        ),
        # gh api repos/owner/repo/pulls/123/files
        ("apps/execution/main.py\ndocs/architecture.md", ""),
    ]

    with patch.dict(
        os.environ, {"GITHUB_REPOSITORY": "owner/repo", "PR_NUMBER": "123"}
    ):
        with patch("sys.exit", side_effect=SystemExit) as mock_exit:
            with pytest.raises(SystemExit):
                main()
            mock_exit.assert_called_once_with(1)
            mock_update_comment.assert_called_once_with("failure")


@patch("scripts.self_heal.run_command")
@patch("scripts.self_heal.update_pr_comment")
def test_main_no_conflict_needed(mock_update_comment, mock_run_cmd):
    # Mock PR with safe-change label, safe files, but already mergeable
    mock_run_cmd.side_effect = [
        # gh pr view
        (
            '{"labels": [{"name": "safe-change"}], "headRefName": "feat-docs", "baseRefName": "main", "mergeable": "MERGEABLE"}',
            "",
        ),
        # gh api repos/owner/repo/pulls/123/files
        ("docs/architecture.md", ""),
    ]

    with patch.dict(
        os.environ, {"GITHUB_REPOSITORY": "owner/repo", "PR_NUMBER": "123"}
    ):
        with patch("sys.exit", side_effect=SystemExit) as mock_exit:
            with pytest.raises(SystemExit):
                main()
            mock_exit.assert_called_once_with(0)
            mock_update_comment.assert_not_called()
