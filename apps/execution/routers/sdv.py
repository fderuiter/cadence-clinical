"""FastAPI router for Targeted SDV (TSDV) and SDV sign-off API endpoints.

Requirements: PRD-QRY-005, PRD-QRY-006, PRD-QRY-007
"""

from datetime import datetime, timezone
from enum import Enum
from typing import List, Optional

from fastapi import APIRouter, Depends, HTTPException, Request
from pydantic import BaseModel, Field, model_validator
from sqlalchemy import select, text

from apps.execution.database.context import current_user_id
from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    ClinicalObservation,
    ClinicalSubject,
    ClinicalVisit,
    SDVSignOff,
    TSDVConfig,
)
from apps.execution.tsdv import evaluate_tsdv_requirement
from packages.security.rbac import (
    ROLE_CRA,
    ROLE_DATA_MANAGER,
    Principal,
    can_access_study,
    get_principal,
    require_permission,
    require_roles,
)

router = APIRouter(prefix="/api/v1/execution", tags=["SDV/TSDV"])


# ==========================================
# Study Scope Guard
# ==========================================


class StudyScopeChecker:
    async def __call__(
        self, request: Request, principal: Principal = Depends(get_principal)
    ) -> Principal:
        study_id = (
            request.path_params.get("study_id")
            or request.query_params.get("study_id")
            or request.headers.get("X-Study-Id")
            or request.headers.get("x-study-id")
        )
        if not study_id:
            try:
                content_type = request.headers.get("content-type", "")
                if "application/json" in content_type:
                    body_bytes = await request.body()
                    if body_bytes:
                        import json

                        body = json.loads(body_bytes)
                        if isinstance(body, dict):
                            study_id = body.get("study_id") or body.get("id")

                        async def receive():
                            return {
                                "type": "http.request",
                                "body": body_bytes,
                                "more_body": False,
                            }

                        request._receive = receive
            except Exception:
                pass

        if study_id:
            study_id = str(study_id).strip()

        if study_id:
            if not can_access_study(principal, study_id):
                raise HTTPException(
                    status_code=403,
                    detail="Forbidden: Insufficient scope access for this study.",
                )

        return principal


def require_study_scope() -> StudyScopeChecker:
    return StudyScopeChecker()


# ==========================================
# Pydantic Schemas
# ==========================================


class SamplingModelEnum(str, Enum):
    SUBJECT_BASED = "SUBJECT_BASED"
    FIELD_BASED = "FIELD_BASED"
    COMBINED = "COMBINED"


class TSDVConfigCreate(BaseModel):
    study_id: str
    sampling_model: SamplingModelEnum
    initial_full_sdv_subject_count: int = Field(default=0, ge=0)
    random_sample_percentage: float = Field(default=0.0, ge=0.0, le=100.0)
    full_sdv_domains: Optional[List[str]] = None
    safety_endpoints: Optional[List[str]] = None
    zero_sdv_domains: Optional[List[str]] = None
    trial_random_seed: Optional[int] = Field(default=None, ge=0)

    @model_validator(mode="after")
    def validate_seed(self) -> "TSDVConfigCreate":
        if self.random_sample_percentage > 0.0 and self.trial_random_seed is None:
            raise ValueError(
                "trial_random_seed is required when random_sample_percentage is greater than 0"
            )
        return self


class TSDVConfigResponse(BaseModel):
    id: str
    study_id: str
    sampling_model: str
    initial_full_sdv_subject_count: int
    random_sample_percentage: float
    full_sdv_domains: Optional[List[str]] = None
    safety_endpoints: Optional[List[str]] = None
    zero_sdv_domains: Optional[List[str]] = None
    trial_random_seed: Optional[int] = None
    version: int

    class Config:
        from_attributes = True


class TSDVEvaluationResponse(BaseModel):
    required: bool
    subject_selected: bool
    field_decision: Optional[bool] = None
    sampling_model: str
    config_id: str
    enrollment_index: int
    explanation: str


class SDVScopeEnum(str, Enum):
    FIELD = "FIELD"
    PAGE = "PAGE"
    VISIT = "VISIT"


class SDVSignOffRequest(BaseModel):
    """Pydantic request schema for SDV sign-off."""

    scope: SDVScopeEnum
    target_id: str
    subject_id: str
    study_id: str
    site_id: Optional[str] = None


class SDVSignOffResponse(BaseModel):
    """Pydantic response schema for SDV sign-off."""

    id: str
    scope: str
    target_id: str
    subject_id: str
    study_id: str
    site_id: Optional[str] = None
    is_verified: bool
    verified_by: Optional[str] = None
    verified_at: Optional[datetime] = None
    dropped_reason: Optional[str] = None
    dropped_at: Optional[datetime] = None


# ==========================================
# SDV/TSDV Endpoints
# ==========================================


@router.post(
    "/tsdv/config",
    response_model=TSDVConfigResponse,
    status_code=201,
)
async def create_or_update_tsdv_config(
    request: Request,
    payload: TSDVConfigCreate,
    roles: list[str] = Depends(require_roles(ROLE_CRA, ROLE_DATA_MANAGER)),
    _study_scope: Principal = Depends(require_study_scope()),
) -> TSDVConfig:
    """Create or update Targeted SDV (TSDV) configuration for a study.

    Restricts config writes to CRA/Data Manager roles with GxP change justifications.
    """
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', true);")
            )
            stmt = select(TSDVConfig).where(TSDVConfig.study_id == payload.study_id)
            res = await session.execute(stmt)
            config = res.scalars().first()

            if config:
                config.sampling_model = payload.sampling_model.value
                config.initial_full_sdv_subject_count = (
                    payload.initial_full_sdv_subject_count
                )
                config.random_sample_percentage = payload.random_sample_percentage
                config.full_sdv_domains = payload.full_sdv_domains
                config.safety_endpoints = payload.safety_endpoints
                config.zero_sdv_domains = payload.zero_sdv_domains
                config.trial_random_seed = payload.trial_random_seed
            else:
                config = TSDVConfig(
                    study_id=payload.study_id,
                    sampling_model=payload.sampling_model.value,
                    initial_full_sdv_subject_count=payload.initial_full_sdv_subject_count,
                    random_sample_percentage=payload.random_sample_percentage,
                    full_sdv_domains=payload.full_sdv_domains,
                    safety_endpoints=payload.safety_endpoints,
                    zero_sdv_domains=payload.zero_sdv_domains,
                    trial_random_seed=payload.trial_random_seed,
                )
                session.add(config)

    async with db_manager.get_session_maker()() as session:
        stmt = select(TSDVConfig).where(TSDVConfig.study_id == payload.study_id)
        res = await session.execute(stmt)
        config = res.scalars().one()
        return config


@router.get(
    "/tsdv/config/{study_id}",
    response_model=TSDVConfigResponse,
)
async def get_tsdv_config(
    study_id: str,
    principal: Principal = Depends(require_permission("sdv:read")),
    _study_scope: Principal = Depends(require_study_scope()),
) -> TSDVConfig:
    """Retrieve existing TSDV configuration for a study."""
    async with db_manager.get_session_maker()() as session:
        stmt = select(TSDVConfig).where(TSDVConfig.study_id == study_id)
        res = await session.execute(stmt)
        config = res.scalars().first()
        if not config:
            raise HTTPException(
                status_code=404,
                detail=f"TSDV configuration not found for study {study_id}",
            )
        return config


@router.get(
    "/tsdv/required",
    response_model=TSDVEvaluationResponse,
)
async def evaluate_tsdv_rule(
    study_id: str,
    subject_id: str,
    domain: Optional[str] = None,
    enrollment_index: Optional[int] = None,
    principal: Principal = Depends(require_permission("sdv:read")),
    _study_scope: Principal = Depends(require_study_scope()),
) -> TSDVEvaluationResponse:
    """Evaluate Targeted SDV (TSDV) requirement for a given context.

    Calculates deterministic sampling decisions and returns component results with an audit explanation.
    """
    async with db_manager.get_session_maker()() as session:
        # 1. Resolve Study TSDV Configuration
        stmt_cfg = select(TSDVConfig).where(TSDVConfig.study_id == study_id)
        res_cfg = await session.execute(stmt_cfg)
        config = res_cfg.scalars().first()
        if not config:
            raise HTTPException(
                status_code=404,
                detail=f"TSDV configuration not found for study {study_id}",
            )

        # 2. Resolve Subject and Enrollment Index
        stmt_subj = select(ClinicalSubject).where(
            ClinicalSubject.study_id == study_id,
            ClinicalSubject.is_deleted.is_(False),
        )
        res_subj = await session.execute(stmt_subj)
        subjects = list(res_subj.scalars().all())

        # Sort alphabetically as a deterministic fallback only
        subjects_sorted = sorted(subjects, key=lambda s: s.subject_id)

        target_sub = None
        fallback_index = None
        for idx, sub in enumerate(subjects_sorted):
            if sub.subject_id == subject_id or sub.id == subject_id:
                target_sub = sub
                fallback_index = idx
                break

        if target_sub is None:
            raise HTTPException(
                status_code=404,
                detail=f"Subject {subject_id} not found in study {study_id}",
            )

        # Resolve persisted enrollment_index, with alphabetical as fallback if not backfilled yet
        resolved_index = (
            target_sub.enrollment_index
            if target_sub.enrollment_index is not None
            else fallback_index
        )

        if enrollment_index is not None:
            if enrollment_index != resolved_index:
                raise HTTPException(
                    status_code=400,
                    detail=f"Conflicting enrollment_index {enrollment_index} supplied. Persisted index is {resolved_index}.",
                )
        else:
            enrollment_index = resolved_index

        subject_uuid = target_sub.id

        # 3. Perform Deterministic Evaluation
        required, subject_selected, field_decision, explanation = (
            evaluate_tsdv_requirement(
                config=config,
                subject_uuid=subject_uuid,
                enrollment_index=enrollment_index,
                domain=domain,
            )
        )

        return TSDVEvaluationResponse(
            required=required,
            subject_selected=subject_selected,
            field_decision=field_decision,
            sampling_model=config.sampling_model,
            config_id=config.id,
            enrollment_index=enrollment_index,
            explanation=explanation,
        )


@router.post(
    "/sdv/signoff",
    response_model=SDVSignOffResponse,
)
async def sdv_signoff(
    payload: SDVSignOffRequest,
    principal: Principal = Depends(require_permission("sdv:create")),
    _study_scope: Principal = Depends(require_study_scope()),
) -> SDVSignOffResponse:
    """CRA/monitor-gated SDV sign-off endpoint for Field, Page, or Visit scopes."""
    from apps.execution.sdv_helper import validate_and_upsert_sdv_target

    async with db_manager.get_session_maker()() as session:
        verifier_id = current_user_id.get() or "system"

        signoff_db, err = await validate_and_upsert_sdv_target(
            session=session,
            scope=payload.scope,
            target_id=payload.target_id,
            subject_id=payload.subject_id,
            study_id=payload.study_id,
            site_id=payload.site_id,
            verifier_id=verifier_id,
        )
        if err:
            raise HTTPException(status_code=404, detail=err)

        # Save changes
        await session.commit()

        # Re-query
        stmt_re = select(SDVSignOff).where(SDVSignOff.id == signoff_db.id)
        res_re = await session.execute(stmt_re)
        re_signoff = res_re.scalar_one()

        return SDVSignOffResponse(
            id=re_signoff.id,
            scope=re_signoff.scope,
            target_id=re_signoff.target_id,
            subject_id=re_signoff.subject_id,
            study_id=re_signoff.study_id,
            site_id=re_signoff.site_id,
            is_verified=re_signoff.is_verified,
            verified_by=re_signoff.verified_by,
            verified_at=re_signoff.verified_at,
            dropped_reason=re_signoff.dropped_reason,
            dropped_at=re_signoff.dropped_at,
        )
