"""GxP compliance synchronization and Requirements Traceability Matrix (RTM) tools."""

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
    print_success,
)

gxp_app = typer.Typer(
    help="GxP compliance, RTM synchronization, and qualification report automation."
)


@gxp_app.command("sync")
def sync_gxp(
    ctx: typer.Context,
    dry_run: bool = typer.Option(
        False, "--dry-run", help="Validate only without staging changes"
    ),
) -> None:
    """Run tests, regenerate RTM documentation, and stage compliance reports."""
    json_mode = is_json_mode(ctx.obj)
    repo_root = Path(__file__).resolve().parents[3]

    cmd = ["uv", "run", "python", "scripts/sync_gxp.py"]
    if dry_run:
        cmd.append("--dry-run")

    if not json_mode:
        print_header(
            "Cadence GxP Compliance Sync",
            "Running test suite & updating Requirements Traceability Matrix",
        )

    res = subprocess.run(cmd, cwd=str(repo_root), capture_output=True, text=True)
    success = res.returncode == 0

    if json_mode:
        output_json(
            {
                "command": "gxp sync",
                "success": success,
                "stdout": res.stdout.strip(),
                "stderr": res.stderr.strip(),
            }
        )
        sys.exit(0 if success else 1)

    if success:
        console.print(res.stdout)
        print_success(
            "GxP compliance documentation is synchronized with current system state."
        )
    else:
        print_error("GxP compliance sync failed:")
        console.print(res.stderr or res.stdout)
        sys.exit(1)


@gxp_app.command("validate")
def validate_gxp(ctx: typer.Context) -> None:
    """Validate that checked-in RTM documentation is strictly up-to-date."""
    json_mode = is_json_mode(ctx.obj)
    repo_root = Path(__file__).resolve().parents[3]

    cmd = ["uv", "run", "python", "scripts/generate_rtm.py", "--validate"]
    res = subprocess.run(cmd, cwd=str(repo_root), capture_output=True, text=True)
    success = res.returncode == 0

    if json_mode:
        output_json({"command": "gxp validate", "up_to_date": success})
        sys.exit(0 if success else 1)

    if success:
        print_success("RTM documentation is verified and strictly up-to-date.")
    else:
        print_error(
            "RTM documentation is out of date! Run 'cadence gxp sync' to update."
        )
        sys.exit(1)
