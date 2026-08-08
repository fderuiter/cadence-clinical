"""
Domain ports for Tickets microservice.
"""

from abc import abstractmethod
from typing import Any

from packages.hexagonal import RepositoryPort


class TicketRepositoryPort(RepositoryPort[Any]):
    """
    Abstract repository port for Ticket persistence operations.
    Inherits from packages.hexagonal.RepositoryPort.
    """

    @abstractmethod
    async def get_by_id(self, entity_id: str) -> Any | None:
        """Fetch ticket entity by primary ID."""
        pass

    @abstractmethod
    async def save(self, entity: Any) -> Any:
        """Save or update ticket entity."""
        pass

    @abstractmethod
    async def get_by_reference(self, reference: str) -> Any | None:
        """Fetch ticket entity by human-readable reference."""
        pass
