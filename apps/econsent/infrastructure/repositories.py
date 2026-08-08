from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.econsent.domain.ports import IEConsentRepository
from apps.econsent.infrastructure.models import ConsentDocument
from packages.database import map_database_exceptions


class SQLEConsentRepository(IEConsentRepository):
    """SQLAlchemy implementation of driven repository port for eConsent."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    @map_database_exceptions
    async def get_by_id(self, entity_id: str) -> ConsentDocument | None:
        stmt = select(ConsentDocument).where(ConsentDocument.id == entity_id)
        result = await self.session.execute(stmt)
        return result.scalars().first()

    @map_database_exceptions
    async def save(self, entity: ConsentDocument) -> ConsentDocument:
        self.session.add(entity)
        await self.session.flush()
        return entity
