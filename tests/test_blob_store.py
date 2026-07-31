import hashlib
import tempfile
from unittest.mock import AsyncMock, MagicMock, patch

import pytest
from botocore.exceptions import ClientError

from packages.storage.blob_store import (
    StorageIntegrityError,
    StorageObjectNotFoundError,
    verify_checksum,
)
from packages.storage.local_store import LocalStorageProvider
from packages.storage.s3_store import S3StorageProvider


class MockStreamingBody:
    """Mock StreamingBody for S3 get_object response streaming.

    Requirements: PRD-SYS-001
    """

    def __init__(self, data: bytes):
        self.data = data
        self.offset = 0

    async def read(self, amt: int = -1):
        if self.offset >= len(self.data):
            return b""
        if amt < 0:
            chunk = self.data[self.offset :]
            self.offset = len(self.data)
        else:
            chunk = self.data[self.offset : self.offset + amt]
            self.offset += amt
        return chunk

    async def __aenter__(self):
        return self

    async def __aexit__(self, exc_type, exc, tb):
        pass


def test_verify_checksum():
    """Verify verify_checksum utility behaves correctly.

    Requirements: PRD-SYS-001
    """
    data = b"gxp regulatory content"
    expected_sha256 = hashlib.sha256(data).hexdigest()

    # Should not raise
    verify_checksum(data, expected_sha256)

    # Should raise StorageIntegrityError
    with pytest.raises(StorageIntegrityError):
        verify_checksum(data, "invalid_hash")


@pytest.mark.asyncio
async def test_local_storage_provider_lifecycle():
    """Verify LocalStorageProvider full write, read, exists, delete lifecycle.

    Requirements: PRD-SYS-001
    """
    with tempfile.TemporaryDirectory() as tmp_dir:
        provider = LocalStorageProvider(tmp_dir)
        key = "study_docs/protocol_v1.pdf"
        data = b"gxp-controlled protocol data"
        expected_hash = hashlib.sha256(data).hexdigest()

        # Check doesn't exist
        assert not await provider.exists(key)

        # Write data
        calculated_hash = await provider.put_object(key, data, expected_hash)
        assert calculated_hash == expected_hash

        # Check exists
        assert await provider.exists(key)

        # Read data
        content, content_hash = await provider.get_object(key)
        assert content == data
        assert content_hash == expected_hash

        # Delete data
        await provider.delete_object(key)
        assert not await provider.exists(key)


@pytest.mark.asyncio
async def test_local_storage_provider_integrity_failure():
    """Verify LocalStorageProvider integrity verification raises StorageIntegrityError.

    Requirements: PRD-SYS-001
    """
    with tempfile.TemporaryDirectory() as tmp_dir:
        provider = LocalStorageProvider(tmp_dir)
        key = "corrupt.txt"
        data = b"unaltered text"
        incorrect_hash = hashlib.sha256(b"altered text").hexdigest()

        # Integrity failure on write
        with pytest.raises(StorageIntegrityError):
            await provider.put_object(key, data, incorrect_hash)

        # File should not exist because replace was never called
        assert not await provider.exists(key)


@pytest.mark.asyncio
async def test_local_storage_provider_not_found():
    """Verify LocalStorageProvider raises StorageObjectNotFoundError for missing files.

    Requirements: PRD-SYS-001
    """
    with tempfile.TemporaryDirectory() as tmp_dir:
        provider = LocalStorageProvider(tmp_dir)

        with pytest.raises(StorageObjectNotFoundError):
            await provider.get_object("nonexistent.txt")

        with pytest.raises(StorageObjectNotFoundError):
            await provider.delete_object("nonexistent.txt")


@pytest.mark.asyncio
async def test_local_storage_provider_traversal_prevention():
    """Verify LocalStorageProvider blocks directory traversal attacks.

    Requirements: PRD-SYS-001
    """
    with tempfile.TemporaryDirectory() as tmp_dir:
        provider = LocalStorageProvider(tmp_dir)

        with pytest.raises(ValueError, match="Path traversal attempt detected"):
            await provider.put_object("../malicious.txt", b"data")


@pytest.mark.asyncio
async def test_s3_storage_provider_lifecycle():
    """Verify S3StorageProvider full write, read, exists, delete lifecycle with correct headers.

    Requirements: PRD-SYS-001
    """
    key = "study_docs/protocol_v2.pdf"
    data = b"gxp protocol s3 content"
    expected_hash = hashlib.sha256(data).hexdigest()

    client_mock = MagicMock()
    client_mock.put_object = AsyncMock()
    client_mock.get_object = AsyncMock(
        return_value={
            "Metadata": {"sha256": expected_hash},
            "Body": MockStreamingBody(data),
        }
    )
    client_mock.head_object = AsyncMock()
    client_mock.delete_object = AsyncMock()

    client_context_mock = AsyncMock()
    client_context_mock.__aenter__.return_value = client_mock

    session_mock = MagicMock()
    session_mock.client.return_value = client_context_mock

    with patch("aioboto3.Session", return_value=session_mock):
        provider = S3StorageProvider(
            bucket_name="test-bucket",
            aws_access_key_id="test",
            aws_secret_access_key="test",
            region_name="us-east-1",
            endpoint_url="http://localhost:9000",
            sse_algorithm="AES256",
        )

        # 1. Put Object
        calculated_hash = await provider.put_object(key, data, expected_hash)
        assert calculated_hash == expected_hash

        # Verify aioboto3 put_object call parameters
        client_mock.put_object.assert_called_once_with(
            Bucket="test-bucket",
            Key=key,
            Body=data,
            Metadata={"sha256": expected_hash},
            ServerSideEncryption="AES256",
        )

        # 2. Exists
        assert await provider.exists(key)
        client_mock.head_object.assert_called_once_with(Bucket="test-bucket", Key=key)

        # 3. Get Object
        content, content_hash = await provider.get_object(key)
        assert content == data
        assert content_hash == expected_hash

        # 4. Delete Object
        await provider.delete_object(key)
        client_mock.delete_object.assert_called_once_with(Bucket="test-bucket", Key=key)


@pytest.mark.asyncio
async def test_s3_storage_provider_integrity_failure():
    """Verify S3StorageProvider raises StorageIntegrityError on mismatch.

    Requirements: PRD-SYS-001
    """
    key = "corrupt_s3.txt"
    data = b"pristine content"
    incorrect_hash = hashlib.sha256(b"corrupted").hexdigest()

    client_mock = MagicMock()
    client_mock.get_object = AsyncMock(
        return_value={
            "Metadata": {"sha256": incorrect_hash},
            "Body": MockStreamingBody(data),
        }
    )

    client_context_mock = AsyncMock()
    client_context_mock.__aenter__.return_value = client_mock

    session_mock = MagicMock()
    session_mock.client.return_value = client_context_mock

    with patch("aioboto3.Session", return_value=session_mock):
        provider = S3StorageProvider(bucket_name="test-bucket")

        # Failure on write (expected_sha256 doesn't match actual data)
        with pytest.raises(StorageIntegrityError):
            await provider.put_object(key, data, incorrect_hash)

        # Failure on read (metadata expected_sha256 doesn't match retrieved data)
        with pytest.raises(StorageIntegrityError):
            await provider.get_object(key)


@pytest.mark.asyncio
async def test_s3_storage_provider_not_found():
    """Verify S3StorageProvider translates NoSuchKey/404 errors to StorageObjectNotFoundError.

    Requirements: PRD-SYS-001
    """
    key = "missing_s3.txt"

    client_error = ClientError(
        error_response={"Error": {"Code": "NoSuchKey", "Message": "Missing object"}},
        operation_name="GetObject",
    )

    client_mock = MagicMock()
    client_mock.get_object.side_effect = client_error
    client_mock.head_object.side_effect = client_error

    client_context_mock = AsyncMock()
    client_context_mock.__aenter__.return_value = client_mock

    session_mock = MagicMock()
    session_mock.client.return_value = client_context_mock

    with patch("aioboto3.Session", return_value=session_mock):
        provider = S3StorageProvider(bucket_name="test-bucket")

        with pytest.raises(StorageObjectNotFoundError):
            await provider.get_object(key)

        with pytest.raises(StorageObjectNotFoundError):
            await provider.delete_object(key)

        # exists should return False
        assert not await provider.exists(key)
