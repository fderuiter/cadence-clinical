"""Unified dynamic port discovery, availability checking, and automatic fallback offset engine."""

import contextlib
import os
import re
import socket
import sys
from collections.abc import Sequence
from pathlib import Path
from typing import Any

import yaml

# Add repository root to sys.path if not present
REPO_ROOT = Path(__file__).resolve().parents[2]
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

# Categorized static fallback listings of default ports for host-native services
# or when the orchestration definition (docker-compose.yml) is missing or unparseable.
STATIC_DEFAULTS: dict[str, list[dict[str, Any]]] = {
    "Frontends": [
        {"name": "Web Application", "ports": [3000], "service_key": "web"},
        {"name": "Subject Portal", "ports": [5174], "service_key": "subject-portal"},
    ],
    "Infrastructure & Databases": [
        {"name": "Postgres Database", "ports": [5432], "service_key": "postgres"},
        {"name": "Postgres eTMF", "ports": [5433], "service_key": "postgres-etmf"},
        {"name": "Postgres CTMS", "ports": [5434], "service_key": "postgres-ctms"},
        {
            "name": "Postgres Quality",
            "ports": [5435],
            "service_key": "postgres-quality",
        },
        {"name": "Neo4j Database", "ports": [7474, 7687], "service_key": "neo4j"},
        {
            "name": "Keycloak Identity Provider",
            "ports": [8080],
            "service_key": "keycloak",
        },
    ],
    "Application Services": [
        {"name": "Gateway Front Proxy", "ports": [8000], "service_key": "front-proxy"},
        {"name": "Gateway API", "ports": [8000], "service_key": "gateway"},
        {"name": "Designer Service", "ports": [8001], "service_key": "designer"},
        {"name": "Execution Service", "ports": [8002], "service_key": "execution"},
        {"name": "eTMF Service", "ports": [8003], "service_key": "etmf"},
        {"name": "Interop Service", "ports": [8004], "service_key": "interop"},
        {"name": "Quality Service", "ports": [8005], "service_key": "quality"},
        {
            "name": "Notifications Service",
            "ports": [8006],
            "service_key": "notifications",
        },
        {"name": "CTMS Service", "ports": [8007], "service_key": "ctms"},
        {"name": "Safety Service", "ports": [8008], "service_key": "safety"},
        {"name": "Tickets Service", "ports": [8009], "service_key": "tickets"},
        {"name": "eISF Service", "ports": [8010], "service_key": "eisf"},
        {"name": "eConsent Service", "ports": [8011], "service_key": "econsent"},
        {"name": "Organization Service", "ports": [8012], "service_key": "org"},
        {"name": "Fileshare Service", "ports": [8013], "service_key": "fileshare"},
    ],
}

DEFAULT_PORTS: dict[str, int] = {
    "Gateway API": 8000,
    "Designer Service": 8001,
    "Execution Service": 8002,
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
    "Fileshare Service": 8013,
}

SERVICE_KEY_DEFAULTS: dict[str, int] = {
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
    "fileshare": 8013,
    "web": 3000,
    "subject-portal": 5174,
    "keycloak": 8080,
    "postgres": 5432,
    "postgres-etmf": 5433,
    "postgres-ctms": 5434,
    "postgres-quality": 5435,
    "neo4j": 7474,
    "front-proxy": 8000,
}

COMPOSE_SERVICE_MAPPING: dict[str, tuple[str, str]] = {
    "postgres": ("Infrastructure & Databases", "Postgres Database"),
    "postgres-etmf": ("Infrastructure & Databases", "Postgres eTMF"),
    "postgres-ctms": ("Infrastructure & Databases", "Postgres CTMS"),
    "postgres-quality": ("Infrastructure & Databases", "Postgres Quality"),
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
    "fileshare": ("Application Services", "Fileshare Service"),
    "subject-portal": ("Frontends", "Subject Portal"),
}


def parse_port_entry(port_entry: Any) -> list[int]:
    """Extract host port(s) from a docker-compose port entry.

    Args:
        port_entry: Port entry which can be int, float, str, or dict.

    Returns:
        List of integer port numbers.
    """
    if isinstance(port_entry, (int, float)):
        return [int(port_entry)]
    if isinstance(port_entry, dict):
        pub = port_entry.get("published") or port_entry.get("target")
        if pub is not None:
            return parse_port_entry(pub)
        return []
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


def extract_ports_from_command(command_entry: Any) -> list[int]:
    """Extract port numbers from a service command entry if '--port' is specified.

    Args:
        command_entry: Command line string or list of argument strings.

    Returns:
        List of detected port numbers.
    """
    ports: list[int] = []
    if isinstance(command_entry, list):
        cmd_str = " ".join(str(arg) for arg in command_entry)
    elif isinstance(command_entry, str):
        cmd_str = command_entry
    else:
        return ports

    match = re.search(r"--port\s+([0-9]+)", cmd_str)
    if match:
        with contextlib.suppress(ValueError):
            ports.append(int(match.group(1)))
    return ports


def find_docker_compose_path(start_path: Path | str | None = None) -> Path | None:
    """Finds the docker-compose.yml file location.

    Args:
        start_path: Optional starting path to search from.

    Returns:
        Path to docker-compose.yml if found, else None.
    """
    if start_path is not None:
        p = Path(start_path).resolve()
        if os.path.isfile(str(p)):
            return p
        candidate = p / "docker" / "docker-compose.yml"
        if os.path.exists(str(candidate)):
            return candidate

    repo_root = Path(__file__).resolve().parents[2]
    candidate = repo_root / "docker" / "docker-compose.yml"
    if os.path.exists(str(candidate)):
        return candidate

    return None


def load_categorized_ports(
    compose_path: Path | str | None = None,
) -> dict[str, list[dict[str, Any]]]:
    """Dynamically discover ports from container orchestration definition (docker-compose.yml).

    If missing, unparseable, or invalid, gracefully fall back to static default listings.

    Args:
        compose_path: Optional explicit path to docker-compose file.

    Returns:
        Categorized dictionary mapping category names to lists of service items with ports.
    """
    if compose_path is None:
        target_path = find_docker_compose_path()
    else:
        target_path = (
            Path(compose_path).resolve() if os.path.exists(str(compose_path)) else None
        )

    categorized_ports: dict[str, list[dict[str, Any]]] = {
        "Frontends": [],
        "Infrastructure & Databases": [],
        "Application Services": [],
    }

    compose_loaded = False
    if target_path and os.path.exists(str(target_path)):
        try:
            with open(target_path, encoding="utf-8") as f:
                compose_data = yaml.safe_load(f)
            services = (
                compose_data.get("services", {})
                if isinstance(compose_data, dict)
                else {}
            )

            for compose_name, (
                category,
                display_name,
            ) in COMPOSE_SERVICE_MAPPING.items():
                if compose_name in services:
                    s_def = services[compose_name] or {}
                    ports: list[int] = []
                    ports_list = s_def.get("ports", [])
                    if ports_list and isinstance(ports_list, list):
                        for entry in ports_list:
                            ports.extend(parse_port_entry(entry))

                    if not ports and "command" in s_def:
                        ports.extend(extract_ports_from_command(s_def["command"]))

                    if ports:
                        categorized_ports[category].append(
                            {
                                "name": display_name,
                                "ports": sorted(list(set(ports))),
                            }
                        )
                    elif "front-proxy" not in services or compose_name != "gateway":
                        default_ports: list[int] = []
                        for item in STATIC_DEFAULTS.get(category, []):
                            if item["name"] == display_name:
                                default_ports = list(item["ports"])
                                break
                        if default_ports:
                            categorized_ports[category].append(
                                {"name": display_name, "ports": default_ports}
                            )
                else:
                    default_ports = []
                    for item in STATIC_DEFAULTS.get(category, []):
                        if item["name"] == display_name:
                            default_ports = list(item["ports"])
                            break
                    if default_ports:
                        categorized_ports[category].append(
                            {"name": display_name, "ports": default_ports}
                        )

            web_present = any(
                item["name"] == "Web Application"
                for item in categorized_ports["Frontends"]
            )
            if not web_present:
                web_ports = [3000]
                for item in STATIC_DEFAULTS["Frontends"]:
                    if item["name"] == "Web Application":
                        web_ports = list(item["ports"])
                        break
                categorized_ports["Frontends"].append(
                    {"name": "Web Application", "ports": web_ports}
                )

            compose_loaded = True
        except Exception:
            compose_loaded = False

    if not compose_loaded:
        for category, items in STATIC_DEFAULTS.items():
            categorized_ports[category] = [
                {"name": item["name"], "ports": list(item["ports"])} for item in items
            ]

    for category in categorized_ports:
        categorized_ports[category].sort(key=lambda x: x["name"])

    return categorized_ports


def get_discovered_service_ports(
    compose_path: Path | str | None = None,
) -> dict[str, int]:
    """Extract a mapping of CLI service key -> primary default host port.

    Args:
        compose_path: Optional explicit path to docker-compose file.

    Returns:
        Dictionary mapping service keys (e.g. 'gateway', 'designer') to default host port numbers.
    """
    discovered: dict[str, int] = dict(SERVICE_KEY_DEFAULTS)
    categorized = load_categorized_ports(compose_path=compose_path)

    display_to_key: dict[str, str] = {
        display_name: compose_name
        for compose_name, (_, display_name) in COMPOSE_SERVICE_MAPPING.items()
    }
    display_to_key["Web Application"] = "web"

    for items in categorized.values():
        for item in items:
            name = item["name"]
            ports = item["ports"]
            if ports:
                key = display_to_key.get(name)
                if key:
                    discovered[key] = ports[0]

    return discovered


def is_port_in_use(port: int, host: str = "127.0.0.1") -> bool:
    """Non-intrusive TCP socket check to verify if a port is in use.

    Args:
        port: TCP port number to check.
        host: Host IP address.

    Returns:
        True if the port is in use, False if available.
    """
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.settimeout(0.1)
        return s.connect_ex((host, port)) == 0


def find_available_port(
    default_port: int,
    host: str = "127.0.0.1",
    max_offset: int = 100,
    reserved_ports: set[int] | None = None,
) -> tuple[int, int]:
    """Finds the next available port sequentially starting from default_port.

    Args:
        default_port: Target base port number.
        host: Host IP address.
        max_offset: Maximum numeric offset range to attempt.
        reserved_ports: Set of port numbers already reserved in current execution session.

    Returns:
        Tuple of (assigned_port, offset_used).

    Raises:
        RuntimeError: If no port is available within max_offset range.
    """
    reserved = reserved_ports if reserved_ports is not None else set()
    for offset in range(max_offset + 1):
        candidate = default_port + offset
        if candidate in reserved:
            continue
        if not is_port_in_use(candidate, host=host):
            return candidate, offset

    raise RuntimeError(
        f"No available port found for base port {default_port} within offset range 0..{max_offset}"
    )


def resolve_service_port(
    service_key: str,
    host: str = "127.0.0.1",
    max_offset: int = 100,
    compose_path: Path | str | None = None,
    reserved_ports: set[int] | None = None,
) -> dict[str, Any]:
    """Resolves active port binding for a service, applying sequential fallback offset if occupied.

    Args:
        service_key: CLI service key (e.g. 'gateway', 'designer') or display name.
        host: Host IP address.
        max_offset: Maximum offset range.
        compose_path: Optional explicit docker-compose path.
        reserved_ports: Set of ports reserved in current session.

    Returns:
        Dictionary with resolution metadata (service, default_port, assigned_port, offset, rebound).
    """
    service_map = get_discovered_service_ports(compose_path=compose_path)
    base_port = service_map.get(
        service_key, SERVICE_KEY_DEFAULTS.get(service_key, 8000)
    )

    assigned_port, offset = find_available_port(
        default_port=base_port,
        host=host,
        max_offset=max_offset,
        reserved_ports=reserved_ports,
    )

    return {
        "service": service_key,
        "default_port": base_port,
        "assigned_port": assigned_port,
        "offset": offset,
        "rebound": offset > 0,
        "host": host,
    }


def resolve_all_service_ports(
    service_keys: Sequence[str],
    host: str = "127.0.0.1",
    max_offset: int = 100,
    compose_path: Path | str | None = None,
) -> dict[str, dict[str, Any]]:
    """Batch resolves active port bindings for multiple services without internal collisions.

    Args:
        service_keys: List of CLI service keys.
        host: Host IP address.
        max_offset: Maximum offset range.
        compose_path: Optional explicit docker-compose path.

    Returns:
        Dictionary mapping service_key -> resolution dict.
    """
    reserved: set[int] = set()
    results: dict[str, dict[str, Any]] = {}

    for skey in service_keys:
        res = resolve_service_port(
            service_key=skey,
            host=host,
            max_offset=max_offset,
            compose_path=compose_path,
            reserved_ports=reserved,
        )
        reserved.add(res["assigned_port"])
        results[skey] = res

    return results
