"""Unit and integration test suite for StoragePort, S3StorageAdapter, and MinioStorageAdapter.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002
"""

import hashlib
import os
from unittest.mock import AsyncMock, patch

import pytest
from botocore.exceptions import ClientError

from packages.storage.adapters.minio_adapter import MinioStorageAdapter
from packages.storage.adapters.s3_adapter import S3StorageAdapter
from packages.storage.blob_store import (
    StorageIntegrityError,
    StorageObjectNotFoundError,
)


class MockStreamingBody:
    """Mock streaming body for async S3 get_object response streaming.

    @req:PRD-SYS-001
    """

    def __init__(self, data: bytes) -> None:
        self.data = data
        self.offset = 0

    async def read(self, amt: int = -1) -> bytes:
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


@pytest.mark.asyncio
async def test_s3_storage_adapter_put_and_get_lifecycle():
    """Verify S3StorageAdapter full put, get, exists, and delete lifecycle with SHA-256 verification.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    @req:PRD-DOC-003
    """
    data = b"GxP regulated binary content for clinical trial"
    expected_hash = hashlib.sha256(data).hexdigest()
    key = "tenant-1/study-101/protocol.pdf"

    mock_client = AsyncMock()
    mock_client.put_object.return_value = {}
    mock_client.get_object.return_value = {
        "Body": MockStreamingBody(data),
        "Metadata": {"sha256": expected_hash},
    }
    mock_client.head_object.return_value = {}
    mock_client.delete_object.return_value = {}

    adapter = S3StorageAdapter(
        bucket_name="test-bucket",
        aws_access_key_id="test-key",
        aws_secret_access_key="test-secret",
        region_name="us-east-1",
        endpoint_url="http://mock-s3:9000",
    )

    with patch.object(adapter, "_get_client") as mock_get_client:
        mock_ctx = AsyncMock()
        mock_ctx.__aenter__.return_value = mock_client
        mock_ctx.__aexit__.return_value = None
        mock_get_client.return_value = mock_ctx

        # 1. Put Object
        calc_hash = await adapter.put_object(
            key=key,
            data=data,
            content_type="application/pdf",
            metadata={"study_id": "study-101"},
            expected_sha256=expected_hash,
        )
        assert calc_hash == expected_hash
        mock_client.put_object.assert_called_once()
        put_kwargs = mock_client.put_object.call_args[1]
        assert put_kwargs["Bucket"] == "test-bucket"
        assert put_kwargs["Key"] == key
        assert put_kwargs["ContentType"] == "application/pdf"
        assert put_kwargs["Metadata"]["sha256"] == expected_hash
        assert put_kwargs["Metadata"]["study_id"] == "study-101"

        # 2. Exists
        assert await adapter.exists(key) is True

        # 3. Get Object
        content, ret_hash = await adapter.get_object(key)
        assert content == data
        assert ret_hash == expected_hash

        # 4. Delete Object
        await adapter.delete_object(key)
        mock_client.delete_object.assert_called_once_with(Bucket="test-bucket", Key=key)


@pytest.mark.asyncio
async def test_s3_storage_adapter_integrity_failure():
    """Verify SHA-256 mismatch raises StorageIntegrityError on put and get.

    @req:PRD-SYS-001
    @req:PRD-DOC-002
    """
    data = b"Tamper-evident clinical data"
    bad_hash = "0000000000000000000000000000000000000000000000000000000000000000"

    adapter = S3StorageAdapter(bucket_name="test-bucket")

    # Put failure
    with pytest.raises(StorageIntegrityError) as exc_info:
        await adapter.put_object(key="test.bin", data=data, expected_sha256=bad_hash)
    assert "Storage integrity verification failed" in str(exc_info.value)

    # Get failure (mismatched metadata)
    mock_client = AsyncMock()
    mock_client.get_object.return_value = {
        "Body": MockStreamingBody(data),
        "Metadata": {"sha256": bad_hash},
    }

    with patch.object(adapter, "_get_client") as mock_get_client:
        mock_ctx = AsyncMock()
        mock_ctx.__aenter__.return_value = mock_client
        mock_ctx.__aexit__.return_value = None
        mock_get_client.return_value = mock_ctx

        with pytest.raises(StorageIntegrityError) as exc_info:
            await adapter.get_object("test.bin")
        assert "Storage integrity verification failed" in str(exc_info.value)


@pytest.mark.asyncio
async def test_s3_storage_adapter_not_found_handling():
    """Verify 404/NoSuchKey mapped to StorageObjectNotFoundError or False.

    @req:PRD-SYS-001
    """
    mock_client = AsyncMock()
    error_response = {
        "Error": {"Code": "NoSuchKey", "Message": "The specified key does not exist."}
    }
    client_err = ClientError(error_response, "get_object")
    mock_client.get_object.side_effect = client_err
    mock_client.head_object.side_effect = client_err

    adapter = S3StorageAdapter(bucket_name="test-bucket")

    with patch.object(adapter, "_get_client") as mock_get_client:
        mock_ctx = AsyncMock()
        mock_ctx.__aenter__.return_value = mock_client
        mock_ctx.__aexit__.return_value = None
        mock_get_client.return_value = mock_ctx

        with pytest.raises(StorageObjectNotFoundError):
            await adapter.get_object("missing.pdf")

        assert await adapter.exists("missing.pdf") is False

        with pytest.raises(StorageObjectNotFoundError):
            await adapter.delete_object("missing.pdf")


@pytest.mark.asyncio
async def test_s3_storage_adapter_presigned_urls():
    """Verify presigned GET and PUT URL generation.

    @req:PRD-DOC-001
    """
    mock_client = AsyncMock()
    mock_client.generate_presigned_url.return_value = (
        "https://s3.amazonaws.com/test-bucket/doc.pdf?signature=123"
    )

    adapter = S3StorageAdapter(bucket_name="test-bucket")

    with patch.object(adapter, "_get_client") as mock_get_client:
        mock_ctx = AsyncMock()
        mock_ctx.__aenter__.return_value = mock_client
        mock_ctx.__aexit__.return_value = None
        mock_get_client.return_value = mock_ctx

        # GET URL
        get_url = await adapter.generate_presigned_get_url(
            key="doc.pdf",
            expires_in=1800,
            response_content_disposition='attachment; filename="doc.pdf"',
        )
        assert "doc.pdf" in get_url
        mock_client.generate_presigned_url.assert_called_with(
            ClientMethod="get_object",
            Params={
                "Bucket": "test-bucket",
                "Key": "doc.pdf",
                "ResponseContentDisposition": 'attachment; filename="doc.pdf"',
            },
            ExpiresIn=1800,
        )

        # PUT URL
        put_url = await adapter.generate_presigned_put_url(
            key="upload.pdf",
            expires_in=900,
            content_type="application/pdf",
        )
        assert "signature" in put_url
        mock_client.generate_presigned_url.assert_called_with(
            ClientMethod="put_object",
            Params={
                "Bucket": "test-bucket",
                "Key": "upload.pdf",
                "ContentType": "application/pdf",
            },
            ExpiresIn=900,
        )


@pytest.mark.asyncio
async def test_s3_storage_adapter_multipart_lifecycle():
    """Verify multipart upload initiation, chunk presigning, completion, and abort.

    @req:PRD-DOC-001
    @req:PRD-SYS-001
    """
    mock_client = AsyncMock()
    mock_client.create_multipart_upload.return_value = {
        "UploadId": "upload-session-xyz"
    }
    mock_client.generate_presigned_url.side_effect = lambda *args, **kwargs: (
        f"https://mock-s3/part-{kwargs['Params']['PartNumber']}"
    )
    mock_client.complete_multipart_upload.return_value = {"ETag": '"complete-etag-123"'}
    mock_client.abort_multipart_upload.return_value = {}

    adapter = S3StorageAdapter(bucket_name="test-bucket")

    with patch.object(adapter, "_get_client") as mock_get_client:
        mock_ctx = AsyncMock()
        mock_ctx.__aenter__.return_value = mock_client
        mock_ctx.__aexit__.return_value = None
        mock_get_client.return_value = mock_ctx

        # 1. Create multipart upload
        upload_id = await adapter.create_multipart_upload(
            key="large-video.mp4",
            content_type="video/mp4",
            metadata={"source": "fileshare"},
        )
        assert upload_id == "upload-session-xyz"
        mock_client.create_multipart_upload.assert_called_once()

        # 2. Generate presigned part URLs
        part_urls = await adapter.generate_presigned_multipart_urls(
            key="large-video.mp4",
            upload_id=upload_id,
            part_numbers=[1, 2, 3],
            expires_in=3600,
        )
        assert len(part_urls) == 3
        assert part_urls[1] == "https://mock-s3/part-1"
        assert part_urls[2] == "https://mock-s3/part-2"
        assert part_urls[3] == "https://mock-s3/part-3"

        # 3. Complete multipart upload
        parts = [
            {"PartNumber": 2, "ETag": "etag-2"},
            {"PartNumber": 1, "ETag": "etag-1"},
            {"PartNumber": 3, "ETag": "etag-3"},
        ]
        etag = await adapter.complete_multipart_upload(
            key="large-video.mp4",
            upload_id=upload_id,
            parts=parts,
        )
        assert etag == '"complete-etag-123"'
        # Verify parts were sorted by PartNumber
        comp_call = mock_client.complete_multipart_upload.call_args[1]
        assert comp_call["MultipartUpload"]["Parts"][0]["PartNumber"] == 1
        assert comp_call["MultipartUpload"]["Parts"][1]["PartNumber"] == 2
        assert comp_call["MultipartUpload"]["Parts"][2]["PartNumber"] == 3

        # 4. Abort multipart upload
        await adapter.abort_multipart_upload(key="large-video.mp4", upload_id=upload_id)
        mock_client.abort_multipart_upload.assert_called_once_with(
            Bucket="test-bucket",
            Key="large-video.mp4",
            UploadId=upload_id,
        )


def test_minio_storage_adapter_env_and_config():
    """Verify MinioStorageAdapter defaults and path-style configuration.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    """
    with patch.dict(
        os.environ,
        {
            "MINIO_ENDPOINT_URL": "http://minio:9000",
            "MINIO_BUCKET": "cadence-test-files",
            "MINIO_ROOT_USER": "minio_user",
            "MINIO_ROOT_PASSWORD": "minio_secret_password",
        },
    ):
        adapter = MinioStorageAdapter()
        assert adapter.endpoint_url == "http://minio:9000"
        assert adapter.bucket_name == "cadence-test-files"
        assert adapter.aws_access_key_id == "minio_user"
        assert adapter.aws_secret_access_key == "minio_secret_password"
        assert adapter.config.s3["addressing_style"] == "path"


@pytest.mark.asyncio
async def test_s3_storage_adapter_empty_data_and_generic_errors():
    """Verify empty payload put_object and unexpected ClientError propagation.

    @req:PRD-SYS-001
    """
    mock_client = AsyncMock()
    mock_client.put_object.return_value = {}
    adapter = S3StorageAdapter(bucket_name="test-bucket")

    with patch.object(adapter, "_get_client") as mock_get_client:
        mock_ctx = AsyncMock()
        mock_ctx.__aenter__.return_value = mock_client
        mock_ctx.__aexit__.return_value = None
        mock_get_client.return_value = mock_ctx

        # Empty data put
        empty_hash = hashlib.sha256(b"").hexdigest()
        calc = await adapter.put_object("empty.bin", b"")
        assert calc == empty_hash

        # Generic ClientError propagation on get
        generic_error = ClientError(
            {"Error": {"Code": "InternalError", "Message": "AWS Error"}}, "get_object"
        )
        mock_client.get_object.side_effect = generic_error
        with pytest.raises(ClientError):
            await adapter.get_object("error.bin")

        # Generic ClientError on exists
        mock_client.head_object.side_effect = generic_error
        with pytest.raises(ClientError):
            await adapter.exists("error.bin")

        # Generic ClientError on delete
        with pytest.raises(ClientError):
            await adapter.delete_object("error.bin")

        # NoSuchBucket error handling
        bucket_error = ClientError(
            {"Error": {"Code": "NoSuchBucket", "Message": "Bucket does not exist"}},
            "head_object",
        )
        mock_client.head_object.side_effect = bucket_error
        assert await adapter.exists("nobucket.bin") is False

        with pytest.raises(StorageObjectNotFoundError):
            await adapter.delete_object("nobucket.bin")
