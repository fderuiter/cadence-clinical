"""Doctor diagnostic subcommand for validating and auto-healing environment, dependencies, ports, and databases."""

import socket
import sqlite3
import subprocess
import sys
from pathlib import Path
from typing import Any

import typer

from packages.cli.formatting import (
    console,
    create_table,
    is_json_mode,
    output_json,
    print_header,
    print_success,
    print_warning,
)

doctor_app = typer.Typer(
    help="Run system diagnostics and auto-heal development environment issues."
)

SQLITE_DBS = [
    "econsent.db",
    "eisf.db",
    "interop.db",
    "notifications.db",
    "safety.db",
    "tickets.db",
]


def is_port_in_use(port: int, host: str = "127.0.0.1") -> bool:
    """Checks if a TCP port is in use."""
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.settimeout(0.2)
        return s.connect_ex((host, port)) == 0


def check_tool_version(cmd: list[str]) -> str | None:
    """Runs a command to retrieve the version string, or returns None if missing."""
    try:
        res = subprocess.run(cmd, capture_output=True, text=True, check=False)
        if res.returncode == 0:
            return res.stdout.strip().split("\n")[0]
    except FileNotFoundError:
        pass
    return None


def _auto_heal_databases(repo_root: Path) -> list[str]:
    """Auto-heals missing SQLite storage files by seeding initial schemas."""
    actions = []
    for db_name in SQLITE_DBS:
        db_path = repo_root / db_name
        if not db_path.exists() or db_path.stat().st_size == 0:
            with sqlite3.connect(db_path) as conn:
                conn.execute(
                    "CREATE TABLE IF NOT EXISTS _schema_init (id INTEGER PRIMARY KEY, initialized_at TEXT)"
                )
                conn.commit()
            actions.append(f"Initialized local SQLite database: {db_name}")
    return actions


@doctor_app.callback(invoke_without_command=True)
def run_doctor(
    ctx: typer.Context,
    auto_fix: bool = typer.Option(
        False,
        "--auto-fix",
        "-f",
        help="Automatically remediate missing databases and recoverable environment issues",
    ),
) -> None:
    """Performs comprehensive diagnostics and optional auto-healing of the Cadence development environment."""
    json_mode = is_json_mode(ctx.obj)
    repo_root = Path(__file__).resolve().parents[3]

    healed_actions: list[str] = []
    if auto_fix:
        healed_actions = _auto_heal_databases(repo_root)

    diagnostics: dict[str, Any] = {
        "status": "healthy",
        "python": {},
        "binaries": {},
        "databases": {},
        "ports": {},
        "auto_healed": healed_actions,
        "recommendations": [],
    }

    # 1. Python Environment Check
    py_version = (
        f"{sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}"
    )
    py_valid = sys.version_info >= (3, 14)
    diagnostics["python"] = {
        "version": py_version,
        "valid": py_valid,
        "executable": sys.executable,
    }
    if not py_valid:
        diagnostics["status"] = "degraded"
        diagnostics["recommendations"].append(
            f"Python {py_version} detected. Cadence requires Python 3.14+ (uv run --python 3.14)."
        )

    # 2. Tooling & Binaries Check
    tools = {
        "uv": ["uv", "--version"],
        "pnpm": ["pnpm", "--version"],
        "node": ["node", "--version"],
        "git": ["git", "--version"],
        "docker": ["docker", "--version"],
    }
    for name, cmd in tools.items():
        ver = check_tool_version(cmd)
        installed = ver is not None
        diagnostics["binaries"][name] = {
            "installed": installed,
            "version": ver,
        }
        if not installed and name in ("uv", "pnpm", "node", "git"):
            diagnostics["status"] = "degraded"
            diagnostics["recommendations"].append(
                f"Required tool '{name}' is not found in PATH."
            )

    # 3. Database Check
    for db_name in SQLITE_DBS:
        db_path = repo_root / db_name
        diagnostics["databases"][db_name] = {
            "type": "sqlite",
            "exists": db_path.exists(),
            "size_bytes": db_path.stat().st_size if db_path.exists() else 0,
        }

    # 4. Port Availability Checks
    services_ports = {
        "Gateway API": 8000,
        "Designer Service": 8001,
        "Execution Engine": 8002,
        "eTMF Service": 8003,
        "Interop Service": 8004,
        "Quality Service": 8005,
        "Notifications Service": 8006,
        "CTMS Service": 8007,
        "Safety Service": 8008,
        "Tickets Service": 8009,
        "eISF Service": 8010,
        "eConsent Service": 8011,
        "Organization Service": 8012,
        "Keycloak IAM": 8080,
        "Web App (Vite)": 3000,
        "Subject Portal": 5174,
        "PostgreSQL": 5432,
        "Neo4j HTTP": 7474,
        "Neo4j Bolt": 7687,
    }
    for s_name, port in services_ports.items():
        in_use = is_port_in_use(port)
        diagnostics["ports"][s_name] = {
            "port": port,
            "in_use": in_use,
            "status": "in_use" if in_use else "available",
        }

    if json_mode:
        output_json(diagnostics)
        return

    # Rich Terminal Output
    print_header(
        "Cadence Clinical Environment Diagnostics & Auto-Healing",
        "Validating runtime environment, CLI dependencies, database files, and ports",
    )

    if healed_actions:
        for act in healed_actions:
            print_success(act)

    # Binaries Table
    t_tools = create_table(
        "Development Tooling",
        [("Tool", "bold white"), ("Status", "bold"), ("Version", "dim")],
    )
    for tool_name, info in diagnostics["binaries"].items():
        status_text = (
            "[green]Installed[/green]" if info["installed"] else "[red]Missing[/red]"
        )
        ver_text = info["version"] or "N/A"
        t_tools.add_row(tool_name, status_text, ver_text)
    console.print(t_tools)

    # SQLite Databases Table
    t_db = create_table(
        "Local SQLite Storage",
        [("Database", "bold white"), ("Status", "bold"), ("Size", "dim")],
    )
    for db_name, info in diagnostics["databases"].items():
        status_text = (
            "[green]Ready[/green]"
            if info["exists"]
            else "[yellow]Not Created (Run 'cadence doctor -f' or 'cadence db seed')[/yellow]"
        )
        size_kb = f"{info['size_bytes'] / 1024:.1f} KB" if info["exists"] else "0 KB"
        t_db.add_row(db_name, status_text, size_kb)
    console.print(t_db)

    # Ports Table
    t_ports = create_table(
        "Service Ports Overview",
        [("Service", "bold white"), ("Port", "cyan"), ("State", "bold")],
    )
    for s_name, p_info in diagnostics["ports"].items():
        state_text = (
            "[yellow]Active / In Use[/yellow]"
            if p_info["in_use"]
            else "[green]Free / Available[/green]"
        )
        t_ports.add_row(s_name, str(p_info["port"]), state_text)
    console.print(t_ports)

    # Final summary
    if diagnostics["status"] == "healthy":
        print_success(
            "All diagnostic checks passed. Environment is optimal for development."
        )
    else:
        print_warning("Environment has warnings:")
        for rec in diagnostics["recommendations"]:
            console.print(f"  • {rec}")
