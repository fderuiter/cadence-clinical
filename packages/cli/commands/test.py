"""Unified test runner subcommand wrapping pytest and vitest with intelligent filtering."""

import subprocess
import sys
from pathlib import Path

import typer

from packages.cli.formatting import (
    is_json_mode,
    output_json,
    print_error,
    print_header,
    print_info,
    print_success,
)

test_app = typer.Typer(
    help="Run backend and frontend test suites with filtering and coverage."
)


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
    failed_first: bool = typer.Option(
        False, "--failed-first", "--lf", help="Run failed tests first"
    ),
    coverage: bool = typer.Option(
        True, "--cov/--no-cov", help="Enable coverage checking"
    ),
    xdist: bool = typer.Option(
        True, "--xdist/--no-xdist", help="Run tests in parallel with pytest-xdist"
    ),
    verbose: bool = typer.Option(
        False, "--verbose", "-v", help="Verbose pytest output"
    ),
) -> None:
    """Executes test suites with intelligent service filtering and reporting."""
    json_mode = is_json_mode(ctx.obj)
    repo_root = Path(__file__).resolve().parents[3]

    if frontend:
        print_info("Running frontend Vitest test suites...")
        cmd = ["pnpm", "-r", "test"]
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

    # Backend Pytest Execution
    cmd = ["uv", "run", "pytest"]
    if xdist:
        cmd.extend(["-n", "auto"])
    if failed_first:
        cmd.append("--failed-first")
    if verbose:
        cmd.append("-v")
    if not coverage:
        cmd.append("--no-cov")

    if service:
        service_test_dir = repo_root / "apps" / service / "tests"
        if service_test_dir.exists():
            cmd.append(str(service_test_dir))
        else:
            package_test_dir = repo_root / "packages" / service / "tests"
            if package_test_dir.exists():
                cmd.append(str(package_test_dir))
            else:
                print_error(f"No test directory found for service '{service}'")
                sys.exit(1)
    elif target:
        cmd.append(target)

    if unit and not integration:
        cmd.extend(["-m", "not integration and not e2e"])
    elif integration and not unit:
        cmd.extend(["-m", "integration"])

    if not json_mode:
        print_header(
            "Cadence Test Runner",
            f"Executing test command: {' '.join(cmd)}",
        )

    res = subprocess.run(cmd, cwd=str(repo_root))

    if json_mode:
        output_json(
            {
                "suite": "backend",
                "command": cmd,
                "exit_code": res.returncode,
                "success": res.returncode == 0,
            }
        )

    if res.returncode == 0:
        if not json_mode:
            print_success("All requested test suites passed successfully!")
        sys.exit(0)
    else:
        if not json_mode:
            print_error(f"Tests failed with exit code {res.returncode}")
        sys.exit(res.returncode)
