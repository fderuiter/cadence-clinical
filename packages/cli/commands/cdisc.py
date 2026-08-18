"""CDISC USDM standards and schema exporter CLI subcommands."""

import json
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

cdisc_app = typer.Typer(
    help="CDISC USDM standards and schema documentation exporter tools."
)


def run_cdisc_export(
    ctx: typer.Context,
    output: str,
    validate: bool,
    command_name: str = "cdisc export",
) -> None:
    """Generate a validated CDISC USDM compliance document from local database models."""
    json_mode = is_json_mode(ctx.obj)
    repo_root = Path(__file__).resolve().parents[3]

    if not json_mode:
        print_header(
            "Cadence CDISC USDM Compliance Exporter",
            "Extracting multi-service schemas into CDISC USDM v3.0 compliance catalog",
        )

    cmd = [
        "uv",
        "run",
        "python",
        "scripts/generate_schema_documentation.py",
        "--output",
        output,
    ]
    if not validate:
        cmd.append("--no-validate")
    if json_mode:
        cmd.append("--json")

    res = subprocess.run(cmd, cwd=str(repo_root), capture_output=True, text=True)
    success = res.returncode == 0

    if json_mode:
        try:
            usdm_doc = json.loads(res.stdout)
        except Exception:
            usdm_doc = {}

        output_json(
            {
                "command": command_name,
                "success": success,
                "output_file": str(repo_root / output),
                "usdm_version": usdm_doc.get("usdmVersion", "3.0")
                if isinstance(usdm_doc, dict)
                else "3.0",
                "biomedical_concepts_count": len(usdm_doc.get("biomedicalConcepts", []))
                if isinstance(usdm_doc, dict)
                else 0,
                "usdm_document": usdm_doc,
            }
        )
        sys.exit(0 if success else 1)

    if success:
        out_path = Path(output)
        if not out_path.is_absolute():
            out_path = repo_root / out_path
        print_success("Generated CDISC USDM compliance document.")
        console.print(f"  Saved to: [bold cyan]{out_path}[/bold cyan]")
    else:
        print_error("CDISC USDM compliance export failed:")
        console.print(res.stderr or res.stdout)
        sys.exit(1)


@cdisc_app.command("export")
def export_cdisc(
    ctx: typer.Context,
    output: str = typer.Option(
        "docs/CDISC/cdisc_usdm_compliance.json",
        "--output",
        "-o",
        help="Path to save generated CDISC USDM compliance JSON document",
    ),
    validate: bool = typer.Option(
        True,
        "--validate/--no-validate",
        help="Validate generated document against CDISC USDM Pydantic schema",
    ),
) -> None:
    """Generate a validated CDISC USDM compliance document from local database models."""
    run_cdisc_export(ctx, output=output, validate=validate, command_name="cdisc export")
