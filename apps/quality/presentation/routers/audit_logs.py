from fastapi import APIRouter, Depends, Request

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
    service: QualityService = Depends(get_quality_service),
    principal: Principal = Depends(get_principal),
):
    user_id, user_role, change_reason = get_user_context(principal)
    logs = await service.list_audit_logs(user_id, user_role)
    return [
        AuditLogResponse(
            id=log.id,
            timestamp=log.timestamp.isoformat() if log.timestamp else "",
            user_id=log.user_id,
            user_role=log.user_role,
            action=log.action,
            details=log.details,
            record_id=log.record_id,
            change_reason=log.change_reason,
            merkle_hash=log.merkle_hash,
        )
        for log in logs
    ]
