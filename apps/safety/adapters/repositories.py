from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.safety.adapters.models import SafetyCaseICSR
from apps.safety.domain.ports import ISafetyRepository
from packages.database import map_database_exceptions


class SQLSafetyRepository(ISafetyRepository):
    """SQLAlchemy implementation of driven repository port for Safety microservice."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    @map_database_exceptions
    async def get_by_id(self, entity_id: str) -> SafetyCaseICSR | None:
        stmt = select(SafetyCaseICSR).where(SafetyCaseICSR.id == entity_id)
        result = await self.session.execute(stmt)
        return result.scalars().first()

    @map_database_exceptions
    async def save(self, entity: SafetyCaseICSR) -> SafetyCaseICSR:
        self.session.add(entity)
        await self.session.flush()
        return entity
