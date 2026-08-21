"""High-performance in-memory repository and storage fakes implementing Hexagonal driven ports."""

import hashlib
import uuid
from typing import Any

from packages.hexagonal import RepositoryPort
from packages.storage.blob_store import (
    StorageIntegrityError,
    StorageObjectNotFoundError,
)
from packages.storage.ports.storage_port import StoragePort


class InMemoryRepository[T](RepositoryPort[T]):
    """Generic in-memory repository implementing RepositoryPort for ultra-fast unit tests."""

    def __init__(self, initial_entities: list[T] | None = None) -> None:
        self._store: dict[str, T] = {}
        if initial_entities:
            for entity in initial_entities:
                entity_id = getattr(entity, "id", None)
                if entity_id:
                    self._store[str(entity_id)] = entity

    async def get_by_id(self, entity_id: str) -> T | None:
        """Retrieve entity by string primary identifier."""
        return self._store.get(str(entity_id))

    async def save(self, entity: T) -> T:
        """Persist or update entity in the in-memory store."""
        entity_id = getattr(entity, "id", None)
        if not entity_id:
            raise ValueError("Entity must have an 'id' attribute to be stored.")
        self._store[str(entity_id)] = entity
        return entity

    async def delete(self, entity_id: str) -> bool:
        """Remove entity by identifier."""
        if str(entity_id) in self._store:
            del self._store[str(entity_id)]
            return True
        return False

    async def list_all(self) -> list[T]:
        """Returns all stored entities."""
        return list(self._store.values())

    def count(self) -> int:
        """Returns total entity count."""
        return len(self._store)

    def clear(self) -> None:
        """Empties the store."""
        self._store.clear()


class InMemoryStoragePort(StoragePort[dict[str, Any]]):
    """In-memory StoragePort fake for lightning-fast testing without AWS or MinIO dependencies."""

    def __init__(self) -> None:
        self._objects: dict[str, bytes] = {}
        self._metadata: dict[str, dict[str, str]] = {}
        self._multipart_sessions: dict[str, dict[str, Any]] = {}

    async def put_object(
        self,
        key: str,
        data: bytes,
        content_type: str | None = None,
        metadata: dict[str, str] | None = None,
        expected_sha256: str | None = None,
    ) -> str:
        calculated_hash = hashlib.sha256(data).hexdigest()
        if expected_sha256 and calculated_hash.lower() != expected_sha256.lower():
            raise StorageIntegrityError(
                f"Storage integrity verification failed! Expected {expected_sha256}, got {calculated_hash}"
            )
        self._objects[key] = data
        meta = dict(metadata or {})
        if content_type:
            meta["content_type"] = content_type
        meta["sha256"] = calculated_hash
        self._metadata[key] = meta
        return calculated_hash

    async def get_object(self, key: str) -> tuple[bytes, str]:
        if key not in self._objects:
            raise StorageObjectNotFoundError(f"Object not found: {key}")
        data = self._objects[key]
        sha = self._metadata.get(key, {}).get(
            "sha256", hashlib.sha256(data).hexdigest()
        )
        return data, sha

    async def exists(self, key: str) -> bool:
        return key in self._objects

    async def delete_object(self, key: str) -> None:
        if key not in self._objects:
            raise StorageObjectNotFoundError(f"Object not found: {key}")
        del self._objects[key]
        self._metadata.pop(key, None)

    async def generate_presigned_get_url(
        self,
        key: str,
        expires_in: int = 3600,
        response_content_disposition: str | None = None,
    ) -> str:
        disp_param = (
            f"&response-content-disposition={response_content_disposition}"
            if response_content_disposition
            else ""
        )
        return (
            f"https://mock-storage.local/get/{key}?expires_in={expires_in}{disp_param}"
        )

    async def generate_presigned_put_url(
        self,
        key: str,
        expires_in: int = 3600,
        content_type: str | None = None,
    ) -> str:
        ct_param = f"&content_type={content_type}" if content_type else ""
        return f"https://mock-storage.local/put/{key}?expires_in={expires_in}{ct_param}"

    async def create_multipart_upload(
        self,
        key: str,
        content_type: str | None = None,
        metadata: dict[str, str] | None = None,
    ) -> str:
        upload_id = f"mpu-{uuid.uuid4().hex[:12]}"
        self._multipart_sessions[upload_id] = {
            "key": key,
            "content_type": content_type,
            "metadata": metadata or {},
            "parts": {},
        }
        return upload_id

    async def generate_presigned_multipart_urls(
        self,
        key: str,
        upload_id: str,
        part_numbers: list[int],
        expires_in: int = 3600,
    ) -> dict[int, str]:
        return {
            p: f"https://mock-storage.local/mpu/{upload_id}/part/{p}?key={key}&expires_in={expires_in}"
            for p in part_numbers
        }

    async def complete_multipart_upload(
        self,
        key: str,
        upload_id: str,
        parts: list[dict[str, Any]],
    ) -> str:
        if upload_id not in self._multipart_sessions:
            raise StorageObjectNotFoundError(
                f"Multipart upload session {upload_id} not found"
            )
        session = self._multipart_sessions.pop(upload_id)
        if key not in self._objects:
            self._objects[key] = b""
            self._metadata[key] = {
                "sha256": hashlib.sha256(b"").hexdigest(),
                "content_type": session.get("content_type", "application/octet-stream"),
            }
        return f'"{uuid.uuid4().hex[:16]}"'

    async def abort_multipart_upload(self, key: str, upload_id: str) -> None:
        self._multipart_sessions.pop(upload_id, None)

    def clear(self) -> None:
        self._objects.clear()
        self._metadata.clear()
        self._multipart_sessions.clear()
