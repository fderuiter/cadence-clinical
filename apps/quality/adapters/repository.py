from typing import Sequence
from sqlalchemy import select
from sqlalchemy.orm import selectinload
from ..ports.repository import QualityRepositoryPort
from ..models import Deviation, RootCauseAnalysis, CAPARecord, QualityAuditLog
from ..database import get_session


class SQLQualityRepository(QualityRepositoryPort):
    async def get_deviations(self) -> Sequence[Deviation]:
        session = get_session()
        stmt = select(Deviation)
        result = await session.execute(stmt)
        return result.scalars().all()

    async def get_deviation_by_id(self, dev_id: str) -> Deviation | None:
        session = get_session()
        stmt = select(Deviation).where(Deviation.id == dev_id)
        result = await session.execute(stmt)
        return result.scalars().first()

    async def save_deviation(self, dev: Deviation) -> Deviation:
        session = get_session()
        session.add(dev)
        await session.flush()
        return dev

    async def get_rca_by_deviation_id(self, dev_id: str) -> RootCauseAnalysis | None:
        session = get_session()
        stmt = select(RootCauseAnalysis).where(RootCauseAnalysis.deviation_id == dev_id)
        result = await session.execute(stmt)
        return result.scalars().first()

    async def get_rca_by_id(self, rca_id: str) -> RootCauseAnalysis | None:
        session = get_session()
        stmt = select(RootCauseAnalysis).where(RootCauseAnalysis.id == rca_id)
        result = await session.execute(stmt)
        return result.scalars().first()

    async def save_rca(self, rca: RootCauseAnalysis) -> RootCauseAnalysis:
        session = get_session()
        session.add(rca)
        await session.flush()
        return rca

    async def get_capa_by_id(self, capa_id: str) -> CAPARecord | None:
        session = get_session()
        stmt = select(CAPARecord).where(CAPARecord.id == capa_id).options(selectinload(CAPARecord.deviation))
        result = await session.execute(stmt)
        return result.scalars().first()

    async def get_capas(self) -> Sequence[CAPARecord]:
        session = get_session()
        stmt = select(CAPARecord)
        result = await session.execute(stmt)
        return result.scalars().all()

    async def save_capa(self, capa: CAPARecord) -> CAPARecord:
        session = get_session()
        session.add(capa)
        await session.flush()
        return capa

    async def get_audit_logs(self) -> Sequence[QualityAuditLog]:
        session = get_session()
        stmt = select(QualityAuditLog).order_by(QualityAuditLog.timestamp.desc())
        result = await session.execute(stmt)
        return result.scalars().all()

    async def save_audit_log(self, log: QualityAuditLog) -> QualityAuditLog:
        session = get_session()
        session.add(log)
        await session.flush()
        return log
