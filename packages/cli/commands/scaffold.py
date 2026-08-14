"""Interactive and automated code scaffolders for hexagonal microservices, ports, adapters, and ADRs."""

import subprocess
import sys
from pathlib import Path

import typer

from packages.cli.formatting import (
    console,
    is_json_mode,
    output_json,
    print_error,
    print_success,
)

scaffold_app = typer.Typer(
    help="Scaffold new hexagonal microservices, ports, adapters, or ADRs."
)


@scaffold_app.command("service")
def scaffold_service(
    ctx: typer.Context,
    name: str = typer.Argument(
        ..., help="Name of the microservice to scaffold (e.g. analytics, billing)"
    ),
    features: str = typer.Option(
        "", "--features", "-f", help="Comma-separated optional features"
    ),
) -> None:
    """Scaffold a new enterprise hexagonal microservice."""
    json_mode = is_json_mode(ctx.obj)
    repo_root = Path(__file__).resolve().parents[3]

    cmd = ["python3", "scripts/scaffold_service.py", name]
    if features:
        cmd.append(features)

    res = subprocess.run(cmd, cwd=str(repo_root), capture_output=True, text=True)
    success = res.returncode == 0

    if json_mode:
        output_json(
            {
                "command": "scaffold service",
                "name": name,
                "success": success,
                "stdout": res.stdout.strip(),
            }
        )
        sys.exit(0 if success else 1)

    if success:
        console.print(res.stdout)
        print_success(f"Microservice '{name}' scaffolded cleanly.")
    else:
        print_error(f"Failed to scaffold microservice '{name}':")
        console.print(res.stderr or res.stdout)
        sys.exit(1)


@scaffold_app.command("adr")
def scaffold_adr(
    ctx: typer.Context,
    title: str = typer.Argument(..., help="Title for the Architecture Decision Record"),
    domain: str = typer.Option(
        "core-platform", "--domain", "-d", help="Domain category"
    ),
    req: str = typer.Option(
        "PRD-SYS-001", "--req", "-r", help="Traceable requirement ID"
    ),
) -> None:
    """Scaffold a new Architecture Decision Record (ADR)."""
    json_mode = is_json_mode(ctx.obj)
    repo_root = Path(__file__).resolve().parents[3]

    cmd = [
        "python3",
        "scripts/create_adr.py",
        "--title",
        title,
        "--domain",
        domain,
        "--req",
        req,
    ]

    res = subprocess.run(cmd, cwd=str(repo_root), capture_output=True, text=True)
    success = res.returncode == 0

    if json_mode:
        output_json(
            {
                "command": "scaffold adr",
                "title": title,
                "success": success,
                "stdout": res.stdout.strip(),
            }
        )
        sys.exit(0 if success else 1)

    if success:
        console.print(res.stdout)
        print_success(f"ADR '{title}' scaffolded and indexed successfully.")
    else:
        print_error(f"Failed to scaffold ADR '{title}':")
        console.print(res.stderr or res.stdout)
        sys.exit(1)
