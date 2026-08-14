#!/usr/bin/env python
"""
Unified startup CLI wrapper for Cadence Clinical microservices.
Automatically identifies and runs pre-boot initialization routines/migrations
for relational services before starting their web servers, while bypassing
migrations for schema-less or self-initializing services.
"""

import argparse
import os
import subprocess
import sys


def find_migration_script(service: str) -> str | None:
    """
    Locates custom pre-boot migration scripts dynamically for the given service.
    """
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

    env = {**os.environ, "PYTHONPATH": os.getcwd()}
    # Run using current virtual env Python
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
    Launches the uvicorn web server. Replaces the current process on Unix systems
    to preserve signal propagation (SIGTERM/SIGINT) as PID 1, or falls back to
    subprocess.run.
    """
    print(f"[{service.upper()}] Launching web server on {host}:{port}...")

    uvicorn_cmd = [
        "uvicorn",
        f"apps.{service}.main:app",
        "--host",
        host,
        "--port",
        str(port),
    ] + extra_args

    if os.name != "nt":
        try:
            os.execvp("uvicorn", uvicorn_cmd)
        except FileNotFoundError:
            # Fallback if uvicorn is not in PATH (e.g. running outside of fully configured venv path)
            python_uvicorn_cmd = [
                sys.executable,
                "-m",
                "uvicorn",
                f"apps.{service}.main:app",
                "--host",
                host,
                "--port",
                str(port),
            ] + extra_args
            os.execvp(sys.executable, python_uvicorn_cmd)
    else:
        # Fallback for Windows where execvp is not available/replaces process differently
        res = subprocess.run(uvicorn_cmd)
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
        default="0.0.0.0",
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
