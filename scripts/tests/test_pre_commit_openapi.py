"""Unit tests for the pre-commit OpenAPI auto-stager hook.

This test suite verifies:
1. Retrieval of staged files from git.
2. Filtering of staged files to identify API routes or Pydantic schemas.
3. Behavior under missing dependencies (graceful exit and instructions).
4. Successful execution of schema validation and automatic staging.
5. Exit codes and outputs under successful and failing conditions.

Compliance:
- Gate 1: Google-style docstrings and clear comments.
- Gate 3: Mandatory test coverage with 100% passing rate.
"""

import os
import subprocess
from unittest.mock import MagicMock, patch

from scripts.pre_commit_openapi import (
    check_and_run_exporter,
    get_staged_files,
    should_trigger_schema_generation,
)


def test_get_staged_files_success() -> None:
    """Test get_staged_files returns the parsed list of files when git succeeds."""
    mock_stdout = "apps/designer/main.py\npackages/security/models.py\n"
    with patch("subprocess.run") as mock_run:
        mock_run.return_value = MagicMock(returncode=0, stdout=mock_stdout, stderr="")
        files = get_staged_files()
        assert files == ["apps/designer/main.py", "packages/security/models.py"]
        mock_run.assert_called_once_with(
            ["git", "diff", "--cached", "--name-only", "--diff-filter=ACM"],
            capture_output=True,
            text=True,
            check=True,
        )


def test_get_staged_files_failure() -> None:
    """Test get_staged_files handles SubprocessError gracefully and returns empty list."""
    with patch("subprocess.run") as mock_run:
        mock_run.side_effect = subprocess.SubprocessError("Git error")
        files = get_staged_files()
        assert files == []


def test_should_trigger_schema_generation() -> None:
    """Test should_trigger_schema_generation identifies API and schema modifications."""
    # Standard non-triggering files
    assert not should_trigger_schema_generation([])
    assert not should_trigger_schema_generation(["tests/test_main.py"])
    assert not should_trigger_schema_generation(["package.json"])
    assert not should_trigger_schema_generation(["apps/designer/README.md"])

    # API route and main app entry point triggers
    assert should_trigger_schema_generation(["apps/designer/main.py"])
    assert should_trigger_schema_generation(["apps/execution/main.py"])

    # Router triggers
    assert should_trigger_schema_generation(["apps/execution/routers/sdv.py"])
    assert should_trigger_schema_generation(["apps/ctms/routers/doa.py"])

    # Model and schema triggers
    assert should_trigger_schema_generation(["apps/ctms/models.py"])
    assert should_trigger_schema_generation(["apps/ctms/models/doa.py"])
    assert should_trigger_schema_generation(
        ["apps/execution/src/domain/sdtm/models.py"]
    )


@patch("scripts.pre_commit_openapi.get_staged_files")
def test_check_and_run_exporter_bypass(mock_get_files: MagicMock) -> None:
    """Test that check_and_run_exporter bypasses when no relevant files are modified."""
    mock_get_files.return_value = ["package.json", "tests/test_main.py"]
    exit_code = check_and_run_exporter()
    assert exit_code == 0


@patch("scripts.pre_commit_openapi.get_staged_files")
@patch("os.path.exists")
def test_check_and_run_exporter_missing_venv(
    mock_exists: MagicMock, mock_get_files: MagicMock
) -> None:
    """Test that check_and_run_exporter fails gracefully when the virtualenv is missing."""
    mock_get_files.return_value = ["apps/designer/main.py"]
    mock_exists.return_value = False

    with patch("sys.stderr"):
        exit_code = check_and_run_exporter()
        assert exit_code == 1


@patch("scripts.pre_commit_openapi.get_staged_files")
@patch("os.path.exists")
@patch("subprocess.run")
def test_check_and_run_exporter_missing_dependencies(
    mock_run: MagicMock, mock_exists: MagicMock, mock_get_files: MagicMock
) -> None:
    """Test that check_and_run_exporter fails gracefully when python dependencies are missing."""
    mock_get_files.return_value = ["apps/designer/main.py"]
    mock_exists.return_value = True

    # First subprocess.run check for fastapi import fails
    mock_run.return_value = MagicMock(returncode=1)

    with patch("sys.stderr"):
        exit_code = check_and_run_exporter()
        assert exit_code == 1


@patch("scripts.pre_commit_openapi.get_staged_files")
@patch("os.path.exists")
@patch("subprocess.run")
def test_check_and_run_exporter_success(
    mock_run: MagicMock, mock_exists: MagicMock, mock_get_files: MagicMock
) -> None:
    """Test successful schema compilation and auto-staging when files are modified."""
    mock_get_files.return_value = ["apps/designer/main.py"]
    mock_exists.return_value = True

    # 1. Dependency check passes (returncode=0)
    # 2. Schema export passes (returncode=0)
    # 3. Git add passes (returncode=0)
    mock_run.side_effect = [
        MagicMock(returncode=0),  # Dependency check
        MagicMock(
            returncode=0, stdout="Successfully exported schemas", stderr=""
        ),  # Exporter
        MagicMock(returncode=0),  # Git add
    ]

    exit_code = check_and_run_exporter()
    assert exit_code == 0
    # Check that git add was indeed called
    mock_run.assert_any_call(
        [
            "git",
            "add",
            os.path.abspath(
                os.path.join(os.path.dirname(__file__), "..", "..", "docs", "openapi")
            ),
        ],
        cwd=os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..")),
        check=True,
    )


@patch("scripts.pre_commit_openapi.get_staged_files")
@patch("os.path.exists")
@patch("subprocess.run")
def test_check_and_run_exporter_validation_failure(
    mock_run: MagicMock, mock_exists: MagicMock, mock_get_files: MagicMock
) -> None:
    """Test check_and_run_exporter propagates failure of the export/validation script."""
    mock_get_files.return_value = ["apps/designer/main.py"]
    mock_exists.return_value = True

    # 1. Dependency check passes (returncode=0)
    # 2. Schema export fails (returncode=5)
    mock_run.side_effect = [
        MagicMock(returncode=0),  # Dependency check
        MagicMock(
            returncode=5, stdout="", stderr="Duplicate prefix collision!"
        ),  # Exporter fails
    ]

    exit_code = check_and_run_exporter()
    assert exit_code == 5
