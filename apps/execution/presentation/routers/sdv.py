"""FastAPI router for Targeted SDV (TSDV) and SDV sign-off API endpoints.

Requirements: PRD-QRY-005, PRD-QRY-006, PRD-QRY-007
"""

import uuid
from datetime import UTC, datetime
from enum import StrEnum

from fastapi import APIRouter, Depends, HTTPException, Request
from pydantic import BaseModel, Field, model_validator
from sqlalchemy import select, text
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.adapters.repositories import get_execution_db_session
from apps.execution.database.context import audit_context, current_user_id
from apps.execution.database.models import (
    ClinicalObservation,
    ClinicalQuery,
    ClinicalSubject,
    ClinicalVisit,
    SDVSignOff,
    TSDVConfig,
)
from apps.execution.domain.sdv_transport_models import (
    BulkQueryGenerationRequest,
    BulkQueryGenerationResponse,
    BulkSdvSignOffRequest,
    BulkSdvSignOffResponse,
)
from apps.execution.rtsm_authz import verify_site_access
from apps.execution.sdv_helper import validate_and_upsert_sdv_target
from apps.execution.tsdv import evaluate_tsdv_requirement
from packages.security import can_access_study, get_principal, run_async
from packages.security.rbac import (
    ROLE_CRA,
    ROLE_DATA_MANAGER,
    Principal,
    require_permission,
    require_roles,
    require_study_scope,
)

router = APIRouter(prefix="/api/v1/execution", tags=["SDV/TSDV"])


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
    def validate_seed(self) -> TSDVConfigCreate:
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
    session: AsyncSession = Depends(get_execution_db_session),
) -> TSDVConfig:
    """CREATE or UPDATE Targeted SDV (TSDV) configuration for a study.

    Restricts config writes to CRA/Data Manager roles WITH GxP change justifications.
    """
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
    session: AsyncSession = Depends(get_execution_db_session),
) -> TSDVConfig:
    """Retrieve existing TSDV configuration for a study."""
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
    session: AsyncSession = Depends(get_execution_db_session),
) -> TSDVEvaluationResponse:
    """Evaluate Targeted SDV (TSDV) requirement for a given context.

    Calculates deterministic sampling decisions and returns component results with an audit explanation.
    """
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
    session: AsyncSession = Depends(get_execution_db_session),
) -> SDVSignOffResponse:
    """CRA/monitor-gated SDV sign-off endpoint for Field, Page, or Visit scopes."""
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


@router.post(
    "/sdv/bulk-sign-off",
    response_model=BulkSdvSignOffResponse,
)
async def bulk_sdv_signoff(
    payload: BulkSdvSignOffRequest,
    principal: Principal = Depends(require_permission("sdv:update")),
    _study_scope: Principal = Depends(require_study_scope()),
    session: AsyncSession = Depends(get_execution_db_session),
) -> BulkSdvSignOffResponse:
    """CRA/monitor-gated bulk SDV sign-off endpoint for Field, Page, or Visit scopes."""
    # Validate request-body
    if not payload.reason_for_change or not payload.reason_for_change.strip():
        raise HTTPException(
            status_code=400, detail="GxP Part 11: reason_for_change cannot be blank."
        )
    if not payload.target_ids:
        raise HTTPException(status_code=400, detail="target_ids list cannot be empty.")

    async with session.begin():
        # Set GxP write permission config if needed
        await session.execute(
            text("SELECT set_config('cadence.app_writing', 'true', true);")
        )

        # Validate Subject exists and is consistent with Study
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

        signed_target_ids = []
        skipped_target_ids = []

        # 2. Scope-specific validation & fetching
        if payload.scope == "FIELD":
            stmt_obs = select(ClinicalObservation).where(
                ClinicalObservation.id.in_(payload.target_ids),
                ClinicalObservation.subject_id == payload.subject_id,
                ClinicalObservation.study_id == payload.study_id,
            )
            res_obs = await session.execute(stmt_obs)
            obs_map = {obs.id: obs for obs in res_obs.scalars().all()}

            # Verify that all provided target_ids actually exist
            for tid in payload.target_ids:
                if tid not in obs_map:
                    raise HTTPException(
                        status_code=404,
                        detail=f"Clinical observation {tid} not found or inconsistent reference.",
                    )
        elif payload.scope == "VISIT":
            stmt_visit = select(ClinicalVisit).where(
                ClinicalVisit.id.in_(payload.target_ids),
                ClinicalVisit.subject_id == payload.subject_id,
                ClinicalVisit.study_id == payload.study_id,
            )
            res_visit = await session.execute(stmt_visit)
            visit_map = {v.id: v for v in res_visit.scalars().all()}
            for tid in payload.target_ids:
                if tid not in visit_map:
                    raise HTTPException(
                        status_code=404,
                        detail=f"Clinical visit {tid} not found or inconsistent reference.",
                    )
        elif payload.scope == "PAGE":
            stmt_page_obs = select(ClinicalObservation).where(
                ClinicalObservation.page_id.in_(payload.target_ids),
                ClinicalObservation.subject_id == payload.subject_id,
                ClinicalObservation.study_id == payload.study_id,
            )
            res_page_obs = await session.execute(stmt_page_obs)
            existing_pages = {obs.page_id for obs in res_page_obs.scalars().all()}
            for tid in payload.target_ids:
                if tid not in existing_pages:
                    raise HTTPException(
                        status_code=404,
                        detail=f"Page ID {tid} not found or inconsistent reference.",
                    )
        else:
            raise HTTPException(status_code=400, detail="Invalid scope.")

        # 3. Apply sign-off behavior
        verifier_id = current_user_id.get() or "system"
        verified_at = datetime.now(UTC).replace(tzinfo=None)
        site_id = payload.site_id or (
            subj_db.site_id if hasattr(subj_db, "site_id") else None
        )

        # Query existing SDVSignOff records for these targets
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

        # We process each target_id
        for tid in payload.target_ids:
            signoff_db = existing_signoffs.get(tid)
            if signoff_db and signoff_db.is_verified:
                skipped_target_ids.append(tid)
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

            # For FIELD scope, update the ClinicalObservation too
            if payload.scope == "FIELD":
                obs_db = obs_map.get(tid)
                if obs_db:
                    obs_db.is_sdv_verified = True
                    obs_db.sdv_verified_by = verifier_id
                    obs_db.sdv_verified_at = verified_at

            signed_target_ids.append(tid)

        # Commit the transaction
        await session.commit()

        # Compute SHA256 digest of signed payload data
        import hashlib
        import json

        digest_payload = {
            "study_id": payload.study_id,
            "subject_id": payload.subject_id,
            "scope": payload.scope,
            "target_ids": sorted(payload.target_ids),
            "reason_for_change": payload.reason_for_change,
        }
        serialized_digest = json.dumps(digest_payload, sort_keys=True)
        content_digest = hashlib.sha256(serialized_digest.encode("utf-8")).hexdigest()

        # Use current timestamp for UTC
        timestamp_str = datetime.now(UTC).isoformat().replace("+00:00", "Z")

        return BulkSdvSignOffResponse(
            signed_count=len(signed_target_ids),
            signed_target_ids=signed_target_ids,
            skipped_target_ids=skipped_target_ids,
            content_digest=content_digest,
            timestamp_utc=timestamp_str,
            audit_tx="tx-" + str(uuid.uuid4())[:8],
        )


@router.post(
    "/queries/generate",
    response_model=BulkQueryGenerationResponse,
)
async def bulk_generate_queries(
    payload: BulkQueryGenerationRequest,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(require_roles(ROLE_CRA, "monitor")),
    _study_scope: Principal = Depends(require_study_scope()),
    session: AsyncSession = Depends(get_execution_db_session),
) -> BulkQueryGenerationResponse:
    """CRA/monitor-gated bulk query generation endpoint."""
    # 1. Validation
    if not payload.reason_for_change or not payload.reason_for_change.strip():
        raise HTTPException(
            status_code=400, detail="GxP Part 11: reason_for_change cannot be blank."
        )
    if not payload.targets:
        raise HTTPException(status_code=400, detail="targets list cannot be empty.")

    user_id = principal.user_id or "system"
    change_reason = payload.reason_for_change

    generated_query_ids = []
    skipped_targets = []

    async with session.begin():
        # Set GxP write permission config
        await session.execute(
            text("SELECT set_config('cadence.app_writing', 'true', true);")
        )

        with audit_context(user_id=user_id, change_reason=change_reason):
            for target in payload.targets:
                # Resolve study_id
                target_study_id = target.study_id or payload.study_id
                if not target_study_id:
                    skipped_targets.append(target)
                    continue

                # Verify that the principal can access the target study
                if not can_access_study(principal, target_study_id):
                    raise HTTPException(
                        status_code=403,
                        detail="Forbidden: access restricted to your assigned study.",
                    )

                # Retrieve subject to find site_id
                stmt_subj = select(ClinicalSubject).where(
                    ClinicalSubject.subject_id == target.subject_id,
                    ClinicalSubject.study_id == target_study_id,
                )
                res_subj = await session.execute(stmt_subj)
                subj_db = res_subj.scalars().first()

                if not subj_db:
                    # Subject not found or inconsistent study reference -> skip target
                    skipped_targets.append(target)
                    continue

                target_site_id = subj_db.site_id

                # Enforce site access verification
                verify_site_access(
                    principal,
                    target_site_id,
                    study_id=target_study_id,
                    subject_id=target.subject_id,
                )

                # Deduplication check: status in OPEN, REOPENED, ANSWERED and not deleted
                stmt_query = select(ClinicalQuery).where(
                    ClinicalQuery.study_id == target_study_id,
                    ClinicalQuery.subject_id == target.subject_id,
                    ClinicalQuery.visit_id == target.visit_id,
                    ClinicalQuery.domain == target.domain,
                    ClinicalQuery.test_code == target.test_code,
                    ClinicalQuery.status.in_(["OPEN", "REOPENED", "ANSWERED"]),
                    ClinicalQuery.is_deleted.is_(False),
                )
                res_query = await session.execute(stmt_query)
                existing_query = res_query.scalars().first()

                if existing_query:
                    # Skip target
                    skipped_targets.append(target)
                    continue

                # Create ClinicalQuery
                explanation_text = target.explanation or change_reason
                query_id = f"qry_{uuid.uuid4().hex[:8]}"
                q = ClinicalQuery(
                    id=query_id,
                    study_id=target_study_id,
                    site_id=target_site_id,
                    subject_id=target.subject_id,
                    visit_id=target.visit_id,
                    domain=target.domain,
                    test_code=target.test_code,
                    status="OPEN",
                    explanation=explanation_text,
                    message=explanation_text,
                    origin="manual",
                    created_by=user_id,
                    observation_id=target.observation_id,
                    form_id=target.form_id,
                    field_id=target.field_id,
                )
                session.add(q)
                generated_query_ids.append(query_id)

            # Explicitly flush
            await session.flush()

    # Commit has been executed, dispatch fire-and-forget notifications
    from apps.execution.notifications_client import publish_notification

    if generated_query_ids:
        stmt_saved = select(ClinicalQuery).where(
            ClinicalQuery.id.in_(generated_query_ids)
        )
        res_saved = await session.execute(stmt_saved)
        saved_queries = res_saved.scalars().all()

        for saved_q in saved_queries:
            payload_notif = {
                "category": "ACTION_ITEMS",
                "priority": "HIGH",
                "channels": "IN_APP",
                "message_content": f"New clinical query raised for subject {saved_q.subject_id}, visit {saved_q.visit_id or 'N/A'}: {saved_q.explanation or 'discrepancy'}",
                "related_entity_type": "query",
                "related_entity_id": saved_q.id,
                "site_id": saved_q.site_id,
                "study_id": saved_q.study_id,
                "recipient_role": "site investigator",
            }
            run_async(publish_notification(payload_notif))

    # Generate batch_id & audit_tx using the generated functions or uuid
    batch_id = f"batch_{uuid.uuid4().hex[:8]}"
    audit_tx_id = f"tx_{uuid.uuid4().hex[:12]}"

    return BulkQueryGenerationResponse(
        batch_id=batch_id,
        audit_tx=audit_tx_id,
        generated_count=len(generated_query_ids),
        generated_query_ids=generated_query_ids,
        skipped_targets=skipped_targets,
    )
