from fastapi import Depends
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.ctms.adapters.database import db_manager
from apps.ctms.adapters.models import CTMSAuditLog, CTMSDelegation
from apps.ctms.domain.models import CTMSAuditLogEntity, CTMSDelegationEntity
from apps.ctms.domain.ports import ICTMSDelegationRepository
from packages.database import DatabaseSessionDependency, map_database_exceptions

get_db_session = DatabaseSessionDependency(db_manager)


class SQLAlchemyCTMSDelegationRepository(ICTMSDelegationRepository):
    """SQLAlchemy implementation of ICTMSDelegationRepository."""

    def __init__(self, session: AsyncSession):
        self.session = session

    @map_database_exceptions
    async def get_by_id(self, entity_id: str) -> CTMSDelegationEntity | None:
        stmt = select(CTMSDelegation).where(CTMSDelegation.id == entity_id)
        res = await self.session.execute(stmt)
        model = res.scalars().first()
        if not model:
            return None
        return self._to_entity(model)

    @map_database_exceptions
    async def get_by_site_id(self, site_id: str) -> list[CTMSDelegationEntity]:
        stmt = select(CTMSDelegation).where(CTMSDelegation.site_id == site_id)
        res = await self.session.execute(stmt)
        models = res.scalars().all()
        return [self._to_entity(m) for m in models]

    @map_database_exceptions
    async def save(self, entity: CTMSDelegationEntity) -> CTMSDelegationEntity:
        if entity.id:
            # Update existing
            stmt = select(CTMSDelegation).where(CTMSDelegation.id == entity.id)
            res = await self.session.execute(stmt)
            model = res.scalars().first()
            if model:
                model.is_active = entity.is_active
                model.end_date = entity.end_date
                model.version_index = entity.version_index
                model.reason_for_change = entity.reason_for_change
                model.signed_off = entity.signed_off
                self.session.add(model)
                await self.session.flush()
                return self._to_entity(model)

        # Create new
        model = CTMSDelegation(
            site_id=entity.site_id,
            staff_user_id=entity.staff_user_id,
            task_codes=entity.task_codes,
            start_date=entity.start_date,
            end_date=entity.end_date,
            is_active=entity.is_active,
            signed_off=entity.signed_off,
            created_by=entity.created_by,
            reason_for_change=entity.reason_for_change,
            version_index=entity.version_index,
        )
        self.session.add(model)
        await self.session.flush()
        return self._to_entity(model)

    @map_database_exceptions
    async def save_audit_log(self, audit: CTMSAuditLogEntity) -> None:
        model = CTMSAuditLog(
            user_id=audit.user_id,
            user_role=audit.user_role,
            action=audit.action,
            details=audit.details,
        )
        self.session.add(model)
        await self.session.flush()

    @map_database_exceptions
    async def get_audit_logs_by_site(self, site_id: str) -> list[CTMSAuditLogEntity]:
        stmt = (
            select(CTMSAuditLog)
            .where(CTMSAuditLog.action == "DOA_LOG_MODIFIED")
            .order_by(CTMSAuditLog.timestamp.desc())
        )
        res = await self.session.execute(stmt)
        models = res.scalars().all()
        entities = []
        for m in models:
            if site_id in m.details:
                entities.append(
                    CTMSAuditLogEntity(
                        id=m.id,
                        user_id=m.user_id,
                        user_role=m.user_role,
                        action=m.action,
                        details=m.details,
                        timestamp=m.timestamp.isoformat(),
                    )
                )
        return entities

    def _to_entity(self, model: CTMSDelegation) -> CTMSDelegationEntity:
        return CTMSDelegationEntity(
            id=model.id,
            site_id=model.site_id,
            staff_user_id=model.staff_user_id,
            task_codes=model.task_codes,
            start_date=model.start_date,
            end_date=model.end_date,
            is_active=model.is_active,
            signed_off=model.signed_off,
            created_by=model.created_by,
            reason_for_change=model.reason_for_change,
            version_index=model.version_index,
        )


SQLAlchemCTMSDelegationRepository = SQLAlchemyCTMSDelegationRepository


async def get_ctms_repository(
    session: AsyncSession = Depends(get_db_session),
) -> SQLAlchemyCTMSDelegationRepository:
    return SQLAlchemyCTMSDelegationRepository(session)
