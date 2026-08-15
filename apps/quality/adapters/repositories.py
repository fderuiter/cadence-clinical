from collections.abc import Sequence

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy.orm import selectinload

from apps.quality.adapters.database import get_session
from apps.quality.adapters.models import (
    CAPARecord,
    Deviation,
    QualityAuditLog,
    RootCauseAnalysis,
)
from apps.quality.domain.ports import QualityRepositoryPort
from packages.database import map_database_exceptions


class SQLQualityRepository(QualityRepositoryPort):
    """SQLAlchemy implementation of QualityRepositoryPort."""

    def __init__(self, session: AsyncSession | None = None) -> None:
        self._session = session

    @property
    def session(self) -> AsyncSession:
        if self._session is not None:
            return self._session
        return get_session()

    def create_deviation_entity(self, **kwargs) -> Deviation:
        return Deviation(**kwargs)

    def create_rca_entity(self, **kwargs) -> RootCauseAnalysis:
        return RootCauseAnalysis(**kwargs)

    def create_capa_entity(self, **kwargs) -> CAPARecord:
        return CAPARecord(**kwargs)

    def create_audit_log_entity(self, **kwargs) -> QualityAuditLog:
        return QualityAuditLog(**kwargs)

    @map_database_exceptions
    async def get_by_id(self, entity_id: str) -> Deviation | None:
        return await self.get_deviation_by_id(entity_id)

    @map_database_exceptions
    async def save(self, entity: Deviation) -> Deviation:
        return await self.save_deviation(entity)

    @map_database_exceptions
    async def get_deviations(self) -> Sequence[Deviation]:
        stmt = select(Deviation)
        result = await self.session.execute(stmt)
        return result.scalars().all()

    @map_database_exceptions
    async def get_deviation_by_id(self, dev_id: str) -> Deviation | None:
        stmt = select(Deviation).where(Deviation.id == dev_id)
        result = await self.session.execute(stmt)
        return result.scalars().first()

    @map_database_exceptions
    async def save_deviation(self, dev: Deviation) -> Deviation:
        self.session.add(dev)
        await self.session.flush()
        return dev

    @map_database_exceptions
    async def get_rca_by_deviation_id(self, dev_id: str) -> RootCauseAnalysis | None:
        stmt = select(RootCauseAnalysis).where(RootCauseAnalysis.deviation_id == dev_id)
        result = await self.session.execute(stmt)
        return result.scalars().first()

    @map_database_exceptions
    async def get_rca_by_id(self, rca_id: str) -> RootCauseAnalysis | None:
        stmt = select(RootCauseAnalysis).where(RootCauseAnalysis.id == rca_id)
        result = await self.session.execute(stmt)
        return result.scalars().first()

    @map_database_exceptions
    async def save_rca(self, rca: RootCauseAnalysis) -> RootCauseAnalysis:
        self.session.add(rca)
        await self.session.flush()
        return rca

    @map_database_exceptions
    async def get_capa_by_id(self, capa_id: str) -> CAPARecord | None:
        stmt = (
            select(CAPARecord)
            .where(CAPARecord.id == capa_id)
            .options(selectinload(CAPARecord.deviation))
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    @map_database_exceptions
    async def get_capas(self) -> Sequence[CAPARecord]:
        stmt = select(CAPARecord)
        result = await self.session.execute(stmt)
        return result.scalars().all()

    @map_database_exceptions
    async def save_capa(self, capa: CAPARecord) -> CAPARecord:
        self.session.add(capa)
        await self.session.flush()
        return capa

    @map_database_exceptions
    async def get_audit_logs(self) -> Sequence[QualityAuditLog]:
        stmt = select(QualityAuditLog).order_by(QualityAuditLog.timestamp.desc())
        result = await self.session.execute(stmt)
        return result.scalars().all()

    @map_database_exceptions
    async def save_audit_log(self, log: QualityAuditLog) -> QualityAuditLog:
        self.session.add(log)
        await self.session.flush()
        return log
