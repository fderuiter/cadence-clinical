import os

from fastapi import FastAPI
from fastapi.responses import JSONResponse

from apps.quality.adapters.database import db_manager
from apps.quality.adapters.models import Base, QualityAuditLog
from apps.quality.adapters.repositories import SQLQualityRepository
from apps.quality.application.services.audit_service import (
    AuditServiceError,
    ClinicalAuditService,
)
from apps.quality.application.services.quality_service import (
    CAPA_TRANSITIONS,
    QualityService,
    QualityServiceError,
)
from apps.quality.application.services.rbqm_service import (
    RBQMService,
    RBQMServiceError,
)
from apps.quality.application.services.serious_breach_service import (
    SeriousBreachService,
    SeriousBreachServiceError,
)
from apps.quality.presentation.dtos import (
    AuditLogResponse,
    CAPACreate,
    CAPAResponse,
    CAPATransitionRequest,
    CAPAUpdate,
    DeviationCreate,
    DeviationResponse,
    RCACreateOrUpdate,
    RCAResponse,
)
from apps.quality.presentation.routers.quality import (
    authorize_quality_oversight,
    authorize_quality_write,
    map_capa_to_response,
    map_deviation_to_response,
    map_rca_to_response,
)
from apps.quality.presentation.routers.quality import (
    router as quality_router,
)
from packages.database import get_relational_db_lifespan
from packages.hexagonal import register_rfc7807_handlers
from packages.security import assert_secure_secrets, validate_branding
from packages.security.middleware import GatewayAuthMiddleware
from packages.security.rbac import Principal

DATABASE_URL = os.getenv("QUALITY_DATABASE_URL", "sqlite+aiosqlite:///:memory:")

assert_secure_secrets("quality", {"GATEWAY_SECRET": os.getenv("GATEWAY_SECRET")})

BRAND_NAME = os.getenv("BRAND_NAME", "Cadence Clinical")

validate_branding("quality")
app = FastAPI(
    title=f"{BRAND_NAME} - Quality & CAPA",
    version="0.2.0",
    lifespan=get_relational_db_lifespan(
        db_manager=db_manager,
        database_url=DATABASE_URL,
        base_metadata=Base.metadata,
    ),
)

app.add_middleware(GatewayAuthMiddleware)
register_rfc7807_handlers(app)


@app.exception_handler(QualityServiceError)
async def quality_service_error_handler(request, exc: QualityServiceError):
    return JSONResponse(
        status_code=exc.status_code,
        content={"detail": str(exc)},
    )


@app.exception_handler(RBQMServiceError)
async def rbqm_service_error_handler(request, exc: RBQMServiceError):
    return JSONResponse(
        status_code=exc.status_code,
        content={"detail": str(exc)},
    )


@app.exception_handler(AuditServiceError)
async def audit_service_error_handler(request, exc: AuditServiceError):
    return JSONResponse(
        status_code=exc.status_code,
        content={"detail": str(exc)},
    )


@app.exception_handler(SeriousBreachServiceError)
async def serious_breach_service_error_handler(request, exc: SeriousBreachServiceError):
    return JSONResponse(
        status_code=exc.status_code,
        content={"detail": str(exc)},
    )


_repo_instance = SQLQualityRepository()


def get_quality_service() -> QualityService:
    """FastAPI dependency to retrieve the QualityService."""
    return QualityService(_repo_instance)


def get_rbqm_service() -> RBQMService:
    """FastAPI dependency to retrieve the RBQMService."""
    return RBQMService(_repo_instance)


def get_audit_service() -> ClinicalAuditService:
    """FastAPI dependency to retrieve the ClinicalAuditService."""
    return ClinicalAuditService(_repo_instance)


def get_serious_breach_service() -> SeriousBreachService:
    """FastAPI dependency to retrieve the SeriousBreachService."""
    return SeriousBreachService(_repo_instance)


def get_user_context(principal: Principal):
    user_id = principal.user_id
    user_role = ",".join(principal.raw_roles) if principal.raw_roles else "system"
    change_reason = principal.change_reason
    return user_id, user_role, change_reason


async def write_audit_log(
    session,
    user_id: str,
    user_role: str,
    action: str,
    details: str,
    record_id: str | None = None,
    change_reason: str | None = None,
) -> None:
    """Utility helper to write to the append-only QualityAuditLog."""
    log_entry = QualityAuditLog(
        user_id=user_id,
        user_role=user_role,
        action=action,
        details=details,
        record_id=record_id,
        change_reason=change_reason,
    )
    session.add(log_entry)
    await session.flush()


@app.get("/health")
async def health_check() -> dict[str, str]:
    """Service health check endpoint."""
    return {"status": "ok", "service": "quality"}


app.include_router(quality_router)

__all__ = [
    "AuditLogResponse",
    "CAPACreate",
    "CAPAResponse",
    "CAPATransitionRequest",
    "CAPAUpdate",
    "CAPA_TRANSITIONS",
    "DeviationCreate",
    "DeviationResponse",
    "RCACreateOrUpdate",
    "RCAResponse",
    "_repo_instance",
    "app",
    "authorize_quality_oversight",
    "authorize_quality_write",
    "get_audit_service",
    "get_quality_service",
    "get_rbqm_service",
    "get_serious_breach_service",
    "get_user_context",
    "map_capa_to_response",
    "map_deviation_to_response",
    "map_rca_to_response",
    "write_audit_log",
]
