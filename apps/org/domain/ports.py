from abc import abstractmethod
from typing import Any

from packages.hexagonal import RepositoryPort


class IOrganizationRepository(RepositoryPort[Any]):
    """Abstract driven port for Organization persistence operations."""

    @abstractmethod
    async def get_by_id(self, entity_id: str) -> Any | None:
        pass

    @abstractmethod
    async def save(self, entity: Any) -> Any:
        pass
