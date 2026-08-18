import os
import socket
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

import yaml

from scripts.runtime_guard import enforce_python_runtime, print_runtime_info

# Static fallback listings of default ports for host-native services
# or when the orchestration definition (docker-compose) is missing.
STATIC_DEFAULTS = {
    "Frontends": [
        {"name": "Web Application", "ports": [3000]},
        {"name": "Subject Portal", "ports": [5174]},
    ],
    "Infrastructure & Databases": [
        {"name": "Postgres Database", "ports": [5432]},
        {"name": "Neo4j Database", "ports": [7474, 7687]},
        {"name": "Keycloak Identity Provider", "ports": [8080]},
    ],
    "Application Services": [
        {"name": "Gateway Front Proxy", "ports": [8000]},
        {"name": "Gateway API", "ports": [8000]},
        {"name": "Designer Service", "ports": [8001]},
        {"name": "Execution Service", "ports": [8002]},
        {"name": "eTMF Service", "ports": [8003]},
        {"name": "Interop Service", "ports": [8004]},
        {"name": "Quality Service", "ports": [8005]},
        {"name": "Notifications Service", "ports": [8006]},
        {"name": "CTMS Service", "ports": [8007]},
        {"name": "Safety Service", "ports": [8008]},
        {"name": "Tickets Service", "ports": [8009]},
        {"name": "eISF Service", "ports": [8010]},
        {"name": "eConsent Service", "ports": [8011]},
        {"name": "Organization Service", "ports": [8012]},
    ],
}

# Compatibility mapping/variable for any external usage
DEFAULT_PORTS = {
    "Gateway API": 8000,
    "Designer Service": 8001,
    "Execution Service": 8002,
    "eTMF Service": 8003,
    "CTMS Service": 8007,
    "Interop Service": 8004,
    "Notifications Service": 8006,
    "Quality Service": 8005,
    "Safety Service": 8008,
    "Tickets Service": 8009,
    "eConsent Service": 8011,
    "eISF Service": 8010,
    "Organization Service": 8012,
}

# Mapping from docker-compose service name to category & default display name
COMPOSE_SERVICE_MAPPING = {
    "postgres": ("Infrastructure & Databases", "Postgres Database"),
    "neo4j": ("Infrastructure & Databases", "Neo4j Database"),
    "keycloak": ("Infrastructure & Databases", "Keycloak Identity Provider"),
    "front-proxy": ("Application Services", "Gateway Front Proxy"),
    "gateway": ("Application Services", "Gateway API"),
    "gateway-rewrite": ("Application Services", "Gateway Rewrite"),
    "designer": ("Application Services", "Designer Service"),
    "execution": ("Application Services", "Execution Service"),
    "etmf": ("Application Services", "eTMF Service"),
    "interop": ("Application Services", "Interop Service"),
    "quality": ("Application Services", "Quality Service"),
    "notifications": ("Application Services", "Notifications Service"),
    "ctms": ("Application Services", "CTMS Service"),
    "safety": ("Application Services", "Safety Service"),
    "tickets": ("Application Services", "Tickets Service"),
    "eisf": ("Application Services", "eISF Service"),
    "econsent": ("Application Services", "eConsent Service"),
    "org": ("Application Services", "Organization Service"),
    "subject-portal": ("Frontends", "Subject Portal"),
}


def parse_port_entry(port_entry) -> list[int]:
    """Extract host port(s) from a docker-compose port entry."""
    if isinstance(port_entry, (int, float)):
        return [int(port_entry)]
    if not isinstance(port_entry, str):
        return []

    # Remove protocol if any (e.g., "5432:5432/tcp")
    port_str = port_entry.split("/")[0]

    # Split by ':' to separate host and container ports
    parts = port_str.split(":")
    host_part = parts[-2] if len(parts) >= 2 else parts[0]

    # Handle ranges like "8080-8085"
    if "-" in host_part:
        try:
            start, end = host_part.split("-")
            return list(range(int(start), int(end) + 1))
        except ValueError:
            return []
    else:
        try:
            return [int(host_part)]
        except ValueError:
            return []


def load_categorized_ports() -> dict[str, list[dict]]:
    """Dynamically discover ports from the container orchestration definition (docker-compose).

    If missing or invalid, gracefully fall back to the static default listings.
    Maintain fallback for host-native services (like Web Application on 3000) that are not containerized.
    """
    script_dir = os.path.dirname(os.path.abspath(__file__))
    root_dir = os.path.dirname(script_dir)
    docker_compose_path = os.path.join(root_dir, "docker", "docker-compose.yml")

    categorized_ports = {
        "Frontends": [],
        "Infrastructure & Databases": [],
        "Application Services": [],
    }

    compose_loaded = False
    if os.path.exists(docker_compose_path):
        try:
            with open(docker_compose_path) as f:
                compose_data = yaml.safe_load(f)
            services = compose_data.get("services", {})

            # 1. Process all known COMPOSE services
            for compose_name, (
                category,
                display_name,
            ) in COMPOSE_SERVICE_MAPPING.items():
                if compose_name in services:
                    ports_list = services[compose_name].get("ports", [])
                    ports = []
                    if ports_list and isinstance(ports_list, list):
                        for port_entry in ports_list:
                            ports.extend(parse_port_entry(port_entry))

                    if ports:
                        categorized_ports[category].append(
                            {
                                "name": display_name,
                                "ports": sorted(list(set(ports))),
                            }
                        )
                    elif "front-proxy" not in services:
                        # Fallback for this service if defined in compose but has no ports configured
                        # Get default ports from static defaults
                        default_ports = []
                        for default_item in STATIC_DEFAULTS.get(category, []):
                            if default_item["name"] == display_name:
                                default_ports = default_item["ports"]
                                break
                        if default_ports:
                            categorized_ports[category].append(
                                {"name": display_name, "ports": default_ports}
                            )
                else:
                    # Service not defined in compose -> it might be running host-native!
                    # Load from static defaults
                    default_ports = []
                    for default_item in STATIC_DEFAULTS[category]:
                        if default_item["name"] == display_name:
                            default_ports = default_item["ports"]
                            break
                    if default_ports:
                        categorized_ports[category].append(
                            {"name": display_name, "ports": default_ports}
                        )

            # 2. Add "Web Application" (which is purely host-native and not in COMPOSE)
            # Find its static defaults
            web_ports = [3000]
            for default_item in STATIC_DEFAULTS["Frontends"]:
                if default_item["name"] == "Web Application":
                    web_ports = default_item["ports"]
                    break

            categorized_ports["Frontends"].append(
                {"name": "Web Application", "ports": web_ports}
            )

            compose_loaded = True
        except Exception:
            # Fall back to static defaults completely on any error
            compose_loaded = False

    if not compose_loaded:
        # Load all static defaults directly
        for category, items in STATIC_DEFAULTS.items():
            categorized_ports[category] = [dict(item) for item in items]

    # Ensure stable ordering of items within categories
    for category in categorized_ports:
        categorized_ports[category].sort(key=lambda x: x["name"])

    return categorized_ports


def load_ports_from_compose():
    """Dummy/compatibility function to keep interface consistent if expected by external tooling."""
    categorized = load_categorized_ports()
    for category, items in categorized.items():
        for item in items:
            name = item["name"]
            ports = item["ports"]
            if ports and name in DEFAULT_PORTS:
                DEFAULT_PORTS[name] = ports[0]


def is_port_in_use(port: int, host: str = "127.0.0.1") -> bool:
    """Non-intrusive TCP socket check with a small timeout."""
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.settimeout(0.1)
        return s.connect_ex((host, port)) == 0


def main():
    print_runtime_info("check_ports.py")
    print("==========================================================")
    print("--- Cadence Clinical Port Allocation & Diagnostic Check ---")
    print("==========================================================\n")

    categorized_ports = load_categorized_ports()
    collisions = []

    for category in [
        "Frontends",
        "Infrastructure & Databases",
        "Application Services",
    ]:
        items = categorized_ports.get(category, [])
        if not items:
            continue

        print(f"[ {category.upper()} ]")
        for item in items:
            name = item["name"]
            ports = item["ports"]
            for port in ports:
                if is_port_in_use(port):
                    collisions.append((name, port, category))
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
