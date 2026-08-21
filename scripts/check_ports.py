import json
import sys
from pathlib import Path

# Add repository root to sys.path
REPO_ROOT = str(Path(__file__).resolve().parent.parent)
if REPO_ROOT not in sys.path:
    sys.path.insert(0, REPO_ROOT)

# Enforce Python 3.14+ runtime before loading standard modules or packages
if sys.version_info < (3, 14):
    try:
        from scripts.runtime_guard import enforce_python_runtime

        enforce_python_runtime()
    except Exception:
        sys.stderr.write(
            f"[FATAL] Incompatible Python runtime {sys.version.split()[0]} ({sys.executable}).\n"
            "Cadence Clinical requires Python 3.14+.\n"
            "Please run: uv run python scripts/check_ports.py\n"
        )
        sys.exit(1)

from packages.cli.ports import (
    COMPOSE_SERVICE_MAPPING,
    DEFAULT_PORTS,
    STATIC_DEFAULTS,
    is_port_in_use,
    load_categorized_ports,
    parse_port_entry,
)
from scripts.runtime_guard import enforce_python_runtime, print_runtime_info

__all__ = [
    "COMPOSE_SERVICE_MAPPING",
    "DEFAULT_PORTS",
    "STATIC_DEFAULTS",
    "is_port_in_use",
    "load_categorized_ports",
    "load_ports_from_compose",
    "parse_port_entry",
]


def load_ports_from_compose() -> None:
    """Dummy/compatibility function to keep interface consistent if expected by external tooling."""
    categorized = load_categorized_ports()
    for category, items in categorized.items():
        for item in items:
            name = item["name"]
            ports = item["ports"]
            if ports and name in DEFAULT_PORTS:
                DEFAULT_PORTS[name] = ports[0]


def main() -> None:
    """Main CLI entry point for check_ports script."""
    json_mode = "--json" in sys.argv or "-j" in sys.argv

    categorized_ports = load_categorized_ports()
    collisions = []
    report_data = {
        "status": "healthy",
        "categories": {},
        "collisions": [],
    }

    for category in [
        "Frontends",
        "Infrastructure & Databases",
        "Application Services",
    ]:
        items = categorized_ports.get(category, [])
        if not items:
            continue

        report_data["categories"][category] = []
        for item in items:
            name = item["name"]
            ports = item["ports"]
            for port in ports:
                in_use = is_port_in_use(port)
                item_report = {
                    "name": name,
                    "port": port,
                    "in_use": in_use,
                    "status": "in_use" if in_use else "available",
                }
                report_data["categories"][category].append(item_report)
                if in_use:
                    collisions.append((name, port, category))

    if collisions:
        report_data["status"] = "degraded"
        report_data["collisions"] = [
            {"service": name, "port": port, "category": category}
            for name, port, category in collisions
        ]

    if json_mode:
        print(json.dumps(report_data, indent=2))
        return

    print_runtime_info("check_ports.py")
    print("==========================================================")
    print("--- Cadence Clinical Port Allocation & Diagnostic Check ---")
    print("==========================================================\n")

    for category, items_report in report_data["categories"].items():
        print(f"[ {category.upper()} ]")
        for item in items_report:
            name = item["name"]
            port = item["port"]
            if item["in_use"]:
                print(f" [!] PORT IN USE: {name:<28} on port {port}")
            else:
                print(f" [✓] AVAILABLE:   {name:<28} on port {port}")
        print()

    print("==========================================================")
    print("--- Diagnostic Summary ---")
    print("==========================================================")
    if collisions:
        print(
            f"Warning: {len(collisions)} port conflict(s) detected across your stack:"
        )
        for name, port, category in collisions:
            print(f"  - {name} ({category}) is blocked on port {port}")
        print("\nTroubleshooting:")
        print(
            "  Please stop any local services or container instances running on these ports"
        )
        print("  before starting your local microservice development environment.")
    else:
        print("All system, database, identity, and frontend ports are free and ready!")
        print("Your development workstation is fully prepared.")
    print("==========================================================")


if __name__ == "__main__":
    main()
