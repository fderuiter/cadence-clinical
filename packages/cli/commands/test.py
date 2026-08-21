"""Unified test runner subcommand wrapping pytest and vitest with intelligent filtering and watch mode."""

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

    # Build backend Pytest command
    base_cmd = ["uv", "run", "pytest"]
    if xdist and not watch:
        base_cmd.extend(["-n", "auto"])
    if failed_first:
        base_cmd.append("--failed-first")
    if verbose:
        base_cmd.append("-v")
    if not coverage or fast or watch:
        base_cmd.append("--no-cov")

    if unit or fast and not integration:
        base_cmd.extend(["-m", "not integration and not e2e"])
    elif integration and not unit:
        base_cmd.extend(["-m", "integration"])

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

    # Watch Mode Loop
    print_header(
        "Cadence Test Watcher",
        "Watching apps/, packages/, tests/ for changes (Ctrl+C to quit)...",
    )

    dep_graph = TestDependencyGraph(repo_root=repo_root)
    dep_graph.load_or_build()

    # Track mtimes of Python files
    def _snapshot_mtimes() -> dict[Path, float]:
        mtimes = {}
        for p in repo_root.glob("**/*.py"):
            if ".venv" in p.parts or "__pycache__" in p.parts or ".git" in p.parts:
                continue
            with contextlib.suppress(OSError):
                mtimes[p] = p.stat().st_mtime
        return mtimes

    last_snapshot = _snapshot_mtimes()

    # Initial Run
    cmd = list(base_cmd)
    if target_path:
        cmd.append(target_path)
    console.print(f"[bold cyan]▶ Initial Test Run: {' '.join(cmd)}[/bold cyan]")
    _run_test_iteration(cmd, repo_root)

    try:
        while True:
            time.sleep(0.5)
            current_snapshot = _snapshot_mtimes()
            modified = []
            for p, mtime in current_snapshot.items():
                if p not in last_snapshot or mtime > last_snapshot[p]:
                    modified.append(p)

            last_snapshot = current_snapshot

            if modified:
                console.print(
                    f"\n[bold yellow]↻ Detected change in {len(modified)} file(s):[/bold yellow]"
                )
                for m in modified[:3]:
                    console.print(f"  • {m.relative_to(repo_root)}")

                # Determine target test
                specific_target = None
                for m in modified:
                    resolved = _find_target_test_file(m, repo_root, dep_graph)
                    if resolved:
                        specific_target = resolved
                        break

                iter_cmd = list(base_cmd)
                if specific_target:
                    iter_cmd.append(specific_target)
                elif target_path:
                    iter_cmd.append(target_path)

                console.print(
                    f"[bold cyan]▶ Running: {' '.join(iter_cmd)}[/bold cyan]\n"
                )
                _run_test_iteration(iter_cmd, repo_root)
                console.print("[dim]Watching for file changes...[/dim]")

    except KeyboardInterrupt:
        print_info("\nTest watcher stopped.")
        sys.exit(0)
