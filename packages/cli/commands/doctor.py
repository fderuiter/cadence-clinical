"""Doctor diagnostic subcommand for validating and auto-healing environment, dependencies, ports, and databases."""

import sqlite3
import subprocess
import sys
from pathlib import Path
from typing import Any

import typer

from packages.cli.formatting import (
    TerminalDocument,
    is_json_mode,
    output_json,
)
from packages.cli.ports import is_port_in_use, load_categorized_ports
from scripts.pre_commit import install_pre_commit_hook

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


def check_tool_version(cmd: list[str]) -> str | None:
    """Runs a command to retrieve the version string, or returns None if missing."""
    try:
        res = subprocess.run(cmd, capture_output=True, text=True, check=False)
        if res.returncode == 0:
            return res.stdout.strip().split("\n")[0]
    except FileNotFoundError:
        pass
    return None


def check_pdf_engine() -> tuple[bool, str | None]:
    """Validates if WeasyPrint and underlying Pango/Cairo C-libraries load properly."""
    try:
        import weasyprint

        return True, f"weasyprint {weasyprint.__version__}"
    except Exception as exc:
        return False, str(exc)


def _auto_heal_databases(repo_root: Path) -> list[str]:
    """Auto-heals missing SQLite storage files and installs pre-commit hooks."""
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

    # Install git pre-commit hook if missing
    hook_ok, hook_msg = install_pre_commit_hook(repo_root)
    if hook_ok:
        actions.append(hook_msg)

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

    # Validate C-FFI PDF layout engine (WeasyPrint / Pango / Cairo)
    pdf_ok, pdf_detail = check_pdf_engine()
    diagnostics["binaries"]["pango"] = {
        "installed": pdf_ok,
        "version": pdf_detail if pdf_ok else "Missing",
    }
    if not pdf_ok:
        diagnostics["status"] = "degraded"
        diagnostics["recommendations"].append(
            "WeasyPrint graphics C-libraries (Pango/Cairo) are missing. "
            "Install with: 'brew install pango' (macOS) or 'apt-get install -y libpango-1.0-0 libcairo2' (Linux)."
        )

    # 3. Database Check
    dbs_ready_count = 0
    for db_name in SQLITE_DBS:
        db_path = repo_root / db_name
        exists = db_path.exists()
        if exists:
            dbs_ready_count += 1
        diagnostics["databases"][db_name] = {
            "type": "sqlite",
            "exists": exists,
            "size_bytes": db_path.stat().st_size if exists else 0,
        }

    # 4. Port Availability Checks
    categorized_ports = load_categorized_ports()
    total_ports = 0
    for cat_name, items in categorized_ports.items():
        for item in items:
            s_name = item["name"]
            ports = item["ports"]
            for port in ports:
                total_ports += 1
                key_name = s_name if len(ports) == 1 else f"{s_name} ({port})"
                in_use = is_port_in_use(port)
                diagnostics["ports"][key_name] = {
                    "service": s_name,
                    "category": cat_name,
                    "port": port,
                    "in_use": in_use,
                    "status": "in_use" if in_use else "available",
                }
                if in_use:
                    diagnostics["status"] = "degraded"
                    diagnostics["recommendations"].append(
                        f"Port collision: {s_name} on port {port} is occupied."
                    )

    if json_mode:
        output_json(diagnostics)
        return

    # Build Authored Terminal Document
    doc = TerminalDocument(
        title="Cadence Clinical Environment Diagnostics",
        subtitle="Runtime, dependencies, database storage, ports, and git guardrails",
    )

    is_healthy = diagnostics["status"] == "healthy"
    doc.add_metric(
        "Status",
        "HEALTHY" if is_healthy else "DEGRADED",
        style="green" if is_healthy else "yellow",
    )
    doc.add_metric("Python", py_version, style="green" if py_valid else "red")
    doc.add_metric(
        "SQLite DBs",
        f"{dbs_ready_count}/{len(SQLITE_DBS)} Ready",
        style="green" if dbs_ready_count == len(SQLITE_DBS) else "yellow",
    )
    doc.add_metric("Ports", f"{total_ports} Configured", style="cyan")

    for act in healed_actions:
        doc.add_item(act, status="pass")

    # Binaries table
    tool_rows = []
    for tool_name, info in diagnostics["binaries"].items():
        tool_rows.append(
            [
                tool_name,
                "Installed" if info["installed"] else "Missing",
                info["version"] or "N/A",
            ]
        )
    doc.add_table_data(
        "Development Tools",
        [("Tool", "bold white"), ("Status", "bold"), ("Version", "dim")],
        tool_rows,
    )

    # SQLite table
    db_rows = []
    for db_name, info in diagnostics["databases"].items():
        size_kb = f"{info['size_bytes'] / 1024:.1f} KB" if info["exists"] else "0 KB"
        db_rows.append(
            [
                db_name,
                "Ready" if info["exists"] else "Missing (Run 'cadence doctor -f')",
                size_kb,
            ]
        )
    doc.add_table_data(
        "Local SQLite Storage",
        [("Database", "bold white"), ("Status", "bold"), ("Size", "dim")],
        db_rows,
    )

    if not is_healthy:
        for rec in diagnostics["recommendations"]:
            doc.add_item(rec, status="warn")
        doc.set_cta(
            "Run 'uv run cadence doctor --auto-fix' to automatically remediate environment drift."
        )
    else:
        doc.set_cta(
            "Run 'uv run cadence check --parallel' to validate repository quality gates."
        )

    doc.display()
