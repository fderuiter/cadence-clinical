"""Dev orchestrator subcommand for starting and monitoring microservices locally."""

import collections
import os
import queue
import select
import subprocess
import sys
import threading
import time
from pathlib import Path
from typing import Any

import typer
from rich.layout import Layout
from rich.live import Live
from rich.panel import Panel
from rich.table import Table
from rich.text import Text

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
    help="Start and orchestrate local microservices with live reloading and interactive TUI."
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


def _spawn_service(
    service_name: str,
    host: str,
    port: int,
    repo_root: Path,
    reload: bool,
    log_queue: queue.Queue[tuple[str, str]],
) -> subprocess.Popen[Any]:
    """Spawns a microservice subprocess with piped logs."""
    cmd = [
        sys.executable,
        "-m",
        "uvicorn",
        f"apps.{service_name}.main:app",
        "--host",
        host,
        "--port",
        str(port),
    ]
    if reload:
        cmd.append("--reload")
    env = {**os.environ, "PYTHONPATH": str(repo_root)}
    proc = subprocess.Popen(
        cmd,
        cwd=str(repo_root),
        env=env,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        bufsize=1,
    )

    def _stream_output(p: subprocess.Popen[Any], name: str) -> None:
        if p.stdout:
            for line in iter(p.stdout.readline, ""):
                log_queue.put((name, line.rstrip()))
            p.stdout.close()

    t = threading.Thread(target=_stream_output, args=(proc, service_name), daemon=True)
    t.start()
    return proc


def _render_tui_layout(
    services_state: dict[str, dict[str, Any]],
    logs_buffer: collections.deque[tuple[str, str, str]],
    host: str,
) -> Layout:
    """Renders the Rich TUI layout for multi-service monitoring."""
    layout = Layout()
    layout.split_column(
        Layout(name="header", size=3),
        Layout(name="services", size=len(services_state) + 5),
        Layout(name="logs", ratio=1),
        Layout(name="footer", size=3),
    )

    # Header
    total = len(services_state)
    running = sum(1 for s in services_state.values() if s.get("status") == "healthy")
    header_text = Text()
    header_text.append(" Cadence Microservices Cockpit ", style="bold cyan")
    header_text.append(f"| Host: {host} | Active: ", style="dim")
    header_text.append(
        f"{running}/{total}", style="bold green" if running == total else "bold yellow"
    )
    layout["header"].update(Panel(header_text, border_style="cyan"))

    # Services Table
    table = Table(expand=True, border_style="dim")
    table.add_column("Service", style="bold white", width=16)
    table.add_column("Port", style="cyan", width=8)
    table.add_column("PID", style="dim", width=8)
    table.add_column("Status", width=12)
    table.add_column("Uptime", style="dim", width=10)
    table.add_column("Documentation URL", style="blue")

    for name, info in services_state.items():
        status = info.get("status", "stopped")
        status_style = "bold green" if status == "healthy" else "bold red"
        uptime_sec = (
            int(time.time() - info["started_at"]) if "started_at" in info else 0
        )
        uptime_str = f"{uptime_sec}s"
        table.add_row(
            name,
            str(info["port"]),
            str(info.get("pid", "N/A")),
            f"[{status_style}]{status.upper()}[/{status_style}]",
            uptime_str,
            f"http://{host}:{info['port']}/docs",
        )
    layout["services"].update(
        Panel(table, title="[bold]Active Services[/bold]", border_style="blue")
    )

    # Log Stream
    log_text = Text()
    for ts, svc, line in logs_buffer:
        log_text.append(f"[{ts}] ", style="dim")
        log_text.append(f"[{svc.ljust(13)}] ", style="bold cyan")
        if "ERROR" in line or "error" in line.lower():
            log_text.append(f"{line}\n", style="bold red")
        elif "WARNING" in line:
            log_text.append(f"{line}\n", style="yellow")
        else:
            log_text.append(f"{line}\n", style="white")
    layout["logs"].update(
        Panel(log_text, title="[bold]Live Log Stream[/bold]", border_style="dim")
    )

    # Footer Shortcuts
    footer_text = Text()
    footer_text.append(" Shortcuts: ", style="bold")
    footer_text.append("[r] Restart All  ", style="bold cyan")
    footer_text.append("[g] Gateway  ", style="cyan")
    footer_text.append("[e] Execution  ", style="cyan")
    footer_text.append("[d] Designer  ", style="cyan")
    footer_text.append("[c] CTMS  ", style="cyan")
    footer_text.append("[q] Graceful Quit", style="bold red")
    layout["footer"].update(Panel(footer_text, border_style="dim"))

    return layout


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
    tui: bool = typer.Option(
        False,
        "--tui",
        "-t",
        help="Launch interactive Rich terminal cockpit UI with live logs and hotkeys",
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

    if len(target_services) == 1 and not tui:
        s = target_services[0]
        port = SERVICE_PORTS.get(s, 8000)
        print_header(
            "Cadence Microservice Runner", f"Starting foreground {s} on port {port}"
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
        return

    # Multi-service / TUI Orchestration
    log_queue: queue.Queue[tuple[str, str]] = queue.Queue()
    logs_buffer: collections.deque[tuple[str, str, str]] = collections.deque(maxlen=40)
    procs: dict[str, subprocess.Popen[Any]] = {}
    services_state: dict[str, dict[str, Any]] = {}

    for s in target_services:
        port = SERVICE_PORTS.get(s, 8000)
        proc = _spawn_service(s, host, port, repo_root, reload, log_queue)
        procs[s] = proc
        services_state[s] = {
            "port": port,
            "pid": proc.pid,
            "status": "healthy",
            "started_at": time.time(),
        }

    if not tui:
        print_header(
            "Cadence Microservice Orchestrator",
            f"Started {len(target_services)} service(s) on host {host}. Press Ctrl+C to stop.",
        )
        table = create_table(
            "Active Services",
            [
                ("Service", "bold white"),
                ("Port", "cyan"),
                ("PID", "dim"),
                ("Docs URL", "blue"),
            ],
        )
        for s in target_services:
            port = SERVICE_PORTS.get(s, 8000)
            table.add_row(s, str(port), str(procs[s].pid), f"http://{host}:{port}/docs")
        console.print(table)

        try:
            while True:
                time.sleep(0.2)
                while not log_queue.empty():
                    svc, line = log_queue.get_nowait()
                    console.print(
                        f"[dim]{time.strftime('%H:%M:%S')}[/dim] [bold cyan][{svc}][/bold cyan] {line}"
                    )
                for s, p in list(procs.items()):
                    if p.poll() is not None:
                        print_warning(f"Service '{s}' exited with code {p.returncode}.")
        except KeyboardInterrupt:
            print_info("\nTerminating background services...")
            for p in procs.values():
                p.terminate()
            print_success("All services stopped.")
        return

    # Interactive TUI Mode
    try:
        with Live(
            _render_tui_layout(services_state, logs_buffer, host),
            refresh_per_second=4,
            screen=True,
        ) as live:
            while True:
                # Ingest queued logs
                while not log_queue.empty():
                    svc, line = log_queue.get_nowait()
                    ts = time.strftime("%H:%M:%S")
                    logs_buffer.append((ts, svc, line))

                # Check process health
                for s, p in list(procs.items()):
                    if p.poll() is not None:
                        services_state[s]["status"] = "exited"
                    else:
                        services_state[s]["status"] = "healthy"

                live.update(_render_tui_layout(services_state, logs_buffer, host))

                # Check keyboard inputs in non-blocking manner if TTY
                if sys.stdin.isatty():
                    r, _, _ = select.select([sys.stdin], [], [], 0.25)
                    if r:
                        key = sys.stdin.read(1).lower()
                        if key == "q":
                            break
                        if key == "r":
                            for s in target_services:
                                procs[s].terminate()
                                port = SERVICE_PORTS.get(s, 8000)
                                proc = _spawn_service(
                                    s, host, port, repo_root, reload, log_queue
                                )
                                procs[s] = proc
                                services_state[s] = {
                                    "port": port,
                                    "pid": proc.pid,
                                    "status": "healthy",
                                    "started_at": time.time(),
                                }
                        elif key in ("g", "e", "d", "c"):
                            key_map = {
                                "g": "gateway",
                                "e": "execution",
                                "d": "designer",
                                "c": "ctms",
                            }
                            target = key_map[key]
                            if target in procs:
                                procs[target].terminate()
                                port = SERVICE_PORTS.get(target, 8000)
                                proc = _spawn_service(
                                    target, host, port, repo_root, reload, log_queue
                                )
                                procs[target] = proc
                                services_state[target] = {
                                    "port": port,
                                    "pid": proc.pid,
                                    "status": "healthy",
                                    "started_at": time.time(),
                                }
                else:
                    time.sleep(0.25)

    except KeyboardInterrupt:
        pass
    finally:
        for p in procs.values():
            p.terminate()
        print_success("All background services terminated cleanly.")
