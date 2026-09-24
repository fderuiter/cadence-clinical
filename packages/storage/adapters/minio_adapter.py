"""MinIO Storage Adapter with path-style addressing for local development.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002
"""

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
        from packages.config import get_storage_settings

        settings = get_storage_settings()
        resolved_endpoint = (
            endpoint_url
            or settings.minio_endpoint_url
            or settings.storage_endpoint_url
            or "http://localhost:9000"
        )
        resolved_bucket = (
            bucket_name
            or settings.minio_bucket
            or settings.storage_bucket
            or "cadence-fileshare"
        )
        resolved_key = (
            aws_access_key_id
            or settings.minio_root_user
            or settings.storage_access_key
            or "minio_admin"
        )
        resolved_secret = (
            aws_secret_access_key
            or settings.minio_root_password
            or settings.storage_secret_key
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
