#!/usr/bin/env python
"""
Unified startup CLI wrapper for Cadence Clinical microservices.
Automatically queries the central service manifest registry, identifies and runs
pre-boot initialization routines/migrations for relational services before starting
their web servers or .NET runners.
"""

import argparse
import os
import subprocess
import sys
from pathlib import Path

# Add repo root to sys.path if not present
REPO_ROOT = Path(__file__).resolve().parents[1]
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

from packages.cli.registry import get_service_profile


def find_migration_script(service: str) -> str | None:
    """
    Locates custom pre-boot migration scripts from the central service registry.
    """
    try:
        profile = get_service_profile(service)
        if profile.migration_script and os.path.exists(profile.migration_script):
            return profile.migration_script
    except KeyError:
        pass

    possible_paths = [
        os.path.join("apps", service, "database", "migrate.py"),
        os.path.join("apps", service, "migrate.py"),
    ]
    for path in possible_paths:
        if os.path.exists(path):
            return path
    return None


def run_pre_boot_migrations(service: str, migrate_path: str) -> None:
    """
    Executes the pre-boot migration routine as a subprocess.
    """
    print(f"[{service.upper()}] Pre-boot: Relational database detected.")
    print(f"[{service.upper()}] Pre-boot: Running migrations via '{migrate_path}'...")

    env = {**os.environ, "PYTHONPATH": str(REPO_ROOT)}
    res = subprocess.run([sys.executable, migrate_path], env=env)

    if res.returncode != 0:
        print(
            f"[{service.upper()}] Pre-boot: Migration failed with exit code {res.returncode}. "
            "Aborting startup sequence to prevent runtime errors or connection failures.",
            file=sys.stderr,
        )
        sys.exit(res.returncode)

    print(f"[{service.upper()}] Pre-boot: Migrations completed successfully.")


def run_web_server(service: str, host: str, port: int, extra_args: list[str]) -> None:
    """
    Launches the target service runtime executable (dotnet, uvicorn, pnpm).
    Replaces current process on Unix systems to preserve signal propagation.
    """
    try:
        profile = get_service_profile(service)
    except KeyError as e:
        print(f"[{service.upper()}] Startup error: {e}", file=sys.stderr)
        sys.exit(1)

    print(
        f"[{profile.name.upper()}] Launching {profile.runtime} runner on {host}:{port} "
        f"({profile.project_path})..."
    )

    cmd = profile.get_start_command(
        host=host, port=port, reload=False, extra_args=extra_args
    )
    env = profile.get_environment(host=host, port=port, repo_root=REPO_ROOT)
    os.environ.update(env)

    binary = cmd[0]
    if os.name != "nt":
        try:
            os.execvp(binary, cmd)
        except FileNotFoundError:
            if binary == "uvicorn":
                python_uvicorn_cmd = [
                    sys.executable,
                    "-m",
                    "uvicorn",
                ] + cmd[1:]
                os.execvp(sys.executable, python_uvicorn_cmd)
            else:
                raise
    else:
        res = subprocess.run(cmd, env=os.environ)
        sys.exit(res.returncode)


def main(args_list: list[str] | None = None) -> None:
    """
    Unified CLI entry point.
    """
    if args_list is None:
        args_list = sys.argv[1:]

    parser = argparse.ArgumentParser(
        description="Unified Microservice Startup CLI Wrapper"
    )
    parser.add_argument(
        "service",
        type=str,
        help="Name of the service to start (e.g., execution, designer, ctms)",
    )
    parser.add_argument(
        "--host",
        type=str,
        default="0.0.0.0",  # nosec B104
        help="Host interface to bind the web server to (default: 0.0.0.0)",
    )
    parser.add_argument(
        "--port",
        type=int,
        default=8000,
        help="Port to bind the web server to (default: 8000)",
    )

    parsed, extra_args = parser.parse_known_args(args_list)

    service = parsed.service
    host = parsed.host
    port = parsed.port

    try:
        get_service_profile(service)
    except KeyError as e:
        print(f"[{service.upper()}] Startup error: {e}", file=sys.stderr)
        sys.exit(1)

    migrate_path = find_migration_script(service)
    if migrate_path:
        run_pre_boot_migrations(service, migrate_path)
    else:
        print(
            f"[{service.upper()}] Pre-boot: Schema-less or self-initializing service. "
            "Bypassing pre-boot migration phase."
        )

    run_web_server(service, host, port, extra_args)


if __name__ == "__main__":
    main()
