from pytest_archon import archrule


def test_ctms_domain_isolation():
    """Ensure CTMS domain layer is completely decoupled from business use-cases, concrete adapters, and third-party frameworks."""
    (
        archrule("CTMS Domain Layer Isolation")
        .match("apps.ctms.domain*")
        .should_not_import("apps.ctms.application*")
        .should_not_import("apps.ctms.adapter*")
        .should_not_import("sqlalchemy*")
        .should_not_import("fastapi*")
        .check("apps", only_direct_imports=True)
    )


def test_ctms_application_isolation():
    """Ensure CTMS application layer does not import concrete adapter implementations or web/framework libraries."""
    (
        archrule("CTMS Application Layer Isolation")
        .match("apps.ctms.application*")
        .should_not_import("apps.ctms.adapter*")
        .should_not_import("sqlalchemy*")
        .should_not_import("fastapi*")
        .check("apps", only_direct_imports=True)
    )


def test_execution_domain_isolation():
    """Ensure Execution domain layer is completely decoupled from application services, adapters, and framework libraries."""
    (
        archrule("Execution Domain Layer Isolation")
        .match("apps.execution.domain*")
        .should_not_import("apps.execution.application*")
        .should_not_import("apps.execution.adapter*")
        .should_not_import("sqlalchemy*")
        .should_not_import("fastapi*")
        .check("apps", only_direct_imports=True)
    )


def test_execution_application_isolation():
    """Ensure Execution application layer does not import concrete adapters or web/framework dependencies."""
    (
        archrule("Execution Application Layer Isolation")
        .match("apps.execution.application*")
        .should_not_import("apps.execution.adapter*")
        .should_not_import("sqlalchemy*")
        .should_not_import("fastapi*")
        .check("apps", only_direct_imports=True)
    )


def test_api_routers_have_no_direct_db_imports():
    """Ensure our refactored API router layers do not directly import sqlalchemy or database models."""
    # CTMS Doa router
    (
        archrule("CTMS DOA Router DB Isolation")
        .match("apps.ctms.routers.doa")
        .should_not_import("sqlalchemy*")
        .should_not_import("apps.ctms.models*")
        .check("apps", only_direct_imports=True)
    )
    # Execution Doa router
    (
        archrule("Execution DOA Router DB Isolation")
        .match("apps.execution.routers.doa")
        .should_not_import("sqlalchemy*")
        .should_not_import("apps.execution.database.models*")
        .check("apps", only_direct_imports=True)
    )


def test_designer_core_isolation():
    """Ensure core designer logic does not import database driver or session packages.

    This ensures that the pure domain layer delta operations remain completely decoupled
    from the neo4j graph database adapter.
    """
    (
        archrule("Designer Core Isolation")
        .match("apps.designer.delta*")
        .should_not_import("neo4j*")
        .check("apps", only_direct_imports=True)
    )
