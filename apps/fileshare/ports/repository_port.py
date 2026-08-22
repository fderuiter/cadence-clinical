"""Repository ports for fileshare domain entities.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002
"""

from abc import ABC, abstractmethod

from apps.fileshare.domain.models import (
    FileAuditLog,
    FileRecord,
    GuestLink,
    PlaybackAuditEvent,
    ShareGrant,
)
from packages.hexagonal import RepositoryPort


class FileRecordRepositoryPort(RepositoryPort[FileRecord], ABC):
    """Port for persisting and querying FileRecord domain entities."""

    @abstractmethod
    async def get_by_id(
        self, entity_id: str, include_deleted: bool = False
    ) -> FileRecord | None:
        """Retrieve file record by its primary key identifier."""
        pass

    @abstractmethod
    async def get_by_object_key(self, object_key: str) -> FileRecord | None:
        """Retrieve file record by its unique object store key."""
        pass

    @abstractmethod
    async def list_by_study(
        self,
        study_id: str | None = None,
        site_id: str | None = None,
        include_deleted: bool = False,
    ) -> list[FileRecord]:
        """List active file records for an optional study and optional site."""
        pass

    @abstractmethod
    async def list_pending_transcodes(self) -> list[FileRecord]:
        """List file records pending video transcoding."""
        pass


class ShareGrantRepositoryPort(RepositoryPort[ShareGrant], ABC):
    """Port for persisting and querying ShareGrant access delegations."""

    @abstractmethod
    async def list_by_file_id(
        self, file_record_id: str, active_only: bool = True
    ) -> list[ShareGrant]:
        """List share grants associated with a file record."""
        pass

    @abstractmethod
    async def find_user_grant(
        self, file_record_id: str, user_id: str
    ) -> ShareGrant | None:
        """Find active share grant for a specific user on a file record."""
        pass


class GuestLinkRepositoryPort(RepositoryPort[GuestLink], ABC):
    """Port for persisting and querying GuestLink records."""

    @abstractmethod
    async def get_by_token_hmac(self, token_hmac: str) -> GuestLink | None:
        """Retrieve guest link by token HMAC hash."""
        pass

    @abstractmethod
    async def list_by_file_id(
        self, file_record_id: str, active_only: bool = True
    ) -> list[GuestLink]:
        """List guest links associated with a file record."""
        pass


class PlaybackAuditRepositoryPort(RepositoryPort[PlaybackAuditEvent], ABC):
    """Port for persisting and querying viewer playback audit checkpoints."""

    @abstractmethod
    async def list_by_file_id(self, file_record_id: str) -> list[PlaybackAuditEvent]:
        """List playback audit events for a file record."""
        pass

    @abstractmethod
    async def list_by_session_id(self, session_id: str) -> list[PlaybackAuditEvent]:
        """List playback audit events for a viewer session."""
        pass


class FileAuditLogRepositoryPort(RepositoryPort[FileAuditLog], ABC):
    """Port for persisting and querying FileAuditLog entries."""

    @abstractmethod
    async def list_by_file_id(self, file_record_id: str) -> list[FileAuditLog]:
        """List audit log entries for a file record ordered by timestamp descending."""
        pass
