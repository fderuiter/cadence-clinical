"""FastAPI sub-router for 21 CFR Part 11 eConsent audit log queries."""

from fastapi import APIRouter, Depends, Query
from sqlalchemy import desc, select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.econsent.adapters.database import db_manager
from apps.econsent.adapters.models import ConsentAuditLog
from apps.econsent.presentation.dtos import ConsentAuditLogResponse
from packages.database import DatabaseSessionDependency

router = APIRouter(prefix="/api/v1/econsent/audit", tags=["Audit Trail"])
get_db_session = DatabaseSessionDependency(db_manager)


@router.get("", response_model=list[ConsentAuditLogResponse])
async def list_audit_logs(
    document_id: str | None = Query(
        None, description="Filter by document / record UUID"
    ),
    actor_id: str | None = Query(None, description="Filter by actor ID"),
    limit: int = Query(100, ge=1, le=1000, description="Max logs to return"),
    session: AsyncSession = Depends(get_db_session),
) -> list[ConsentAuditLogResponse]:
    """Retrieves 21 CFR Part 11 immutable audit trail records."""
    stmt = select(ConsentAuditLog)
    if document_id:
        stmt = stmt.where(ConsentAuditLog.document_id == document_id)
    if actor_id:
        stmt = stmt.where(ConsentAuditLog.actor_id == actor_id)
    stmt = stmt.order_by(desc(ConsentAuditLog.timestamp)).limit(limit)

    res = await session.execute(stmt)
    rows = res.scalars().all()
    return [
        ConsentAuditLogResponse(
            id=r.id,
            timestamp=r.timestamp,
            actor_id=r.actor_id,
            actor_role=r.actor_role,
            action=r.action,
            document_id=r.document_id,
            details=r.details,
            reason_for_change=r.reason_for_change,
        )
        for r in rows
    ]
