"""21 CFR Part 11 Financial Audit Logging helper for Site Payments microservice.

Owns: Immutable financial audit log recording and querying across all payment engines.

Requirements: ADR-2199, PRD-PAY-002, PRD-PAY-003, PRD-PAY-004, PRD-PAY-006
"""

from __future__ import annotations

import uuid
from typing import Any

from sqlalchemy import desc, select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.payments.infrastructure.models import PaymentAuditLogModel


async def record_payment_audit_log(
    session: AsyncSession,
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
    """Records an immutable 21 CFR Part 11 compliant audit trail entry.

    Args:
        session: Active SQLAlchemy async database session.
        entity_type: Category of the financial entity (e.g. CONTRACT, PAYABLE, INVOICE).
        entity_id: Unique identifier of the audited entity.
        action: Audit action verb (e.g. CREATE, UPDATE, APPROVE, DISBURSE).
        user_id: Identity of the executing principal or system actor.
        user_roles: Role permissions of the principal.
        previous_state: Optional snapshot of entity attributes before modification.
        new_state: Optional snapshot of entity attributes after modification.
        details: Human-readable narrative or operational context.
        reason_for_change: 21 CFR Part 11 required rationale for modification.
        version_index: Monotonically increasing version counter.

    Returns:
        Persisted PaymentAuditLogModel record with database timestamp.
    """
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
        reason_for_change=reason_for_change or f"Logged financial transaction {action}",
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
    user_id: str | None = None,
) -> list[PaymentAuditLogModel]:
    """Retrieves financial audit log entries matching filtering criteria.

    Args:
        session: Active SQLAlchemy async database session.
        entity_type: Optional entity type filter.
        entity_id: Optional entity ID filter.
        user_id: Optional user ID filter.

    Returns:
        List of PaymentAuditLogModel records ordered chronologically descending.
    """
    stmt = select(PaymentAuditLogModel)
    if entity_type:
        stmt = stmt.where(PaymentAuditLogModel.entity_type == entity_type)
    if entity_id:
        stmt = stmt.where(PaymentAuditLogModel.entity_id == entity_id)
    if user_id:
        stmt = stmt.where(PaymentAuditLogModel.user_id == user_id)
    stmt = stmt.order_by(desc(PaymentAuditLogModel.created_at))
    result = await session.execute(stmt)
    return list(result.scalars().all())


__all__ = [
    "list_payment_audit_logs",
    "record_payment_audit_log",
]
