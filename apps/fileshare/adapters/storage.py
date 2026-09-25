"""Storage adapter factory and provider for the Fileshare microservice.

Delegates storage adapter creation and global registry management to packages.storage.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002
"""

from typing import Any

from packages.storage import (
    get_storage_adapter as _shared_get_storage_adapter,
    reset_storage_adapter as _shared_reset_storage_adapter,
    set_storage_adapter as _shared_set_storage_adapter,
)
from packages.storage.ports.storage_port import StoragePort


def set_storage_adapter(adapter: StoragePort[dict[str, Any]] | None) -> None:
    """Explicitly set global storage adapter (delegated to packages.storage)."""
    _shared_set_storage_adapter(adapter)


def get_storage_adapter() -> StoragePort[dict[str, Any]]:
    """Resolve active StoragePort adapter for fileshare operations (delegated to packages.storage)."""
    return _shared_get_storage_adapter()


def reset_storage_adapter() -> None:
    """Reset active global storage adapter (delegated to packages.storage)."""
    _shared_reset_storage_adapter()


__all__ = ["get_storage_adapter", "reset_storage_adapter", "set_storage_adapter"]
