import os

from packages.storage.blob_store import (
    BlobStorageProvider,
    StorageIntegrityError,
    StorageObjectNotFoundError,
    validate_key,
    verify_checksum,
)
from packages.storage.local_store import LocalStorageProvider
from packages.storage.s3_store import S3StorageProvider

_provider_instance: BlobStorageProvider | None = None


def get_storage_provider() -> BlobStorageProvider:
    """Resolve and return the correct storage provider dynamically using an environment toggle.

    Toggles between LocalStorageProvider and S3StorageProvider.
    """
    global _provider_instance
    if _provider_instance is not None:
        return _provider_instance

    provider_type = os.getenv("STORAGE_PROVIDER", "local").lower().strip()
    if provider_type in ("s3", "cloud"):
        bucket_name = (
            os.getenv("S3_BUCKET_NAME")
            or os.getenv("AWS_STORAGE_BUCKET_NAME")
            or "cadence-documents"
        )
        aws_access_key_id = os.getenv("AWS_ACCESS_KEY_ID")
        aws_secret_access_key = os.getenv("AWS_SECRET_ACCESS_KEY")
        region_name = (
            os.getenv("AWS_DEFAULT_REGION") or os.getenv("AWS_REGION") or "us-east-1"
        )
        endpoint_url = os.getenv("S3_ENDPOINT_URL") or os.getenv("AWS_S3_ENDPOINT_URL")

        _provider_instance = S3StorageProvider(
            bucket_name=bucket_name,
            aws_access_key_id=aws_access_key_id,
            aws_secret_access_key=aws_secret_access_key,
            region_name=region_name,
            endpoint_url=endpoint_url,
        )
    else:
        # Default to local
        base_dir = (
            os.getenv("LOCAL_STORAGE_DIR")
            or os.getenv("LOCAL_STORE_DIR")
            or "/tmp/local_storage"
        )
        _provider_instance = LocalStorageProvider(base_dir=base_dir)

    return _provider_instance


def reset_storage_provider() -> None:
    """Reset the cached storage provider instance. Useful for testing."""
    global _provider_instance
    _provider_instance = None


__all__ = [
    "BlobStorageProvider",
    "LocalStorageProvider",
    "S3StorageProvider",
    "StorageIntegrityError",
    "StorageObjectNotFoundError",
    "validate_key",
    "verify_checksum",
    "get_storage_provider",
    "reset_storage_provider",
]
