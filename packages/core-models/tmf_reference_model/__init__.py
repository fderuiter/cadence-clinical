"""
The shared packages.core-models.tmf_reference_model package.
Exposes the canonical, versioned DIA TMF Reference Model catalog.
"""

from .models import Zone, Section, Artifact, TaxonomyCatalog
from .registry import (
    get_catalog,
    get_active_catalog,
    list_catalog_versions,
    register_catalog,
    set_active_taxonomy_version,
)

__all__ = [
    "Zone",
    "Section",
    "Artifact",
    "TaxonomyCatalog",
    "get_catalog",
    "get_active_catalog",
    "list_catalog_versions",
    "register_catalog",
    "set_active_taxonomy_version",
]
