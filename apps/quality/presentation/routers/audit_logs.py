from datetime import datetime

from fastapi import APIRouter, Depends, Query, Request

from apps.quality.adapters.database import transactional
from apps.quality.application.services.quality_service import QualityService
from apps.quality.presentation.dtos import AuditLogResponse
from packages.security.rbac import Principal, get_principal

router = APIRouter()


def get_quality_service() -> QualityService:
    import apps.quality.main as main_module

    return main_module.get_quality_service()


def get_user_context(principal: Principal):
    import apps.quality.main as main_module

    return main_module.get_user_context(principal)


@router.get("/api/v1/quality/audit-logs", response_model=list[AuditLogResponse])
@transactional
async def list_audit_logs(
    request: Request,
    user_id: str | None = Query(None, description="Filter logs by actor/user ID"),
    action: str | None = Query(None, description="Filter logs by action type"),
    entity_type: str | None = Query(None, description="Filter logs by entity type"),
    start_date: datetime | str | None = Query(
        None, description="Filter logs from start date"
    ),
    end_date: datetime | str | None = Query(
        None, description="Filter logs up to end date"
    ),
    record_id: str | None = Query(None, description="Filter logs by record ID"),
    limit: int = Query(100, ge=1, le=500, description="Max logs to return"),
    offset: int = Query(0, ge=0, description="Pagination offset"),
    service: QualityService = Depends(get_quality_service),
    principal: Principal = Depends(get_principal),
):
    req_user_id, user_role, change_reason = get_user_context(principal)
    logs = await service.list_audit_logs(req_user_id, user_role)

    filtered = []
    for log in logs:
        if user_id and getattr(log, "user_id", None) != user_id:
            continue
        if action and getattr(log, "action", None) != action:
            continue
        if record_id and getattr(log, "record_id", None) != record_id:
            continue
        if entity_type:
            log_ent = getattr(log, "entity_type", None)
            log_details = getattr(log, "details", "") or ""
            log_action = getattr(log, "action", "") or ""
            if not (
                (log_ent and log_ent.lower() == entity_type.lower())
                or (entity_type.lower() in log_details.lower())
                or (entity_type.lower() in log_action.lower())
            ):
                continue
        if start_date:
            ts = getattr(log, "timestamp", None)
            if ts:
                ts_str = ts.isoformat() if isinstance(ts, datetime) else str(ts)
                start_str = (
                    start_date.isoformat()
                    if isinstance(start_date, datetime)
                    else str(start_date)
                )
                if ts_str < start_str:
                    continue
        if end_date:
            ts = getattr(log, "timestamp", None)
            if ts:
                ts_str = ts.isoformat() if isinstance(ts, datetime) else str(ts)
                end_str = (
                    end_date.isoformat()
                    if isinstance(end_date, datetime)
                    else str(end_date)
                )
                if ts_str > end_str:
                    continue
        filtered.append(log)

    paginated = filtered[offset : offset + limit] if (offset or limit) else filtered

    return [
        AuditLogResponse(
            id=log.id,
            timestamp=log.timestamp.isoformat()
            if isinstance(log.timestamp, datetime)
            else str(log.timestamp or ""),
            user_id=log.user_id,
            user_role=log.user_role,
            action=log.action,
            details=log.details,
            entity_type=getattr(log, "entity_type", None),
            record_id=log.record_id,
            old_value=getattr(log, "old_value", None),
            new_value=getattr(log, "new_value", None),
            change_reason=log.change_reason,
            merkle_hash=log.merkle_hash,
            sha256_hash=getattr(log, "sha256_hash", getattr(log, "merkle_hash", None)),
            signature_hash=getattr(log, "signature_hash", None),
        )
        for log in paginated
    ]
