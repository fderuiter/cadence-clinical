"""Self-healing and auto-remediation command for code style, schemas, and documentation."""

import subprocess
import sys
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

fix_app = typer.Typer(
    help="Auto-fix lint errors, format code, index ADRs, and synchronize schemas."
)


@fix_app.callback(invoke_without_command=True)
def run_fix(
    ctx: typer.Context,
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
        ("Ruff Linter Fix", ["uv", "run", "ruff", "check", ".", "--fix"]),
        (
            "Ruff Code Formatter",
            ["uv", "run", "ruff", "format", "--target-version", "py313", "."],
        ),
        ("ADR Index Auto-Fix", ["python3", "scripts/validate_adrs.py", "--fix-index"]),
        (
            "OpenAPI Schema Export",
            [
                "uv",
                "run",
                "python",
                "scripts/validate_schemas.py",
                "--export-dir",
                "docs/openapi",
            ],
        ),
        (
            "Prettier Formatting",
            ["pnpm", "exec", "prettier", "--write", "**/*.{json,css,md}"],
        ),
    ]

    if sync_gxp:
        steps.append(
            ("GxP Compliance Sync", ["uv", "run", "python", "scripts/sync_gxp.py"])
        )

    if not json_mode:
        print_header(
            "Cadence Self-Healing & Auto-Fix Tool",
            "Remediating lint issues, formatting source files, and aligning schemas",
        )

    results = []
    for name, cmd in steps:
        if not json_mode:
            print_info(f"Running: [bold]{name}[/bold]...")
        res = subprocess.run(cmd, cwd=str(repo_root), capture_output=True, text=True)
        results.append(
            {
                "step": name,
                "success": res.returncode == 0,
                "exit_code": res.returncode,
                "stdout": res.stdout.strip(),
                "stderr": res.stderr.strip(),
            }
        )

    all_passed = all(r["success"] for r in results)

    if json_mode:
        output_json(
            {
                "all_passed": all_passed,
                "results": results,
            }
        )
        sys.exit(0 if all_passed else 1)

    if all_passed:
        print_success("Self-healing and formatting completed successfully!")
    else:
        print_error("Some self-healing steps encountered errors:")
        for r in results:
            if not r["success"]:
                console.print(
                    f"[bold red]Step failed: {r['step']}[/bold red]\n{r['stderr'] or r['stdout']}"
                )
        sys.exit(1)
