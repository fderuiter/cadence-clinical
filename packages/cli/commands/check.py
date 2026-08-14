"""Quality gates check runner for validating format, lint, security, ADRs, imports, contracts, and drift."""

import concurrent.futures
import subprocess
import sys
import time
from pathlib import Path
from typing import Any

import typer

from packages.cli.formatting import (
    console,
    create_table,
    is_json_mode,
    output_json,
    print_error,
    print_header,
    print_success,
)

check_app = typer.Typer(help="Run all pre-commit and pre-push quality gates.")


def run_gate(name: str, cmd: list[str], cwd: Path) -> dict[str, Any]:
    """Executes a single quality gate and captures its duration, output, and exit status."""
    start_time = time.time()
    res = subprocess.run(cmd, cwd=str(cwd), capture_output=True, text=True)
    duration = time.time() - start_time
    return {
        "name": name,
        "command": cmd,
        "passed": res.returncode == 0,
        "exit_code": res.returncode,
        "duration_seconds": round(duration, 2),
        "stdout": res.stdout.strip(),
        "stderr": res.stderr.strip(),
    }


@check_app.callback(invoke_without_command=True)
def run_check(
    ctx: typer.Context,
    gate: str = typer.Option(
        None,
        "--gate",
        "-g",
        help="Run a specific gate only (e.g. lint, security, imports)",
    ),
    parallel: bool = typer.Option(
        True, "--parallel/--sequential", help="Run quality gates concurrently"
    ),
) -> None:
    """Runs all repository quality gates and architecture sentinels."""
    json_mode = is_json_mode(ctx.obj)
    repo_root = Path(__file__).resolve().parents[3]

    all_gates = [
        ("path-patterns", ["python3", "scripts/validate_path_patterns.py", "--all"]),
        (
            "ruff-lint",
            [
                "uv",
                "run",
                "ruff",
                "check",
                ".",
                "--exclude",
                "apps/execution/database/models.py",
            ],
        ),
        (
            "ruff-format",
            [
                "uv",
                "run",
                "ruff",
                "format",
                "--check",
                "--target-version",
                "py313",
                ".",
                "--exclude",
                "apps/execution/database/models.py",
            ],
        ),
        ("secrets-scan", ["python3", "scripts/clean_secrets_baseline.py"]),
        ("adr-validation", ["python3", "scripts/validate_adrs.py"]),
        ("markdown-validation", ["python3", "scripts/validate_markdown.py"]),
        (
            "security-audit",
            [
                "uv",
                "run",
                "bandit",
                "-c",
                "pyproject.toml",
                "-ll",
                "-ii",
                "-r",
                "apps",
                "packages",
            ],
        ),
        ("import-boundaries", ["python3", "scripts/validate_imports.py"]),
        ("architecture-drift", ["python3", "scripts/validate_architecture_drift.py"]),
        ("contract-verification", ["python3", "scripts/verify_contracts.py"]),
    ]

    selected_gates = [g for g in all_gates if not gate or g[0] == gate]
    if not selected_gates:
        print_error(
            f"Unknown gate '{gate}'. Available: {', '.join(g[0] for g in all_gates)}"
        )
        sys.exit(1)

    if not json_mode:
        print_header(
            "Cadence Architecture & Quality Gates",
            f"Executing {len(selected_gates)} validation gate(s)...",
        )

    results: list[dict[str, Any]] = []
    if parallel and len(selected_gates) > 1:
        with concurrent.futures.ThreadPoolExecutor(
            max_workers=min(len(selected_gates), 8)
        ) as executor:
            future_to_gate = {
                executor.submit(run_gate, name, cmd, repo_root): name
                for name, cmd in selected_gates
            }
            for future in concurrent.futures.as_completed(future_to_gate):
                results.append(future.result())
    else:
        for name, cmd in selected_gates:
            if not json_mode:
                console.print(f"Running gate: [bold cyan]{name}[/bold cyan]...")
            results.append(run_gate(name, cmd, repo_root))

    # Sort results to match original order
    results.sort(key=lambda r: [g[0] for g in all_gates].index(r["name"]))

    all_passed = all(r["passed"] for r in results)

    if json_mode:
        output_json(
            {
                "all_passed": all_passed,
                "total_gates": len(results),
                "passed_gates": sum(1 for r in results if r["passed"]),
                "failed_gates": sum(1 for r in results if not r["passed"]),
                "results": results,
            }
        )
        sys.exit(0 if all_passed else 1)

    # Terminal Output Table
    table = create_table(
        "Quality Gates Verification Summary",
        [
            ("Gate Name", "bold white"),
            ("Status", "bold"),
            ("Duration", "dim"),
            ("Details", "dim"),
        ],
    )
    for r in results:
        status = "[green]PASSED[/green]" if r["passed"] else "[red]FAILED[/red]"
        details = "OK" if r["passed"] else f"Exit code {r['exit_code']}"
        table.add_row(r["name"], status, f"{r['duration_seconds']}s", details)
    console.print(table)

    if not all_passed:
        print_error("One or more quality gates failed:")
        for r in results:
            if not r["passed"]:
                console.print(
                    f"\n[bold red]--- Gate Failure: {r['name']} ---[/bold red]"
                )
                if r["stdout"]:
                    console.print(r["stdout"])
                if r["stderr"]:
                    console.print(r["stderr"])
        sys.exit(1)
    else:
        print_success("All quality gates and architecture sentinels passed cleanly!")
        sys.exit(0)
