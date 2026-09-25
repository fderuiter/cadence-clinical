#!/usr/bin/env python3
"""Unified Python quality execution engine for pre-commit hooks and CI pipelines.

Consolidates linting, formatting, import boundary rules, dependency analysis,
and OpenAPI schema drift verification into a single execution module.

Requirements: Requirement 1, Requirement 2, Requirement 3, Requirement 4
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
import time
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


def check_ruff_lint_and_format(
    staged: bool = False,
    staged_files: list[str] | None = None,
    repo_root: Path | None = None,
) -> dict[str, Any]:
    """Executes Ruff linting and formatting on Python files.

    Args:
        staged: If True, operates on staged files; if False, scans repository.
        staged_files: List of staged file paths if in staged mode.
        repo_root: Optional repository root path.

    Returns:
        Standardized check result dict.
    """
    start_time = time.time()
    root = repo_root or REPO_ROOT
    errors: list[str] = []
    failed_files: list[str] = []

    if staged:
        files = staged_files if staged_files is not None else get_staged_files(root)
        py_files = [f for f in files if f.endswith(".py")]
        if not py_files:
            return {
                "name": "ruff",
                "passed": True,
                "failed_files": [],
                "error_message": "",
                "execution_time_seconds": round(time.time() - start_time, 4),
            }

        # 1. Ruff lint check & fix
        res_check = subprocess.run(
            ["uv", "run", "--python", "3.14", "ruff", "check", "--fix", *py_files],
            cwd=str(root),
            capture_output=True,
            text=True,
            check=False,
        )
        if res_check.returncode != 0:
            errors.append(f"Ruff lint error:\n{res_check.stdout}\n{res_check.stderr}")
            failed_files.extend(py_files)

        # 2. Ruff format check
        res_fmt = subprocess.run(
            ["uv", "run", "--python", "3.14", "ruff", "format", *py_files],
            cwd=str(root),
            capture_output=True,
            text=True,
            check=False,
        )
        if res_fmt.returncode != 0:
            errors.append(f"Ruff format error:\n{res_fmt.stdout}\n{res_fmt.stderr}")
            failed_files.extend(py_files)

        # Re-stage any auto-fixed files
        if py_files:
            subprocess.run(
                ["git", "add", *py_files],
                cwd=str(root),
                capture_output=True,
                check=False,
            )
    else:
        # Full repo mode
        res_check = subprocess.run(
            ["uv", "run", "--python", "3.14", "ruff", "check", "."],
            cwd=str(root),
            capture_output=True,
            text=True,
            check=False,
        )
        if res_check.returncode != 0:
            errors.append(f"Ruff lint error:\n{res_check.stdout}\n{res_check.stderr}")

        res_fmt = subprocess.run(
            ["uv", "run", "--python", "3.14", "ruff", "format", "--check", "."],
            cwd=str(root),
            capture_output=True,
            text=True,
            check=False,
        )
        if res_fmt.returncode != 0:
            errors.append(f"Ruff format error:\n{res_fmt.stdout}\n{res_fmt.stderr}")

    unique_failed = sorted(list(set(failed_files)))
    return {
        "name": "ruff",
        "passed": len(errors) == 0,
        "failed_files": unique_failed,
        "error_message": "\n".join(errors),
        "execution_time_seconds": round(time.time() - start_time, 4),
    }


def check_dotnet_format(
    staged: bool = False,
    staged_files: list[str] | None = None,
    repo_root: Path | None = None,
) -> dict[str, Any]:
    """Executes C# dotnet format checks (whitespace and style).

    Args:
        staged: If True, operates on staged files filter; if False, scans solution.
        staged_files: List of staged file paths if in staged mode.
        repo_root: Optional repository root path.

    Returns:
        Standardized check result dict.
    """
    start_time = time.time()
    root = repo_root or REPO_ROOT
    errors: list[str] = []
    failed_files: list[str] = []

    if not shutil.which("dotnet"):
        return {
            "name": "dotnet_format",
            "passed": True,
            "failed_files": [],
            "error_message": "dotnet SDK not found; skipped .NET formatting.",
            "execution_time_seconds": round(time.time() - start_time, 4),
        }

    if staged:
        files = staged_files if staged_files is not None else get_staged_files(root)
        cs_files = [
            f for f in files if f.endswith(".cs") or f.endswith(".csproj")
        ]
        if not cs_files:
            return {
                "name": "dotnet_format",
                "passed": True,
                "failed_files": [],
                "error_message": "",
                "execution_time_seconds": round(time.time() - start_time, 4),
            }

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
        "name": "dotnet_format",
        "passed": len(errors) == 0,
        "failed_files": failed_files,
        "error_message": "\n".join(errors),
        "execution_time_seconds": round(time.time() - start_time, 4),
    }


def check_import_boundaries(
    staged: bool = False,
    staged_files: list[str] | None = None,
    repo_root: Path | None = None,
) -> dict[str, Any]:
    """Executes cross-service import boundary validation.

    Args:
        staged: If True, inspects staged files; if False, scans apps/ and packages/.
        staged_files: List of staged file paths.
        repo_root: Optional repository root path.

    Returns:
        Standardized check result dict.
    """
    start_time = time.time()
    root = repo_root or REPO_ROOT
    from scripts.validate_imports import check_file_imports, APPS_DIR, PACKAGES_DIR

    errors: list[str] = []
    failed_files: list[str] = []

    files_to_check: list[Path] = []

    if staged and staged_files is not None:
        py_files = [f for f in staged_files if f.endswith(".py")]
        for rel in py_files:
            p = root / rel
            if p.exists() and (
                p.is_relative_to(root / "apps") or p.is_relative_to(root / "packages")
            ):
                files_to_check.append(p)
    else:
        # Full scan or staged without files specified
        for d in [root / "apps", root / "packages"]:
            if d.exists():
                for dirpath, _, filenames in os.walk(d):
                    for fname in filenames:
                        if fname.endswith(".py"):
                            files_to_check.append(Path(dirpath) / fname)

    for file_path in files_to_check:
        violations = check_file_imports(file_path)
        if violations:
            try:
                rel_path = str(file_path.relative_to(root))
            except ValueError:
                rel_path = str(file_path)
            failed_files.append(rel_path)
            for v in violations:
                errors.append(f"File {rel_path}: {v}")

    return {
        "name": "import_boundaries",
        "passed": len(errors) == 0,
        "failed_files": sorted(list(set(failed_files))),
        "error_message": "\n".join(errors),
        "execution_time_seconds": round(time.time() - start_time, 4),
    }


def check_dependency_analysis(
    staged: bool = False,
    staged_files: list[str] | None = None,
    repo_root: Path | None = None,
) -> dict[str, Any]:
    """Executes dependency analysis for forbidden asymmetric crypto packages in package.json files.

    Args:
        staged: If True, inspects staged package.json files; if False, scans repository.
        staged_files: List of staged file paths.
        repo_root: Optional repository root path.

    Returns:
        Standardized check result dict.
    """
    start_time = time.time()
    root = repo_root or REPO_ROOT
    from scripts.validate_dependencies import check_package_json

    errors: list[str] = []
    failed_files: list[str] = []

    pkg_files: list[Path] = []

    if staged and staged_files is not None:
        json_files = [f for f in staged_files if f.endswith("package.json")]
        for rel in json_files:
            p = root / rel
            if p.exists():
                pkg_files.append(p)
    else:
        exclude_dirs = {
            ".venv",
            "venv",
            "node_modules",
            ".git",
            "dist",
            "build",
            ".pytest_cache",
            ".ruff_cache",
        }
        for dirpath, dirs, filenames in os.walk(root):
            dirs[:] = [d for d in dirs if d not in exclude_dirs]
            for fname in filenames:
                if fname == "package.json":
                    pkg_files.append(Path(dirpath) / fname)

    for pkg_file in pkg_files:
        violations = check_package_json(pkg_file)
        if violations:
            try:
                rel_path = str(pkg_file.relative_to(root))
            except ValueError:
                rel_path = str(pkg_file)
            failed_files.append(rel_path)
            for v in violations:
                errors.append(f"File {rel_path}: {v}")

    return {
        "name": "dependency_analysis",
        "passed": len(errors) == 0,
        "failed_files": sorted(list(set(failed_files))),
        "error_message": "\n".join(errors),
        "execution_time_seconds": round(time.time() - start_time, 4),
    }


def check_openapi_drift(
    staged: bool = False,
    staged_files: list[str] | None = None,
    repo_root: Path | None = None,
) -> dict[str, Any]:
    """Executes OpenAPI schema drift and contract checks.

    Args:
        staged: If True, checks if staged files trigger schema generation; if False, runs checks.
        staged_files: List of staged file paths.
        repo_root: Optional repository root path.

    Returns:
        Standardized check result dict.
    """
    start_time = time.time()
    root = repo_root or REPO_ROOT
    from scripts.pre_commit_openapi import (
        should_trigger_schema_generation,
        check_and_run_exporter,
    )

    errors: list[str] = []
    failed_files: list[str] = []

    s_files = staged_files if staged_files is not None else get_staged_files(root)

    if staged and not should_trigger_schema_generation(s_files):
        return {
            "name": "openapi_drift",
            "passed": True,
            "failed_files": [],
            "error_message": "",
            "execution_time_seconds": round(time.time() - start_time, 4),
        }

    exit_code = check_and_run_exporter()
    if exit_code != 0:
        errors.append(f"OpenAPI schema drift or typecheck violation (exit code {exit_code}).")

    return {
        "name": "openapi_drift",
        "passed": exit_code == 0,
        "failed_files": failed_files,
        "error_message": "\n".join(errors),
        "execution_time_seconds": round(time.time() - start_time, 4),
    }


class QualityRunner:
    """Unified quality check engine."""

    def __init__(self, repo_root: Path | None = None) -> None:
        self.repo_root = repo_root or REPO_ROOT

    def run_checks(
        self, staged: bool = False, staged_files: list[str] | None = None
    ) -> dict[str, Any]:
        """Executes all registered quality checks in the specified mode.

        Args:
            staged: True for staged file mode (pre-commit), False for full repository mode (CI).
            staged_files: Optional pre-fetched list of staged file paths.

        Returns:
            Standardized JSON-serializable result dictionary.
        """
        start_time = time.time()
        files = (
            staged_files
            if staged_files is not None
            else (get_staged_files(self.repo_root) if staged else [])
        )

        checks_to_run = [
            check_ruff_lint_and_format,
            check_dotnet_format,
            check_import_boundaries,
            check_dependency_analysis,
            check_openapi_drift,
        ]

        check_results: list[dict[str, Any]] = []
        all_failed_files: list[str] = []
        all_errors: list[str] = []

        for check_fn in checks_to_run:
            res = check_fn(
                staged=staged, staged_files=files, repo_root=self.repo_root
            )
            check_results.append(res)
            if not res["passed"]:
                if res.get("error_message"):
                    all_errors.append(f"[{res['name']}] {res['error_message']}")
                if res.get("failed_files"):
                    all_failed_files.extend(res["failed_files"])

        total_passed = all(c["passed"] for c in check_results)
        unique_failed_files = sorted(list(set(all_failed_files)))

        return {
            "passed": total_passed,
            "mode": "staged" if staged else "all-files",
            "staged_files": files if staged else [],
            "checks": check_results,
            "failed_files": unique_failed_files,
            "errors": all_errors,
            "total_execution_time_seconds": round(time.time() - start_time, 4),
        }

    def run_staged_checks(
        self, staged_files: list[str] | None = None
    ) -> dict[str, Any]:
        """Convenience method to execute staged checks."""
        return self.run_checks(staged=True, staged_files=staged_files)

    def run_all_checks(self) -> dict[str, Any]:
        """Convenience method to execute full repository checks."""
        return self.run_checks(staged=False)


def run_quality_checks(
    staged: bool = False,
    staged_files: list[str] | None = None,
    repo_root: Path | None = None,
) -> dict[str, Any]:
    """Helper function to execute quality engine checks.

    Args:
        staged: True for staged mode, False for full repo mode.
        staged_files: Optional staged files list.
        repo_root: Optional repo root path.

    Returns:
        Standardized JSON-serializable result dictionary.
    """
    runner = QualityRunner(repo_root=repo_root)
    return runner.run_checks(staged=staged, staged_files=staged_files)


def main() -> int:
    """CLI entrypoint for quality runner."""
    parser = argparse.ArgumentParser(
        description="Unified Python Quality Execution Engine"
    )
    mode_group = parser.add_mutually_exclusive_group()
    mode_group.add_argument(
        "--staged",
        action="store_true",
        help="Run quality checks in staged pre-commit mode",
    )
    mode_group.add_argument(
        "--all-files",
        action="store_true",
        help="Run quality checks on full repository (CI pipeline mode)",
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="Output check results in standardized JSON format",
    )
    parser.add_argument(
        "--repo-root",
        type=str,
        default=None,
        help="Path to repository root",
    )

    args = parser.parse_args()
    staged_mode = args.staged or (not args.all_files)
    root_path = Path(args.repo_root).resolve() if args.repo_root else REPO_ROOT

    results = run_quality_checks(staged=staged_mode, repo_root=root_path)

    if args.json:
        print(json.dumps(results, indent=2))
    else:
        print(f"=== Unified Quality Engine ({results['mode']} mode) ===")
        print(f"Total time: {results['total_execution_time_seconds']}s")
        print("Checks execution status:")
        for check in results["checks"]:
            status = "PASSED" if check["passed"] else "FAILED"
            print(f" - {check['name']}: {status} ({check['execution_time_seconds']}s)")
            if not check["passed"] and check["error_message"]:
                print(f"   Error: {check['error_message']}")

        if not results["passed"]:
            print("\n[ERROR] Quality checks failed.")

    return 0 if results["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
