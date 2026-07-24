"""
Canonical, versioned DIA TMF Reference Model catalog.
Shared source of truth for TMF Zones, Sections, Artifacts, and their valid hierarchy.
"""

from .models import Artifact, Catalog, Section, Zone
from .registry import CatalogRegistry, registry


# Helper functions to fetch active catalog definitions directly
def get_catalog(version: str) -> Catalog:
    """
    Retrieve a registered catalog by version string.
    """
    return registry.get_catalog(version)


def get_active_catalog() -> Catalog:
    """
    Retrieve the active default catalog version.
    """
    return registry.get_active_catalog()


def get_active_version() -> str:
    """
    Retrieve the active catalog version identifier.
    """
    return registry.get_active_version()


def list_versions() -> list[str]:
    """
    List all registered catalog versions.
    """
    return registry.list_versions()


__all__ = [
    "Zone",
    "Section",
    "Artifact",
    "Catalog",
    "registry",
    "CatalogRegistry",
    "get_catalog",
    "get_active_catalog",
    "get_active_version",
    "list_versions",
]
