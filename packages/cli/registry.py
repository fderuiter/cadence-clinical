"""Declarative service manifest registry with explicit runtime execution profiles."""

import os
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Literal

RuntimeEngine = Literal["dotnet", "python", "node"]


@dataclass
class ServiceRuntimeProfile:
    """Runtime metadata and execution specification for a microservice."""

    name: str
    runtime: RuntimeEngine
    project_path: str
    default_port: int
    migration_script: str | None = None
    extra_args: list[str] = field(default_factory=list)

    def get_start_command(
        self,
        host: str = "127.0.0.1",
        port: int | None = None,
        reload: bool = True,
        extra_args: list[str] | None = None,
    ) -> list[str]:
        """Generates the shell command list to launch this service."""
        effective_port = port if port is not None else self.default_port
        user_extra = extra_args or []

        if self.runtime == "dotnet":
            # dotnet run --project <project_path>
            cmd = ["dotnet", "run", "--project", self.project_path]
            if user_extra:
                cmd.extend(user_extra)
            return cmd

        if self.runtime == "python":
            # uvicorn apps.<name>.main:app --host <host> --port <port> [--reload]
            cmd = [
                "uvicorn",
                f"apps.{self.name}.main:app",
                "--host",
                host,
                "--port",
                str(effective_port),
            ]
            if reload:
                cmd.append("--reload")
            if user_extra:
                cmd.extend(user_extra)
            return cmd

        if self.runtime == "node":
            # pnpm --filter <name> dev
            cmd = ["pnpm", "--filter", self.name, "dev"]
            if user_extra:
                cmd.extend(user_extra)
            return cmd

        raise ValueError(f"Unsupported runtime engine: {self.runtime}")

    def get_environment(
        self,
        host: str = "127.0.0.1",
        port: int | None = None,
        repo_root: Path | None = None,
    ) -> dict[str, str]:
        """Builds process environment variables for service execution."""
        effective_port = port if port is not None else self.default_port
        env = dict(os.environ)

        if self.runtime == "dotnet":
            env["ASPNETCORE_URLS"] = f"http://{host}:{effective_port}"
            if "ASPNETCORE_ENVIRONMENT" not in env:
                env["ASPNETCORE_ENVIRONMENT"] = "Development"
        elif self.runtime == "python":
            if repo_root:
                env["PYTHONPATH"] = str(repo_root)
        elif self.runtime == "node":
            env["PORT"] = str(effective_port)

        return env

    def to_dict(self) -> dict[str, Any]:
        """Serializes registry profile metadata to a dictionary."""
        return {
            "name": self.name,
            "runtime": self.runtime,
            "project_path": self.project_path,
            "default_port": self.default_port,
            "migration_script": self.migration_script,
            "extra_args": list(self.extra_args),
        }


SERVICE_REGISTRY: dict[str, ServiceRuntimeProfile] = {
    # .NET 10 Migrated Application Microservices
    "gateway": ServiceRuntimeProfile(
        name="gateway",
        runtime="dotnet",
        project_path="apps/gateway/src/Cadence.Apps.Gateway.csproj",
        default_port=8000,
    ),
    "designer": ServiceRuntimeProfile(
        name="designer",
        runtime="dotnet",
        project_path="apps/designer/src/Cadence.Apps.Designer.csproj",
        default_port=8001,
    ),
    "execution": ServiceRuntimeProfile(
        name="execution",
        runtime="dotnet",
        project_path="apps/execution/src/Cadence.Apps.Execution.csproj",
        default_port=8002,
    ),
    "etmf": ServiceRuntimeProfile(
        name="etmf",
        runtime="dotnet",
        project_path="apps/etmf/src/Cadence.Apps.Etmf.csproj",
        default_port=8003,
        migration_script="apps/etmf/migrate.py",
    ),
    "interop": ServiceRuntimeProfile(
        name="interop",
        runtime="dotnet",
        project_path="apps/interop/src/Cadence.Apps.Interop.csproj",
        default_port=8004,
    ),
    "quality": ServiceRuntimeProfile(
        name="quality",
        runtime="dotnet",
        project_path="apps/quality/src/Cadence.Apps.Quality.csproj",
        default_port=8005,
        migration_script="apps/quality/migrate.py",
    ),
    "notifications": ServiceRuntimeProfile(
        name="notifications",
        runtime="dotnet",
        project_path="apps/notifications/src/Cadence.Apps.Notifications.csproj",
        default_port=8006,
    ),
    "ctms": ServiceRuntimeProfile(
        name="ctms",
        runtime="dotnet",
        project_path="apps/ctms/src/Cadence.Apps.Ctms.csproj",
        default_port=8007,
        migration_script="apps/ctms/migrate.py",
    ),
    "safety": ServiceRuntimeProfile(
        name="safety",
        runtime="dotnet",
        project_path="apps/safety/src/Cadence.Apps.Safety.csproj",
        default_port=8008,
    ),
    "tickets": ServiceRuntimeProfile(
        name="tickets",
        runtime="dotnet",
        project_path="apps/tickets/src/Cadence.Apps.Tickets.csproj",
        default_port=8009,
    ),
    "eisf": ServiceRuntimeProfile(
        name="eisf",
        runtime="dotnet",
        project_path="apps/eisf/src/Cadence.Apps.Eisf.csproj",
        default_port=8010,
        migration_script="apps/eisf/database/migrate.py",
    ),
    "econsent": ServiceRuntimeProfile(
        name="econsent",
        runtime="dotnet",
        project_path="apps/econsent/src/Cadence.Apps.Econsent.csproj",
        default_port=8011,
    ),
    "org": ServiceRuntimeProfile(
        name="org",
        runtime="dotnet",
        project_path="apps/org/src/Cadence.Apps.Org.csproj",
        default_port=8012,
    ),
    "fileshare": ServiceRuntimeProfile(
        name="fileshare",
        runtime="dotnet",
        project_path="apps/fileshare/src/Cadence.Apps.Fileshare.csproj",
        default_port=8013,
        migration_script="apps/fileshare/migrate.py",
    ),
    "imaging": ServiceRuntimeProfile(
        name="imaging",
        runtime="dotnet",
        project_path="apps/imaging/src/Cadence.Apps.Imaging.csproj",
        default_port=8014,
    ),
    "knowledge": ServiceRuntimeProfile(
        name="knowledge",
        runtime="dotnet",
        project_path="apps/knowledge/src/Cadence.Apps.Knowledge.csproj",
        default_port=8015,
    ),
    "payments": ServiceRuntimeProfile(
        name="payments",
        runtime="dotnet",
        project_path="apps/payments/src/Cadence.Apps.Payments.csproj",
        default_port=8016,
    ),
    "ai_gateway": ServiceRuntimeProfile(
        name="ai_gateway",
        runtime="dotnet",
        project_path="apps/ai_gateway/src/Cadence.Apps.AiGateway.csproj",
        default_port=8017,
    ),
    # Frontend / Node Workspace Applications
    "web": ServiceRuntimeProfile(
        name="web",
        runtime="node",
        project_path="apps/web",
        default_port=3000,
    ),
    "subject-portal": ServiceRuntimeProfile(
        name="subject-portal",
        runtime="node",
        project_path="apps/subject-portal",
        default_port=5174,
    ),
}

SERVICE_ALIAS_MAP: dict[str, str] = {
    "ai-gateway": "ai_gateway",
    "subject_portal": "subject-portal",
    "front-proxy": "gateway",
}


def normalize_service_name(name: str) -> str:
    """Normalizes service name key applying standard alias mappings."""
    key = name.lower().strip()
    return SERVICE_ALIAS_MAP.get(key, key)


def get_service_profile(name: str) -> ServiceRuntimeProfile:
    """Retrieves a service execution profile from the registry, failing fast on unknown keys."""
    normalized = normalize_service_name(name)
    if normalized not in SERVICE_REGISTRY:
        valid_names = ", ".join(sorted(SERVICE_REGISTRY.keys()))
        raise KeyError(
            f"Unknown service '{name}'. Valid registered services are: {valid_names}"
        )
    return SERVICE_REGISTRY[normalized]


def list_service_profiles() -> list[ServiceRuntimeProfile]:
    """Returns list of all registered service execution profiles."""
    return list(SERVICE_REGISTRY.values())
