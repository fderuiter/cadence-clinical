from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.notifications.domain.ports import INotificationRepository
from apps.notifications.infrastructure.models import Notification
from packages.database import map_database_exceptions


class SQLNotificationRepository(INotificationRepository):
    """SQLAlchemy implementation of driven repository port for Notifications."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    @map_database_exceptions
    async def get_by_id(self, entity_id: str) -> Notification | None:
        stmt = select(Notification).where(Notification.id == entity_id)
        result = await self.session.execute(stmt)
        return result.scalars().first()

    @map_database_exceptions
    async def save(self, entity: Notification) -> Notification:
        self.session.add(entity)
        await self.session.flush()
        return entity
