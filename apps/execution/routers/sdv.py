"""FastAPI router for Targeted SDV (TSDV) and SDV sign-off API endpoints.

Requirements: PRD-QRY-005, PRD-QRY-006, PRD-QRY-007
"""

import uuid
from datetime import UTC, datetime
from enum import StrEnum

from execution.sdv_transport_models import (
    BulkQueryGenerationRequest,
    BulkQueryGenerationResponse,
    BulkSdvSignOffRequest,
    BulkSdvSignOffResponse,
)
from fastapi import APIRouter, Depends, HTTPException, Request, status
from pydantic import BaseModel, Field, model_validator
from sqlalchemy import select, text

from apps.execution.database.context import current_user_id
from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    ClinicalObservation,
    ClinicalQuery,
    ClinicalSubject,
    ClinicalVisit,
    SDVSignOff,
    TSDVConfig,
)
from apps.execution.notifications_client import publish_notification
from apps.execution.rtsm_authz import verify_site_access
from apps.execution.sdv_helper import validate_and_upsert_sdv_target
from apps.execution.trial_lock import TrialLockManager
from apps.execution.tsdv import evaluate_tsdv_requirement
from packages.security import run_async
from packages.security.rbac import (
    ROLE_CRA,
    ROLE_DATA_MANAGER,
    Principal,
    can_access_study,
    get_principal,
    require_permission,
    require_roles,
)
from packages.security.signature_builder import CryptographicSignatureBuilder

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

        if study_id and not can_access_study(principal, study_id):
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


class SamplingModelEnum(StrEnum):
    SUBJECT_BASED = "SUBJECT_BASED"
    FIELD_BASED = "FIELD_BASED"
    COMBINED = "COMBINED"


class TSDVConfigCreate(BaseModel):
    study_id: str
    sampling_model: SamplingModelEnum
    initial_full_sdv_subject_count: int = Field(default=0, ge=0)
    random_sample_percentage: float = Field(default=0.0, ge=0.0, le=100.0)
    full_sdv_domains: list[str] | None = None
    safety_endpoints: list[str] | None = None
    zero_sdv_domains: list[str] | None = None
    trial_random_seed: int | None = Field(default=None, ge=0)

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
    full_sdv_domains: list[str] | None = None
    safety_endpoints: list[str] | None = None
    zero_sdv_domains: list[str] | None = None
    trial_random_seed: int | None = None
    version: int

    class Config:
        from_attributes = True


class TSDVEvaluationResponse(BaseModel):
    required: bool
    subject_selected: bool
    field_decision: bool | None = None
    sampling_model: str
    config_id: str
    enrollment_index: int
    explanation: str


class SDVScopeEnum(StrEnum):
    FIELD = "FIELD"
    PAGE = "PAGE"
    VISIT = "VISIT"


class SDVSignOffRequest(BaseModel):
    """Pydantic request schema for SDV sign-off."""

    scope: SDVScopeEnum
    target_id: str
    subject_id: str
    study_id: str
    site_id: str | None = None


class SDVSignOffResponse(BaseModel):
    """Pydantic response schema for SDV sign-off."""

    id: str
    scope: str
    target_id: str
    subject_id: str
    study_id: str
    site_id: str | None = None
    is_verified: bool
    verified_by: str | None = None
    verified_at: datetime | None = None
    dropped_reason: str | None = None
    dropped_at: datetime | None = None


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
    async with db_manager.get_session_maker()() as session, session.begin():
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
        return res.scalars().one()


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
    domain: str | None = None,
    enrollment_index: int | None = None,
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
    async with db_manager.get_session_maker()() as session:
        verifier_id = current_user_id.get() or "system"
        success, err_msg = await validate_and_upsert_sdv_target(
            session=session,
            scope=payload.scope,
            target_id=payload.target_id,
            subject_id=payload.subject_id,
            study_id=payload.study_id,
            site_id=payload.site_id,
            verifier_id=verifier_id,
        )
        if not success:
            raise HTTPException(status_code=404, detail=err_msg)

        await session.commit()

        # Re-query
        stmt_re = select(SDVSignOff).where(
            SDVSignOff.scope == payload.scope.value,
            SDVSignOff.target_id == payload.target_id,
            SDVSignOff.subject_id == payload.subject_id,
            SDVSignOff.study_id == payload.study_id,
        )
        res_re = await session.execute(stmt_re)
        re_signoff = res_re.scalars().first()

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


# New dedicated APIRouter for bulk SDV endpoints (Task 1)
bulk_sdv_router = APIRouter(prefix="/api/v1/execution/sdv", tags=["SDV"])

# New dedicated APIRouter for bulk query generation endpoints (Task 3)
queries_router = APIRouter(prefix="/api/v1/execution/queries", tags=["Queries"])


@bulk_sdv_router.post(
    "/bulk-sign-off",
    response_model=BulkSdvSignOffResponse,
    status_code=status.HTTP_200_OK,
)
async def bulk_sdv_signoff(
    request: Request,
    payload: BulkSdvSignOffRequest,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(require_roles(ROLE_CRA, "monitor")),
) -> BulkSdvSignOffResponse:
    """CRA/monitor-gated bulk SDV sign-off endpoint for Field, Page, or Visit scopes."""
    # 1. Input Validation
    if not payload.reason_for_change or not payload.reason_for_change.strip():
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="GxP Part 11: reason_for_change cannot be blank.",
        )
    if not payload.target_ids:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="target_ids list cannot be empty.",
        )

    # Note: X-Sig-Token batch binding validation is executed at the GatewayAuthMiddleware layer.
    # On mismatch, it has already raised a 401.

    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            # Enable app-writing config for GxP auditing
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', true);")
            )

            # Validate Subject exists and is consistent with Study
            stmt_subj = select(ClinicalSubject).where(
                ClinicalSubject.subject_id == payload.subject_id,
                ClinicalSubject.study_id == payload.study_id,
                ClinicalSubject.is_deleted.is_(False),
            )
            res_subj = await session.execute(stmt_subj)
            subj_db = res_subj.scalars().first()
            if not subj_db:
                raise HTTPException(
                    status_code=status.HTTP_404_NOT_FOUND,
                    detail="Subject not found or inconsistent study reference.",
                )

            # Study scope authorization check
            if not can_access_study(principal, payload.study_id):
                raise HTTPException(
                    status_code=status.HTTP_403_FORBIDDEN,
                    detail="Forbidden: Insufficient scope access for this study.",
                )

            # Verify site access (security gating)
            if principal.assigned_sites:
                verify_site_access(
                    principal,
                    subj_db.site_id,
                    study_id=payload.study_id,
                    subject_id=subj_db.subject_id,
                )

            # Trial-level and Site-level locking checks (atomic rejects)
            if TrialLockManager.is_locked():
                raise HTTPException(
                    status_code=status.HTTP_423_LOCKED,
                    detail="Trial is currently locked in a read-only state.",
                )
            if subj_db.site_id and TrialLockManager.is_site_locked(subj_db.site_id):
                raise HTTPException(
                    status_code=status.HTTP_423_LOCKED,
                    detail=f"Site {subj_db.site_id} is currently locked in a read-only state.",
                )
            if subj_db.subject_id and TrialLockManager.is_subject_locked(
                subj_db.subject_id
            ):
                raise HTTPException(
                    status_code=status.HTTP_423_LOCKED,
                    detail=f"Subject {subj_db.subject_id} is currently locked in a read-only state.",
                )

            signed_target_ids = []
            skipped_target_ids = []
            skipped_targets_info = []

            # Pre-fetch existing SDVSignOff records for these targets to ensure idempotency
            stmt_signoffs = select(SDVSignOff).where(
                SDVSignOff.scope == payload.scope,
                SDVSignOff.target_id.in_(payload.target_ids),
                SDVSignOff.subject_id == payload.subject_id,
                SDVSignOff.study_id == payload.study_id,
            )
            res_signoffs = await session.execute(stmt_signoffs)
            existing_signoffs = {
                so.target_id: so for so in res_signoffs.scalars().all()
            }

            # Pre-fetch target map for existence / consistency checks
            obs_map = {}
            visit_map = {}
            existing_pages = set()

            if payload.scope == "FIELD":
                stmt_obs = select(ClinicalObservation).where(
                    ClinicalObservation.id.in_(payload.target_ids),
                    ClinicalObservation.subject_id == payload.subject_id,
                    ClinicalObservation.study_id == payload.study_id,
                )
                res_obs = await session.execute(stmt_obs)
                obs_map = {obs.id: obs for obs in res_obs.scalars().all()}
            elif payload.scope == "VISIT":
                stmt_visit = select(ClinicalVisit).where(
                    ClinicalVisit.id.in_(payload.target_ids),
                    ClinicalVisit.subject_id == payload.subject_id,
                    ClinicalVisit.study_id == payload.study_id,
                )
                res_visit = await session.execute(stmt_visit)
                visit_map = {v.id: v for v in res_visit.scalars().all()}
            elif payload.scope == "PAGE":
                stmt_page_obs = select(ClinicalObservation).where(
                    ClinicalObservation.page_id.in_(payload.target_ids),
                    ClinicalObservation.subject_id == payload.subject_id,
                    ClinicalObservation.study_id == payload.study_id,
                )
                res_page_obs = await session.execute(stmt_page_obs)
                existing_pages = {obs.page_id for obs in res_page_obs.scalars().all()}

            verifier_id = current_user_id.get() or "system"
            verified_at = datetime.now(UTC).replace(tzinfo=None)
            site_id = payload.site_id or subj_db.site_id

            for tid in payload.target_ids:
                # 1. Existence / Consistency validations
                if payload.scope == "FIELD" and tid not in obs_map:
                    skipped_target_ids.append(tid)
                    skipped_targets_info.append(
                        {
                            "target_id": tid,
                            "reason": "Clinical observation not found or inconsistent reference.",
                        }
                    )
                    continue
                if payload.scope == "VISIT" and tid not in visit_map:
                    skipped_target_ids.append(tid)
                    skipped_targets_info.append(
                        {
                            "target_id": tid,
                            "reason": "Clinical visit not found or inconsistent reference.",
                        }
                    )
                    continue
                if payload.scope == "PAGE" and tid not in existing_pages:
                    skipped_target_ids.append(tid)
                    skipped_targets_info.append(
                        {
                            "target_id": tid,
                            "reason": "Page ID not found or inconsistent reference.",
                        }
                    )
                    continue

                # 2. Target lock checking
                if payload.scope == "VISIT" and TrialLockManager.is_visit_locked(tid):
                    raise HTTPException(
                        status_code=status.HTTP_423_LOCKED,
                        detail=f"Clinical visit {tid} is currently locked.",
                    )
                if payload.scope == "PAGE" and TrialLockManager.is_form_locked(tid):
                    raise HTTPException(
                        status_code=status.HTTP_423_LOCKED,
                        detail=f"Form/Page {tid} is currently locked.",
                    )

                # 3. Idempotent check
                signoff_db = existing_signoffs.get(tid)
                if signoff_db and signoff_db.is_verified:
                    skipped_target_ids.append(tid)
                    skipped_targets_info.append(
                        {"target_id": tid, "reason": "Target already verified."}
                    )
                    continue

                if signoff_db:
                    signoff_db.is_verified = True
                    signoff_db.verified_by = verifier_id
                    signoff_db.verified_at = verified_at
                    signoff_db.dropped_reason = None
                    signoff_db.dropped_at = None
                else:
                    signoff_db = SDVSignOff(
                        scope=payload.scope,
                        target_id=tid,
                        subject_id=payload.subject_id,
                        study_id=payload.study_id,
                        site_id=site_id,
                        is_verified=True,
                        verified_by=verifier_id,
                        verified_at=verified_at,
                    )
                    session.add(signoff_db)

                # For FIELD scope, also update the matching ClinicalObservation verification columns
                if payload.scope == "FIELD":
                    obs_db = obs_map.get(tid)
                    if obs_db:
                        obs_db.is_sdv_verified = True
                        obs_db.sdv_verified_by = verifier_id
                        obs_db.sdv_verified_at = verified_at

                signed_target_ids.append(tid)

            await session.commit()

    # Content digest and response building
    builder = CryptographicSignatureBuilder()
    content_digest = builder.compute_content_digest(sorted(payload.target_ids))

    timestamp_str = datetime.now(UTC).isoformat().replace("+00:00", "Z")
    audit_tx = f"tx_{uuid.uuid4().hex[:12]}"
    bulk_id = f"bulk_{uuid.uuid4().hex[:8]}"

    return BulkSdvSignOffResponse(
        bulk_id=bulk_id,
        content_digest=content_digest,
        timestamp_utc=timestamp_str,
        audit_tx=audit_tx,
        verified_count=len(signed_target_ids),
        verified_target_ids=signed_target_ids,
        skipped_targets=skipped_targets_info,
        signed_count=len(signed_target_ids),
        signed_target_ids=signed_target_ids,
        skipped_target_ids=skipped_target_ids,
    )


@queries_router.post(
    "/generate",
    response_model=BulkQueryGenerationResponse,
    status_code=status.HTTP_201_CREATED,
)
async def bulk_generate_queries(
    request: Request,
    payload: BulkQueryGenerationRequest,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(require_roles(ROLE_CRA, "monitor")),
) -> BulkQueryGenerationResponse:
    """CRA/monitor-gated endpoint to raise multiple clinical queries at once."""
    # 1. Validation
    if not payload.reason_for_change or not payload.reason_for_change.strip():
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="GxP Part 11: reason_for_change cannot be blank.",
        )
    if not payload.targets:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="targets list cannot be empty.",
        )

    # Enable trial-locking and site-locking gating checks
    if TrialLockManager.is_locked():
        raise HTTPException(
            status_code=status.HTTP_423_LOCKED,
            detail="Trial is currently locked in a read-only state.",
        )

    generated_queries = []
    skipped_targets = []

    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            # Enable app-writing config for GxP auditing
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', true);")
            )

            for target in payload.targets:
                resolved_study_id = target.study_id or payload.study_id
                if not resolved_study_id:
                    # Missing study ID coordinate -> skip target
                    skipped_targets.append(target)
                    continue

                # Study authorization check
                if not can_access_study(principal, resolved_study_id):
                    raise HTTPException(
                        status_code=status.HTTP_403_FORBIDDEN,
                        detail="Forbidden: Insufficient scope access for this study.",
                    )

                # Fetch ClinicalSubject to resolve site_id and check existence/consistency
                stmt_subj = select(ClinicalSubject).where(
                    ClinicalSubject.subject_id == target.subject_id,
                    ClinicalSubject.study_id == resolved_study_id,
                    ClinicalSubject.is_deleted.is_(False),
                )
                res_subj = await session.execute(stmt_subj)
                subj_db = res_subj.scalars().first()
                if not subj_db:
                    # Subject is not found or is inconsistent -> skip target
                    skipped_targets.append(target)
                    continue

                # Verify site scoping
                if principal.assigned_sites:
                    verify_site_access(
                        principal,
                        subj_db.site_id,
                        study_id=resolved_study_id,
                        subject_id=subj_db.subject_id,
                    )

                # Lock checking
                if subj_db.site_id and TrialLockManager.is_site_locked(subj_db.site_id):
                    raise HTTPException(
                        status_code=status.HTTP_423_LOCKED,
                        detail=f"Site {subj_db.site_id} is currently locked.",
                    )
                if TrialLockManager.is_subject_locked(subj_db.subject_id):
                    raise HTTPException(
                        status_code=status.HTTP_423_LOCKED,
                        detail=f"Subject {subj_db.subject_id} is currently locked.",
                    )

                # Active-query deduplication rule: status in OPEN / REOPENED / ANSWERED and not deleted
                stmt_exist = select(ClinicalQuery).where(
                    ClinicalQuery.study_id == resolved_study_id,
                    ClinicalQuery.subject_id == target.subject_id,
                    ClinicalQuery.visit_id == target.visit_id,
                    ClinicalQuery.domain == target.domain,
                    ClinicalQuery.test_code == target.test_code,
                    ClinicalQuery.status.in_(["OPEN", "REOPENED", "ANSWERED"]),
                    ClinicalQuery.is_deleted.is_(False),
                )
                res_exist = await session.execute(stmt_exist)
                if res_exist.scalars().first():
                    skipped_targets.append(target)
                    continue

                # Create the ClinicalQuery record
                q_id = f"q_{uuid.uuid4().hex[:8]}"
                q = ClinicalQuery(
                    id=q_id,
                    study_id=resolved_study_id,
                    site_id=subj_db.site_id,
                    subject_id=target.subject_id,
                    visit_id=target.visit_id,
                    domain=target.domain,
                    test_code=target.test_code,
                    status="OPEN",
                    explanation=target.explanation,
                    message=target.explanation,
                    origin="manual",
                    created_by=principal.user_id,
                    observation_id=target.observation_id,
                    form_id=target.form_id,
                    field_id=target.field_id,
                )
                session.add(q)
                generated_queries.append(q)

            await session.commit()

    # Dispatch fire-and-forget notifications post-commit
    for q in generated_queries:
        payload_notif = {
            "recipient_role": "Site Investigator",
            "category": "ACTION_ITEMS",
            "priority": "HIGH",
            "channels": "IN_APP",
            "message_content": f"New clinical query raised for subject {q.subject_id} in study {q.study_id}: {q.explanation}",
            "related_entity_type": "query",
            "related_entity_id": q.id,
        }
        run_async(publish_notification(payload_notif))

    batch_id = f"batch_{uuid.uuid4().hex[:8]}"
    audit_tx = f"tx_{uuid.uuid4().hex[:12]}"
    timestamp_str = datetime.now(UTC).isoformat().replace("+00:00", "Z")

    return BulkQueryGenerationResponse(
        batch_id=batch_id,
        audit_tx=audit_tx,
        generated_count=len(generated_queries),
        generated_query_ids=[q.id for q in generated_queries],
        skipped_targets=skipped_targets,
        timestamp_utc=timestamp_str,
    )
