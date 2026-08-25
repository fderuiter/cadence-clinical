"""Unit tests for the OpenAPI documentation drift verification sentinel.

This test suite verifies:
1. Successful drift check when documentation matches code.
2. Graceful failure propagation when microservice schema validation fails.
3. Graceful failure propagation when spec synchronization fails.
4. Detection of uncommitted documentation drift and execution of non-destructive cleanup.

Compliance:
- Gate 1: Google-style docstrings and clear comments.
- Gate 3: Mandatory test coverage with 100% passing rate.
"""

from unittest.mock import MagicMock, patch

from scripts.validate_openapi_drift import check_openapi_drift


@patch("subprocess.run")
def test_check_openapi_drift_success(mock_run: MagicMock) -> None:
    """Test check_openapi_drift returns True when all scripts succeed and no drift is found."""
    mock_run.side_effect = [
        MagicMock(
            returncode=0, stdout="Schema validation passed", stderr=""
        ),  # validate_schemas
        MagicMock(
            returncode=0, stdout="Sync spec passed", stderr=""
        ),  # sync_openapi_spec
        MagicMock(returncode=0, stdout="", stderr=""),  # git status --porcelain
    ]

    result = check_openapi_drift()
    assert result is True
    assert mock_run.call_count == 3


@patch("subprocess.run")
def test_check_openapi_drift_schema_validation_failure(mock_run: MagicMock) -> None:
    """Test check_openapi_drift returns False when schema validation fails."""
    mock_run.side_effect = [
        MagicMock(returncode=1, stdout="", stderr="Import failure in microservice"),
    ]

    result = check_openapi_drift()
    assert result is False
    assert mock_run.call_count == 1


@patch("subprocess.run")
def test_check_openapi_drift_sync_spec_failure(mock_run: MagicMock) -> None:
    """Test check_openapi_drift returns False when sync_openapi_spec fails."""
    mock_run.side_effect = [
        MagicMock(returncode=0, stdout="Schema validation passed", stderr=""),
        MagicMock(returncode=1, stdout="", stderr="Markdown section missing"),
    ]

    result = check_openapi_drift()
    assert result is False
    assert mock_run.call_count == 2


@patch("subprocess.run")
def test_check_openapi_drift_detected_and_reverted(mock_run: MagicMock) -> None:
    """Test check_openapi_drift detects drift, runs non-destructive cleanup, and returns False."""
    mock_run.side_effect = [
        MagicMock(returncode=0, stdout="Schema validation passed", stderr=""),  # Step 1
        MagicMock(returncode=0, stdout="Sync spec passed", stderr=""),  # Step 2
        MagicMock(
            returncode=0, stdout=" M docs/openapi/designer_openapi.json\n", stderr=""
        ),  # git status
        MagicMock(
            returncode=0,
            stdout="diff --git a/docs/openapi/designer_openapi.json",
            stderr="",
        ),  # git diff
        MagicMock(returncode=0, stdout="", stderr=""),  # git checkout -- docs/...
        MagicMock(returncode=0, stdout="", stderr=""),  # git clean -fd docs/openapi/
    ]

    result = check_openapi_drift()
    assert result is False
    assert mock_run.call_count == 6

    # Verify non-destructive cleanup calls were made
    assert any(
        "git" in c[0][0] and "checkout" in c[0][0] for c in mock_run.call_args_list
    )
    assert any("git" in c[0][0] and "clean" in c[0][0] for c in mock_run.call_args_list)
