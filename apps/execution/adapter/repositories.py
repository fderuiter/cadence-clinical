from fastapi import Depends
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.application.ports import IExecutionDOARepository
from apps.execution.database import db_manager
from apps.execution.database.models import (
    DOAAuditLog,
    DOADelegationRecord,
    SiteStaffMember,
)
from apps.execution.domain.models import (
    ExecutionAuditLogEntity,
    ExecutionDelegationEntity,
    ExecutionStaffEntity,
)
from packages.hexagonal import map_database_exceptions


class SQLAlchemExecutionDOARepository(IExecutionDOARepository):
    def __init__(self, session: AsyncSession):
        self.session = session

    @map_database_exceptions
    async def get_staff(
        self, site_id: str, staff_user_id: str
    ) -> ExecutionStaffEntity | None:
        stmt = select(SiteStaffMember).where(
            SiteStaffMember.site_id == site_id,
            SiteStaffMember.staff_user_id == staff_user_id,
        )
        res = await self.session.execute(stmt)
        model = res.scalar_one_or_none()
        if not model:
            return None
        return ExecutionStaffEntity(
            id=model.id,
            site_id=model.site_id,
            staff_user_id=model.staff_user_id,
            name=model.name,
            email=model.email,
            has_gcp_training=model.has_gcp_training,
        )

    @map_database_exceptions
    async def get_staff_by_user_id(
        self, staff_user_id: str
    ) -> ExecutionStaffEntity | None:
        stmt = select(SiteStaffMember).where(
            SiteStaffMember.staff_user_id == staff_user_id
        )
        res = await self.session.execute(stmt)
        model = res.scalar_one_or_none()
        if not model:
            return None
        return ExecutionStaffEntity(
            id=model.id,
            site_id=model.site_id,
            staff_user_id=model.staff_user_id,
            name=model.name,
            email=model.email,
            has_gcp_training=model.has_gcp_training,
        )

    @map_database_exceptions
    async def save_staff(self, staff: ExecutionStaffEntity) -> ExecutionStaffEntity:
        if staff.id:
            stmt = select(SiteStaffMember).where(SiteStaffMember.id == staff.id)
            res = await self.session.execute(stmt)
            model = res.scalar_one_or_none()
            if model:
                model.site_id = staff.site_id
                model.name = staff.name
                model.email = staff.email
                model.has_gcp_training = staff.has_gcp_training
                self.session.add(model)
                await self.session.commit()
                return staff

        model = SiteStaffMember(
            site_id=staff.site_id,
            staff_user_id=staff.staff_user_id,
            name=staff.name,
            email=staff.email,
            has_gcp_training=staff.has_gcp_training,
        )
        self.session.add(model)
        await self.session.commit()
        await self.session.refresh(model)
        staff.id = model.id
        return staff

    @map_database_exceptions
    async def get_delegation_by_id(
        self, delegation_id: str
    ) -> ExecutionDelegationEntity | None:
        stmt = select(DOADelegationRecord).where(
            DOADelegationRecord.id == delegation_id,
            DOADelegationRecord.is_active.is_(True),
        )
        res = await self.session.execute(stmt)
        model = res.scalar_one_or_none()
        if not model:
            return None
        return ExecutionDelegationEntity(
            id=model.id,
            site_id=model.site_id,
            staff_user_id=model.staff_user_id,
            task_code=model.task_code,
            pi_user_id=model.pi_user_id,
            status=model.status,
            pi_signature_hash=model.pi_signature_hash,
            pi_approved_at=model.pi_approved_at,
            end_date=model.end_date,
            reason_for_change=model.reason_for_change,
            is_active=model.is_active,
        )

    @map_database_exceptions
    async def save_delegation(
        self, delegation: ExecutionDelegationEntity
    ) -> ExecutionDelegationEntity:
        if delegation.id:
            stmt = select(DOADelegationRecord).where(
                DOADelegationRecord.id == delegation.id
            )
            res = await self.session.execute(stmt)
            model = res.scalar_one_or_none()
            if model:
                model.status = delegation.status
                model.pi_approved_at = delegation.pi_approved_at
                model.pi_signature_hash = delegation.pi_signature_hash
                model.reason_for_change = delegation.reason_for_change
                model.end_date = delegation.end_date
                model.is_active = delegation.is_active
                self.session.add(model)
                await self.session.commit()
                return delegation

        model = DOADelegationRecord(
            site_id=delegation.site_id,
            staff_user_id=delegation.staff_user_id,
            task_code=delegation.task_code,
            pi_user_id=delegation.pi_user_id,
            status=delegation.status,
            reason_for_change=delegation.reason_for_change,
            is_active=delegation.is_active,
        )
        self.session.add(model)
        await self.session.commit()
        await self.session.refresh(model)
        delegation.id = model.id
        return delegation

    @map_database_exceptions
    async def save_audit_log(self, audit: ExecutionAuditLogEntity) -> None:
        model = DOAAuditLog(
            user_id=audit.user_id,
            action=audit.action,
            details=audit.details,
        )
        self.session.add(model)
        await self.session.commit()

    @map_database_exceptions
    async def get_all_audit_logs(self) -> list[ExecutionAuditLogEntity]:
        stmt = select(DOAAuditLog).order_by(DOAAuditLog.timestamp.desc())
        res = await self.session.execute(stmt)
        models = res.scalars().all()
        return [
            ExecutionAuditLogEntity(
                id=m.id,
                user_id=m.user_id,
                action=m.action,
                details=m.details,
                timestamp=m.timestamp,
            )
            for m in models
        ]

    @map_database_exceptions
    async def get_all_delegations(self) -> list[ExecutionDelegationEntity]:
        stmt = select(DOADelegationRecord)
        res = await self.session.execute(stmt)
        models = res.scalars().all()
        return [
            ExecutionDelegationEntity(
                id=m.id,
                site_id=m.site_id,
                staff_user_id=m.staff_user_id,
                task_code=m.task_code,
                pi_user_id=m.pi_user_id,
                status=m.status,
                pi_signature_hash=m.pi_signature_hash,
                pi_approved_at=m.pi_approved_at,
                end_date=m.end_date,
                reason_for_change=m.reason_for_change,
                is_active=m.is_active,
            )
            for m in models
        ]


async def get_execution_db_session():
    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        yield session


async def get_execution_doa_repository(
    session: AsyncSession = Depends(get_execution_db_session),
) -> SQLAlchemExecutionDOARepository:
    return SQLAlchemExecutionDOARepository(session)
