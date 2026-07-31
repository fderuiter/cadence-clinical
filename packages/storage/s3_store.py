import hashlib
from typing import Optional, Tuple

import aioboto3
from botocore.exceptions import ClientError

from packages.storage.blob_store import (
    BlobStorageProvider,
    StorageIntegrityError,
    StorageObjectNotFoundError,
)


class S3StorageProvider(BlobStorageProvider):
    """S3/MinIO storage provider with inline SHA-256 integrity validation and SSE encryption.

    Requirements: PRD-SYS-001
    """

    def __init__(
        self,
        bucket_name: str,
        aws_access_key_id: Optional[str] = None,
        aws_secret_access_key: Optional[str] = None,
        region_name: Optional[str] = None,
        endpoint_url: Optional[str] = None,
        sse_algorithm: str = "AES256",
    ):
        self.bucket_name = bucket_name
        self.aws_access_key_id = aws_access_key_id
        self.aws_secret_access_key = aws_secret_access_key
        self.region_name = region_name
        self.endpoint_url = endpoint_url
        self.sse_algorithm = sse_algorithm
        self.session = aioboto3.Session(
            aws_access_key_id=self.aws_access_key_id,
            aws_secret_access_key=self.aws_secret_access_key,
            region_name=self.region_name,
        )

    def _get_client(self):
        """Get an aioboto3 S3 client context manager."""
        return self.session.client("s3", endpoint_url=self.endpoint_url)

    async def put_object(
        self, key: str, data: bytes, expected_sha256: Optional[str] = None
    ) -> str:
        """Write binary blob to storage and return verified SHA-256 digest.

        Requirements: PRD-SYS-001
        """
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

        async with self._get_client() as client:
            await client.put_object(
                Bucket=self.bucket_name,
                Key=key,
                Body=data,
                Metadata={"sha256": calculated_hash},
                ServerSideEncryption=self.sse_algorithm,
            )
        return calculated_hash

    async def get_object(self, key: str) -> Tuple[bytes, str]:
        """Read binary blob from storage and return (content, sha256_hash).

        Requirements: PRD-SYS-001
        """
        async with self._get_client() as client:
            try:
                response = await client.get_object(Bucket=self.bucket_name, Key=key)
            except ClientError as e:
                error_code = e.response.get("Error", {}).get("Code", "Unknown")
                if error_code in ("NoSuchKey", "404"):
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
                if error_code in ("NoSuchKey", "404"):
                    return False
                raise

    async def delete_object(self, key: str) -> None:
        """Delete an object from storage."""
        async with self._get_client() as client:
            try:
                await client.head_object(Bucket=self.bucket_name, Key=key)
            except ClientError as e:
                error_code = e.response.get("Error", {}).get("Code", "Unknown")
                if error_code in ("NoSuchKey", "404"):
                    raise StorageObjectNotFoundError(f"Object not found: {key}") from e
                raise

            await client.delete_object(Bucket=self.bucket_name, Key=key)
