"""Storage adapter and provider factory for Medical Imaging microservice.

Delegates storage adapter creation and global registry management to packages.storage.

Requirements: ADR-2199, PRD-IMG-001, PRD-IMG-002
"""

from typing import Any

from packages.storage import (
    get_storage_adapter as _shared_get_storage_adapter,
    reset_storage_adapter as _shared_reset_storage_adapter,
    set_storage_adapter as _shared_set_storage_adapter,
)
from packages.storage.ports.storage_port import StoragePort


def get_storage_port() -> StoragePort[dict[str, Any]]:
    """Resolves and returns the configured StoragePort implementation (delegated to packages.storage)."""
    return _shared_get_storage_adapter()


def set_storage_port(port: StoragePort[dict[str, Any]] | None) -> None:
    """Overrides the StoragePort singleton for isolated testing (delegated to packages.storage)."""
    _shared_set_storage_adapter(port)


def reset_storage_port() -> None:
    """Resets the StoragePort singleton (delegated to packages.storage)."""
    _shared_reset_storage_adapter()


get_storage_adapter = get_storage_port
set_storage_adapter = set_storage_port
reset_storage_adapter = reset_storage_port

__all__ = [
    "get_storage_adapter",
    "get_storage_port",
    "reset_storage_adapter",
    "reset_storage_port",
    "set_storage_adapter",
    "set_storage_port",
]
