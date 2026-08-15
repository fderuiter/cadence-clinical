from typing import Any

from sqlalchemy.ext.asyncio import AsyncSession

from apps.interop.domain.ports import IInteropRepository
from packages.database import map_database_exceptions


class SQLInteropRepository(IInteropRepository):
    """SQLAlchemy implementation of driven repository port for Interop."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    @map_database_exceptions
    async def get_by_id(self, entity_id: str) -> Any | None:
        return None

    @map_database_exceptions
    async def save(self, entity: Any) -> Any:
        self.session.add(entity)
        await self.session.flush()
        return entity
