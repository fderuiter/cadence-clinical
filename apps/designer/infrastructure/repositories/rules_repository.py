"""
Repository for authored clinical validation rules.
Subclasses apps.designer.domain.ports.RulesRepositoryPort.
"""

from typing import Any

from apps.designer.domain.ports import RulesRepositoryPort
from packages.database import map_database_exceptions


class Neo4jRulesRepository(RulesRepositoryPort):
    """
    Neo4j graph persistence implementation for authored rules.
    Subclasses RulesRepositoryPort.
    """

    def __init__(self, driver: Any):
        self.driver = driver

    @map_database_exceptions
    async def get_by_id(self, entity_id: str) -> dict[str, Any] | None:
        if not self.driver:
            from apps.designer.db import get_mock_rule_by_id

            return get_mock_rule_by_id(entity_id)

        async with self.driver.session() as session:
            query = "MATCH (r:Rule {id: $id}) RETURN r {.*} AS rule"
            result = await session.run(query, id=entity_id)
            record = await result.single()
            if record:
                return dict(record["rule"])
            return None

    @map_database_exceptions
    async def save(self, entity: dict[str, Any]) -> dict[str, Any]:
        rule_id = entity.get("id")
        if not self.driver:
            from apps.designer.db import create_mock_rule

            return create_mock_rule(entity)

        async with self.driver.session() as session:
            query = "MERGE (r:Rule {id: $id}) SET r += $props RETURN r {.*} AS rule"
            result = await session.run(query, id=rule_id, props=entity)
            record = await result.single()
            return dict(record["rule"])
