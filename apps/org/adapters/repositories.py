from typing import Any

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.org.adapters.models import Organization
from apps.org.domain.ports import IOrganizationRepository
from packages.database import map_database_exceptions


class SQLOrganizationRepository(IOrganizationRepository):
    """SQLAlchemy repository implementation for Organization domain model."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    @map_database_exceptions
    async def get_by_id(self, entity_id: str) -> Organization | None:
        stmt = select(Organization).where(Organization.id == entity_id)
        res = await self.session.execute(stmt)
        return res.scalars().first()

    @map_database_exceptions
    async def save(self, entity: Any) -> Any:
        self.session.add(entity)
        await self.session.flush()
        return entity
