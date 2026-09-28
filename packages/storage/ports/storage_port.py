"""StoragePort port abstraction for GxP-compliant binary and object storage operations.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002
"""

import abc
from typing import Any


class StoragePort[T](abc.ABC):
    """Abstract port for binary object storage and presigned access lifecycle.

    Native PEP 695 generic class parameter syntax.
    """

    @abc.abstractmethod
    async def put_object(
        self,
        key: str,
        data: bytes,
        content_type: str | None = None,
        metadata: dict[str, str] | None = None,
        expected_sha256: str | None = None,
    ) -> str:
        """Store a binary payload in the object store with SHA-256 validation.

        Args:
            key: Object storage key / path.
            data: Binary payload bytes.
            content_type: MIME content type (e.g. 'application/pdf').
            metadata: Custom key-value metadata tags.
            expected_sha256: Optional precalculated SHA-256 hash to verify against.

        Returns:
            Calculated SHA-256 hex digest string.

        Raises:
            StorageIntegrityError: If expected_sha256 fails validation.
        """
        ...

    @abc.abstractmethod
    async def get_object(self, key: str) -> tuple[bytes, str]:
        """Retrieve binary payload and its SHA-256 digest from the object store.

        Args:
            key: Object storage key / path.

        Returns:
            Tuple of (payload_bytes, sha256_hex_digest).

        Raises:
            StorageObjectNotFoundError: If the key does not exist.
            StorageIntegrityError: If the stored SHA-256 metadata doesn't match content.
        """
        ...

    @abc.abstractmethod
    async def exists(self, key: str) -> bool:
        """Check whether an object exists at the given key.

        Args:
            key: Object storage key / path.

        Returns:
            True if object exists, False otherwise.
        """
        ...

    @abc.abstractmethod
    async def delete_object(self, key: str) -> None:
        """Delete an object from the object store.

        Args:
            key: Object storage key / path.

        Raises:
            StorageObjectNotFoundError: If the key does not exist.
        """
        ...

    @abc.abstractmethod
    async def generate_presigned_get_url(
        self,
        key: str,
        expires_in: int = 3600,
        response_content_disposition: str | None = None,
    ) -> str:
        """Generate a short-lived presigned GET URL for client-side download or protected view.

        Args:
            key: Object storage key / path.
            expires_in: URL time-to-live in seconds (default 3600).
            response_content_disposition: Optional Content-Disposition header override.

        Returns:
            Presigned GET URL string.
        """
        ...

    @abc.abstractmethod
    async def generate_presigned_put_url(
        self,
        key: str,
        expires_in: int = 3600,
        content_type: str | None = None,
    ) -> str:
        """Generate a short-lived presigned PUT URL for direct client-side single-part upload.

        Args:
            key: Object storage key / path.
            expires_in: URL time-to-live in seconds (default 3600).
            content_type: MIME content type restriction.

        Returns:
            Presigned PUT URL string.
        """
        ...

    @abc.abstractmethod
    async def create_multipart_upload(
        self,
        key: str,
        content_type: str | None = None,
        metadata: dict[str, str] | None = None,
    ) -> str:
        """Initiate an S3-compatible multipart upload and return the upload ID.

        Args:
            key: Object storage key / path.
            content_type: MIME content type.
            metadata: Custom key-value metadata tags.

        Returns:
            Upload ID string.
        """
        ...

    @abc.abstractmethod
    async def generate_presigned_multipart_urls(
        self,
        key: str,
        upload_id: str,
        part_numbers: list[int],
        expires_in: int = 3600,
    ) -> dict[int, str]:
        """Generate presigned upload part URLs for a multipart upload session.

        Args:
            key: Object storage key / path.
            upload_id: Active multipart upload ID.
            part_numbers: List of 1-indexed part numbers to generate URLs for.
            expires_in: URL time-to-live in seconds (default 3600).

        Returns:
            Dictionary mapping part_number to presigned URL string.
        """
        ...

    @abc.abstractmethod
    async def complete_multipart_upload(
        self,
        key: str,
        upload_id: str,
        parts: list[dict[str, Any]],
    ) -> str:
        """Complete a multipart upload session after all parts are uploaded.

        Args:
            key: Object storage key / path.
            upload_id: Active multipart upload ID.
            parts: List of completed part dicts, each with 'PartNumber' (int) and 'ETag' (str).

        Returns:
            Final S3 object ETag or key.
        """
        ...

    @abc.abstractmethod
    async def abort_multipart_upload(self, key: str, upload_id: str) -> None:
        """Abort an active multipart upload and clean up uploaded chunks.

        Args:
            key: Object storage key / path.
            upload_id: Active multipart upload ID.
        """
        ...
