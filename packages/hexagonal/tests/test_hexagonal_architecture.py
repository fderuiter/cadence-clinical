import ast
import glob
from pathlib import Path

import pytest
from pytest_archon import archrule

# Centralized pre-approved legacy directories excluded from verification checks
LEGACY_EXCLUDED_DIRECTORIES = {"web", "subject-portal"}


def discover_services() -> list[str]:
    root_dir = Path(__file__).resolve().parent.parent.parent.parent
    apps_dir = root_dir / "apps"
    services = []
    if apps_dir.exists():
        for p in apps_dir.iterdir():
            if (
                p.is_dir()
                and p.name not in LEGACY_EXCLUDED_DIRECTORIES
                and p.name != "__pycache__"
            ):
                if (
                    (p / "pyproject.toml").exists()
                    or (p / "main.py").exists()
                    or (p / "domain").exists()
                ):
                    services.append(p.name)
    return sorted(services)


SERVICES = discover_services()


@pytest.mark.parametrize("service", SERVICES)
def test_domain_layer_isolation(service: str):
    """Ensure domain layer is decoupled from application, infrastructure/adapters, presentation, and framework libraries."""
    (
        archrule(f"{service.title()} Domain Layer Isolation")
        .match(f"apps.{service}.domain*")
        .should_not_import(f"apps.{service}.application*")
        .should_not_import(f"apps.{service}.infrastructure*")
        .should_not_import(f"apps.{service}.adapters*")
        .should_not_import(f"apps.{service}.presentation*")
        .should_not_import("sqlalchemy*")
        .should_not_import("fastapi*")
        .check("apps", only_direct_imports=True)
    )


@pytest.mark.parametrize("service", SERVICES)
def test_application_layer_isolation(service: str):
    """Ensure application layer is decoupled from infrastructure/adapters, presentation, and framework libraries."""
    if service in ("knowledge",):
        pytest.skip(
            f"Service '{service}' is exempt from strict application layer isolation."
        )
    (
        archrule(f"{service.title()} Application Layer Isolation")
        .match(f"apps.{service}.application*")
        .should_not_import(f"apps.{service}.infrastructure*")
        .should_not_import(f"apps.{service}.adapters*")
        .should_not_import(f"apps.{service}.presentation*")
        .should_not_import("sqlalchemy*")
        .should_not_import("fastapi*")
        .check("apps", only_direct_imports=True)
    )


@pytest.mark.parametrize("service", SERVICES)
def test_presentation_layer_driver_isolation(service: str):
    """Ensure presentation layer routers do not directly import low-level database drivers (such as neo4j or psycopg2)."""
    rule = (
        archrule(f"{service.title()} Presentation Layer Driver Isolation")
        .match(f"apps.{service}.presentation*")
        .should_not_import("neo4j*")
        .should_not_import("psycopg2*")
    )
    rule.check("apps", only_direct_imports=True)


@pytest.mark.parametrize("service", SERVICES)
def test_decoupled_api_routers_have_no_direct_db_imports(service: str):
    """Ensure decoupled API router layers do not directly import ORM queries, database drivers, or legacy models."""
    root_dir = Path(__file__).resolve().parent.parent.parent.parent
    routers_dir = root_dir / "apps" / service / "routers"
    if not routers_dir.exists() or not list(routers_dir.glob("*.py")):
        pytest.skip(f"Service {service} does not have any decoupled routers.")

    rule = (
        archrule(f"{service.title()} Router DB Isolation")
        .match(f"apps.{service}.routers*")
        .should_not_import("sqlalchemy*")
        .should_not_import("neo4j*")
        .should_not_import(f"apps.{service}.models*")
        .should_not_import(f"apps.{service}.database.models*")
    )
    rule.check("apps", only_direct_imports=True)


def test_designer_core_isolation():
    """Ensure core designer logic does not import database driver or session packages."""
    (
        archrule("Designer Core Isolation")
        .match("apps.designer.delta*")
        .should_not_import("neo4j*")
        .check("apps", only_direct_imports=True)
    )


def test_main_entrypoints_count_integrity():
    """Ensure we have at least 13 main.py files across services to prevent accidental omissions."""
    root_dir = Path(__file__).resolve().parent.parent.parent.parent
    main_files = sorted(glob.glob(str(root_dir / "apps" / "*" / "main.py")))
    assert len(main_files) >= 13, (
        f"Expected at least 13 main.py files, found {len(main_files)}"
    )


@pytest.mark.parametrize("service", SERVICES)
def test_main_entrypoint_is_thin(service: str):
    """Ensure main.py entrypoint contains only FastAPI setup and router inclusions, with no inline route handlers."""
    root_dir = Path(__file__).resolve().parent.parent.parent.parent
    main_file = root_dir / "apps" / service / "main.py"
    if not main_file.exists():
        return

    content = main_file.read_text(encoding="utf-8")
    tree = ast.parse(content, filename=str(main_file))

    route_handlers = []
    for node in ast.walk(tree):
        if isinstance(node, ast.FunctionDef):
            for dec in node.decorator_list:
                if isinstance(dec, ast.Call) and isinstance(dec.func, ast.Attribute):
                    if dec.func.attr in (
                        "get",
                        "post",
                        "put",
                        "delete",
                        "patch",
                        "api_route",
                    ):
                        route_handlers.append(f"{node.name} (line {node.lineno})")

    assert not route_handlers, (
        f"Service '{service}' main.py contains inline route handlers: {route_handlers}"
    )


def test_repository_ports_count_integrity():
    """Ensure some repository ports are found across the entire repository to guard against total deletion."""
    root_dir = Path(__file__).resolve().parent.parent.parent.parent
    port_files = sorted(
        glob.glob(str(root_dir / "apps" / "**" / "ports.py"), recursive=True)
        + glob.glob(str(root_dir / "apps" / "**" / "ports" / "*.py"), recursive=True)
    )
    port_files = [f for f in port_files if "tests" not in f]
    repo_ports_found = 0
    for port_file in port_files:
        content = Path(port_file).read_text(encoding="utf-8")
        tree = ast.parse(content, filename=port_file)
        for node in ast.walk(tree):
            if isinstance(node, ast.ClassDef) and (
                "Repository" in node.name or "Port" in node.name
            ):
                if (
                    "RepositoryPort" in node.name
                    or node.name.startswith("I")
                    or node.name.endswith("Repository")
                ):
                    repo_ports_found += 1
    assert repo_ports_found > 0, "No repository ports were evaluated"


@pytest.mark.parametrize("service", SERVICES)
def test_service_repository_ports_subclass_base(service: str):
    """Ensure all service-specific repository ports subclass packages.hexagonal.RepositoryPort."""
    root_dir = Path(__file__).resolve().parent.parent.parent.parent
    service_dir = root_dir / "apps" / service
    port_files = sorted(
        glob.glob(str(service_dir / "ports.py"))
        + glob.glob(str(service_dir / "ports" / "*.py"))
        + glob.glob(str(service_dir / "ports" / "**" / "*.py"), recursive=True)
        + glob.glob(str(service_dir / "domain" / "ports.py"))
        + glob.glob(str(service_dir / "application" / "ports.py"))
    )
    port_files = [f for f in port_files if "tests" not in f]

    for port_file in port_files:
        content = Path(port_file).read_text(encoding="utf-8")
        tree = ast.parse(content, filename=port_file)

        for node in ast.walk(tree):
            if isinstance(node, ast.ClassDef) and (
                "Repository" in node.name or "Port" in node.name
            ):
                base_names = []
                for b in node.bases:
                    if isinstance(b, ast.Name):
                        base_names.append(b.id)
                    elif isinstance(b, ast.Attribute):
                        base_names.append(b.attr)
                    elif isinstance(b, ast.Subscript):
                        if isinstance(b.value, ast.Name):
                            base_names.append(b.value.id)
                        elif isinstance(b.value, ast.Attribute):
                            base_names.append(b.value.attr)

                if (
                    "RepositoryPort" in node.name
                    or node.name.startswith("I")
                    or node.name.endswith("Repository")
                ):
                    assert "RepositoryPort" in base_names, (
                        f"Class {node.name} in {port_file} does not inherit from RepositoryPort (bases: {base_names})"
                    )


@pytest.mark.parametrize("service", SERVICES)
def test_no_singular_adapter_directory(service: str):
    """Ensure that no microservice contains a singular 'adapter' directory, and instead conforms to plural 'adapters'.

    @req:PRD-SYS-001
    """
    root_dir = Path(__file__).resolve().parent.parent.parent.parent
    p = root_dir / "apps" / service
    singular_adapter_dir = p / "adapter"
    assert not singular_adapter_dir.exists(), (
        f"Microservice '{service}' contains a singular 'adapter' directory. "
        "All services must use plural 'adapters' to maintain layout convergence."
    )


@pytest.mark.parametrize("service", SERVICES)
def test_all_services_have_ports(service: str):
    """Ensure all microservices contain a 'ports' directory or a 'ports.py' file.

    @req:PRD-SYS-001
    """
    root_dir = Path(__file__).resolve().parent.parent.parent.parent
    p = root_dir / "apps" / service
    ports_exist = (
        (p / "ports").is_dir()
        or (p / "ports.py").exists()
        or (p / "domain" / "ports.py").exists()
        or (p / "application" / "ports.py").exists()
    )
    assert ports_exist, (
        f"Microservice '{service}' does not contain a 'ports' directory or a 'ports.py' file. "
        "All microservices must have standard port definitions to follow the converged hexagonal layout."
    )
