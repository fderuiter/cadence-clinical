import os
import tempfile

import pytest

from packages.storage import (
    LocalStorageProvider,
    S3StorageProvider,
    StorageIntegrityError,
    get_storage_provider,
    reset_storage_provider,
)


# Async integration tests for dynamic storage providers
@pytest.mark.asyncio
async def test_default_storage_provider_is_local():
    """Verify default storage provider resolves to LocalStorageProvider.

    Requirements: PRD-SYS-001
    """
    # @req:PRD-SYS-001
    reset_storage_provider()
    # Save original env
    orig_provider = os.environ.get("STORAGE_PROVIDER")
    if "STORAGE_PROVIDER" in os.environ:
        del os.environ["STORAGE_PROVIDER"]

    try:
        provider = get_storage_provider()
        assert isinstance(provider, LocalStorageProvider)
    finally:
        if orig_provider is not None:
            os.environ["STORAGE_PROVIDER"] = orig_provider
        reset_storage_provider()


@pytest.mark.asyncio
async def test_s3_storage_provider_toggle():
    """Verify switching STORAGE_PROVIDER environment variable resolves to S3StorageProvider.

    Requirements: PRD-SYS-001
    """
    # @req:PRD-SYS-001
    reset_storage_provider()
    orig_provider = os.environ.get("STORAGE_PROVIDER")
    os.environ["STORAGE_PROVIDER"] = "s3"

    try:
        provider = get_storage_provider()
        assert isinstance(provider, S3StorageProvider)
        assert provider.bucket_name == "cadence-documents"
    finally:
        if orig_provider is not None:
            os.environ["STORAGE_PROVIDER"] = orig_provider
        else:
            del os.environ["STORAGE_PROVIDER"]
        reset_storage_provider()


@pytest.mark.asyncio
async def test_path_traversal_prevention_on_dynamic_providers():
    """Verify that both local and S3 providers automatically block directory traversal keys.

    Requirements: PRD-SYS-001
    """
    # @req:PRD-SYS-001
    reset_storage_provider()
    provider = get_storage_provider()

    with pytest.raises(ValueError, match="Path traversal attempt detected"):
        await provider.put_object("foo/../../bar.txt", b"malicious content")

    with pytest.raises(ValueError, match="Path traversal attempt detected"):
        await provider.get_object("../illegal.txt")

    # Now switch to S3 and verify the same blocks apply
    os.environ["STORAGE_PROVIDER"] = "s3"
    reset_storage_provider()
    s3_provider = get_storage_provider()

    with pytest.raises(ValueError, match="Path traversal attempt detected"):
        await s3_provider.put_object("foo/../../bar.txt", b"malicious content")

    with pytest.raises(ValueError, match="Path traversal attempt detected"):
        await s3_provider.get_object("../illegal.txt")

    # Cleanup
    del os.environ["STORAGE_PROVIDER"]
    reset_storage_provider()


@pytest.mark.asyncio
async def test_local_storage_atomic_write_integrity():
    """Verify atomic write and SHA-256 integrity checks fail on corrupted uploads.

    Requirements: PRD-SYS-001
    """
    # @req:PRD-SYS-001
    with tempfile.TemporaryDirectory() as tmp_dir:
        os.environ["STORAGE_PROVIDER"] = "local"
        os.environ["LOCAL_STORAGE_DIR"] = tmp_dir
        reset_storage_provider()

        provider = get_storage_provider()
        key = "valid_doc.pdf"
        data = b"GxP Clinical Protocol"
        wrong_hash = "wrong_hash_value"

        with pytest.raises(StorageIntegrityError):
            await provider.put_object(key, data, wrong_hash)

        # File should not exist because replace is atomic and only happens on successful validation
        assert not await provider.exists(key)

        # Cleanup
        del os.environ["STORAGE_PROVIDER"]
        del os.environ["LOCAL_STORAGE_DIR"]
        reset_storage_provider()
