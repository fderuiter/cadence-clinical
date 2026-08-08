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

    # 2. Our self-healing script, tests, and configurations (allowed)
    assert is_safe_file("scripts/self_heal.py")
    assert is_safe_file("tests/test_self_heal.py")
    assert is_safe_file(".github/workflows/conflict-check.yml")
    assert is_safe_file("scripts/post_pr_comment.py")

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
    assert not is_safe_file("apps/execution/src/domain/sdtm/models.py")
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


def test_handle_github_api_error():
    from scripts.self_heal import handle_github_api_error

    with patch("sys.exit", side_effect=SystemExit) as mock_exit:
        # 1. Matching error pattern
        with pytest.raises(SystemExit):
            handle_github_api_error("populate the GH_TOKEN environment variable")
        mock_exit.assert_called_once_with(0)

        # 2. Non-matching error pattern
        mock_exit.reset_mock()
        handle_github_api_error("some random other git/CLI failure")
        mock_exit.assert_not_called()


@patch("scripts.self_heal.run_command")
@patch("scripts.self_heal.update_pr_comment")
def test_main_graceful_on_github_api_error(mock_update_comment, mock_run_cmd):
    # Mock gh pr view returning empty and error message containing 403 Forbidden
    mock_run_cmd.side_effect = [
        (
            "",
            "HTTP 403 Forbidden: Resource not accessible by integration",
        )
    ]

    with patch.dict(
        os.environ, {"GITHUB_REPOSITORY": "owner/repo", "PR_NUMBER": "123"}
    ):
        with patch("sys.exit", side_effect=SystemExit) as mock_exit:
            with pytest.raises(SystemExit):
                main()
            # It should exit with 0 due to graceful API error handling
            mock_exit.assert_called_once_with(0)


@patch("scripts.self_heal.run_command")
@patch("scripts.self_heal.update_pr_comment")
def test_main_graceful_exit_on_api_error(mock_update_comment, mock_run_cmd):
    # Mock gh pr view to fail with authorization error
    mock_run_cmd.side_effect = [
        ("", "To get started with GitHub CLI, please run: gh auth login")
    ]

    with patch.dict(
        os.environ, {"GITHUB_REPOSITORY": "owner/repo", "PR_NUMBER": "123"}
    ):
        with patch("sys.exit", side_effect=SystemExit) as mock_exit:
            with pytest.raises(SystemExit):
                main()
            mock_exit.assert_called_once_with(0)


@patch("scripts.self_heal.run_command")
@patch("scripts.self_heal.update_pr_comment")
def test_main_no_conflict_with_non_safe_files(mock_update_comment, mock_run_cmd):
    # Mock PR with safe-change label, non-safe files, but already mergeable.
    # The script should exit with 0 during the conflict check step without
    # failing on file guardrails.
    mock_run_cmd.side_effect = [
        # gh pr view
        (
            '{"labels": [{"name": "safe-change"}], "headRefName": "feat-docs", "baseRefName": "main", "mergeable": "MERGEABLE"}',
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
            mock_update_comment.assert_not_called()


def test_is_tampering_attempt():
    from scripts.self_heal import is_tampering_attempt

    assert is_tampering_attempt(".github/workflows/conflict-check.yml")
    assert is_tampering_attempt("scripts/self_heal.py")
    assert is_tampering_attempt("scripts/post_pr_comment.py")
    assert not is_tampering_attempt("tests/test_self_heal.py")
    assert not is_tampering_attempt("README.md")


def test_is_executable_or_test_file():
    from scripts.self_heal import is_executable_or_test_file

    assert is_executable_or_test_file("tests/test_self_heal.py")
    assert is_executable_or_test_file("apps/execution/main.py")
    assert is_executable_or_test_file("scripts/self_heal.py")
    assert is_executable_or_test_file("fixture/some_data.json")
    assert is_executable_or_test_file("test_script.sh")
    assert not is_executable_or_test_file("README.md")
    assert not is_executable_or_test_file("uv.lock")
    assert not is_executable_or_test_file("package.json")


@patch("scripts.self_heal.run_command")
@patch("scripts.self_heal.update_pr_comment")
def test_tampering_blocked_on_workflow_change(mock_update_comment, mock_run_cmd):
    # Mock PR with safe-change label, but contains a modified workflow file
    mock_run_cmd.side_effect = [
        # gh pr view
        (
            '{"labels": [{"name": "safe-change"}], "headRefName": "feat-docs", "baseRefName": "main", "mergeable": "CONFLICTING"}',
            "",
        ),
        # gh api repos/owner/repo/pulls/123/files
        (".github/workflows/conflict-check.yml", ""),
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
def test_validation_bypassed_on_code_change(mock_update_comment, mock_run_cmd):
    # Mock PR with safe-change label and modified test file
    mock_run_cmd.side_effect = [
        # 1. gh pr view
        (
            '{"labels": [{"name": "safe-change"}], "headRefName": "feat-docs", "baseRefName": "main", "mergeable": "CONFLICTING"}',
            "",
        ),
        # 2. gh api repos/owner/repo/pulls/123/files
        ("tests/test_something.py", ""),
        # 3. git config user.name
        ("", ""),
        # 4. git config user.email
        ("", ""),
        # 5. git fetch origin main
        ("", ""),
        # 6. git merge origin/main --no-commit --no-ff
        ("", ""),
        # 7. git diff --name-only --diff-filter=U (conflicting files)
        ("tests/test_something.py", ""),
        # 8. git checkout --ours tests/test_something.py
        ("", ""),
        # 9. git add tests/test_something.py
        ("", ""),
        # 10. git status --porcelain
        ("M tests/test_something.py", ""),
        # 11. git commit -m ...
        ("committed", ""),
        # 12. git remote set-url origin ...
        ("", ""),
        # 13. git push origin HEAD:feat-docs
        ("pushed", ""),
    ]

    with patch.dict(
        os.environ,
        {
            "GITHUB_REPOSITORY": "owner/repo",
            "PR_NUMBER": "123",
            "PAT_FDERUITER": "token_val",
        },
    ):
        main()

        # Verify LINTING_OUTCOME and TEST_OUTCOME are set to skipped
        assert os.environ.get("LINTING_OUTCOME") == "skipped"
        assert os.environ.get("TEST_OUTCOME") == "skipped"

        # Verify comment was updated with success after push
        mock_update_comment.assert_called_once_with("success")

        # Check that we did not run ruff or pytest command
        for call_args in mock_run_cmd.call_args_list:
            cmd = call_args[0][0]
            assert "ruff" not in cmd
            assert "pytest" not in cmd


@patch("scripts.self_heal.run_command")
@patch("scripts.self_heal.update_pr_comment")
def test_validation_executed_on_non_executable_change(
    mock_update_comment, mock_run_cmd
):
    # Reset env vars
    os.environ.pop("LINTING_OUTCOME", None)
    os.environ.pop("TEST_OUTCOME", None)

    # Mock PR with safe-change label and modified lockfile (non-executable)
    mock_run_cmd.side_effect = [
        # 1. gh pr view
        (
            '{"labels": [{"name": "safe-change"}], "headRefName": "feat-docs", "baseRefName": "main", "mergeable": "CONFLICTING"}',
            "",
        ),
        # 2. gh api repos/owner/repo/pulls/123/files
        ("uv.lock\nREADME.md", ""),
        # 3. git config user.name
        ("", ""),
        # 4. git config user.email
        ("", ""),
        # 5. git fetch origin main
        ("", ""),
        # 6. git merge origin/main --no-commit --no-ff
        ("", ""),
        # 7. git diff --name-only --diff-filter=U (conflicting files)
        ("uv.lock", ""),
        # 8. git checkout --ours uv.lock
        ("", ""),
        # 9. git add uv.lock
        ("", ""),
        # 10. uv sync --python 3.14 --all-extras
        ("", ""),
        # 11. git add uv.lock
        ("", ""),
        # 12. git status --porcelain
        ("M uv.lock", ""),
        # 13. git commit -m ...
        ("committed", ""),
        # 14. ruff check
        ("ruff clean", ""),
        # 15. pytest
        ("pytest passed", ""),
        # 16. git remote set-url origin ...
        ("", ""),
        # 17. git push origin HEAD:feat-docs
        ("pushed", ""),
    ]

    with patch.dict(
        os.environ,
        {
            "GITHUB_REPOSITORY": "owner/repo",
            "PR_NUMBER": "123",
            "PAT_FDERUITER": "token_val",
        },
    ):
        main()

        # Verify LINTING_OUTCOME and TEST_OUTCOME are set to success
        assert os.environ.get("LINTING_OUTCOME") == "success"
        assert os.environ.get("TEST_OUTCOME") == "success"

        mock_update_comment.assert_called_once_with("success")
