"""S3/MinIO Storage Adapter implementing StoragePort and BlobStorageProvider.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002
"""

import hashlib
import os
from typing import Any

import aioboto3
from botocore.config import Config
from botocore.exceptions import ClientError

from packages.storage.blob_store import (
    BlobStorageProvider,
    StorageIntegrityError,
    StorageObjectNotFoundError,
)
from packages.storage.ports.storage_port import StoragePort


class S3StorageAdapter(StoragePort[dict[str, Any]], BlobStorageProvider):
    """S3/MinIO storage adapter with inline SHA-256 integrity validation and SSE encryption.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    """

    def __init__(
        self,
        bucket_name: str | None = None,
        aws_access_key_id: str | None = None,
        aws_secret_access_key: str | None = None,
        region_name: str | None = None,
        endpoint_url: str | None = None,
        sse_algorithm: str | None = "AES256",
        config: Config | None = None,
    ) -> None:
        self.bucket_name = (
            bucket_name
            or os.getenv("STORAGE_BUCKET")
            or os.getenv("S3_BUCKET_NAME")
            or "cadence-fileshare"
        )
        self.aws_access_key_id = (
            aws_access_key_id
            or os.getenv("STORAGE_ACCESS_KEY")
            or os.getenv("AWS_ACCESS_KEY_ID")
        )
        self.aws_secret_access_key = (
            aws_secret_access_key
            or os.getenv("STORAGE_SECRET_KEY")
            or os.getenv("AWS_SECRET_ACCESS_KEY")
        )
        self.region_name = (
            region_name
            or os.getenv("STORAGE_REGION")
            or os.getenv("AWS_REGION")
            or "us-east-1"
        )
        self.endpoint_url = (
            endpoint_url
            or os.getenv("STORAGE_ENDPOINT_URL")
            or os.getenv("AWS_ENDPOINT_URL")
        )
        self.sse_algorithm = (
            sse_algorithm
            if sse_algorithm is not None
            else os.getenv("STORAGE_SSE_ALGORITHM", "AES256")
        )
        self.config = config
        self.session = aioboto3.Session(
            aws_access_key_id=self.aws_access_key_id,
            aws_secret_access_key=self.aws_secret_access_key,
            region_name=self.region_name,
        )

    def _get_client(self):
        """Get an aioboto3 S3 client context manager."""
        kwargs: dict[str, Any] = {"endpoint_url": self.endpoint_url}
        if self.config:
            kwargs["config"] = self.config
        return self.session.client("s3", **kwargs)

    async def put_object(
        self,
        key: str,
        data: bytes,
        content_type: str | None = None,
        metadata: dict[str, str] | None = None,
        expected_sha256: str | None = None,
    ) -> str:
        """Write binary blob to storage and return verified SHA-256 digest."""
        hasher = hashlib.sha256()
        chunk_size = 65536
        if not data:
            hasher.update(b"")
        else:
            for i in range(0, len(data), chunk_size):
                chunk = data[i : i + chunk_size]
                hasher.update(chunk)

        calculated_hash = hasher.hexdigest()
        if (
            expected_sha256 is not None
            and calculated_hash.lower() != expected_sha256.lower()
        ):
            raise StorageIntegrityError(
                f"Storage integrity verification failed! Expected {expected_sha256}, got {calculated_hash}"
            )

        tags = dict(metadata or {})
        tags["sha256"] = calculated_hash

        put_kwargs: dict[str, Any] = {
            "Bucket": self.bucket_name,
            "Key": key,
            "Body": data,
            "Metadata": tags,
        }
        if content_type:
            put_kwargs["ContentType"] = content_type
        if self.sse_algorithm:
            put_kwargs["ServerSideEncryption"] = self.sse_algorithm

        async with self._get_client() as client:
            await client.put_object(**put_kwargs)
        return calculated_hash

    async def get_object(self, key: str) -> tuple[bytes, str]:
        """Read binary blob from storage and return (content, sha256_hash)."""
        async with self._get_client() as client:
            try:
                response = await client.get_object(Bucket=self.bucket_name, Key=key)
            except ClientError as e:
                error_code = e.response.get("Error", {}).get("Code", "Unknown")
                if error_code in ("NoSuchKey", "404", "NoSuchBucket"):
                    raise StorageObjectNotFoundError(f"Object not found: {key}") from e
                raise

            metadata = response.get("Metadata", {})
            expected_sha256 = metadata.get("sha256")

            content_chunks = []
            hasher = hashlib.sha256()
            async with response["Body"] as stream:
                while True:
                    chunk = await stream.read(65536)
                    if not chunk:
                        break
                    content_chunks.append(chunk)
                    hasher.update(chunk)

            content = b"".join(content_chunks)
            calculated_hash = hasher.hexdigest()

            if (
                expected_sha256 is not None
                and calculated_hash.lower() != expected_sha256.lower()
            ):
                raise StorageIntegrityError(
                    f"Storage integrity verification failed! Expected {expected_sha256}, got {calculated_hash}"
                )

            return content, calculated_hash

    async def exists(self, key: str) -> bool:
        """Check if an object exists in storage."""
        async with self._get_client() as client:
            try:
                await client.head_object(Bucket=self.bucket_name, Key=key)
                return True
            except ClientError as e:
                error_code = e.response.get("Error", {}).get("Code", "Unknown")
                if error_code in ("NoSuchKey", "404", "NoSuchBucket"):
                    return False
                raise

    async def delete_object(self, key: str) -> None:
        """Delete an object from storage."""
        async with self._get_client() as client:
            try:
                await client.head_object(Bucket=self.bucket_name, Key=key)
            except ClientError as e:
                error_code = e.response.get("Error", {}).get("Code", "Unknown")
                if error_code in ("NoSuchKey", "404", "NoSuchBucket"):
                    raise StorageObjectNotFoundError(f"Object not found: {key}") from e
                raise

            await client.delete_object(Bucket=self.bucket_name, Key=key)

    async def generate_presigned_get_url(
        self,
        key: str,
        expires_in: int = 3600,
        response_content_disposition: str | None = None,
    ) -> str:
        """Generate a short-lived presigned GET URL for client-side download."""
        params: dict[str, Any] = {
            "Bucket": self.bucket_name,
            "Key": key,
        }
        if response_content_disposition:
            params["ResponseContentDisposition"] = response_content_disposition

        async with self._get_client() as client:
            return await client.generate_presigned_url(
                ClientMethod="get_object",
                Params=params,
                ExpiresIn=expires_in,
            )

    async def generate_presigned_put_url(
        self,
        key: str,
        expires_in: int = 3600,
        content_type: str | None = None,
    ) -> str:
        """Generate a short-lived presigned PUT URL for direct client upload."""
        params: dict[str, Any] = {
            "Bucket": self.bucket_name,
            "Key": key,
        }
        if content_type:
            params["ContentType"] = content_type

        async with self._get_client() as client:
            return await client.generate_presigned_url(
                ClientMethod="put_object",
                Params=params,
                ExpiresIn=expires_in,
            )

    async def create_multipart_upload(
        self,
        key: str,
        content_type: str | None = None,
        metadata: dict[str, str] | None = None,
    ) -> str:
        """Initiate a multipart upload session."""
        params: dict[str, Any] = {
            "Bucket": self.bucket_name,
            "Key": key,
        }
        if content_type:
            params["ContentType"] = content_type
        if metadata:
            params["Metadata"] = metadata
        if self.sse_algorithm:
            params["ServerSideEncryption"] = self.sse_algorithm

        async with self._get_client() as client:
            response = await client.create_multipart_upload(**params)
            return response["UploadId"]

    async def generate_presigned_multipart_urls(
        self,
        key: str,
        upload_id: str,
        part_numbers: list[int],
        expires_in: int = 3600,
    ) -> dict[int, str]:
        """Generate presigned part URLs for multipart upload."""
        urls: dict[int, str] = {}
        async with self._get_client() as client:
            for part_num in part_numbers:
                url = await client.generate_presigned_url(
                    ClientMethod="upload_part",
                    Params={
                        "Bucket": self.bucket_name,
                        "Key": key,
                        "UploadId": upload_id,
                        "PartNumber": part_num,
                    },
                    ExpiresIn=expires_in,
                )
                urls[part_num] = url
        return urls

    async def complete_multipart_upload(
        self,
        key: str,
        upload_id: str,
        parts: list[dict[str, Any]],
    ) -> str:
        """Complete multipart upload by assembling uploaded parts."""
        # S3 requires parts to be strictly ordered by PartNumber
        sorted_parts = sorted(parts, key=lambda p: p["PartNumber"])
        async with self._get_client() as client:
            response = await client.complete_multipart_upload(
                Bucket=self.bucket_name,
                Key=key,
                UploadId=upload_id,
                MultipartUpload={"Parts": sorted_parts},
            )
            return response.get("ETag", key)

    async def abort_multipart_upload(self, key: str, upload_id: str) -> None:
        """Abort an in-progress multipart upload."""
        async with self._get_client() as client:
            await client.abort_multipart_upload(
                Bucket=self.bucket_name,
                Key=key,
                UploadId=upload_id,
            )
