import os

import yaml


def test_docker_compose_named_volumes():
    """Verify that backend microservices read and write to persistent SQLite databases housed in dedicated, independent Docker named volumes.

    @req: PRD-SYS-101
    """
    compose_path = "/app/docker/docker-compose.yml"
    assert os.path.exists(compose_path)

    with open(compose_path) as f:
        config = yaml.safe_load(f)

    # All 9 SQLite microservices must have dedicated named volumes
    sqlite_services = [
        "eisf",
        "etmf",
        "ctms",
        "quality",
        "interop",
        "tickets",
        "safety",
        "notifications",
        "econsent",
    ]

    # Verify each SQLite service maps to its own named volume
    for service_name in sqlite_services:
        service = config["services"][service_name]

        # Verify there's a named volume in the volumes section
        volumes = service.get("volumes", [])
        has_named_volume = False
        volume_name = f"{service_name}_data"

        # We expect a mount of format "volume_name:/app/some_path"
        for vol in volumes:
            if vol.startswith(f"{volume_name}:"):
                has_named_volume = True
                # Path inside must be isolated (e.g. /app/service_name_data)
                assert vol.endswith(f"/app/{service_name}_data")

        assert has_named_volume, (
            f"Service {service_name} is missing a mount for named volume {volume_name}"
        )

        # Verify the database URL environment variable points to the named volume directory
        env = service.get("environment", [])
        # Environment variables can be list of strings "KEY=VALUE"
        db_url_env_var = None
        for item in env:
            if "=" in item:
                k, v = item.split("=", 1)
                if k.endswith("_DATABASE_URL"):
                    db_url_env_var = v
                    break

        assert db_url_env_var is not None, (
            f"Service {service_name} is missing database URL environment variable"
        )
        assert f"/app/{service_name}_data/" in db_url_env_var, (
            f"Database URL {db_url_env_var} for {service_name} does not point to the named volume mount directory /app/{service_name}_data/"
        )

    # Verify that the defined root volumes contains all 9 SQLite named volumes plus postgres/neo4j
    root_volumes = config.get("volumes", {})
    expected_root_volumes = [f"{s}_data" for s in sqlite_services] + [
        "postgres_data",
        "neo4j_data",
    ]
    for rv in expected_root_volumes:
        assert rv in root_volumes, f"Root volumes is missing {rv}"


def test_docker_compose_targeted_reloads():
    """Verify that backend service file-watchers monitor only service-specific directory paths instead of the entire project root.

    @req: PRD-SYS-102
    """
    compose_path = "/app/docker/docker-compose.yml"
    with open(compose_path) as f:
        config = yaml.safe_load(f)

    # Services running uvicorn with hot-reload
    services_with_reloads = [
        "designer",
        "execution",
        "org",
        "eisf",
        "etmf",
        "ctms",
        "quality",
        "interop",
        "tickets",
        "safety",
        "notifications",
        "econsent",
        "gateway",
    ]

    for service_name in services_with_reloads:
        service = config["services"][service_name]
        command = service.get("command", [])

        # Command could be a single string or a list of strings
        command_str = command if isinstance(command, str) else " ".join(command)

        assert "--reload" in command_str, (
            f"Service {service_name} command does not have --reload option"
        )
        assert "--reload-dir" in command_str, (
            f"Service {service_name} command does not have --reload-dir option"
        )
        assert f"/app/apps/{service_name}" in command_str, (
            f"Service {service_name} command is not watching /app/apps/{service_name}"
        )


def test_docker_compose_node_modules_isolation():
    """Verify that container orchestration prevents container-compiled node_modules from overwriting or corrupting the host's dependencies.

    @req: PRD-SYS-103
    """
    compose_path = "/app/docker/docker-compose.yml"
    with open(compose_path) as f:
        config = yaml.safe_load(f)

    portal_service = config["services"]["subject-portal"]
    volumes = portal_service.get("volumes", [])

    # We expect /app/node_modules and /app/apps/subject-portal/node_modules to be mounted as anonymous/named volumes to shield the host
    assert "/app/node_modules" in volumes, (
        "subject-portal service is missing anonymous volume mount for /app/node_modules"
    )
    assert "/app/apps/subject-portal/node_modules" in volumes, (
        "subject-portal service is missing anonymous volume mount for /app/apps/subject-portal/node_modules"
    )


def test_docker_compose_user_permissions_mapped():
    """Verify that standard local development containers run with user permissions mapped to the host developer's UID and GID to avoid root-owned file conflicts.

    @req: PRD-SYS-104
    """
    compose_path = "/app/docker/docker-compose.yml"
    with open(compose_path) as f:
        config = yaml.safe_load(f)

    custom_services = [
        "designer",
        "execution",
        "org",
        "eisf",
        "etmf",
        "ctms",
        "quality",
        "interop",
        "tickets",
        "safety",
        "notifications",
        "econsent",
        "subject-portal",
        "gateway",
    ]

    for service_name in custom_services:
        service = config["services"][service_name]
        user_mapping = service.get("user")
        assert user_mapping == "${UID:-1000}:${GID:-1000}", (
            f"Service {service_name} does not have UID:GID user mapping configured"
        )
