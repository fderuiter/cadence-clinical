"""FastAPI router for Targeted SDV (TSDV) and SDV sign-off API endpoints.

Requirements: PRD-QRY-005, PRD-QRY-006, PRD-QRY-007
"""

import os
import uuid
import hashlib
from datetime import datetime, timezone, UTC
from enum import Enum
from typing import List, Optional

from fastapi import APIRouter, Depends, HTTPException, Request, status
from pydantic import BaseModel, Field, model_validator
from sqlalchemy import select, text
from jose import JWTError, jwt

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
    Principal,
    can_access_study,
    get_principal,
    require_permission,
)
from packages.security import ROLE_CRA, require_roles, run_async
from packages.security.context import audit_context
from execution.sdv_transport_models import (
    BulkSdvSignOffRequest,
    BulkSdvSignOffResponse,
    BulkQueryGenerationRequest,
    BulkQueryGenerationResponse,
    QueryTargetDescriptor,
)
from apps.execution.rtsm_authz import verify_site_access
from apps.execution.database.models import ClinicalQuery
from apps.execution.notifications_client import publish_notification
from apps.execution.trial_lock import TrialLockManager
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
    principal: Principal = Depends(require_permission("sdv:update")),
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
    async with db_manager.get_session_maker()() as session:
        # 1. Validate Subject exists and is consistent with Study
        stmt_subj = select(ClinicalSubject).where(
            ClinicalSubject.subject_id == payload.subject_id,
            ClinicalSubject.study_id == payload.study_id,
        )
        res_subj = await session.execute(stmt_subj)
        subj_db = res_subj.scalars().first()
        if not subj_db:
            raise HTTPException(
                status_code=404,
                detail="Subject not found or inconsistent study reference.",
            )

        # 2. Scope-specific validation
        obs_db = None
        if payload.scope == SDVScopeEnum.FIELD:
            stmt_obs = select(ClinicalObservation).where(
                ClinicalObservation.id == payload.target_id,
                ClinicalObservation.subject_id == payload.subject_id,
                ClinicalObservation.study_id == payload.study_id,
            )
            res_obs = await session.execute(stmt_obs)
            obs_db = res_obs.scalars().first()
            if not obs_db:
                raise HTTPException(
                    status_code=404,
                    detail="Clinical observation not found or inconsistent target/subject/study reference.",
                )
        elif payload.scope == SDVScopeEnum.VISIT:
            stmt_visit = select(ClinicalVisit).where(
                ClinicalVisit.id == payload.target_id,
                ClinicalVisit.subject_id == payload.subject_id,
                ClinicalVisit.study_id == payload.study_id,
            )
            res_visit = await session.execute(stmt_visit)
            visit_db = res_visit.scalars().first()
            if not visit_db:
                raise HTTPException(
                    status_code=404,
                    detail="Clinical visit not found or inconsistent target/subject/study reference.",
                )
        elif payload.scope == SDVScopeEnum.PAGE:
            stmt_page_obs = select(ClinicalObservation).where(
                ClinicalObservation.page_id == payload.target_id,
                ClinicalObservation.subject_id == payload.subject_id,
                ClinicalObservation.study_id == payload.study_id,
            )
            res_page_obs = await session.execute(stmt_page_obs)
            if not res_page_obs.scalars().first():
                raise HTTPException(
                    status_code=404,
                    detail="Page ID not found or inconsistent target/subject/study reference.",
                )

        # 3. Apply sign-off behavior
        verifier_id = current_user_id.get() or "system"
        verified_at = datetime.now(timezone.utc)

        # Update or create the matching SDVSignOff record
        stmt_signoff = select(SDVSignOff).where(
            SDVSignOff.scope == payload.scope.value,
            SDVSignOff.target_id == payload.target_id,
            SDVSignOff.subject_id == payload.subject_id,
            SDVSignOff.study_id == payload.study_id,
        )
        res_signoff = await session.execute(stmt_signoff)
        signoff_db = res_signoff.scalars().first()

        site_id = payload.site_id or (
            subj_db.site_id if hasattr(subj_db, "site_id") else None
        )

        if signoff_db:
            signoff_db.is_verified = True
            signoff_db.verified_by = verifier_id
            signoff_db.verified_at = verified_at
            signoff_db.dropped_reason = None
            signoff_db.dropped_at = None
        else:
            signoff_db = SDVSignOff(
                scope=payload.scope.value,
                target_id=payload.target_id,
                subject_id=payload.subject_id,
                study_id=payload.study_id,
                site_id=site_id,
                is_verified=True,
                verified_by=verifier_id,
                verified_at=verified_at,
            )
            session.add(signoff_db)

        # For FIELD scope, update the ClinicalObservation too
        if payload.scope == SDVScopeEnum.FIELD and obs_db:
            obs_db.is_sdv_verified = True
            obs_db.sdv_verified_by = verifier_id
            obs_db.sdv_verified_at = verified_at

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


@router.post(
    "/sdv/bulk-sign-off",
    response_model=BulkSdvSignOffResponse,
    status_code=status.HTTP_201_CREATED,
)
async def bulk_sdv_sign_off_endpoint(
    request: Request,
    payload: BulkSdvSignOffRequest,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(require_roles(ROLE_CRA, "monitor")),
) -> BulkSdvSignOffResponse:
    """Execute bulk SDV sign-off verification for many targets in one transaction.

    Requirements: PRD-SYS-001, PRD-QRY-005, PRD-QRY-007
    """
    # 1. Trial lock check
    if TrialLockManager.is_locked():
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Trial is currently locked in a read-only state due to a security violation.",
        )

    # 2. Input validation
    if not payload.target_ids:
        raise HTTPException(
            status_code=400,
            detail="At least one target ID must be provided for bulk sign-off.",
        )

    if not payload.reason_for_change or not payload.reason_for_change.strip():
        raise HTTPException(
            status_code=400,
            detail="GxP reason for change must be provided.",
        )

    # 3. Validate X-Sig-Token batch binding before any write (Phase 3 requirement)
    sig_token = request.headers.get("X-Sig-Token")
    if not sig_token:
        raise HTTPException(
            status_code=401,
            detail="REAUTHENTICATION_REQUIRED",
        )

    secret = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345").encode()
    try:
        sig_payload = jwt.decode(sig_token, secret, algorithms=["HS256"])
    except JWTError:
        raise HTTPException(
            status_code=401,
            detail="REAUTHENTICATION_REQUIRED",
        )

    token_batch_id = sig_payload.get("batch_id")
    if not token_batch_id:
        raise HTTPException(
            status_code=401,
            detail="REAUTHENTICATION_REQUIRED",
        )

    # Compute expected batch_id
    norm_study = str(payload.study_id).strip()
    norm_scope = str(payload.scope).strip().upper()
    sorted_ids = sorted([str(tid).strip() for tid in payload.target_ids])
    norm_ids = ",".join(sorted_ids)
    norm_reason = str(payload.reason_for_change).strip()

    binding_str = f"{norm_study}:{norm_scope}:{norm_ids}:{norm_reason}"
    computed_batch_id = hashlib.sha256(binding_str.encode("utf-8")).hexdigest()

    if token_batch_id != computed_batch_id:
        raise HTTPException(
            status_code=401,
            detail="REAUTHENTICATION_REQUIRED",
        )

    # 4. Authorization and study scoping checks
    if not can_access_study(principal, payload.study_id):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: access restricted to your assigned study.",
        )

    # 5. Open database session maker and process bulk upserts inside single transaction
    signed_target_ids = []
    skipped_target_ids = []

    with audit_context(user_id=principal.user_id, change_reason=payload.reason_for_change):
        async with db_manager.get_session_maker()() as session:
            async with session.begin():
                await session.execute(
                    text("SELECT set_config('cadence.app_writing', 'true', true);")
                )
                # 5a. Validate Subject exists and is consistent with Study
                stmt_subj = select(ClinicalSubject).where(
                    ClinicalSubject.subject_id == payload.subject_id,
                    ClinicalSubject.study_id == payload.study_id,
                )
                res_subj = await session.execute(stmt_subj)
                subj_db = res_subj.scalars().first()
                if not subj_db:
                    raise HTTPException(
                        status_code=404,
                        detail="Subject not found or inconsistent study reference.",
                    )

                # 5b. Verify site isolation
                verify_site_access(
                    principal,
                    site_id=subj_db.site_id,
                    study_id=payload.study_id,
                    subject_id=payload.subject_id,
                )

                verifier_id = principal.user_id or "system"
                verified_at = datetime.now(UTC)

                for target_id in payload.target_ids:
                    # Existence & Consistency validation per target
                    obs_db = None
                    is_valid = True

                    scope_upper = payload.scope.upper()
                    if scope_upper == "FIELD":
                        stmt_obs = select(ClinicalObservation).where(
                            ClinicalObservation.id == target_id,
                            ClinicalObservation.subject_id == payload.subject_id,
                            ClinicalObservation.study_id == payload.study_id,
                        )
                        res_obs = await session.execute(stmt_obs)
                        obs_db = res_obs.scalars().first()
                        if not obs_db:
                            is_valid = False

                    elif scope_upper == "VISIT":
                        stmt_visit = select(ClinicalVisit).where(
                            ClinicalVisit.id == target_id,
                            ClinicalVisit.subject_id == payload.subject_id,
                            ClinicalVisit.study_id == payload.study_id,
                        )
                        res_visit = await session.execute(stmt_visit)
                        visit_db = res_visit.scalars().first()
                        if not visit_db:
                            is_valid = False

                    elif scope_upper == "PAGE":
                        stmt_page_obs = select(ClinicalObservation).where(
                            ClinicalObservation.page_id == target_id,
                            ClinicalObservation.subject_id == payload.subject_id,
                            ClinicalObservation.study_id == payload.study_id,
                        )
                        res_page_obs = await session.execute(stmt_page_obs)
                        if not res_page_obs.scalars().first():
                            is_valid = False

                    else:
                        is_valid = False

                    if not is_valid:
                        skipped_target_ids.append(target_id)
                        continue

                    # Upsert SDVSignOff
                    stmt_signoff = select(SDVSignOff).where(
                        SDVSignOff.scope == scope_upper,
                        SDVSignOff.target_id == target_id,
                        SDVSignOff.subject_id == payload.subject_id,
                        SDVSignOff.study_id == payload.study_id,
                    )
                    res_signoff = await session.execute(stmt_signoff)
                    signoff_db = res_signoff.scalars().first()

                    site_id = payload.site_id or subj_db.site_id

                    if signoff_db:
                        signoff_db.is_verified = True
                        signoff_db.verified_by = verifier_id
                        signoff_db.verified_at = verified_at
                        signoff_db.dropped_reason = None
                        signoff_db.dropped_at = None
                    else:
                        signoff_db = SDVSignOff(
                            scope=scope_upper,
                            target_id=target_id,
                            subject_id=payload.subject_id,
                            study_id=payload.study_id,
                            site_id=site_id,
                            is_verified=True,
                            verified_by=verifier_id,
                            verified_at=verified_at,
                        )
                        session.add(signoff_db)

                    # Update ClinicalObservation verification columns for FIELD scope
                    if scope_upper == "FIELD" and obs_db:
                        obs_db.is_sdv_verified = True
                        obs_db.sdv_verified_by = verifier_id
                        obs_db.sdv_verified_at = verified_at

                    signed_target_ids.append(target_id)

    builder = CryptographicSignatureBuilder()
    content_digest = builder.compute_content_digest(sorted_ids)
    audit_tx = f"tx_{uuid.uuid4().hex[:12]}"
    now_iso = datetime.now(UTC).isoformat()

    return BulkSdvSignOffResponse(
        signed_count=len(signed_target_ids),
        signed_target_ids=signed_target_ids,
        skipped_target_ids=skipped_target_ids,
        content_digest=content_digest,
        timestamp_utc=now_iso,
        audit_tx=audit_tx,
    )


@router.post(
    "/queries/generate",
    response_model=BulkQueryGenerationResponse,
    status_code=status.HTTP_201_CREATED,
)
async def bulk_query_generation_endpoint(
    request: Request,
    payload: BulkQueryGenerationRequest,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(require_roles(ROLE_CRA, "monitor")),
) -> BulkQueryGenerationResponse:
    """Execute bulk clinical query generation for many targets.

    Requirements: PRD-SYS-001, PRD-QRY-005, PRD-QRY-007
    """
    # 1. Trial lock check
    if TrialLockManager.is_locked():
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Trial is currently locked in a read-only state due to a security violation.",
        )

    # 2. Input validation
    if not payload.targets:
        raise HTTPException(
            status_code=400,
            detail="At least one target descriptor must be provided for query generation.",
        )

    if not payload.reason_for_change or not payload.reason_for_change.strip():
        raise HTTPException(
            status_code=400,
            detail="GxP reason for change must be provided.",
        )

    # 3. Authorization and study scoping checks
    if not can_access_study(principal, payload.study_id):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: access restricted to your assigned study.",
        )

    # Site-level access verification
    if payload.site_id:
        verify_site_access(
            principal,
            site_id=payload.site_id,
            study_id=payload.study_id,
        )

    generated_query_ids = []
    skipped_targets = []

    with audit_context(user_id=principal.user_id, change_reason=payload.reason_for_change):
        async with db_manager.get_session_maker()() as session:
            async with session.begin():
                await session.execute(
                    text("SELECT set_config('cadence.app_writing', 'true', true);")
                )
                for target in payload.targets:
                    # Deduplication check: status in OPEN/REOPENED/ANSWERED, and is_deleted is False
                    stmt_dup = select(ClinicalQuery).where(
                        ClinicalQuery.study_id == payload.study_id,
                        ClinicalQuery.subject_id == target.subject_id,
                        ClinicalQuery.visit_id == target.visit_id,
                        ClinicalQuery.domain == target.domain,
                        ClinicalQuery.test_code == target.test_code,
                        ClinicalQuery.status.in_(["OPEN", "REOPENED", "ANSWERED"]),
                        ClinicalQuery.is_deleted.is_(False),
                    )
                    res_dup = await session.execute(stmt_dup)
                    if res_dup.scalars().first():
                        skipped_targets.append(target)
                        continue

                    # Create a new ClinicalQuery record
                    new_id = f"qry_{uuid.uuid4().hex[:8]}"
                    q = ClinicalQuery(
                        id=new_id,
                        study_id=payload.study_id,
                        site_id=payload.site_id,
                        subject_id=target.subject_id,
                        visit_id=target.visit_id,
                        domain=target.domain,
                        test_code=target.test_code,
                        status="OPEN",
                        explanation=target.explanation,
                        observation_id=target.observation_id,
                        message=target.explanation,
                        origin="manual",
                        priority="HIGH",
                        created_by=principal.user_id,
                    )
                    session.add(q)
                    generated_query_ids.append(new_id)

    # Dispatch fire-and-forget notifications on successful transaction commit
    for q_id in generated_query_ids:
        # Match pattern from NotificationRouter / trial_lock.py
        notif_payload = {
            "recipient_role": "Sponsor DM/MM",
            "category": "ACTION_ITEMS",
            "priority": "HIGH",
            "channels": "IN_APP",
            "message_content": f"Bulk clinical query generated. Coordination - Study: {payload.study_id}, ID: {q_id}.",
            "related_entity_type": "query",
            "related_entity_id": q_id,
        }
        run_async(publish_notification(notif_payload))

    now_iso = datetime.now(UTC).isoformat()

    return BulkQueryGenerationResponse(
        generated_count=len(generated_query_ids),
        generated_query_ids=generated_query_ids,
        skipped_targets=skipped_targets,
        timestamp_utc=now_iso,
    )
