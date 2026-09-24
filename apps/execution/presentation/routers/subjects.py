"""FastAPI presentation router for Clinical Subject management, screening, consent, and demographics.

Requirements: PRD-SYS-001, PRD-SYS-004, PRD-SYS-042, PRD-SYS-121, PRD-SUB-001, PRD-SSU-003, ADR-2202, Trace-1
"""

from contextlib import suppress
from datetime import datetime
from typing import Any

from fastapi import (
    APIRouter,
    Depends,
    HTTPException,
    Query,
    Request,
)
from pydantic import BaseModel, Field
from sqlalchemy import select, text

from apps.execution.adapters.repositories import (
    get_subject_lifecycle_service,
)
from apps.execution.application.services import SubjectLifecycleService
from apps.execution.database.context import current_change_reason, current_user_id
from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    AuditLog,
    ClinicalSubject,
    SubjectRandomization,
)
from apps.execution.database.models.compliance import SiteComplianceCache
from apps.execution.demographics import (
    decrypt_demographics,
    encrypt_demographics,
)
from apps.execution.dependencies import verify_change_justification
from apps.execution.domain.acl.protocol_version_ref_dto import ProtocolVersionRefDTO
from apps.execution.domain.exceptions import SubjectNotFoundError
from apps.execution.field_masking import apply_rtsm_blinded_filter
from apps.execution.site_execution_gatekeeper import (
    require_site_mutation_authorization,
)
from apps.execution.subject_lifecycle import InvalidStateTransitionError
from packages.security import enforce_site_isolation
from packages.security.rbac import (
    ROLE_DATA_MANAGER,
    ROLE_SITE_INVESTIGATOR,
    Principal,
    get_principal,
    require_roles,
    verify_not_auditor,
)

router = APIRouter(tags=["Subjects"])


class Demographics(BaseModel):
    name: str | None = None
    birthdate: str | None = None
    gender: str | None = None
    race: str | None = None


class SubjectCreate(BaseModel):
    subject_id: str
    study_id: str
    site_id: str | None = None
    arm_id: str | None = None
    demographics: Demographics | None = None


class SubjectResponse(BaseModel):
    id: str
    subject_id: str
    study_id: str
    site_id: str | None = None
    treatment_group: str | None = None
    status: str | None = "SCREENING"
    active_protocol_version: str | None = "1.0.0"
    encrypted_demographics: str | None = None


class CriterionLevelResult(BaseModel):
    criterion_id: str
    criterion_type: str
    description: str
    dsl_source: str
    is_met: bool
    is_indeterminate: bool


class SubjectScreeningResponse(BaseModel):
    eligible: bool | None = None
    failed_criteria: list[str] = Field(default_factory=list)
    indeterminate_criteria: list[str] = Field(default_factory=list)
    criterion_evaluations: list[CriterionLevelResult] = Field(default_factory=list)


class SubjectScreeningRequest(BaseModel):
    study_id: str | None = None


class SubjectConsentRequest(BaseModel):
    protocol_version: ProtocolVersionRefDTO
    icf_signed: bool
    icf_signed_date: datetime | None = None
    requires_reconsent: bool = False
    is_paper_override: bool = False


class SubjectConsentResponse(BaseModel):
    id: str
    subject_id: str
    study_id: str
    version_tag: str
    version_index: int
    icf_signed: bool
    icf_signed_date: datetime | None = None
    requires_reconsent: bool
    is_paper_override: bool = False
    version: int


class ComplianceStatusResponse(BaseModel):
    study_id: str
    site_id: str | None
    milestone: str
    is_complete: bool
    missing_documents: list[str]


class ETMFCompletenessWebhookPayload(BaseModel):
    study_id: str
    site_id: str | None = None
    milestone: str
    is_complete: bool
    missing_artifacts: list[str] = Field(default_factory=list)


class SiteActivationRequest(BaseModel):
    study_id: str


class SubjectDetailResponse(BaseModel):
    subject_id: str
    study_id: str
    status: str
    site_id: str | None = None
    treatment_group: str | None = None
    randomization_seed: str | None = None
    investigational_product_id: str | None = None


class SubjectStateUpdateRequest(BaseModel):
    status: str | None = None
    state: str | None = None


class SubjectDemographicsUpdateRequest(BaseModel):
    demographics: Demographics | None = None
    strat_factors: dict[str, Any] | None = None


async def check_site_compliance_for_enrollment(
    session: Any, study_id: str, site_id: str | None
) -> bool:
    if not site_id:
        return True

    stmt = select(SiteComplianceCache).where(
        SiteComplianceCache.study_id == study_id,
        SiteComplianceCache.site_id == site_id,
        SiteComplianceCache.milestone == "SITE_ACTIVATION",
    )
    result = await session.execute(stmt)
    cache_entry = result.scalars().first()
    return bool(cache_entry and cache_entry.is_complete)


@router.post("/api/v1/execution/subjects", response_model=SubjectResponse)
async def create_subject(
    payload: SubjectCreate,
    roles: list[str] = Depends(verify_not_auditor),
    lifecycle_service: SubjectLifecycleService = Depends(get_subject_lifecycle_service),
) -> SubjectResponse:
    u_id = current_user_id.get() or "system"
    reason = current_change_reason.get() or "Initial Subject Creation"
    if payload.study_id and payload.site_id:
        await require_site_mutation_authorization(
            site_id=payload.site_id,
            user_id=u_id,
            operation_type="SUBJECT_CREATION",
            study_id=payload.study_id,
        )

    demo_dict = (
        payload.demographics.model_dump(exclude_none=True)
        if payload.demographics is not None
        else None
    )

    subj = await lifecycle_service.enroll_subject(
        study_id=payload.study_id,
        site_id=payload.site_id,
        subject_id=payload.subject_id,
        arm_id=payload.arm_id,
        demographics=demo_dict,
        user_id=u_id,
        reason=reason,
    )

    return SubjectResponse(
        id=subj.id,
        subject_id=subj.subject_id,
        study_id=subj.study_id,
        site_id=subj.site_id,
        treatment_group=subj.treatment_group,
        status=subj.status,
        active_protocol_version=getattr(subj, "active_protocol_version", None),
        encrypted_demographics=subj.encrypted_demographics,
    )


@router.post(
    "/api/v1/execution/subjects/{subject_id}/consent",
    response_model=SubjectConsentResponse,
)
@router.post(
    "/api/v1/execution/subjects/{subject_id}/consents",
    response_model=SubjectConsentResponse,
)
async def record_subject_consent_endpoint(
    subject_id: str,
    payload: SubjectConsentRequest,
    roles: list[str] = Depends(verify_not_auditor),
    lifecycle_service: SubjectLifecycleService = Depends(get_subject_lifecycle_service),
) -> SubjectConsentResponse:
    u_id = current_user_id.get() or "system"
    reason = current_change_reason.get() or "Record Subject Consent"
    study_id = payload.protocol_version.study_id
    version_tag = payload.protocol_version.version_tag
    version_index = payload.protocol_version.version_index

    saved = await lifecycle_service.record_consent(
        subject_id=subject_id,
        study_id=study_id,
        site_id=None,
        version_tag=version_tag,
        version_index=version_index,
        icf_signed=payload.icf_signed,
        icf_signed_date=payload.icf_signed_date,
        requires_reconsent=payload.requires_reconsent,
        is_paper_override=payload.is_paper_override,
        user_id=u_id,
        reason=reason,
    )

    return SubjectConsentResponse(
        id=saved.id,
        subject_id=saved.subject_id,
        study_id=saved.study_id,
        version_tag=saved.version_tag,
        version_index=saved.version_index,
        icf_signed=saved.icf_signed,
        icf_signed_date=saved.icf_signed_date,
        requires_reconsent=saved.requires_reconsent,
        is_paper_override=saved.is_paper_override,
        version=saved.version,
    )


@router.get(
    "/api/v1/execution/sites/{site_id}/compliance-status",
    response_model=ComplianceStatusResponse,
)
async def get_site_compliance_status(
    site_id: str,
    study_id: str = Query(..., description="The clinical study ID"),
    milestone: str = Query("SITE_ACTIVATION", description="The milestone to check"),
) -> ComplianceStatusResponse:
    async with db_manager.get_session_maker()() as session:
        stmt = select(SiteComplianceCache).where(
            SiteComplianceCache.study_id == study_id,
            SiteComplianceCache.site_id == site_id,
            SiteComplianceCache.milestone == milestone,
        )
        result = await session.execute(stmt)
        cache_entry = result.scalars().first()

        if not cache_entry:
            return ComplianceStatusResponse(
                study_id=study_id,
                site_id=site_id,
                milestone=milestone,
                is_complete=False,
                missing_documents=[],
            )

        missing_list = []
        if cache_entry.missing_documents:
            missing_list = [
                d.strip() for d in cache_entry.missing_documents.split(",") if d.strip()
            ]

        return ComplianceStatusResponse(
            study_id=cache_entry.study_id,
            site_id=cache_entry.site_id,
            milestone=cache_entry.milestone,
            is_complete=cache_entry.is_complete,
            missing_documents=missing_list,
        )


@router.post("/api/v1/execution/webhooks/etmf")
async def etmf_completeness_webhook(
    payload: ETMFCompletenessWebhookPayload,
) -> dict[str, Any]:
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            stmt = select(SiteComplianceCache).where(
                SiteComplianceCache.study_id == payload.study_id,
                SiteComplianceCache.site_id == payload.site_id,
                SiteComplianceCache.milestone == payload.milestone,
            )
            result = await session.execute(stmt)
            cache_entry = result.scalars().first()

            missing_docs_str = (
                ",".join(payload.missing_artifacts)
                if payload.missing_artifacts
                else None
            )

            if cache_entry:
                cache_entry.is_complete = payload.is_complete
                cache_entry.missing_documents = missing_docs_str
                cache_entry.version += 1
            else:
                cache_entry = SiteComplianceCache(
                    study_id=payload.study_id,
                    site_id=payload.site_id,
                    milestone=payload.milestone,
                    is_complete=payload.is_complete,
                    missing_documents=missing_docs_str,
                    version=1,
                )
                session.add(cache_entry)
            await session.commit()

    return {"status": "success", "message": "Compliance cache updated successfully."}


@router.post("/api/v1/execution/sites/{site_id}/activate")
async def activate_site_endpoint(
    site_id: str,
    payload: SiteActivationRequest,
    principal: Principal = Depends(get_principal),
) -> dict[str, Any]:
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            stmt = select(SiteComplianceCache).where(
                SiteComplianceCache.study_id == payload.study_id,
                SiteComplianceCache.site_id == site_id,
                SiteComplianceCache.milestone == "SITE_ACTIVATION",
            )
            result = await session.execute(stmt)
            cache_entry = result.scalars().first()

            if not cache_entry or cache_entry.is_complete is False:
                async with db_manager.get_session_maker()() as audit_session:
                    async with audit_session.begin():
                        block_audit = AuditLog(
                            table_name="site_compliance_caches",
                            record_id=site_id,
                            action="BLOCKED_ACTIVATION",
                            user_id=principal.user_id if principal else "system",
                            change_reason=f"Blocked site activation for site {site_id} due to incomplete eTMF compliance status.",
                        )
                        audit_session.add(block_audit)

                raise HTTPException(
                    status_code=400,
                    detail=f"Site activation blocked: Site {site_id} is not compliant in eTMF. Missing expected documents.",
                )

            success_audit = AuditLog(
                table_name="site_compliance_caches",
                record_id=site_id,
                action="SITE_ACTIVATION",
                user_id=principal.user_id if principal else "system",
                change_reason=f"Site {site_id} successfully activated after eTMF compliance completeness verification.",
            )
            session.add(success_audit)
            await session.commit()

    return {"status": "success", "message": f"Site {site_id} activated successfully."}


@router.post(
    "/api/v1/execution/subjects/{subject_id}/screening",
    response_model=SubjectScreeningResponse,
)
async def evaluate_and_transition_screening(
    subject_id: str,
    request: Request,
    payload: SubjectScreeningRequest | None = None,
    roles: list[str] = Depends(
        require_roles(ROLE_SITE_INVESTIGATOR, ROLE_DATA_MANAGER, "investigator")
    ),
    _justification=Depends(verify_change_justification),
) -> SubjectScreeningResponse:
    change_reason = request.headers.get("X-Change-Reason", "")

    async with db_manager.get_session_maker()() as session:
        try:
            user_val = current_user_id.get()
        except LookupError:
            user_val = "system"
        try:
            reason_val = current_change_reason.get()
        except LookupError:
            reason_val = change_reason or "system_operation"

        await session.execute(
            text("SELECT set_config('cadence.current_user_id', :user_id, true);"),
            {"user_id": user_val},
        )
        await session.execute(
            text("SELECT set_config('cadence.current_change_reason', :reason, true);"),
            {"reason": reason_val},
        )
        await session.execute(
            text("SELECT set_config('cadence.app_writing', 'true', true);")
        )

        stmt_subj = select(ClinicalSubject).where(
            (ClinicalSubject.subject_id == subject_id)
            | (ClinicalSubject.id == subject_id)
        )
        res_subj = await session.execute(stmt_subj)
        subject_obj = res_subj.scalars().first()
        if not subject_obj:
            raise HTTPException(status_code=404, detail="Clinical subject not found.")

        study_id = payload.study_id if payload else None
        if not study_id:
            study_id = subject_obj.study_id

        if not study_id:
            raise HTTPException(
                status_code=400,
                detail="Study ID must be provided in the payload or resolved from the ClinicalSubject.",
            )

        from apps.execution.eligibility_service import evaluate_subject_eligibility

        res = await evaluate_subject_eligibility(study_id, subject_obj, session)

        try:
            if res.eligible is False:
                failed_ids = ", ".join(res.failed_criteria)
                custom_reason = f"Screen failure due to failed criteria: {failed_ids}. Original reason: {change_reason}"
                current_change_reason.set(custom_reason)

                subject_obj.status = "SCREEN_FAILED"
                session.add(subject_obj)
                await session.commit()
            elif res.eligible is True:
                is_compliant = await check_site_compliance_for_enrollment(
                    session, study_id, subject_obj.site_id
                )
                if not is_compliant:
                    async with db_manager.get_session_maker()() as audit_session:
                        async with audit_session.begin():
                            blocked_audit = AuditLog(
                                table_name="clinical_subjects",
                                record_id=subject_obj.id,
                                action="BLOCKED_ENROLLMENT",
                                user_id=user_val,
                                change_reason=f"Blocked enrollment of subject {subject_id} due to non-compliant site {subject_obj.site_id} in study {study_id}.",
                            )
                            audit_session.add(blocked_audit)
                    raise HTTPException(
                        status_code=400,
                        detail=f"Subject enrollment blocked: Site {subject_obj.site_id} is not compliant.",
                    )

                custom_reason = f"Subject met all eligibility criteria and transitioned to ENROLLED. Original reason: {change_reason}"
                current_change_reason.set(custom_reason)

                subject_obj.status = "ENROLLED"
                session.add(subject_obj)
                await session.commit()
            else:
                pass
        except InvalidStateTransitionError as e:
            raise HTTPException(status_code=400, detail=str(e))

        eval_list = []
        for cid, cev in res.criteria_evaluations.items():
            eval_list.append(
                CriterionLevelResult(
                    criterion_id=cev.criterion_id,
                    criterion_type=cev.criterion_type,
                    description=cev.description,
                    dsl_source=cev.dsl_source,
                    is_met=cev.is_met,
                    is_indeterminate=cev.is_indeterminate,
                )
            )

        return SubjectScreeningResponse(
            eligible=res.eligible,
            failed_criteria=res.failed_criteria,
            indeterminate_criteria=res.indeterminate_criteria,
            criterion_evaluations=eval_list,
        )


@router.get(
    "/api/v1/execution/subjects/{subject_id}",
    response_model=SubjectDetailResponse,
)
async def get_subject_detail(
    subject_id: str,
    request: Request,
    principal: Principal = Depends(get_principal),
) -> SubjectDetailResponse:
    async with db_manager.get_session_maker()() as session:
        stmt = select(ClinicalSubject).where(ClinicalSubject.subject_id == subject_id)
        result = await session.execute(stmt)
        subject = result.scalars().first()
        if not subject:
            raise HTTPException(status_code=404, detail="Subject not found")

        enforce_site_isolation(request, subject.site_id, principal)

        stmt_rand = select(SubjectRandomization).where(
            SubjectRandomization.subject_id == subject_id
        )
        rand_res = await session.execute(stmt_rand)
        rand = rand_res.scalars().first()

        treatment_group = None
        randomization_seed = None
        investigational_product_id = None

        if rand:
            from apps.execution.cryptography import AllocationKeyManager

            key_mgr = AllocationKeyManager()
            await key_mgr.load_from_db(session)
            try:
                decrypted = key_mgr.decrypt(rand.encrypted_allocation)
                treatment_group = decrypted.get("allocation")
            except Exception:
                treatment_group = "Decryption Failed"

            randomization_seed = "12345"
            investigational_product_id = rand.kit_reference

        response_dict = {
            "subject_id": subject.subject_id,
            "study_id": subject.study_id,
            "status": subject.status,
            "site_id": subject.site_id,
            "treatment_group": treatment_group,
            "randomization_seed": randomization_seed,
            "investigational_product_id": investigational_product_id,
        }

        roles = getattr(request.state, "roles", None)
        if roles is None:
            roles = principal.roles
        masked = apply_rtsm_blinded_filter(response_dict, roles)
        return SubjectDetailResponse(**masked)


@router.patch(
    "/api/v1/execution/subjects/{id}/state",
    response_model=SubjectResponse,
)
@router.patch(
    "/subjects/{id}/state",
    response_model=SubjectResponse,
)
async def update_subject_state_endpoint(
    id: str,
    payload: SubjectStateUpdateRequest,
    request: Request,
    principal: Principal = Depends(get_principal),
    lifecycle_service: SubjectLifecycleService = Depends(get_subject_lifecycle_service),
) -> SubjectResponse:
    verify_change_justification(request)

    target_state = payload.status or payload.state
    if not target_state:
        raise HTTPException(
            status_code=400, detail="Either status or state must be provided."
        )

    u_id = principal.user_id if principal else (current_user_id.get() or "system")
    reason = current_change_reason.get() or f"Transition to {target_state}"

    if target_state == "ENROLLED":
        subj_obj = await lifecycle_service.subject_repo.get_by_id(id)
        if not subj_obj:
            subj_obj = await lifecycle_service.subject_repo.get_by_subject_code(id)
        if not subj_obj:
            raise HTTPException(status_code=404, detail="Subject not found")

        async with db_manager.get_session_maker()() as session:
            is_compliant = await check_site_compliance_for_enrollment(
                session, subj_obj.study_id, subj_obj.site_id
            )
            if not is_compliant:
                async with db_manager.get_session_maker()() as audit_session:
                    async with audit_session.begin():
                        blocked_audit = AuditLog(
                            table_name="clinical_subjects",
                            record_id=subj_obj.id,
                            action="BLOCKED_ENROLLMENT",
                            user_id=u_id,
                            change_reason=f"Blocked enrollment of subject {id} due to non-compliant site {subj_obj.site_id} in study {subj_obj.study_id}.",
                        )
                        audit_session.add(blocked_audit)
                raise HTTPException(
                    status_code=400,
                    detail=f"Subject enrollment blocked: Site {subj_obj.site_id} is not compliant.",
                )

    try:
        updated = await lifecycle_service.advance_subject_state(
            subject_id=id,
            target_state=target_state,
            user_id=u_id,
            reason=reason,
        )
    except SubjectNotFoundError:
        raise HTTPException(status_code=404, detail="Subject not found")
    except InvalidStateTransitionError:
        raise HTTPException(status_code=400, detail="INVALID_STATE_TRANSITION")
    except Exception as e:
        raise HTTPException(status_code=400, detail=str(e))

    return SubjectResponse(
        id=updated.id,
        subject_id=updated.subject_id,
        study_id=updated.study_id,
        encrypted_demographics=updated.encrypted_demographics,
    )


@router.put(
    "/api/v1/execution/subjects/{id}/demographics",
    response_model=SubjectResponse,
)
@router.put(
    "/subjects/{id}/demographics",
    response_model=SubjectResponse,
)
async def update_subject_demographics_endpoint(
    id: str,
    payload: SubjectDemographicsUpdateRequest,
    request: Request,
    principal: Principal = Depends(get_principal),
) -> SubjectResponse:
    verify_change_justification(request)

    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            stmt = select(ClinicalSubject).where(
                (ClinicalSubject.subject_id == id) | (ClinicalSubject.id == id)
            )
            result = await session.execute(stmt)
            subject = result.scalars().first()
            if not subject:
                raise HTTPException(status_code=404, detail="Subject not found")

            is_post_rand = subject.status in (
                "RANDOMIZED",
                "ACTIVE",
                "COMPLETED",
                "UNBLINDED",
                "WITHDRAWN",
            )

            if payload.strat_factors is not None:
                if is_post_rand and subject.strat_factors != payload.strat_factors:
                    raise HTTPException(
                        status_code=422, detail="LOCKED_FACTOR_MUTATION"
                    )
                subject.strat_factors = payload.strat_factors

            if payload.demographics is not None:
                current_demo = {}
                if subject.encrypted_demographics:
                    with suppress(Exception):
                        current_demo = decrypt_demographics(
                            subject.encrypted_demographics
                        )

                new_demo = payload.demographics.model_dump(exclude_none=True)
                if is_post_rand and current_demo != new_demo:
                    raise HTTPException(
                        status_code=422, detail="LOCKED_FACTOR_MUTATION"
                    )
                encrypted_demo = encrypt_demographics(new_demo)
                subject.encrypted_demographics = encrypted_demo

            session.add(subject)

        stmt_ref = select(ClinicalSubject).where(ClinicalSubject.id == subject.id)
        res_ref = await session.execute(stmt_ref)
        subj_db = res_ref.scalar_one()
        return SubjectResponse(
            id=subj_db.id,
            subject_id=subj_db.subject_id,
            study_id=subj_db.study_id,
            encrypted_demographics=subj_db.encrypted_demographics,
        )


@router.delete(
    "/api/v1/execution/subjects/{id}/demographics",
    response_model=SubjectResponse,
)
@router.delete(
    "/subjects/{id}/demographics",
    response_model=SubjectResponse,
)
async def delete_subject_demographics_endpoint(
    id: str,
    request: Request,
    principal: Principal = Depends(get_principal),
) -> SubjectResponse:
    verify_change_justification(request)

    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            stmt = select(ClinicalSubject).where(
                (ClinicalSubject.subject_id == id) | (ClinicalSubject.id == id)
            )
            result = await session.execute(stmt)
            subject = result.scalars().first()
            if not subject:
                raise HTTPException(status_code=404, detail="Subject not found")

            is_post_rand = subject.status in (
                "RANDOMIZED",
                "ACTIVE",
                "COMPLETED",
                "UNBLINDED",
                "WITHDRAWN",
            )

            if is_post_rand:
                raise HTTPException(status_code=403, detail="SOFT_DELETE_BLOCKED")

            subject.encrypted_demographics = None
            subject.strat_factors = None
            session.add(subject)

        stmt_ref = select(ClinicalSubject).where(ClinicalSubject.id == subject.id)
        res_ref = await session.execute(stmt_ref)
        subj_db = res_ref.scalar_one()
        return SubjectResponse(
            id=subj_db.id,
            subject_id=subj_db.subject_id,
            study_id=subj_db.study_id,
            encrypted_demographics=subj_db.encrypted_demographics,
        )
