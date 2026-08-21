from abc import abstractmethod
from typing import Any

from packages.hexagonal import RepositoryPort


class IKnowledgeRepository(RepositoryPort[Any]):
    """Driven repository port for Knowledge service."""

    @abstractmethod
    async def get_by_id(self, entity_id: str) -> Any | None:
        pass

    @abstractmethod
    async def save(self, entity: Any) -> Any:
        pass
