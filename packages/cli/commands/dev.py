"""Dev orchestrator subcommand for starting and monitoring microservices locally."""

import os
import subprocess
import sys
import time
from pathlib import Path
from typing import Any

import typer

from packages.cli.formatting import (
    console,
    create_table,
    is_json_mode,
    output_json,
    print_header,
    print_info,
    print_success,
    print_warning,
)

dev_app = typer.Typer(
    help="Start and orchestrate local microservices with live reloading."
)

SERVICE_PORTS = {
    "gateway": 8000,
    "designer": 8001,
    "execution": 8002,
    "etmf": 8003,
    "interop": 8004,
    "quality": 8005,
    "notifications": 8006,
    "ctms": 8007,
    "safety": 8008,
    "tickets": 8009,
    "eisf": 8010,
    "econsent": 8011,
    "org": 8012,
}


@dev_app.callback(invoke_without_command=True)
def run_dev(
    ctx: typer.Context,
    services: list[str] = typer.Argument(
        None, help="Names of services to start (default: start core services)"
    ),
    host: str = typer.Option(
        "127.0.0.1", "--host", "-h", help="Host interface to bind"
    ),
    reload: bool = typer.Option(
        True, "--reload/--no-reload", help="Enable live auto-reloading on file change"
    ),
    single: str = typer.Option(
        None,
        "--service",
        "-s",
        help="Start a single specific microservice in foreground",
    ),
) -> None:
    """Orchestrates local microservices development."""
    json_mode = is_json_mode(ctx.obj)
    target_services = services or (
        ["gateway", "designer", "execution"] if not single else [single]
    )
    if single and single not in target_services:
        target_services = [single]

    repo_root = Path(__file__).resolve().parents[3]

    if json_mode:
        output_json(
            {
                "status": "ready",
                "host": host,
                "reload": reload,
                "services": [
                    {
                        "name": s,
                        "port": SERVICE_PORTS.get(s, 8000),
                        "app": f"apps.{s}.main:app",
                    }
                    for s in target_services
                ],
            }
        )
        return

    print_header(
        "Cadence Microservice Orchestrator",
        f"Preparing {len(target_services)} service(s) on host {host}",
    )

    table = create_table(
        "Selected Services",
        [("Service", "bold white"), ("Port", "cyan"), ("URL", "blue")],
    )
    for s in target_services:
        port = SERVICE_PORTS.get(s, 8000)
        table.add_row(s, str(port), f"http://{host}:{port}/docs")
    console.print(table)

    if len(target_services) == 1:
        s = target_services[0]
        port = SERVICE_PORTS.get(s, 8000)
        print_info(
            f"Starting foreground Uvicorn server for [bold]{s}[/bold] on port {port}..."
        )
        cmd = [
            "uvicorn",
            f"apps.{s}.main:app",
            "--host",
            host,
            "--port",
            str(port),
        ]
        if reload:
            cmd.append("--reload")
        env = {**os.environ, "PYTHONPATH": str(repo_root)}
        try:
            subprocess.run(cmd, cwd=str(repo_root), env=env)
        except KeyboardInterrupt:
            print_info("\nStopped service.")
    else:
        print_info(
            "Multi-service mode: Starting services in background processes. Press Ctrl+C to terminate all."
        )
        procs: list[tuple[str, subprocess.Popen[Any]]] = []
        env = {**os.environ, "PYTHONPATH": str(repo_root)}

        try:
            for s in target_services:
                port = SERVICE_PORTS.get(s, 8000)
                cmd = [
                    sys.executable,
                    "-m",
                    "uvicorn",
                    f"apps.{s}.main:app",
                    "--host",
                    host,
                    "--port",
                    str(port),
                ]
                if reload:
                    cmd.append("--reload")
                p = subprocess.Popen(
                    cmd,
                    cwd=str(repo_root),
                    env=env,
                    stdout=subprocess.DEVNULL,
                    stderr=subprocess.PIPE,
                )
                procs.append((s, p))
                print_success(
                    f"Started [bold]{s}[/bold] (PID {p.pid}) -> http://{host}:{port}/docs"
                )

            print_info(
                "All requested services are running. Waiting for signals (Ctrl+C to quit)..."
            )
            while True:
                time.sleep(1)
                for s, p in procs:
                    if p.poll() is not None:
                        err = p.stderr.read().decode("utf-8") if p.stderr else ""
                        print_warning(
                            f"Service [bold]{s}[/bold] exited with code {p.returncode}: {err}"
                        )
        except KeyboardInterrupt:
            print_info("\nGracefully terminating all background services...")
            for _, p in procs:
                p.terminate()
            for _, p in procs:
                p.wait(timeout=3)
            print_success("All services stopped.")
