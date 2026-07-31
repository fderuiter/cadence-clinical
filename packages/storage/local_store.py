import hashlib
import os
import tempfile
from pathlib import Path
from typing import Optional, Tuple

from packages.storage.blob_store import (
    BlobStorageProvider,
    StorageIntegrityError,
    StorageObjectNotFoundError,
)


class LocalStorageProvider(BlobStorageProvider):
    """Local filesystem storage provider with atomic writes and SHA-256 validation.

    Requirements: PRD-SYS-001
    """

    def __init__(self, base_dir: str | Path):
        self.base_dir = Path(base_dir).resolve()
        self.base_dir.mkdir(parents=True, exist_ok=True)

    def _get_path(self, key: str) -> Path:
        # Prevent relative directory traversal
        path = (self.base_dir / key).resolve()
        try:
            path.relative_to(self.base_dir)
        except ValueError:
            raise ValueError(f"Path traversal attempt detected: {key}")
        return path

    async def put_object(
        self, key: str, data: bytes, expected_sha256: Optional[str] = None
    ) -> str:
        """Write binary blob to storage and return verified SHA-256 digest.

        Requirements: PRD-SYS-001
        """
        target_path = self._get_path(key)
        target_path.parent.mkdir(parents=True, exist_ok=True)

        hasher = hashlib.sha256()
        fd, temp_path_str = tempfile.mkstemp(dir=target_path.parent)
        temp_path = Path(temp_path_str)

        try:
            with os.fdopen(fd, "wb") as f:
                chunk_size = 65536
                if not data:
                    f.write(b"")
                else:
                    for i in range(0, len(data), chunk_size):
                        chunk = data[i : i + chunk_size]
                        f.write(chunk)
                        hasher.update(chunk)

            calculated_hash = hasher.hexdigest()
            if (
                expected_sha256 is not None
                and calculated_hash.lower() != expected_sha256.lower()
            ):
                raise StorageIntegrityError(
                    f"Storage integrity verification failed! Expected {expected_sha256}, got {calculated_hash}"
                )

            # Atomic rename (guaranteed atomic on POSIX when on same filesystem)
            os.replace(temp_path, target_path)
            return calculated_hash
        except Exception:
            if temp_path.exists():
                try:
                    os.unlink(temp_path)
                except OSError:
                    pass
            raise

    async def get_object(self, key: str) -> Tuple[bytes, str]:
        """Read binary blob from storage and return (content, sha256_hash).

        Requirements: PRD-SYS-001
        """
        target_path = self._get_path(key)
        if not target_path.is_file():
            raise StorageObjectNotFoundError(f"Object not found: {key}")

        content_chunks = []
        hasher = hashlib.sha256()
        with open(target_path, "rb") as f:
            while True:
                chunk = f.read(65536)
                if not chunk:
                    break
                content_chunks.append(chunk)
                hasher.update(chunk)

        content = b"".join(content_chunks)
        calculated_hash = hasher.hexdigest()
        return content, calculated_hash

    async def exists(self, key: str) -> bool:
        """Check if an object exists in storage."""
        try:
            target_path = self._get_path(key)
            return target_path.is_file()
        except ValueError:
            return False

    async def delete_object(self, key: str) -> None:
        """Delete an object from storage."""
        target_path = self._get_path(key)
        if not target_path.is_file():
            raise StorageObjectNotFoundError(f"Object not found: {key}")
        try:
            os.unlink(target_path)
        except FileNotFoundError:
            raise StorageObjectNotFoundError(f"Object not found: {key}")
