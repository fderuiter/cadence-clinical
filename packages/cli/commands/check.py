"""Quality gates check runner for validating format, lint, security, ADRs, imports, contracts, and drift."""

import concurrent.futures
import subprocess
import sys
import time
from pathlib import Path
from typing import Any

import typer

from packages.cli.formatting import (
    TerminalDocument,
    is_json_mode,
    output_json,
    print_error,
)

check_app = typer.Typer(help="Run all pre-commit and pre-push quality gates.")


def run_gate(name: str, cmd: list[str], cwd: Path) -> dict[str, Any]:
    """Executes a single quality gate and captures its duration, output, and exit status."""
    start_time = time.time()
    res = subprocess.run(cmd, cwd=str(cwd), capture_output=True, text=True, check=False)
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
        (
            "path-patterns",
            [sys.executable, "scripts/validate_path_patterns.py", "--all"],
        ),
        (
            "ruff-lint",
            [
                sys.executable,
                "-m",
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
                sys.executable,
                "-m",
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
        ("secrets-scan", [sys.executable, "scripts/clean_secrets_baseline.py"]),
        ("adr-validation", [sys.executable, "scripts/validate_adrs.py"]),
        ("markdown-validation", [sys.executable, "scripts/validate_markdown.py"]),
        (
            "security-audit",
            [
                sys.executable,
                "-m",
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
        ("import-boundaries", [sys.executable, "scripts/validate_imports.py"]),
        (
            "architecture-drift",
            [sys.executable, "scripts/validate_architecture_drift.py"],
        ),
        ("contract-verification", [sys.executable, "scripts/verify_contracts.py"]),
    ]

    selected_gates = [g for g in all_gates if not gate or g[0] == gate]
    if not selected_gates:
        print_error(
            f"Unknown gate '{gate}'. Available: {', '.join(g[0] for g in all_gates)}"
        )
        sys.exit(1)

    start_total = time.time()
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
            results.append(run_gate(name, cmd, repo_root))

    # Sort results to match original order
    results.sort(key=lambda r: [g[0] for g in all_gates].index(r["name"]))

    total_duration = round(time.time() - start_total, 2)
    all_passed = all(r["passed"] for r in results)
    passed_count = sum(1 for r in results if r["passed"])
    failed_count = len(results) - passed_count

    if json_mode:
        output_json(
            {
                "command": "check",
                "success": all_passed,
                "total_gates": len(results),
                "passed_gates": passed_count,
                "failed_gates": failed_count,
                "duration_seconds": total_duration,
                "results": results,
            }
        )
        if not all_passed:
            sys.exit(1)
        return

    doc = TerminalDocument(
        title="Cadence Architecture Sentinels & Quality Gates",
        subtitle=f"Evaluating {len(selected_gates)} architecture gate(s) {'concurrently' if parallel else 'sequentially'}...",
    )

    doc.add_metric(
        "Status",
        "ALL PASSED" if all_passed else "FAILURES DETECTED",
        style="green" if all_passed else "red",
    )
    doc.add_metric("Passed", f"{passed_count}/{len(results)}", style="green")
    doc.add_metric(
        "Failed", f"{failed_count}", style="red" if failed_count > 0 else "dim"
    )
    doc.add_metric("Duration", f"{total_duration}s", style="cyan")

    gate_rows = []
    for r in results:
        status_label = "✔ Passed" if r["passed"] else "✘ Failed"
        gate_rows.append([r["name"], status_label, f"{r['duration_seconds']}s"])

    doc.add_table_data(
        "Architecture Gate Evaluations",
        [("Gate", "bold white"), ("Status", "bold"), ("Duration", "dim")],
        gate_rows,
    )

    if not all_passed:
        for r in results:
            if not r["passed"]:
                err_detail = r["stdout"] or r["stderr"]
                first_line = (
                    err_detail.split("\n")[0] if err_detail else "Exit code non-zero"
                )
                doc.add_item(
                    f"Gate '{r['name']}' failed", status="fail", detail=first_line
                )
        doc.set_cta(
            "Run 'uv run cadence fix --all' to automatically remediate lint, formatting, and schema drift."
        )
        doc.display()
        sys.exit(1)
    else:
        doc.set_cta("All architecture sentinels passed cleanly. Ready for PR commit.")
        doc.display()
