"""Automated unit tests for unified quality engine and pre-commit hook runner.

Verifies identical check behavior, standardized JSON result structures, and correct
filtering in both staged and full-repository modes.

Requirements: Requirement 1, Requirement 2, Requirement 3, Requirement 4, Requirement 5
"""

import json
import subprocess
import sys
from pathlib import Path
import pytest

REPO_ROOT = Path(__file__).resolve().parents[2]
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

from scripts.quality_runner import (
    QualityRunner,
    get_staged_files,
    run_quality_checks,
    check_ruff_lint_and_format,
    check_dotnet_format,
    check_import_boundaries,
    check_dependency_analysis,
    check_openapi_drift,
)
from scripts.pre_commit import (
    get_staged_files as pre_commit_get_staged_files,
    run_tier1_staged_checks,
    run_tier2_repo_checks,
    main as pre_commit_main,
)


def test_staged_mode_schema_and_keys():
    """Verify quality runner in staged mode returns standardized JSON schema."""
    result = run_quality_checks(staged=True, staged_files=[], repo_root=REPO_ROOT)

    assert "passed" in result
    assert result["mode"] == "staged"
    assert "staged_files" in result
    assert "checks" in result
    assert "failed_files" in result
    assert "errors" in result
    assert "total_execution_time_seconds" in result
    assert isinstance(result["checks"], list)
    assert len(result["checks"]) == 5

    check_names = [c["name"] for c in result["checks"]]
    expected_names = [
        "ruff",
        "dotnet_format",
        "import_boundaries",
        "dependency_analysis",
        "openapi_drift",
    ]
    assert check_names == expected_names

    for check in result["checks"]:
        assert "name" in check
        assert "passed" in check
        assert "failed_files" in check
        assert "error_message" in check
        assert "execution_time_seconds" in check


def test_full_repo_mode_schema_and_keys():
    """Verify quality runner in full repo mode returns identical standardized JSON schema."""
    result = run_quality_checks(staged=False, repo_root=REPO_ROOT)

    assert "passed" in result
    assert result["mode"] == "all-files"
    assert "staged_files" in result
    assert "checks" in result
    assert "failed_files" in result
    assert "errors" in result
    assert "total_execution_time_seconds" in result
    assert isinstance(result["checks"], list)
    assert len(result["checks"]) == 5

    check_names = [c["name"] for c in result["checks"]]
    expected_names = [
        "ruff",
        "dotnet_format",
        "import_boundaries",
        "dependency_analysis",
        "openapi_drift",
    ]
    assert check_names == expected_names

    for check in result["checks"]:
        assert "name" in check
        assert "passed" in check
        assert "failed_files" in check
        assert "error_message" in check
        assert "execution_time_seconds" in check


def test_staged_vs_all_files_parity():
    """Verify parity in check registration and result structure between staged and all-files modes."""
    staged_res = run_quality_checks(staged=True, staged_files=["apps/test.py"], repo_root=REPO_ROOT)
    all_res = run_quality_checks(staged=False, repo_root=REPO_ROOT)

    staged_checks = [c["name"] for c in staged_res["checks"]]
    all_checks = [c["name"] for c in all_res["checks"]]

    assert staged_checks == all_checks
    assert set(staged_res.keys()) == set(all_res.keys())


def test_pre_commit_delegation():
    """Verify pre_commit.py functions properly delegate to the quality runner."""
    staged = pre_commit_get_staged_files(REPO_ROOT)
    assert isinstance(staged, list)

    t1 = run_tier1_staged_checks(staged, repo_root=REPO_ROOT)
    assert "passed" in t1
    assert "staged_count" in t1
    assert "errors" in t1

    t2 = run_tier2_repo_checks(repo_root=REPO_ROOT)
    assert "passed" in t2
    assert "errors" in t2


def test_quality_runner_cli_json():
    """Verify CLI execution of quality_runner.py with --json flag."""
    cmd = [
        sys.executable,
        str(REPO_ROOT / "scripts" / "quality_runner.py"),
        "--staged",
        "--json",
    ]
    res = subprocess.run(cmd, capture_output=True, text=True, cwd=str(REPO_ROOT))
    assert res.returncode == 0
    parsed = json.loads(res.stdout)
    assert parsed["mode"] == "staged"
    assert "checks" in parsed


def test_quality_runner_failure_handling(tmp_path):
    """Verify quality runner detects violations and reports failure status."""
    # Create temporary package.json with forbidden crypto package
    bad_pkg_json = tmp_path / "package.json"
    bad_pkg_json.write_text(
        json.dumps(
            {
                "name": "bad-pkg",
                "dependencies": {"node-forge": "1.0.0"},
            }
        )
    )

    res = check_dependency_analysis(
        staged=True,
        staged_files=[str(bad_pkg_json.relative_to(tmp_path))],
        repo_root=tmp_path,
    )
    assert res["passed"] is False
    assert len(res["failed_files"]) > 0
    assert "node-forge" in res["error_message"]
