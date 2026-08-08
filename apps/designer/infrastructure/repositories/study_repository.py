"""
Neo4j and relational database repository for Clinical Studies.
Subclasses apps.designer.domain.ports.StudyRepositoryPort.
"""

from typing import Any

from apps.designer.domain.ports import StudyRepositoryPort
from packages.database import map_database_exceptions


class Neo4jStudyRepository(StudyRepositoryPort):
    """
    Neo4j graph persistence implementation for Clinical Studies.
    Subclasses StudyRepositoryPort.
    """

    def __init__(self, driver: Any):
        self.driver = driver

    @map_database_exceptions
    async def get_by_id(self, entity_id: str) -> dict[str, Any] | None:
        if not self.driver:
            from apps.designer.db import MOCK_STUDIES

            return MOCK_STUDIES.get(entity_id)

        async with self.driver.session() as session:
            query = "MATCH (s:Study {id: $study_id}) RETURN s {.*} AS study"
            result = await session.run(query, study_id=entity_id)
            record = await result.single()
            if record:
                return dict(record["study"])
            return None

    @map_database_exceptions
    async def save(self, entity: dict[str, Any]) -> dict[str, Any]:
        study_id = entity.get("id")
        if not self.driver:
            from apps.designer.db import MOCK_STUDIES

            MOCK_STUDIES[study_id] = entity
            return entity

        async with self.driver.session() as session:
            query = (
                "MERGE (s:Study {id: $study_id}) SET s += $props RETURN s {.*} AS study"
            )
            result = await session.run(query, study_id=study_id, props=entity)
            record = await result.single()
            return dict(record["study"])

    @map_database_exceptions
    async def get_study_version(
        self, study_id: str, version_id: str
    ) -> dict[str, Any] | None:
        if not self.driver:
            from apps.designer.db import MOCK_STUDY_VERSIONS

            return MOCK_STUDY_VERSIONS.get(version_id)

        async with self.driver.session() as session:
            query = (
                "MATCH (s:Study {id: $study_id})-[:HAS_VERSION]->(sv:StudyVersion {id: $version_id}) "
                "RETURN sv {.*} AS version"
            )
            result = await session.run(query, study_id=study_id, version_id=version_id)
            record = await result.single()
            if record:
                return dict(record["version"])
            return None
