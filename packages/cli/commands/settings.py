"""Cadence CLI commands for managing Pydantic settings models and schema exports."""

import subprocess
import sys
import time
from pathlib import Path

import typer

from packages.cli.formatting import (
    TerminalDocument,
    is_json_mode,
    output_json,
)

settings_app = typer.Typer(
    help="Manage microservice configuration settings models and template exports."
)


@settings_app.command("export")
def export_settings(
    ctx: typer.Context,
    check: bool = typer.Option(
        False, "--check", help="Check for configuration drift without modifying files"
    ),
    json_mode_opt: bool = typer.Option(
        False, "--json", help="Emit output in structured JSON format"
    ),
) -> None:
    """Export Pydantic settings models into .env.example and docs/ENVIRONMENT_VARIABLES.md."""
    json_mode = is_json_mode(ctx.obj) or json_mode_opt
    repo_root = Path(__file__).resolve().parents[3]
    script_path = repo_root / "scripts" / "extract_env_vars.py"

    start_time = time.time()

    cmd = [sys.executable, str(script_path)]
    if check:
        cmd.append("--check")

    res = subprocess.run(cmd, cwd=str(repo_root), capture_output=True, text=True, check=False)
    duration = round(time.time() - start_time, 2)

    success = res.returncode == 0

    if json_mode:
        output_json(
            {
                "command": "settings export",
                "check": check,
                "success": success,
                "duration_seconds": duration,
                "stdout": res.stdout.strip(),
                "stderr": res.stderr.strip(),
            }
        )
        if not success:
            sys.exit(1)
        return

    doc = TerminalDocument(
        title="Cadence Configuration Settings Model Exporter",
        subtitle="Extracting and validating Pydantic BaseSettings models across microservices...",
    )

    doc.add_metric(
        "Status",
        "SYNCED" if success else "DRIFT DETECTED",
        style="green" if success else "red",
    )
    doc.add_metric("Mode", "Validation Check" if check else "Export Write", style="cyan")
    doc.add_metric("Duration", f"{duration}s", style="magenta")

    if success:
        doc.add_item(
            "Pydantic BaseSettings Export",
            status="pass",
            detail=res.stdout.strip() or "Successfully exported settings templates.",
        )
        doc.set_cta("Settings schemas are synchronized with .env.example and docs.")
        doc.display()
    else:
        doc.add_item(
            "Pydantic BaseSettings Export",
            status="fail",
            detail=res.stdout.strip() or res.stderr.strip() or "Schema drift detected.",
        )
        doc.set_cta("Run 'uv run cadence settings export' to regenerate configuration templates.")
        doc.display()
        sys.exit(1)
