from typing import Any

from apps.gateway.domain.ports import IGatewaySessionRepository


class InMemoryGatewaySessionRepository(IGatewaySessionRepository):
    """In-memory implementation of Gateway Session / Token Cache repository."""

    def __init__(self) -> None:
        self._store: dict[str, Any] = {}

    async def get_by_id(self, entity_id: str) -> Any | None:
        return self._store.get(entity_id)

    async def save(self, entity: Any) -> Any:
        key = getattr(entity, "id", str(entity))
        self._store[key] = entity
        return entity
