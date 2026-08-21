"""MinIO Storage Adapter with path-style addressing for local development.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002
"""

import os

from botocore.config import Config

from packages.storage.adapters.s3_adapter import S3StorageAdapter


class MinioStorageAdapter(S3StorageAdapter):
    """MinIO storage adapter customized for local Docker Compose and path-style addressing.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    """

    def __init__(
        self,
        bucket_name: str | None = None,
        aws_access_key_id: str | None = None,
        aws_secret_access_key: str | None = None,
        endpoint_url: str | None = None,
        region_name: str | None = "us-east-1",
        sse_algorithm: str | None = None,
    ) -> None:
        resolved_endpoint = (
            endpoint_url
            or os.getenv("MINIO_ENDPOINT_URL")
            or os.getenv("STORAGE_ENDPOINT_URL")
            or "http://localhost:9000"
        )
        resolved_bucket = (
            bucket_name
            or os.getenv("MINIO_BUCKET")
            or os.getenv("STORAGE_BUCKET")
            or "cadence-fileshare"
        )
        resolved_key = (
            aws_access_key_id
            or os.getenv("MINIO_ROOT_USER")
            or os.getenv("STORAGE_ACCESS_KEY")
            or "minio_admin"
        )
        resolved_secret = (
            aws_secret_access_key
            or os.getenv("MINIO_ROOT_PASSWORD")
            or os.getenv("STORAGE_SECRET_KEY")
            or "minio_password"
        )

        minio_config = Config(
            s3={"addressing_style": "path"},
            signature_version="s3v4",
        )

        super().__init__(
            bucket_name=resolved_bucket,
            aws_access_key_id=resolved_key,
            aws_secret_access_key=resolved_secret,
            region_name=region_name,
            endpoint_url=resolved_endpoint,
            sse_algorithm=sse_algorithm,
            config=minio_config,
        )
