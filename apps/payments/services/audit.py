"""21 CFR Part 11 Financial Audit Logging for Site Payments.

Requirements: PRD-PAY-006, 21 CFR Part 11
"""

from __future__ import annotations

import uuid
from typing import Any

from sqlalchemy import desc, select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.payments.adapters.models import PaymentAuditLogModel


async def record_payment_audit_log(
    session: AsyncSession,
    entity_type: str,
    entity_id: str,
    action: str,
    user_id: str = "system",
    user_roles: str = "System",
    previous_state: dict[str, Any] | None = None,
    new_state: dict[str, Any] | None = None,
    details: str | None = None,
    reason_for_change: str | None = None,
    version_index: int = 1,
) -> PaymentAuditLogModel:
    """Records a tamper-evident financial audit log entry adhering to 21 CFR Part 11 standards."""
    log_entry = PaymentAuditLogModel(
        id=str(uuid.uuid4()),
        entity_type=entity_type,
        entity_id=entity_id,
        action=action,
        user_id=user_id,
        user_roles=user_roles,
        previous_state=previous_state,
        new_state=new_state,
        details=details,
        created_by=user_id or "system",
        reason_for_change=reason_for_change,
        version_index=version_index,
    )
    session.add(log_entry)
    await session.commit()
    await session.refresh(log_entry)
    return log_entry


async def list_payment_audit_logs(
    session: AsyncSession,
    entity_type: str | None = None,
    entity_id: str | None = None,
    action: str | None = None,
    user_id: str | None = None,
    limit: int = 100,
) -> list[PaymentAuditLogModel]:
    """Lists financial audit trail logs filtered by entity, action, or user."""
    stmt = select(PaymentAuditLogModel)
    if entity_type:
        stmt = stmt.where(PaymentAuditLogModel.entity_type == entity_type)
    if entity_id:
        stmt = stmt.where(PaymentAuditLogModel.entity_id == entity_id)
    if action:
        stmt = stmt.where(PaymentAuditLogModel.action == action)
    if user_id:
        stmt = stmt.where(PaymentAuditLogModel.user_id == user_id)
    stmt = stmt.order_by(desc(PaymentAuditLogModel.created_at)).limit(limit)
    result = await session.execute(stmt)
    return list(result.scalars().all())


__all__ = ["list_payment_audit_logs", "record_payment_audit_log"]
