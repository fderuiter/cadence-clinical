"""Storage adapter and provider factory for Medical Imaging microservice.

Requirements: ADR-2199, PRD-IMG-001, PRD-IMG-002
"""

from typing import Any

from packages.storage.adapters.minio_adapter import MinioStorageAdapter
from packages.storage.adapters.s3_adapter import S3StorageAdapter
from packages.storage.ports.storage_port import StoragePort

_storage_singleton: StoragePort[dict[str, Any]] | None = None


def get_storage_port() -> StoragePort[dict[str, Any]]:
    """Resolves and returns the configured StoragePort implementation."""
    global _storage_singleton
    if _storage_singleton is not None:
        return _storage_singleton

    from packages.config import get_imaging_settings

    settings = get_imaging_settings()
    bucket_name = settings.imaging_storage_bucket
    minio_url = settings.minio_endpoint_url or settings.storage_endpoint_url
    s3_endpoint = settings.aws_endpoint_url

    if minio_url:
        _storage_singleton = MinioStorageAdapter(
            bucket_name=bucket_name,
            endpoint_url=minio_url,
        )
    else:
        _storage_singleton = S3StorageAdapter(
            bucket_name=bucket_name,
            endpoint_url=s3_endpoint,
        )

    return _storage_singleton


def set_storage_port(port: StoragePort[dict[str, Any]] | None) -> None:
    """Overrides the StoragePort singleton for isolated testing."""
    global _storage_singleton
    _storage_singleton = port


get_storage_adapter = get_storage_port

__all__ = ["get_storage_adapter", "get_storage_port", "set_storage_port"]
