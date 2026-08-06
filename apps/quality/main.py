import os
import sys
from datetime import datetime

from fastapi import Depends, FastAPI, HTTPException, Query, Request
from pydantic import BaseModel, ConfigDict, Field

from apps.quality.adapters.repository import SQLQualityRepository
from apps.quality.database import db_manager
from apps.quality.models import (
    Base,
    CAPARecord,
    CAPAStatus,
    Deviation,
    DeviationSeverity,
    DeviationStatus,
    DeviationType,
    QualityAuditLog,
    RootCauseAnalysis,
)
from apps.quality.services.quality_service import QualityService
from packages.database import get_relational_db_lifespan
from packages.security import assert_secure_secrets
from packages.security.middleware import GatewayAuthMiddleware
from packages.security.rbac import (
    Principal,
    get_principal,
    has_permission,
)


# Pydantic Schemas for Request/Response Validation
class DeviationCreate(BaseModel):
    study_id: str = Field(..., description="Unique identifier of the clinical study")
    site_id: str | None = Field(None, description="Optional clinical site ID")
    title: str = Field(
        ..., max_length=255, description="A short summary of the deviation"
    )
    description: str = Field(..., description="Detailed explanation of the deviation")
    severity: DeviationSeverity = Field(
        ..., description="Severity level: MINOR, MAJOR, CRITICAL"
    )
    type: DeviationType = Field(
        ..., description="Type of deviation, e.g., INFORMED_CONSENT"
    )
    is_protocol_violation: bool = Field(
        False, description="Whether this constitutes a protocol violation"
    )


class DeviationResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    study_id: str
    site_id: str | None = None
    title: str
    description: str
    severity: DeviationSeverity
    status: DeviationStatus
    type: DeviationType
    is_protocol_violation: bool
    created_at: str
    created_by: str
    version_index: int
    reason_for_change: str


class RCACreateOrUpdate(BaseModel):
    methodology: str = Field(
        ..., max_length=255, description="RCA methodology used, e.g., 5 Whys, Fishbone"
    )
    investigation_details: str = Field(
        ..., description="Full details of the investigation"
    )
    root_cause_summary: str = Field(
        ..., description="Summary of the determined root cause"
    )
    version_index: int | None = Field(
        None, description="Current expected version index for optimistic locking"
    )


class RCAResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    deviation_id: str
    methodology: str
    investigation_details: str
    root_cause_summary: str
    study_id: str
    site_id: str | None = None
    created_at: str
    created_by: str
    version_index: int
    reason_for_change: str


class CAPACreate(BaseModel):
    deviation_id: str = Field(..., description="Reference to the parent deviation ID")
    rca_id: str | None = Field(
        None, description="Optional reference to the Root Cause Analysis ID"
    )
    capa_type: str = Field(..., description="Type of CAPA: CORRECTIVE or PREVENTIVE")
    action_plan: str = Field(
        ..., description="The planned corrective/preventive action steps"
    )
    preventive_measures: str | None = Field(
        None, description="Specific measures to prevent recurrence"
    )
    target_completion_date: datetime | None = Field(
        None, description="Optional expected completion timestamp"
    )


class CAPATransitionRequest(BaseModel):
    to_status: CAPAStatus = Field(
        ..., description="Target CAPA Status to transition to"
    )
    version_index: int | None = Field(
        None, description="Expected version index for optimistic locking"
    )


class CAPAUpdate(BaseModel):
    action_plan: str | None = Field(
        None, description="The planned corrective/preventive action steps"
    )
    preventive_measures: str | None = Field(
        None, description="Specific measures to prevent recurrence"
    )
    target_completion_date: datetime | None = Field(
        None, description="Optional expected completion timestamp"
    )
    version_index: int | None = Field(
        None, description="Current expected version index for optimistic locking"
    )


class CAPAResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    deviation_id: str
    rca_id: str | None = None
    capa_type: str
    action_plan: str
    status: CAPAStatus
    preventive_measures: str | None = None
    target_completion_date: str | None = None
    study_id: str
    site_id: str | None = None
    created_at: str
    created_by: str
    version_index: int
    reason_for_change: str


DATABASE_URL = os.getenv("QUALITY_DATABASE_URL", "sqlite+aiosqlite:///:memory:")

assert_secure_secrets("quality", {"GATEWAY_SECRET": os.getenv("GATEWAY_SECRET")})


BRAND_NAME = os.getenv("BRAND_NAME", "Cadence Clinical")


def validate_branding_and_domain() -> None:
    app_env = os.getenv("APP_ENV", "").strip().lower()
    is_prod_or_staging = app_env not in ("development", "dev", "test", "")
    if is_prod_or_staging:
        invalid = []
        if not os.getenv("BRAND_NAME") or os.getenv("BRAND_NAME") == "Cadence Clinical":
            invalid.append("BRAND_NAME")
        if (
            not os.getenv("BRAND_DOMAIN")
            or os.getenv("BRAND_DOMAIN") == "cadenceclinical.com"
        ):
            invalid.append("BRAND_DOMAIN")
        if invalid:
            error_msg = f"STARTUP ERROR: Outdated default 'Cadence' branding or missing secure configurations detected in environment '{app_env}' for variables: {', '.join(invalid)}. Halting boot sequence."
            print(error_msg, file=sys.stderr)
            sys.exit(1)


validate_branding_and_domain()


app = FastAPI(
    title=f"{BRAND_NAME} - Quality & CAPA",
    version="0.1.0",
    lifespan=get_relational_db_lifespan(
        db_manager=db_manager,
        database_url=DATABASE_URL,
        base_metadata=Base.metadata,
    ),
)

# Enforce secure gateway authentication middleware
app.add_middleware(GatewayAuthMiddleware)


_repo_instance = SQLQualityRepository()


def get_quality_service() -> QualityService:
    """FastAPI dependency to retrieve the decoupled clinical service."""
    return QualityService(_repo_instance)


async def write_audit_log(
    session,
    user_id: str,
    user_role: str,
    action: str,
    details: str,
    record_id: str | None = None,
    change_reason: str | None = None,
) -> None:
    """Utility helper to write to the append-only QualityAuditLog (backward compatibility)."""
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


def authorize_quality_write(principal: Principal) -> list[str]:
    if not has_permission(principal, "quality_event:create"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Read-only roles are restricted to read-only access.",
        )
    return principal.roles


def authorize_quality_oversight(principal: Principal) -> list[str]:
    if not has_permission(principal, "quality_event:investigate"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Quality oversight role required for CAPA approval or closure.",
        )
    return principal.roles


def get_user_context(principal: Principal):
    user_id = principal.user_id
    user_role = ",".join(principal.raw_roles) if principal.raw_roles else "system"
    change_reason = principal.change_reason
    return user_id, user_role, change_reason


@app.get("/health")
async def health_check() -> dict[str, str]:
    """
    Service health check endpoint.
    """
    return {"status": "ok", "service": "quality"}


# CAPA explicit transition map
CAPA_TRANSITIONS = {
    CAPAStatus.INITIATED: {CAPAStatus.UNDER_REVIEW, CAPAStatus.CANCELLED},
    CAPAStatus.UNDER_REVIEW: {
        CAPAStatus.IMPLEMENTATION,
        CAPAStatus.INITIATED,
        CAPAStatus.CANCELLED,
    },
    CAPAStatus.IMPLEMENTATION: {CAPAStatus.EFFECTIVENESS_CHECK, CAPAStatus.CANCELLED},
    CAPAStatus.EFFECTIVENESS_CHECK: {CAPAStatus.CLOSED, CAPAStatus.CANCELLED},
    CAPAStatus.CLOSED: set(),
    CAPAStatus.CANCELLED: set(),
}


def map_deviation_to_response(dev: Deviation) -> DeviationResponse:
    return DeviationResponse(
        id=dev.id,
        study_id=dev.study_id,
        site_id=dev.site_id,
        title=dev.title,
        description=dev.description,
        severity=dev.severity,
        status=dev.status,
        type=dev.type,
        is_protocol_violation=dev.is_protocol_violation,
        created_at=dev.created_at.isoformat(),
        created_by=dev.created_by,
        version_index=dev.version_index,
        reason_for_change=dev.reason_for_change,
    )


def map_rca_to_response(rca: RootCauseAnalysis) -> RCAResponse:
    return RCAResponse(
        id=rca.id,
        deviation_id=rca.deviation_id,
        methodology=rca.methodology,
        investigation_details=rca.investigation_details,
        root_cause_summary=rca.root_cause_summary,
        study_id=rca.study_id,
        site_id=rca.site_id,
        created_at=rca.created_at.isoformat(),
        created_by=rca.created_by,
        version_index=rca.version_index,
        reason_for_change=rca.reason_for_change,
    )


def map_capa_to_response(capa: CAPARecord) -> CAPAResponse:
    return CAPAResponse(
        id=capa.id,
        deviation_id=capa.deviation_id,
        rca_id=capa.rca_id,
        capa_type=capa.capa_type,
        action_plan=capa.action_plan,
        status=capa.status,
        preventive_measures=capa.preventive_measures,
        target_completion_date=(
            capa.target_completion_date.isoformat()
            if capa.target_completion_date
            else None
        ),
        study_id=capa.study_id,
        site_id=capa.site_id,
        created_at=capa.created_at.isoformat(),
        created_by=capa.created_by,
        version_index=capa.version_index,
        reason_for_change=capa.reason_for_change,
    )


@app.post(
    "/api/v1/quality/deviations", response_model=DeviationResponse, status_code=201
)
async def create_deviation(
    request: Request,
    payload: DeviationCreate,
    service: QualityService = Depends(get_quality_service),
    principal: Principal = Depends(get_principal),
):
    """
    Create a new clinical protocol deviation or quality deviation event.
    """
    authorize_quality_write(principal)
    user_id, user_role, change_reason = get_user_context(principal)
    dev = await service.create_deviation(payload, user_id, user_role, change_reason)
    return map_deviation_to_response(dev)


@app.get("/api/v1/quality/deviations", response_model=list[DeviationResponse])
async def list_deviations(
    request: Request,
    study_id: str | None = Query(None, description="Filter by study ID"),
    site_id: str | None = Query(None, description="Filter by site ID"),
    status: DeviationStatus | None = Query(None, description="Filter by status"),
    service: QualityService = Depends(get_quality_service),
    principal: Principal = Depends(get_principal),
):
    """
    Retrieve clinical deviation records with optional filtering.
    """
    user_id, user_role, change_reason = get_user_context(principal)
    deviations = await service.list_deviations(
        study_id, site_id, status, user_id, user_role
    )
    return [map_deviation_to_response(dev) for dev in deviations]


@app.get("/api/v1/quality/deviations/{id}", response_model=DeviationResponse)
async def view_deviation(
    request: Request,
    id: str,
    service: QualityService = Depends(get_quality_service),
    principal: Principal = Depends(get_principal),
):
    """
    Retrieve a specific clinical deviation by ID.
    """
    user_id, user_role, change_reason = get_user_context(principal)
    dev = await service.view_deviation(id, user_id, user_role)
    return map_deviation_to_response(dev)


@app.post("/api/v1/quality/deviations/{id}/rca", response_model=RCAResponse)
@app.put("/api/v1/quality/deviations/{id}/rca", response_model=RCAResponse)
async def create_or_update_rca(
    request: Request,
    id: str,
    payload: RCACreateOrUpdate,
    service: QualityService = Depends(get_quality_service),
    principal: Principal = Depends(get_principal),
):
    """
    CREATE or UPDATE Root Cause Analysis (RCA) linked to a specific deviation.
    Transitions the deviation status to RCA_COMPLETE.
    """
    authorize_quality_write(principal)
    user_id, user_role, change_reason = get_user_context(principal)
    rca = await service.create_or_update_rca(
        id, payload, user_id, user_role, change_reason
    )
    return map_rca_to_response(rca)


@app.post("/api/v1/quality/capas", response_model=CAPAResponse, status_code=201)
async def create_capa(
    request: Request,
    payload: CAPACreate,
    service: QualityService = Depends(get_quality_service),
    principal: Principal = Depends(get_principal),
):
    """
    CREATE a new Corrective and Preventive Action (CAPA) record linked to a deviation.
    """
    authorize_quality_write(principal)
    user_id, user_role, change_reason = get_user_context(principal)
    capa = await service.create_capa(payload, user_id, user_role, change_reason)
    return map_capa_to_response(capa)


@app.post("/api/v1/quality/capas/{id}/transition", response_model=CAPAResponse)
async def transition_capa(
    request: Request,
    id: str,
    payload: CAPATransitionRequest,
    service: QualityService = Depends(get_quality_service),
    principal: Principal = Depends(get_principal),
):
    """
    Perform a secure, 21 CFR Part 11 compliant status transition on a CAPA record.
    """
    if payload.to_status in (CAPAStatus.CLOSED, CAPAStatus.CANCELLED):
        authorize_quality_oversight(principal)

        # Enforce semantic step-up authentication gating
        from packages.security.middleware import (
            downstream_replay_cache,
            verify_sig_token,
        )
        from packages.security.regulated_actions import SemanticAction

        sig_token = request.headers.get("X-Sig-Token") or request.headers.get(
            "x-sig-token"
        )
        secret = os.getenv(
            "GATEWAY_SECRET", "internal-gateway-secret-12345"
        ).encode()  # pragma: allowlist secret
        expected_semantic = (
            SemanticAction.CAPA_CLOSE
            if payload.to_status == CAPAStatus.CLOSED
            else SemanticAction.CAPA_CANCEL
        )

        success, result = verify_sig_token(
            sig_token=sig_token,
            user_id=principal.user_id,
            request_path=request.url.path,
            secret=secret,
            replay_cache=downstream_replay_cache,
            expected_semantic_action=expected_semantic,
            check_replay=False,
        )
        if not success:
            raise HTTPException(status_code=401, detail="REAUTHENTICATION_REQUIRED")
    else:
        authorize_quality_write(principal)

    user_id, user_role, change_reason = get_user_context(principal)
    if not change_reason:
        raise HTTPException(
            status_code=403, detail="Missing change justification reason"
        )
    capa = await service.transition_capa(
        id, payload.to_status, payload.version_index, user_id, user_role, change_reason
    )
    return map_capa_to_response(capa)


@app.put("/api/v1/quality/capas/{id}", response_model=CAPAResponse)
async def update_capa(
    request: Request,
    id: str,
    payload: CAPAUpdate,
    service: QualityService = Depends(get_quality_service),
    principal: Principal = Depends(get_principal),
):
    """
    UPDATE non-status attributes of a CAPA record. Disallowed once terminal (CLOSED/CANCELLED).
    """
    authorize_quality_write(principal)
    user_id, user_role, change_reason = get_user_context(principal)
    capa = await service.update_capa(id, payload, user_id, user_role, change_reason)
    return map_capa_to_response(capa)


class AuditLogResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    timestamp: str
    user_id: str
    user_role: str
    action: str
    details: str
    record_id: str | None = None
    change_reason: str | None = None


@app.get("/api/v1/quality/audit-logs", response_model=list[AuditLogResponse])
async def list_audit_logs(
    request: Request,
    service: QualityService = Depends(get_quality_service),
    principal: Principal = Depends(get_principal),
):
    """
    Retrieve quality audit logs in descending chronological order.
    """
    user_id, user_role, change_reason = get_user_context(principal)
    logs = await service.list_audit_logs(user_id, user_role)
    return [
        AuditLogResponse(
            id=log.id,
            timestamp=log.timestamp.isoformat() if log.timestamp else None,
            user_id=log.user_id,
            user_role=log.user_role,
            action=log.action,
            details=log.details,
            record_id=log.record_id,
            change_reason=log.change_reason,
        )
        for log in logs
    ]
