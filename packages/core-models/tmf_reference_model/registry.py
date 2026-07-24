"""
Thread-safe Registry for versioned DIA TMF Reference Model catalogs.
Allows registration of prior, active, or future taxonomy versions in-memory.
"""

import threading
from typing import Dict, List, Optional
from .models import TaxonomyCatalog
from .catalog_v3_2_0 import V3_2_0_CATALOG


class TaxonomyRegistry:
    """
    A thread-safe catalog registry for managing versioned DIA TMF taxonomy definitions.
    """
    def __init__(self) -> None:
        self._catalogs: Dict[str, TaxonomyCatalog] = {}
        self._active_version: Optional[str] = None
        self._lock = threading.Lock()

    def register(self, catalog: TaxonomyCatalog, set_active: bool = False) -> None:
        """
        Registers a new TaxonomyCatalog version in the catalog.
        Optionally sets it as the active default catalog.
        """
        with self._lock:
            self._catalogs[catalog.version] = catalog
            if set_active or self._active_version is None:
                self._active_version = catalog.version

    def get_catalog(self, version: str) -> TaxonomyCatalog:
        """
        Retrieves a registered TaxonomyCatalog by its explicit version name.
        """
        with self._lock:
            if version not in self._catalogs:
                raise KeyError(f"Taxonomy catalog version '{version}' not found in registry.")
            return self._catalogs[version]

    def get_active_catalog(self) -> TaxonomyCatalog:
        """
        Retrieves the current active default TaxonomyCatalog version.
        """
        with self._lock:
            if self._active_version is None:
                raise ValueError("No active taxonomy catalog version set in registry.")
            return self._catalogs[self._active_version]

    def set_active_version(self, version: str) -> None:
        """
        Updates the active catalog version in the registry to an already registered version.
        """
        with self._lock:
            if version not in self._catalogs:
                raise KeyError(f"Cannot set active version to '{version}' because it is not registered.")
            self._active_version = version

    def list_versions(self) -> List[str]:
        """
        Lists all registered taxonomy catalog versions in sorted order.
        """
        with self._lock:
            return sorted(list(self._catalogs.keys()))


# Instantiate the global thread-safe registry
registry = TaxonomyRegistry()

# Auto-register our baseline catalog v3.2.0 as active by default
registry.register(V3_2_0_CATALOG, set_active=True)


# Module-level convenience wrapper functions for consumers
def register_catalog(catalog: TaxonomyCatalog, set_active: bool = False) -> None:
    """
    Register a TaxonomyCatalog with the global registry.
    """
    registry.register(catalog, set_active=set_active)


def get_catalog(version: str) -> TaxonomyCatalog:
    """
    Retrieve a registered TaxonomyCatalog by its version tag.
    """
    return registry.get_catalog(version)


def get_active_catalog() -> TaxonomyCatalog:
    """
    Retrieve the active default TaxonomyCatalog.
    """
    return registry.get_active_catalog()


def list_catalog_versions() -> List[str]:
    """
    List all registered TaxonomyCatalog version tags.
    """
    return registry.list_versions()


def set_active_taxonomy_version(version: str) -> None:
    """
    Set the active catalog version.
    """
    registry.set_active_version(version)
