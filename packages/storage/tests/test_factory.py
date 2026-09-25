"""Unit tests for centralized storage factory and provider registry in packages/storage/factory.py.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002, ADR-2199
"""

import os
import threading
from unittest.mock import MagicMock, patch

from packages.storage.adapters.minio_adapter import MinioStorageAdapter
from packages.storage.adapters.s3_adapter import S3StorageAdapter
from packages.storage.factory import (
    create_storage_adapter,
    get_storage_adapter,
    reset_storage_adapter,
    set_storage_adapter,
)
from packages.testing.fakes import InMemoryStoragePort


def test_create_storage_adapter_fake():
    """Verify fake/memory provider resolution returns InMemoryStoragePort."""
    fake_adapter = create_storage_adapter(provider="fake")
    assert isinstance(fake_adapter, InMemoryStoragePort)

    mem_adapter = create_storage_adapter(provider="memory")
    assert isinstance(mem_adapter, InMemoryStoragePort)


def test_create_storage_adapter_explicit_minio():
    """Verify explicit provider='minio' returns MinioStorageAdapter."""
    adapter = create_storage_adapter(
        provider="minio",
        bucket_name="test-minio-bucket",
        endpoint_url="http://minio.local:9000",
    )
    assert isinstance(adapter, MinioStorageAdapter)
    assert adapter.bucket_name == "test-minio-bucket"
    assert adapter.endpoint_url == "http://minio.local:9000"


def test_create_storage_adapter_explicit_s3():
    """Verify explicit provider='s3' returns S3StorageAdapter."""
    adapter = create_storage_adapter(
        provider="s3",
        bucket_name="test-s3-bucket",
        endpoint_url="https://s3.us-west-2.amazonaws.com",
    )
    assert isinstance(adapter, S3StorageAdapter)
    assert not isinstance(adapter, MinioStorageAdapter)
    assert adapter.bucket_name == "test-s3-bucket"


def test_create_storage_adapter_endpoint_auto_detection():
    """Verify auto-detection of MinIO based on endpoint containing localhost or minio."""
    minio_adapter = create_storage_adapter(endpoint_url="http://localhost:9000")
    assert isinstance(minio_adapter, MinioStorageAdapter)

    s3_adapter = create_storage_adapter(endpoint_url="https://s3.amazonaws.com")
    assert isinstance(s3_adapter, S3StorageAdapter)
    assert not isinstance(s3_adapter, MinioStorageAdapter)


def test_create_storage_adapter_env_priority():
    """Verify priority: explicit args > settings > environment variables."""
    mock_settings = MagicMock()
    mock_settings.storage_provider = "s3"
    mock_settings.minio_endpoint_url = "http://settings-minio:9000"
    mock_settings.imaging_storage_bucket = "settings-bucket"

    with patch.dict(
        os.environ,
        {
            "STORAGE_PROVIDER": "minio",
            "MINIO_ENDPOINT_URL": "http://env-minio:9000",
            "STORAGE_BUCKET_NAME": "env-bucket",
        },
    ):
        # 1. Explicit argument overrides settings & env
        adapter1 = create_storage_adapter(
            provider="fake",
            settings=mock_settings,
        )
        assert isinstance(adapter1, InMemoryStoragePort)

        # 2. Settings override env vars when explicit arg is None
        adapter2 = create_storage_adapter(settings=mock_settings)
        assert isinstance(adapter2, S3StorageAdapter)
        assert adapter2.bucket_name == "settings-bucket"

        # 3. Environment variables used when explicit args and settings are None
        adapter3 = create_storage_adapter()
        assert isinstance(adapter3, MinioStorageAdapter)
        assert adapter3.bucket_name == "env-bucket"


def test_get_and_set_storage_adapter_registry():
    """Verify global registry getter, setter, and reset functions."""
    reset_storage_adapter()

    # Default adapter initialization
    fake_adapter = InMemoryStoragePort()
    set_storage_adapter(fake_adapter)

    retrieved = get_storage_adapter()
    assert retrieved is fake_adapter

    # Reset clears the registry
    reset_storage_adapter()
    # Mock create_storage_adapter for predictable return
    with patch("packages.storage.factory.create_storage_adapter") as mock_create:
        mock_create.return_value = fake_adapter
        adapter_a = get_storage_adapter()
        adapter_b = get_storage_adapter()
        assert adapter_a is fake_adapter
        assert adapter_b is fake_adapter
        mock_create.assert_called_once()


def test_get_storage_adapter_override_param():
    """Verify override parameter returns override without mutating global singleton."""
    reset_storage_adapter()

    default_fake = InMemoryStoragePort()
    override_fake = InMemoryStoragePort()

    set_storage_adapter(default_fake)

    res = get_storage_adapter(override=override_fake)
    assert res is override_fake
    # Active global singleton remains default_fake
    assert get_storage_adapter() is default_fake


def test_registry_thread_safety():
    """Verify thread-safety when accessing get_storage_adapter across multiple threads."""
    reset_storage_adapter()

    fake = InMemoryStoragePort()
    results = []

    def worker():
        adapter = get_storage_adapter(override=fake)
        results.append(adapter)

    threads = [threading.Thread(target=worker) for _ in range(10)]
    for t in threads:
        t.start()
    for t in threads:
        t.join()

    assert len(results) == 10
    assert all(res is fake for res in results)
