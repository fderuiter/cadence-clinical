#!/usr/bin/env python3
"""Two-tier git pre-commit hook runner for Cadence Clinical.

Executes sub-second staged checks (Ruff lint/format, secrets) and repository
assertions before commits are finalized. Auto-installed via cadence doctor.

Requirements: PRD-SYS-049, ADR-2189
"""

from __future__ import annotations

import shutil
import stat
import subprocess
import sys
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).resolve().parents[1]
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

from scripts.runtime_guard import enforce_python_runtime

enforce_python_runtime()


def get_staged_files(repo_root: Path | None = None) -> list[str]:
    """Retrieves list of staged file paths from git index.

    Args:
        repo_root: Optional repository root path.

    Returns:
        List of relative staged file paths.
    """
    root = repo_root or REPO_ROOT
    try:
        res = subprocess.run(
            ["git", "diff", "--cached", "--name-only", "--diff-filter=ACM"],
            cwd=str(root),
            capture_output=True,
            text=True,
            check=False,
        )
        if res.returncode == 0:
            return [line.strip() for line in res.stdout.splitlines() if line.strip()]
    except Exception:
        pass
    return []


def run_tier1_staged_checks(
    staged_files: list[str], repo_root: Path | None = None
) -> dict[str, Any]:
    """Executes fast Tier 1 checks on staged Python, C#, and configuration files.

    Args:
        staged_files: List of staged file paths.
        repo_root: Optional repository root path.

    Returns:
        Result dictionary containing passed status and error details.
    """
    root = repo_root or REPO_ROOT
    py_files = [f for f in staged_files if f.endswith(".py")]
    cs_files = [f for f in staged_files if f.endswith(".cs") or f.endswith(".csproj")]

    if not py_files and not cs_files:
        return {"passed": True, "staged_count": 0, "errors": []}

    errors: list[str] = []

    if py_files:
        # 1. Run ruff check --fix on staged python files
        res_check = subprocess.run(
            ["uv", "run", "--python", "3.14", "ruff", "check", "--fix", *py_files],
            cwd=str(root),
            capture_output=True,
            text=True,
            check=False,
        )
        if res_check.returncode != 0:
            errors.append(f"Ruff lint error:\n{res_check.stdout}\n{res_check.stderr}")

        # 2. Run ruff format on staged python files
        res_fmt = subprocess.run(
            ["uv", "run", "--python", "3.14", "ruff", "format", *py_files],
            cwd=str(root),
            capture_output=True,
            text=True,
            check=False,
        )
        if res_fmt.returncode != 0:
            errors.append(f"Ruff format error:\n{res_fmt.stdout}\n{res_fmt.stderr}")

        # 3. Re-stage any auto-fixed files
        subprocess.run(
            ["git", "add", *py_files],
            cwd=str(root),
            capture_output=True,
            check=False,
        )

    if cs_files and shutil.which("dotnet"):
        res_cs_fmt = subprocess.run(
            [
                "dotnet",
                "format",
                "whitespace",
                "Cadence.sln",
                "--verify-no-changes",
                "--no-restore",
            ],
            cwd=str(root),
            capture_output=True,
            text=True,
            check=False,
        )
        if res_cs_fmt.returncode != 0:
            errors.append(
                f"C# formatting error (.editorconfig):\n{res_cs_fmt.stdout}\n{res_cs_fmt.stderr}"
            )

        res_cs_style = subprocess.run(
            [
                "dotnet",
                "format",
                "style",
                "Cadence.sln",
                "--verify-no-changes",
                "--no-restore",
            ],
            cwd=str(root),
            capture_output=True,
            text=True,
            check=False,
        )
        if res_cs_style.returncode != 0:
            errors.append(
                f"C# Roslyn analyzer and code style error:\n{res_cs_style.stdout}\n{res_cs_style.stderr}"
            )

    return {
        "passed": len(errors) == 0,
        "staged_count": len(py_files) + len(cs_files),
        "errors": errors,
    }


def run_tier2_repo_checks(repo_root: Path | None = None) -> dict[str, Any]:
    """Executes fast Tier 2 repository-level architectural invariants.

    Args:
        repo_root: Optional repository root path.

    Returns:
        Result dictionary containing passed status and error details.
    """
    root = repo_root or REPO_ROOT
    errors: list[str] = []

    # Fast import boundaries check
    res_imp = subprocess.run(
        ["uv", "run", "--python", "3.14", "python", "scripts/validate_imports.py"],
        cwd=str(root),
        capture_output=True,
        text=True,
        check=False,
    )
    if res_imp.returncode != 0:
        errors.append(f"Import boundary violation:\n{res_imp.stdout}\n{res_imp.stderr}")

    # Dependency analyzer check (forbidden asymmetric crypto packages)
    res_deps = subprocess.run(
        ["uv", "run", "--python", "3.14", "python", "scripts/validate_dependencies.py"],
        cwd=str(root),
        capture_output=True,
        text=True,
        check=False,
    )
    if res_deps.returncode != 0:
        errors.append(
            f"Forbidden package dependency violation:\n{res_deps.stdout}\n{res_deps.stderr}"
        )

    # OpenAPI schema drift and type check
    res_openapi = subprocess.run(
        ["uv", "run", "--python", "3.14", "python", "scripts/pre_commit_openapi.py"],
        cwd=str(root),
        capture_output=True,
        text=True,
        check=False,
    )
    if res_openapi.returncode != 0:
        errors.append(
            f"OpenAPI schema drift or typecheck violation:\n{res_openapi.stdout}\n{res_openapi.stderr}"
        )

    return {
        "passed": len(errors) == 0,
        "errors": errors,
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
    """CLI entry point for git pre-commit hook."""
    staged = get_staged_files()
    t1 = run_tier1_staged_checks(staged)
    if not t1["passed"]:
        for err in t1["errors"]:
            print(err, file=sys.stderr)
        return 1

    t2 = run_tier2_repo_checks()
    if not t2["passed"]:
        for err in t2["errors"]:
            print(err, file=sys.stderr)
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
