"""Repository ports for fileshare domain entities.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002
"""

from abc import ABC, abstractmethod

from apps.fileshare.domain.models import FileRecord, GuestLink, ShareGrant
from packages.hexagonal import RepositoryPort


class FileRecordRepositoryPort(RepositoryPort[FileRecord], ABC):
    """Port for persisting and querying FileRecord domain entities."""

    @abstractmethod
    async def get_by_object_key(self, object_key: str) -> FileRecord | None:
        """Retrieve file record by its unique object store key."""
        pass

    @abstractmethod
    async def list_by_study(
        self,
        study_id: str,
        site_id: str | None = None,
        include_deleted: bool = False,
    ) -> list[FileRecord]:
        """List active file records for a given study and optional site."""
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

