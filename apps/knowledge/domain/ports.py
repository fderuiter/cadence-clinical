"""Domain ports for Knowledge microservice.

Follows Hexagonal Architecture separating inbound drivers from outbound driven adapters.
"""

from abc import abstractmethod
from typing import Any

from packages.hexagonal import RepositoryPort


class IKnowledgeRepository(RepositoryPort[Any]):
    """Repository port for Knowledge & Support Hub articles and categories."""

    @abstractmethod
    async def get_by_id(self, entity_id: str) -> Any | None:
        pass

    @abstractmethod
    async def save(self, entity: Any) -> Any:
        pass
