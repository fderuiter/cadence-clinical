"""Centralized storage adapter factory and provider registry.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002, ADR-2199
"""

import os
import threading
from typing import Any

from packages.storage.adapters.minio_adapter import MinioStorageAdapter
from packages.storage.adapters.s3_adapter import S3StorageAdapter
from packages.storage.ports.storage_port import StoragePort

_global_storage_adapter: StoragePort[dict[str, Any]] | None = None
_registry_lock = threading.Lock()


def create_storage_adapter(
    provider: str | None = None,
    bucket_name: str | None = None,
    endpoint_url: str | None = None,
    aws_access_key_id: str | None = None,
    aws_secret_access_key: str | None = None,
    region_name: str | None = None,
    settings: Any | None = None,
    **kwargs: Any,
) -> StoragePort[dict[str, Any]]:
    """Factory to create a StoragePort instance (MinioStorageAdapter, S3StorageAdapter, or InMemoryStoragePort).

    Priority for resolving configuration:
    1. Explicit function arguments (`provider`, `bucket_name`, `endpoint_url`, etc.)
    2. Service settings object (e.g. `settings` or kwargs)
    3. Environment variables (`STORAGE_PROVIDER`, `MINIO_ENDPOINT_URL`, `STORAGE_ENDPOINT_URL`, `AWS_ENDPOINT_URL`).
    """
    # 1. Resolve provider string
    resolved_provider = (
        provider
        or getattr(settings, "storage_provider", None)
        or os.getenv("STORAGE_PROVIDER")
    )
    if resolved_provider:
        resolved_provider = str(resolved_provider).lower().strip()

    # 2. Resolve endpoint URL
    resolved_endpoint = (
        endpoint_url
        or getattr(settings, "minio_endpoint_url", None)
        or getattr(settings, "storage_endpoint_url", None)
        or getattr(settings, "aws_endpoint_url", None)
        or os.getenv("MINIO_ENDPOINT_URL")
        or os.getenv("STORAGE_ENDPOINT_URL")
        or os.getenv("AWS_ENDPOINT_URL")
    )

    # 3. Resolve bucket name
    resolved_bucket = (
        bucket_name
        or getattr(settings, "imaging_storage_bucket", None)
        or getattr(settings, "fileshare_storage_bucket", None)
        or getattr(settings, "etmf_storage_bucket", None)
        or getattr(settings, "storage_bucket", None)
        or getattr(settings, "s3_bucket_name", None)
        or os.getenv("STORAGE_BUCKET_NAME")
        or os.getenv("MINIO_BUCKET")
        or os.getenv("S3_BUCKET_NAME")
    )

    # Handle fake/in-memory adapter
    if resolved_provider in ("fake", "memory", "in_memory"):
        from packages.testing.fakes import InMemoryStoragePort

        return InMemoryStoragePort()

    # Determine whether MinIO or standard S3 adapter should be instantiated
    use_minio = False
    if resolved_provider == "minio":
        use_minio = True
    elif resolved_provider == "s3":
        use_minio = False
    elif resolved_endpoint and ("localhost" in resolved_endpoint or "minio" in resolved_endpoint):
        use_minio = True

    adapter_kwargs: dict[str, Any] = {}
    if resolved_bucket:
        adapter_kwargs["bucket_name"] = resolved_bucket
    if resolved_endpoint:
        adapter_kwargs["endpoint_url"] = resolved_endpoint

    resolved_access_key = aws_access_key_id or getattr(settings, "aws_access_key_id", None) or getattr(settings, "storage_access_key", None)
    if resolved_access_key:
        adapter_kwargs["aws_access_key_id"] = resolved_access_key

    resolved_secret_key = aws_secret_access_key or getattr(settings, "aws_secret_access_key", None) or getattr(settings, "storage_secret_key", None)
    if resolved_secret_key:
        adapter_kwargs["aws_secret_access_key"] = resolved_secret_key

    resolved_region = region_name or getattr(settings, "aws_region", None) or getattr(settings, "storage_region", None)
    if resolved_region:
        adapter_kwargs["region_name"] = resolved_region

    for k, v in kwargs.items():
        if k not in adapter_kwargs:
            adapter_kwargs[k] = v

    if use_minio:
        return MinioStorageAdapter(**adapter_kwargs)
    return S3StorageAdapter(**adapter_kwargs)


def get_storage_adapter(
    override: StoragePort[dict[str, Any]] | None = None,
    **kwargs: Any,
) -> StoragePort[dict[str, Any]]:
    """Get active global StoragePort adapter, or create one if not yet initialized.

    If override is provided, returns override directly without mutating global singleton.
    """
    if override is not None:
        return override

    global _global_storage_adapter
    with _registry_lock:
        if _global_storage_adapter is None:
            _global_storage_adapter = create_storage_adapter(**kwargs)
        return _global_storage_adapter


def set_storage_adapter(adapter: StoragePort[dict[str, Any]] | None) -> None:
    """Set or override the global storage adapter singleton."""
    global _global_storage_adapter
    with _registry_lock:
        _global_storage_adapter = adapter


def reset_storage_adapter() -> None:
    """Reset the global storage adapter singleton to None."""
    global _global_storage_adapter
    with _registry_lock:
        _global_storage_adapter = None


__all__ = [
    "create_storage_adapter",
    "get_storage_adapter",
    "reset_storage_adapter",
    "set_storage_adapter",
]
