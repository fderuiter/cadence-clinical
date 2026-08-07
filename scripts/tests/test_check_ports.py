import os
import tempfile

from scripts import check_ports


def test_parse_port_entry():
    assert check_ports.parse_port_entry("5432:5432") == [5432]
    assert check_ports.parse_port_entry("5432") == [5432]
    assert check_ports.parse_port_entry(5432) == [5432]
    assert check_ports.parse_port_entry("127.0.0.1:8080:8080") == [8080]
    assert check_ports.parse_port_entry("8080-8082:8080-8082") == [
        8080,
        8081,
        8082,
    ]
    assert check_ports.parse_port_entry("invalid") == []


def test_load_categorized_ports_fallback():
    # If docker-compose.yml is missing, it should fall back to static defaults
    original_exists = os.path.exists

    def mock_exists(path):
        if "docker-compose.yml" in path:
            return False
        return original_exists(path)

    os.path.exists = mock_exists
    try:
        ports = check_ports.load_categorized_ports()
        assert "Frontends" in ports
        assert "Infrastructure & Databases" in ports
        assert "Application Services" in ports

        # Verify Web Application is in Frontends
        frontend_names = [item["name"] for item in ports["Frontends"]]
        assert "Web Application" in frontend_names
        assert "Subject Portal" in frontend_names
    finally:
        os.path.exists = original_exists


def test_load_categorized_ports_with_compose():
    # Create a dummy docker-compose.yml
    dummy_compose = """
version: '3.8'
services:
  postgres:
    ports:
      - "5432:5432"
  neo4j:
    ports:
      - "7474:7474"
      - "7687:7687"
  gateway:
    ports:
      - "8999:8000"
"""
    with tempfile.TemporaryDirectory() as tmpdir:
        docker_dir = os.path.join(tmpdir, "docker")
        os.makedirs(docker_dir)
        compose_path = os.path.join(docker_dir, "docker-compose.yml")
        with open(compose_path, "w") as f:
            f.write(dummy_compose)

        # Temporarily mock os.path.join to point to this tmpdir's compose file
        original_join = os.path.join

        def mock_join(*args):
            if "docker-compose.yml" in args:
                return compose_path
            return original_join(*args)

        os.path.join = mock_join

        # Mock os.path.exists to return True for our mock path
        original_exists = os.path.exists

        def mock_exists(path):
            if path == compose_path:
                return True
            return original_exists(path)

        os.path.exists = mock_exists
        try:
            ports = check_ports.load_categorized_ports()

            # Check gateway port was overridden to 8999
            gateway_item = next(
                item
                for item in ports["Application Services"]
                if item["name"] == "Gateway API"
            )
            assert gateway_item["ports"] == [8999]

            # Check multi-port database neo4j
            neo4j_item = next(
                item
                for item in ports["Infrastructure & Databases"]
                if item["name"] == "Neo4j Database"
            )
            assert neo4j_item["ports"] == [7474, 7687]

            # Check host-native Web Application is still included
            frontend_names = [item["name"] for item in ports["Frontends"]]
            assert "Web Application" in frontend_names
        finally:
            os.path.join = original_join
            os.path.exists = original_exists
