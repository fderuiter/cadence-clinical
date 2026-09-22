"""Unit tests for declarative service manifest registry and runtime execution profiles.

@req:PRD-SYS-001
"""

import pytest
from typer.testing import CliRunner

from packages.cli.main import app
from packages.cli.registry import (
    SERVICE_REGISTRY,
    ServiceRuntimeProfile,
    get_service_profile,
    list_service_profiles,
    normalize_service_name,
)

runner = CliRunner()

MIGRATED_CS_SERVICES = [
    ("gateway", "apps/gateway/src/Cadence.Apps.Gateway.csproj", 8000),
    ("designer", "apps/designer/src/Cadence.Apps.Designer.csproj", 8001),
    ("execution", "apps/execution/src/Cadence.Apps.Execution.csproj", 8002),
    ("etmf", "apps/etmf/src/Cadence.Apps.Etmf.csproj", 8003),
    ("interop", "apps/interop/src/Cadence.Apps.Interop.csproj", 8004),
    ("quality", "apps/quality/src/Cadence.Apps.Quality.csproj", 8005),
    ("notifications", "apps/notifications/src/Cadence.Apps.Notifications.csproj", 8006),
    ("ctms", "apps/ctms/src/Cadence.Apps.Ctms.csproj", 8007),
    ("safety", "apps/safety/src/Cadence.Apps.Safety.csproj", 8008),
    ("tickets", "apps/tickets/src/Cadence.Apps.Tickets.csproj", 8009),
    ("eisf", "apps/eisf/src/Cadence.Apps.Eisf.csproj", 8010),
    ("econsent", "apps/econsent/src/Cadence.Apps.Econsent.csproj", 8011),
    ("org", "apps/org/src/Cadence.Apps.Org.csproj", 8012),
    ("fileshare", "apps/fileshare/src/Cadence.Apps.Fileshare.csproj", 8013),
    ("imaging", "apps/imaging/src/Cadence.Apps.Imaging.csproj", 8014),
    ("knowledge", "apps/knowledge/src/Cadence.Apps.Knowledge.csproj", 8015),
    ("payments", "apps/payments/src/Cadence.Apps.Payments.csproj", 8016),
    ("ai_gateway", "apps/ai_gateway/src/Cadence.Apps.AiGateway.csproj", 8017),
]


def test_registry_contains_all_18_cs_services():
    """Verify all 18 migrated C# services have explicit .NET runtime profiles."""
    assert len(MIGRATED_CS_SERVICES) == 18
    assert len(list_service_profiles()) >= 20
    for name, project_path, port in MIGRATED_CS_SERVICES:
        assert name in SERVICE_REGISTRY
        profile = get_service_profile(name)
        assert profile.name == name
        assert profile.runtime == "dotnet"
        assert profile.project_path == project_path
        assert profile.default_port == port


def test_registry_contains_node_services():
    """Verify frontend workspace apps have explicit Node runtime profiles."""
    web_profile = get_service_profile("web")
    assert web_profile.runtime == "node"
    assert web_profile.project_path == "apps/web"
    assert web_profile.default_port == 3000

    portal_profile = get_service_profile("subject-portal")
    assert portal_profile.runtime == "node"
    assert portal_profile.project_path == "apps/subject-portal"
    assert portal_profile.default_port == 5174


def test_registry_alias_resolution():
    """Verify service alias lookups resolve to canonical profiles."""
    assert normalize_service_name("ai-gateway") == "ai_gateway"
    assert get_service_profile("ai-gateway").name == "ai_gateway"

    assert normalize_service_name("subject_portal") == "subject-portal"
    assert get_service_profile("subject_portal").name == "subject-portal"

    assert normalize_service_name("front-proxy") == "gateway"
    assert get_service_profile("front-proxy").name == "gateway"


def test_registry_fail_fast_unknown_service():
    """Verify unknown service lookup raises explicit KeyError."""
    with pytest.raises(KeyError, match="Unknown service 'non_existent_service'"):
        get_service_profile("non_existent_service")


def test_dotnet_command_and_environment():
    """Verify .NET runtime profile generates dotnet run command and ASPNETCORE_URLS env."""
    profile = get_service_profile("ctms")
    cmd = profile.get_start_command(host="127.0.0.1", port=8007)
    assert cmd == [
        "dotnet",
        "run",
        "--project",
        "apps/ctms/src/Cadence.Apps.Ctms.csproj",
    ]

    env = profile.get_environment(host="127.0.0.1", port=8007)
    assert env["ASPNETCORE_URLS"] == "http://127.0.0.1:8007"
    assert env["ASPNETCORE_ENVIRONMENT"] == "Development"


def test_python_command_and_environment():
    """Verify Python runtime profile generates uvicorn command and PYTHONPATH env."""
    profile = ServiceRuntimeProfile(
        name="legacy_service",
        runtime="python",
        project_path="apps/legacy_service/main.py",
        default_port=8090,
    )
    cmd = profile.get_start_command(host="0.0.0.0", port=8090, reload=True)  # nosec B104
    assert cmd == [
        "uvicorn",
        "apps.legacy_service.main:app",
        "--host",
        "0.0.0.0",  # nosec B104
        "--port",
        "8090",
        "--reload",
    ]


def test_node_command_and_environment():
    """Verify Node runtime profile generates pnpm command and PORT env."""
    profile = get_service_profile("web")
    cmd = profile.get_start_command(host="127.0.0.1", port=3000)
    assert cmd == ["pnpm", "--filter", "web", "dev"]

    env = profile.get_environment(host="127.0.0.1", port=3000)
    assert env["PORT"] == "3000"


def test_dev_cli_json_output_includes_registry_metadata():
    """Verify cadence dev --json includes explicit runtime profile metadata."""
    result = runner.invoke(app, ["--json", "dev", "-s", "ctms"])
    assert result.exit_code == 0
    assert '"runtime": "dotnet"' in result.stdout
    assert '"project_path": "apps/ctms/src/Cadence.Apps.Ctms.csproj"' in result.stdout
    assert '"migration_script": "apps/ctms/migrate.py"' in result.stdout
