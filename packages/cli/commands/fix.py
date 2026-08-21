"""Self-healing and auto-remediation command for code style, schemas, and documentation."""

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
)
from scripts.pre_commit import install_pre_commit_hook

fix_app = typer.Typer(
    help="Auto-fix lint errors, format code, index ADRs, and synchronize schemas."
)


@fix_app.callback(invoke_without_command=True)
def run_fix(
    ctx: typer.Context,
    all_remediations: bool = typer.Option(
        False,
        "--all",
        "-a",
        help="Execute all self-healing, schema synchronization, and formatting steps",
    ),
    sync_gxp: bool = typer.Option(
        False,
        "--sync-gxp",
        help="Also synchronize GxP compliance and RTM documentation",
    ),
) -> None:
    """Executes automated self-healing and code formatting across the repository."""
    json_mode = is_json_mode(ctx.obj)
    repo_root = Path(__file__).resolve().parents[3]

    steps = [
        (
            "Ruff Linter Fix",
            [
                "uv",
                "run",
                "ruff",
                "check",
                ".",
                "--fix",
                "--exclude",
                "apps/execution/database/models.py",
            ],
        ),
        (
            "Ruff Code Formatter",
            [
                "uv",
                "run",
                "ruff",
                "format",
                ".",
                "--exclude",
                "apps/execution/database/models.py",
            ],
        ),
        (
            "Secrets Baseline Sync",
            [sys.executable, "scripts/clean_secrets_baseline.py"],
        ),
        ("ADR Index Validation", [sys.executable, "scripts/validate_adrs.py"]),
    ]

    if all_remediations:
        steps.append(
            (
                "OpenAPI Schemas Export",
                [
                    sys.executable,
                    "scripts/validate_schemas.py",
                    "--export-dir",
                    "docs/openapi",
                ],
            )
        )

    if sync_gxp:
        steps.append(("GxP Compliance Sync", [sys.executable, "scripts/sync_gxp.py"]))

    start_total = time.time()
    results: list[dict[str, Any]] = []

    for name, cmd in steps:
        step_start = time.time()
        res = subprocess.run(
            cmd, cwd=str(repo_root), capture_output=True, text=True, check=False
        )
        duration = round(time.time() - step_start, 2)
        results.append(
            {
                "name": name,
                "passed": res.returncode == 0,
                "exit_code": res.returncode,
                "duration_seconds": duration,
                "stdout": res.stdout.strip(),
                "stderr": res.stderr.strip(),
            }
        )

    # Ensure pre-commit hook is installed
    hook_ok, hook_msg = install_pre_commit_hook(repo_root)
    results.append(
        {
            "name": "Pre-Commit Hook Installation",
            "passed": hook_ok,
            "exit_code": 0 if hook_ok else 1,
            "duration_seconds": 0.01,
            "stdout": hook_msg,
            "stderr": "",
        }
    )

    total_duration = round(time.time() - start_total, 2)
    all_passed = all(r["passed"] for r in results)
    passed_count = sum(1 for r in results if r["passed"])
    failed_count = len(results) - passed_count

    if json_mode:
        output_json(
            {
                "command": "fix",
                "success": all_passed,
                "total_remediations": len(results),
                "passed_remediations": passed_count,
                "failed_remediations": failed_count,
                "duration_seconds": total_duration,
                "results": results,
            }
        )
        if not all_passed:
            sys.exit(1)
        return

    doc = TerminalDocument(
        title="Cadence Self-Healing & Remediation Engine",
        subtitle=f"Executed {len(results)} automated self-healing step(s)...",
    )

    doc.add_metric(
        "Status",
        "ALL REMEDIATED" if all_passed else "WARNINGS PRESENT",
        style="green" if all_passed else "yellow",
    )
    doc.add_metric("Steps Completed", f"{passed_count}/{len(results)}", style="green")
    doc.add_metric("Duration", f"{total_duration}s", style="cyan")

    remediation_rows = []
    for r in results:
        status_label = (
            "✔ Remediation Applied" if r["passed"] else "✘ Remediation Warning"
        )
        remediation_rows.append([r["name"], status_label, f"{r['duration_seconds']}s"])

    doc.add_table_data(
        "Remediation Steps Applied",
        [("Step", "bold white"), ("Status", "bold"), ("Duration", "dim")],
        remediation_rows,
    )

    for r in results:
        if r["name"] == "Pre-Commit Hook Installation":
            doc.add_item(r["stdout"], status="pass")

    doc.set_cta(
        "Run 'uv run cadence check --parallel' to verify all quality gates pass."
    )
    doc.display()

    if not all_passed:
        sys.exit(1)
