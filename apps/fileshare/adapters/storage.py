"""Storage adapter factory and provider for the Fileshare microservice.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002
"""

from typing import Any

from packages.storage.adapters.s3_adapter import S3StorageAdapter
from packages.storage.ports.storage_port import StoragePort

_global_storage_adapter: StoragePort[dict[str, Any]] | None = None


def set_storage_adapter(adapter: StoragePort[dict[str, Any]]) -> None:
    """Explicitly set global storage adapter (used in test fixtures)."""
    global _global_storage_adapter
    _global_storage_adapter = adapter


def get_storage_adapter() -> StoragePort[dict[str, Any]]:
    """Resolve active StoragePort adapter for fileshare operations.

    Defaults to MinioStorageAdapter or S3StorageAdapter based on configuration.
    """
    global _global_storage_adapter
    if _global_storage_adapter is not None:
        return _global_storage_adapter

    return S3StorageAdapter()


__all__ = ["get_storage_adapter", "set_storage_adapter"]
