"""
Repository for Global MDR Library objects.
Subclasses apps.designer.domain.ports.LibraryRepositoryPort.
"""

from typing import Any

from apps.designer.domain.ports import LibraryRepositoryPort
from packages.database import map_database_exceptions


class Neo4jLibraryRepository(LibraryRepositoryPort):
    """
    Neo4j graph persistence implementation for Library Objects.
    Subclasses LibraryRepositoryPort.
    """

    def __init__(self, driver: Any):
        self.driver = driver

    @map_database_exceptions
    async def get_by_id(self, entity_id: str) -> dict[str, Any] | None:
        if not self.driver:
            from apps.designer.db import MOCK_LIBRARY_OBJECTS

            return MOCK_LIBRARY_OBJECTS.get(entity_id)

        async with self.driver.session() as session:
            query = "MATCH (l:LibraryObject {id: $id}) RETURN l {.*} AS obj"
            result = await session.run(query, id=entity_id)
            record = await result.single()
            if record:
                return dict(record["obj"])
            return None

    @map_database_exceptions
    async def save(self, entity: dict[str, Any]) -> dict[str, Any]:
        obj_id = entity.get("id")
        if not self.driver:
            from apps.designer.db import MOCK_LIBRARY_OBJECTS

            MOCK_LIBRARY_OBJECTS[obj_id] = entity
            return entity

        async with self.driver.session() as session:
            query = (
                "MERGE (l:LibraryObject {id: $id}) SET l += $props RETURN l {.*} AS obj"
            )
            result = await session.run(query, id=obj_id, props=entity)
            record = await result.single()
            return dict(record["obj"])

    @map_database_exceptions
    async def get_latest_version(self, object_id: str) -> dict[str, Any] | None:
        if not self.driver:
            from apps.designer.db import MOCK_LIBRARY_OBJECTS

            return MOCK_LIBRARY_OBJECTS.get(object_id)

        async with self.driver.session() as session:
            query = (
                "MATCH (old:LibraryObject {id: $object_id}) "
                "WHERE NOT (old)<-[:PREVIOUS_VERSION]-() "
                "RETURN old {.*} AS obj"
            )
            result = await session.run(query, object_id=object_id)
            record = await result.single()
            if record:
                return dict(record["obj"])
            return None
