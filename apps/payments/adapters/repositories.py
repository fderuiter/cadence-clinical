"""SQLAlchemy Repository implementation for Site Payments microservice.

Requirements: ADR-2199, PRD-PAY-002, PRD-PAY-003, PRD-PAY-006
"""

from __future__ import annotations

import uuid
from typing import Any

from fastapi import Depends
from sqlalchemy import desc, select
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy.orm import selectinload

from apps.payments.adapters.database import get_db_session
from apps.payments.domain.ports import IPaymentsRepository
from apps.payments.infrastructure.models import (
    BudgetContractModel,
    FeeScheduleModel,
    HoldbackReserveModel,
    PayableAllocationModel,
    PayableModel,
    PaymentAuditLogModel,
    SplitPayeeConfigModel,
)


class SQLAlchemyPaymentsRepository(IPaymentsRepository):
    """SQLAlchemy implementation of the Site Payments repository."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    async def get_active_contract(
        self, study_id: str, site_id: str
    ) -> BudgetContractModel | None:
        stmt = (
            select(BudgetContractModel)
            .where(
                BudgetContractModel.study_id == study_id,
                BudgetContractModel.site_id == site_id,
                BudgetContractModel.is_active.is_(True),
                BudgetContractModel.status == "ACTIVE",
            )
            .options(
                selectinload(BudgetContractModel.fee_schedules),
                selectinload(BudgetContractModel.split_payees),
            )
            .order_by(desc(BudgetContractModel.version_index))
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    async def get_contract_by_id(self, contract_id: str) -> BudgetContractModel | None:
        stmt = (
            select(BudgetContractModel)
            .where(BudgetContractModel.id == contract_id)
            .options(
                selectinload(BudgetContractModel.fee_schedules),
                selectinload(BudgetContractModel.split_payees),
            )
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    async def save_contract(self, contract: BudgetContractModel) -> BudgetContractModel:
        if not contract.id:
            contract.id = str(uuid.uuid4())
        self.session.add(contract)
        await self.session.commit()
        await self.session.refresh(contract)
        return contract

    async def list_contracts(
        self,
        study_id: str | None = None,
        site_id: str | None = None,
        is_active_only: bool = True,
    ) -> list[BudgetContractModel]:
        stmt = select(BudgetContractModel).options(
            selectinload(BudgetContractModel.fee_schedules),
            selectinload(BudgetContractModel.split_payees),
        )
        if study_id:
            stmt = stmt.where(BudgetContractModel.study_id == study_id)
        if site_id:
            stmt = stmt.where(BudgetContractModel.site_id == site_id)
        if is_active_only:
            stmt = stmt.where(BudgetContractModel.is_active.is_(True))
        stmt = stmt.order_by(desc(BudgetContractModel.created_at))
        result = await self.session.execute(stmt)
        return list(result.scalars().all())

    async def save_fee_schedule(
        self, fee_schedule: FeeScheduleModel
    ) -> FeeScheduleModel:
        if not fee_schedule.id:
            fee_schedule.id = str(uuid.uuid4())
        self.session.add(fee_schedule)
        await self.session.commit()
        await self.session.refresh(fee_schedule)
        return fee_schedule

    async def list_fee_schedules(
        self,
        contract_id: str,
        visit_code: str | None = None,
        form_id: str | None = None,
        is_active_only: bool = True,
    ) -> list[FeeScheduleModel]:
        stmt = select(FeeScheduleModel).where(
            FeeScheduleModel.contract_id == contract_id
        )
        if visit_code:
            stmt = stmt.where(FeeScheduleModel.visit_code == visit_code)
        if form_id:
            stmt = stmt.where(FeeScheduleModel.form_id == form_id)
        if is_active_only:
            stmt = stmt.where(FeeScheduleModel.is_active.is_(True))
        stmt = stmt.order_by(FeeScheduleModel.procedure_code)
        result = await self.session.execute(stmt)
        return list(result.scalars().all())

    async def save_split_payees(
        self, split_payees: list[SplitPayeeConfigModel]
    ) -> list[SplitPayeeConfigModel]:
        for payee in split_payees:
            if not payee.id:
                payee.id = str(uuid.uuid4())
            self.session.add(payee)
        await self.session.commit()
        for payee in split_payees:
            await self.session.refresh(payee)
        return split_payees

    async def list_split_payees(
        self, contract_id: str, is_active_only: bool = True
    ) -> list[SplitPayeeConfigModel]:
        stmt = select(SplitPayeeConfigModel).where(
            SplitPayeeConfigModel.contract_id == contract_id
        )
        if is_active_only:
            stmt = stmt.where(SplitPayeeConfigModel.is_active.is_(True))
        stmt = stmt.order_by(desc(SplitPayeeConfigModel.allocation_percentage))
        result = await self.session.execute(stmt)
        return list(result.scalars().all())

    async def save_payable(self, payable: PayableModel) -> PayableModel:
        if not payable.id:
            payable.id = str(uuid.uuid4())
        self.session.add(payable)
        await self.session.commit()
        await self.session.refresh(payable)
        return payable

    async def get_payable_by_id(self, payable_id: str) -> PayableModel | None:
        stmt = (
            select(PayableModel)
            .where(PayableModel.id == payable_id)
            .options(
                selectinload(PayableModel.allocations),
                selectinload(PayableModel.holdback_reserve),
            )
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    async def get_payable_by_event_id(self, event_id: str) -> PayableModel | None:
        stmt = (
            select(PayableModel)
            .where(PayableModel.trigger_event_id == event_id)
            .options(
                selectinload(PayableModel.allocations),
                selectinload(PayableModel.holdback_reserve),
            )
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    async def list_payables(
        self,
        study_id: str | None = None,
        site_id: str | None = None,
        subject_id: str | None = None,
        visit_code: str | None = None,
        form_id: str | None = None,
        status: str | None = None,
        is_sdv_gated: bool | None = None,
    ) -> list[PayableModel]:
        stmt = select(PayableModel).options(
            selectinload(PayableModel.allocations),
            selectinload(PayableModel.holdback_reserve),
        )
        if study_id:
            stmt = stmt.where(PayableModel.study_id == study_id)
        if site_id:
            stmt = stmt.where(PayableModel.site_id == site_id)
        if subject_id:
            stmt = stmt.where(PayableModel.subject_id == subject_id)
        if visit_code:
            stmt = stmt.where(PayableModel.visit_code == visit_code)
        if form_id:
            stmt = stmt.where(PayableModel.form_id == form_id)
        if status:
            stmt = stmt.where(PayableModel.status == status)
        if is_sdv_gated is not None:
            stmt = (
                stmt.where(PayableModel.is_sdv_gated.is_(True))
                if is_sdv_gated
                else stmt.where(PayableModel.is_sdv_gated.is_(False))
            )
        stmt = stmt.order_by(desc(PayableModel.created_at))
        result = await self.session.execute(stmt)
        return list(result.scalars().all())

    async def save_payable_allocations(
        self, allocations: list[PayableAllocationModel]
    ) -> list[PayableAllocationModel]:
        for alloc in allocations:
            if not alloc.id:
                alloc.id = str(uuid.uuid4())
            self.session.add(alloc)
        await self.session.commit()
        for alloc in allocations:
            await self.session.refresh(alloc)
        return allocations

    async def save_holdback_reserve(
        self, holdback: HoldbackReserveModel
    ) -> HoldbackReserveModel:
        if not holdback.id:
            holdback.id = str(uuid.uuid4())
        self.session.add(holdback)
        await self.session.commit()
        await self.session.refresh(holdback)
        return holdback

    async def get_holdback_reserve(
        self, holdback_id: str
    ) -> HoldbackReserveModel | None:
        stmt = select(HoldbackReserveModel).where(
            HoldbackReserveModel.id == holdback_id
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    async def list_holdback_reserves(
        self,
        study_id: str | None = None,
        site_id: str | None = None,
        status: str | None = None,
    ) -> list[HoldbackReserveModel]:
        stmt = select(HoldbackReserveModel)
        if study_id:
            stmt = stmt.where(HoldbackReserveModel.study_id == study_id)
        if site_id:
            stmt = stmt.where(HoldbackReserveModel.site_id == site_id)
        if status:
            stmt = stmt.where(HoldbackReserveModel.status == status)
        stmt = stmt.order_by(desc(HoldbackReserveModel.created_at))
        result = await self.session.execute(stmt)
        return list(result.scalars().all())

    async def record_audit_log(
        self,
        entity_type: str,
        entity_id: str,
        action: str,
        user_id: str,
        user_roles: str,
        previous_state: dict[str, Any] | None = None,
        new_state: dict[str, Any] | None = None,
        details: str | None = None,
        reason_for_change: str | None = None,
        version_index: int = 1,
    ) -> PaymentAuditLogModel:
        log_entry = PaymentAuditLogModel(
            id=str(uuid.uuid4()),
            entity_type=entity_type,
            entity_id=entity_id,
            action=action,
            previous_state=previous_state,
            new_state=new_state,
            details=details,
            user_id=user_id,
            user_roles=user_roles,
            created_by=user_id,
            reason_for_change=reason_for_change
            or f"Logged financial transaction {action}",
            version_index=version_index,
        )
        self.session.add(log_entry)
        await self.session.commit()
        await self.session.refresh(log_entry)
        return log_entry

    async def list_audit_logs(
        self,
        entity_type: str | None = None,
        entity_id: str | None = None,
        user_id: str | None = None,
    ) -> list[PaymentAuditLogModel]:
        stmt = select(PaymentAuditLogModel)
        if entity_type:
            stmt = stmt.where(PaymentAuditLogModel.entity_type == entity_type)
        if entity_id:
            stmt = stmt.where(PaymentAuditLogModel.entity_id == entity_id)
        if user_id:
            stmt = stmt.where(PaymentAuditLogModel.user_id == user_id)
        stmt = stmt.order_by(desc(PaymentAuditLogModel.created_at))
        result = await self.session.execute(stmt)
        return list(result.scalars().all())


async def get_payments_repository(
    session: AsyncSession = Depends(get_db_session),
) -> SQLAlchemyPaymentsRepository:
    """FastAPI dependency yielding a SQLAlchemyPaymentsRepository instance."""
    return SQLAlchemyPaymentsRepository(session)
