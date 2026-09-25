#!/usr/bin/env python3
"""Two-tier git pre-commit hook runner for Cadence Clinical.

Delegates staged checks and repository assertions to the unified quality runner.
Auto-installed via cadence doctor.

Requirements: PRD-SYS-049, ADR-2189, Requirement 3
"""

from __future__ import annotations

import stat
import sys
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).resolve().parents[1]
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

from scripts.runtime_guard import enforce_python_runtime
from scripts.quality_runner import (
    QualityRunner,
    get_staged_files as runner_get_staged_files,
    run_quality_checks,
)

enforce_python_runtime()


def get_staged_files(repo_root: Path | None = None) -> list[str]:
    """Retrieves list of staged file paths from git index.

    Args:
        repo_root: Optional repository root path.

    Returns:
        List of relative staged file paths.
    """
    return runner_get_staged_files(repo_root or REPO_ROOT)


def run_tier1_staged_checks(
    staged_files: list[str], repo_root: Path | None = None
) -> dict[str, Any]:
    """Executes fast Tier 1 checks on staged Python, C#, and configuration files via quality runner.

    Args:
        staged_files: List of staged file paths.
        repo_root: Optional repository root path.

    Returns:
        Result dictionary containing passed status and error details.
    """
    root = repo_root or REPO_ROOT
    results = run_quality_checks(staged=True, staged_files=staged_files, repo_root=root)
    t1_checks = [c for c in results["checks"] if c["name"] in ("ruff", "dotnet_format")]
    t1_passed = all(c["passed"] for c in t1_checks)
    t1_errors = [c["error_message"] for c in t1_checks if not c["passed"] and c["error_message"]]

    py_cs_count = len([f for f in staged_files if f.endswith((".py", ".cs", ".csproj"))])
    return {
        "passed": t1_passed,
        "staged_count": py_cs_count,
        "errors": t1_errors,
    }


def run_tier2_repo_checks(repo_root: Path | None = None) -> dict[str, Any]:
    """Executes fast Tier 2 repository-level architectural invariants via quality runner.

    Args:
        repo_root: Optional repository root path.

    Returns:
        Result dictionary containing passed status and error details.
    """
    root = repo_root or REPO_ROOT
    staged = get_staged_files(root)
    results = run_quality_checks(staged=True, staged_files=staged, repo_root=root)
    t2_checks = [
        c
        for c in results["checks"]
        if c["name"] in ("import_boundaries", "dependency_analysis", "openapi_drift")
    ]
    t2_passed = all(c["passed"] for c in t2_checks)
    t2_errors = [c["error_message"] for c in t2_checks if not c["passed"] and c["error_message"]]

    return {
        "passed": t2_passed,
        "errors": t2_errors,
    }


def install_pre_commit_hook(repo_root: Path | None = None) -> tuple[bool, str]:
    """Installs or updates the git pre-commit hook script in .git/hooks/.

    Args:
        repo_root: Optional repository root path.

    Returns:
        Tuple of (success_boolean, status_message).
    """
    root = repo_root or REPO_ROOT
    git_hooks_dir = root / ".git" / "hooks"

    if not (root / ".git").exists():
        return False, "Not a git repository: .git directory not found."

    git_hooks_dir.mkdir(parents=True, exist_ok=True)
    hook_path = git_hooks_dir / "pre-commit"

    hook_content = (
        "#!/bin/sh\n"
        "# Cadence Clinical Pre-Commit Hook\n"
        'exec uv run --python 3.14 python scripts/pre_commit.py "$@"\n'
    )

    try:
        hook_path.write_text(hook_content)
        current_mode = hook_path.stat().st_mode
        hook_path.chmod(current_mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)
        return True, "Pre-commit hook installed successfully."
    except Exception as exc:
        return False, f"Failed to install pre-commit hook: {exc}"


def main() -> int:
    """CLI entry point for git pre-commit hook using unified quality runner."""
    staged = get_staged_files()
    runner = QualityRunner(repo_root=REPO_ROOT)
    res = runner.run_staged_checks(staged_files=staged)

    if not res["passed"]:
        for err in res["errors"]:
            print(err, file=sys.stderr)
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
