"""Setup and credential bootstrapping subcommand for developer and cloud environments."""

import os
import subprocess
from pathlib import Path

import typer

from packages.cli.formatting import (
    is_json_mode,
    output_json,
    print_error,
    print_header,
    print_success,
)

setup_app = typer.Typer(
    help="Environment setup, cryptographic key generation, and cloud credential bootstrapping."
)


@setup_app.command("credentials")
def setup_credentials(
    ctx: typer.Context,
    dev: bool = typer.Option(
        False,
        "--dev",
        "-d",
        help="Run in automated local development mode (auto-generate all cryptographic keys)",
    ),
    env_file: str = typer.Option(
        ".env",
        "--env-file",
        "-e",
        help="Target environment file path",
    ),
) -> None:
    """Launch the interactive credential bootstrapping wizard (/wizard standard)."""
    json_mode = is_json_mode(ctx.obj)
    repo_root = Path(__file__).resolve().parents[3]
    script_path = repo_root / "scripts" / "setup_credentials.sh"

    if not script_path.exists():
        if json_mode:
            output_json(
                {
                    "success": False,
                    "error": f"Script not found at {script_path}",
                }
            )
        else:
            print_error(f"Setup script not found at {script_path}")
        raise typer.Exit(1)

    cmd = [str(script_path), f"--env-file={env_file}"]
    if dev:
        cmd.append("--dev")

    if not json_mode and not dev:
        print_header(
            "Cadence Clinical — Credential Setup Wizard",
            "Interactive Part 11 Cryptographic & Cloud Environment Bootstrapping",
        )

    # If in JSON mode or dev mode without a tty, capture output; otherwise run interactively with inherited stdio
    if json_mode:
        env = dict(os.environ)
        if dev:
            env["CADENCE_WIZARD_AUTO_ACCEPT"] = "1"
        res = subprocess.run(
            cmd,
            cwd=str(repo_root),
            capture_output=True,
            text=True,
            env=env,
            check=False,
        )
        output_json(
            {
                "command": "setup credentials",
                "success": res.returncode == 0,
                "env_file": env_file,
                "dev_mode": dev,
                "stdout": res.stdout.strip(),
                "stderr": res.stderr.strip(),
            }
        )
        if res.returncode != 0:
            raise typer.Exit(res.returncode)
    else:
        env = dict(os.environ)
        if dev:
            env["CADENCE_WIZARD_AUTO_ACCEPT"] = "1"
        res = subprocess.run(cmd, cwd=str(repo_root), env=env, check=False)
        if res.returncode == 0:
            print_success(f"Environment setup successfully written to {env_file}")
        else:
            raise typer.Exit(res.returncode)


@setup_app.callback(invoke_without_command=True)
def setup_default(
    ctx: typer.Context,
    dev: bool = typer.Option(
        False,
        "--dev",
        "-d",
        help="Run in automated local development mode (auto-generate all cryptographic keys)",
    ),
    env_file: str = typer.Option(
        ".env",
        "--env-file",
        "-e",
        help="Target environment file path",
    ),
) -> None:
    """Launch the credential bootstrapping wizard when 'cadence setup' is called directly."""
    if ctx.invoked_subcommand is None:
        setup_credentials(ctx=ctx, dev=dev, env_file=env_file)
