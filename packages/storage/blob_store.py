import abc
import hashlib
from typing import Optional, Tuple


class StorageIntegrityError(Exception):
    """Raised when SHA-256 checksum verification fails during blob write/read."""

    pass


class StorageObjectNotFoundError(Exception):
    """Raised when a requested storage object is not found."""

    pass


class BlobStorageProvider(abc.ABC):

    @abc.abstractmethod
    async def put_object(
        self, key: str, data: bytes, expected_sha256: Optional[str] = None
    ) -> str:
        """Write binary blob to storage and return verified SHA-256 digest.

        Requirements: PRD-SYS-001
        """
        pass

    @abc.abstractmethod
    async def get_object(self, key: str) -> Tuple[bytes, str]:
        """Read binary blob from storage and return (content, sha256_hash).

        Requirements: PRD-SYS-001
        """
        pass

    @abc.abstractmethod
    async def exists(self, key: str) -> bool:
        """Check if an object exists in storage."""
        pass

    @abc.abstractmethod
    async def delete_object(self, key: str) -> None:
        """Delete an object from storage."""
        pass


def verify_checksum(data: bytes, expected_sha256: str) -> None:
    actual = hashlib.sha256(data).hexdigest()
    if actual.lower() != expected_sha256.lower():
        raise StorageIntegrityError(
            f"Storage integrity verification failed! Expected {expected_sha256}, got {actual}"
        )
