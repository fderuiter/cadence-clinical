"""Unit tests for unified dynamic port discovery, socket checks, and fallback offset resolution.

@req:PRD-SYS-001
"""

import socket
from pathlib import Path

from packages.cli.ports import (
    extract_ports_from_command,
    find_available_port,
    get_discovered_service_ports,
    is_port_in_use,
    load_categorized_ports,
    parse_port_entry,
    resolve_all_service_ports,
    resolve_service_port,
)


def test_parse_port_entry_variations():
    """Validate port entry parsing across int, string, range, dict, and invalid inputs.

    @req:PRD-SYS-001
    """
    assert parse_port_entry(8000) == [8000]
    assert parse_port_entry("8000") == [8000]
    assert parse_port_entry("8000:8000") == [8000]
    assert parse_port_entry("127.0.0.1:8000:8000") == [8000]
    assert parse_port_entry("8000-8002:8000-8002") == [8000, 8001, 8002]
    assert parse_port_entry({"published": 8001, "target": 8000}) == [8001]
    assert parse_port_entry({"target": 8002}) == [8002]
    assert parse_port_entry("invalid") == []
    assert parse_port_entry(None) == []


def test_extract_ports_from_command():
    """Validate extracting --port parameters from service command strings and lists.

    @req:PRD-SYS-001
    """
    cmd_list = ["python", "scripts/start.py", "designer", "--port", "8001"]
    assert extract_ports_from_command(cmd_list) == [8001]

    cmd_str = "python scripts/start.py execution --port 8002 --reload"
    assert extract_ports_from_command(cmd_str) == [8002]

    assert extract_ports_from_command("python scripts/start.py") == []


def test_load_categorized_ports_compose(tmp_path: Path):
    """Validate dynamic extraction of ports from docker-compose orchestration file.

    @req:PRD-SYS-001
    """
    dummy_compose = """
version: '3.8'
services:
  postgres:
    ports:
      - "5432:5432"
  gateway:
    ports:
      - "8999:8000"
  designer:
    command: ["python", "scripts/start.py", "designer", "--port", "8001"]
"""
    compose_path = tmp_path / "docker-compose.yml"
    compose_path.write_text(dummy_compose, encoding="utf-8")

    ports = load_categorized_ports(compose_path=compose_path)
    assert "Frontends" in ports
    assert "Infrastructure & Databases" in ports
    assert "Application Services" in ports

    gateway_item = next(
        item for item in ports["Application Services"] if item["name"] == "Gateway API"
    )
    assert gateway_item["ports"] == [8999]

    designer_item = next(
        item
        for item in ports["Application Services"]
        if item["name"] == "Designer Service"
    )
    assert designer_item["ports"] == [8001]


def test_load_categorized_ports_fallback_on_missing(tmp_path: Path):
    """Validate graceful fallback to default port maps when orchestration file is missing.

    @req:PRD-SYS-001
    """
    non_existent = tmp_path / "non_existent_compose.yml"
    ports = load_categorized_ports(compose_path=non_existent)

    assert "Frontends" in ports
    assert "Application Services" in ports
    gateway_item = next(
        item for item in ports["Application Services"] if item["name"] == "Gateway API"
    )
    assert gateway_item["ports"] == [8000]


def test_get_discovered_service_ports():
    """Validate mapping CLI service keys to default host ports.

    @req:PRD-SYS-001
    """
    discovered = get_discovered_service_ports()
    assert discovered["gateway"] == 8000
    assert discovered["designer"] == 8001
    assert discovered["execution"] == 8002
    assert discovered["web"] == 3000


def test_is_port_in_use_and_find_available_port():
    """Validate socket connectivity checks and available port resolution.

    @req:PRD-SYS-002
    """
    # Bind a socket on a local port to simulate an occupied port
    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s.bind(("127.0.0.1", 0))
    occupied_port = s.getsockname()[1]
    s.listen(128)

    try:
        assert is_port_in_use(occupied_port) is True

        assigned, offset = find_available_port(default_port=occupied_port)
        assert assigned > occupied_port
        assert offset > 0
        assert is_port_in_use(assigned) is False
    finally:
        s.close()


def test_resolve_service_port_collision():
    """Validate automatic fallback port offset assignment when base port is occupied.

    @req:PRD-SYS-002
    """
    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s.bind(("127.0.0.1", 0))
    occupied_port = s.getsockname()[1]
    s.listen(128)

    try:
        # Mock SERVICE_KEY_DEFAULTS via reserved_ports
        res = resolve_service_port(
            service_key="custom_test_service",
            host="127.0.0.1",
            reserved_ports={occupied_port},
        )
        assert "service" in res
        assert "assigned_port" in res
        assert "offset" in res
        assert "rebound" in res
    finally:
        s.close()


def test_resolve_all_service_ports_batch():
    """Validate batch port resolution ensures no internal collisions.

    @req:PRD-SYS-002
    """
    manifest = resolve_all_service_ports(["gateway", "designer", "execution"])
    assert len(manifest) == 3
    ports = [item["assigned_port"] for item in manifest.values()]
    assert len(set(ports)) == 3
