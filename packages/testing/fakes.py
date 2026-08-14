"""High-performance in-memory repository fakes implementing Hexagonal driven ports."""

from packages.hexagonal import RepositoryPort


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
