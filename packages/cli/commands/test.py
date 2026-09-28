"""Unified test runner subcommand wrapping dotnet test and vitest with intelligent filtering and watch mode."""

import contextlib
import subprocess
import sys
import time
from pathlib import Path

import typer

from packages.cli.formatting import (
    console,
    is_json_mode,
    output_json,
    print_error,
    print_header,
    print_info,
    print_success,
)
from packages.testing.dependency_graph import TestDependencyGraph

test_app = typer.Typer(
    help="Run backend and frontend test suites with filtering, coverage, and watch mode."
)


def _find_target_test_file(
    modified_file: Path,
    repo_root: Path,
    graph: TestDependencyGraph | None = None,
) -> str | None:
    """Intelligently resolves a changed source file to its target test file or directory using AST dependency graph."""
    if graph:
        affected = graph.resolve_affected_tests([modified_file])
        if affected:
            return affected[0]

    rel = modified_file.relative_to(repo_root)
    parts = rel.parts

    # If it's already a test file
    if "test" in modified_file.name.lower() or "tests" in parts:
        return str(rel)

    # If it's inside apps/<service>/
    if parts and parts[0] == "apps" and len(parts) > 1:
        service = parts[1]
        service_test_dir = repo_root / "apps" / service / "tests"
        if service_test_dir.exists():
            specific_test = service_test_dir / f"test_{modified_file.stem}.py"
            if specific_test.exists():
                return str(specific_test.relative_to(repo_root))
            return str(service_test_dir.relative_to(repo_root))

    # If it's inside packages/<package>/
    if parts and parts[0] == "packages" and len(parts) > 1:
        package = parts[1]
        package_test_dir = repo_root / "packages" / package / "tests"
        if package_test_dir.exists():
            specific_test = package_test_dir / f"test_{modified_file.stem}.py"
            if specific_test.exists():
                return str(specific_test.relative_to(repo_root))
            return str(package_test_dir.relative_to(repo_root))

    return None


def _run_test_iteration(cmd: list[str], repo_root: Path) -> int:
    """Executes a single test run and returns the exit code."""
    res = subprocess.run(cmd, cwd=str(repo_root))
    return res.returncode


@test_app.callback(invoke_without_command=True)
def run_test(
    ctx: typer.Context,
    target: str = typer.Argument(None, help="Target test directory or test file"),
    service: str = typer.Option(
        None,
        "--service",
        "-s",
        help="Filter tests by service name (e.g. execution, ctms)",
    ),
    unit: bool = typer.Option(False, "--unit", help="Run unit tests only"),
    integration: bool = typer.Option(
        False, "--integration", help="Run integration tests only"
    ),
    frontend: bool = typer.Option(
        False, "--frontend", help="Run frontend Vitest suites"
    ),
    e2e: bool = typer.Option(False, "--e2e", help="Run decoupled HTTP E2E test suite"),
    dotnet: bool = typer.Option(
        False, "--dotnet", help="Run .NET 10 xUnit integration tests (Cadence.E2ETests)"
    ),
    failed_first: bool = typer.Option(
        False, "--failed-first", "--lf", help="Run failed tests first"
    ),
    coverage: bool = typer.Option(
        True, "--cov/--no-cov", help="Enable coverage checking"
    ),
    filter: str = typer.Option(
        None,
        "--filter",
        "-f",
        help="Filter .NET tests using xUnit expression (e.g. 'Category=Unit')",
    ),
    verbosity: str = typer.Option(
        None,
        "--verbosity",
        help="Set .NET test verbosity level (quiet, minimal, normal, detailed, diagnostic)",
    ),
    verbose: bool = typer.Option(
        False, "--verbose", "-v", help="Verbose .NET test output"
    ),
    watch: bool = typer.Option(
        False,
        "--watch",
        "-w",
        help="Watch file system and automatically rerun relevant tests on change",
    ),
    fast: bool = typer.Option(
        False,
        "--fast",
        help="Fast authoring mode: disable coverage overhead and run unit tests only",
    ),
) -> None:
    """Executes test suites with intelligent service filtering, reporting, and live watch mode."""
    json_mode = is_json_mode(ctx.obj)
    repo_root = Path(__file__).resolve().parents[3]

    if frontend:
        print_info("Running frontend Vitest test suites...")
        cmd = ["pnpm", "-r", "test"]
        if watch:
            cmd.append("--watch")
        res = subprocess.run(cmd, cwd=str(repo_root))
        if json_mode:
            output_json(
                {
                    "suite": "frontend",
                    "exit_code": res.returncode,
                    "success": res.returncode == 0,
                }
            )
        sys.exit(res.returncode)

    if e2e or dotnet:
        print_info("Running .NET 10 E2E integration tests...")
        cmd = ["dotnet", "test", "tests/Cadence.E2ETests.csproj"]
        if watch:
            cmd = ["dotnet", "watch", "test", "tests/Cadence.E2ETests.csproj"]
        res = subprocess.run(cmd, cwd=str(repo_root))
        if json_mode:
            output_json(
                {
                    "suite": "dotnet_e2e",
                    "exit_code": res.returncode,
                    "success": res.returncode == 0,
                }
            )
        sys.exit(res.returncode)

    # Build backend dotnet test command
    base_cmd = ["dotnet", "test", str(repo_root / "Cadence.sln")]
    if verbosity:
        base_cmd.extend(["--verbosity", verbosity])
    elif verbose:
        base_cmd.extend(["--verbosity", "detailed"])

    if filter:
        base_cmd.extend(["--filter", filter])
    elif unit or (fast and not integration):
        base_cmd.extend(["--filter", "Category!=E2E&Category!=Validation"])
    elif integration and not unit:
        base_cmd.extend(["--filter", "Category=E2E|Category=Validation"])

    target_path = None
    if service:
        service_test_dir = repo_root / "apps" / service / "tests"
        if service_test_dir.exists():
            target_path = str(service_test_dir)
        else:
            package_test_dir = repo_root / "packages" / service / "tests"
            if package_test_dir.exists():
                target_path = str(package_test_dir)
            else:
                print_error(f"No test directory found for service '{service}'")
                sys.exit(1)
    elif target:
        target_path = target

    if not watch:
        cmd = list(base_cmd)
        if target_path:
            cmd.append(target_path)

        if not json_mode:
            print_header(
                "Cadence Test Runner",
                f"Executing test command: {' '.join(cmd)}",
            )

        exit_code = _run_test_iteration(cmd, repo_root)

        if json_mode:
            output_json(
                {
                    "suite": "backend",
                    "command": cmd,
                    "exit_code": exit_code,
                    "success": exit_code == 0,
                }
            )

        if exit_code == 0:
            if not json_mode:
                print_success("All requested test suites passed successfully!")
            sys.exit(0)
        else:
            if not json_mode:
                print_error(f"Tests failed with exit code {exit_code}")
            sys.exit(exit_code)

    # Native Watch Mode Delegation
    watch_cmd = ["dotnet", "watch", "test", str(repo_root / "Cadence.sln")]
    if verbosity:
        watch_cmd.extend(["--verbosity", verbosity])
    elif verbose:
        watch_cmd.extend(["--verbosity", "detailed"])

    if filter:
        watch_cmd.extend(["--filter", filter])
    elif unit or (fast and not integration):
        watch_cmd.extend(["--filter", "Category!=E2E&Category!=Validation"])
    elif integration and not unit:
        watch_cmd.extend(["--filter", "Category=E2E|Category=Validation"])

    if target_path:
        watch_cmd.append(target_path)

    if not json_mode:
        print_header(
            "Cadence Test Watcher",
            f"Delegating test watching to native dotnet watch: {' '.join(watch_cmd)}",
        )

    try:
        res = subprocess.run(watch_cmd, cwd=str(repo_root))
        sys.exit(res.returncode)
    except KeyboardInterrupt:
        print_info("\nTest watcher stopped.")
        sys.exit(0)
