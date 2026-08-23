from typing import Any

from fastapi import APIRouter, Depends, HTTPException, Query, status

from apps.ctms.adapters.repositories import (
    SQLAlchemCTMSDelegationRepository,
    SQLAlchemySiteStartupRepository,
    get_ctms_repository,
    get_site_startup_repository,
)
from apps.ctms.application.site_startup_service import SiteStartupService
from apps.ctms.domain.exceptions import GreenlightPrerequisiteError
from apps.ctms.presentation.dtos import (
    CountryMilestoneCreate,
    CountryMilestoneResponse,
    CountryPrerequisiteRuleCreate,
    CountryPrerequisiteRuleResponse,
    EssentialDocumentCreate,
    EssentialDocumentResponse,
    EssentialDocumentReview,
    GreenlightTokenVerifyRequest,
    GreenlightTokenVerifyResponse,
    SiteGreenlightGateResponse,
    SiteGreenlightOverrideRequest,
)
from packages.security.rbac import Principal, get_principal, has_permission

router = APIRouter(prefix="/api/v1/ctms/startup", tags=["CTMS Site Startup"])


def get_site_startup_service(
    startup_repo: SQLAlchemySiteStartupRepository = Depends(
        get_site_startup_repository
    ),
    doa_repo: SQLAlchemCTMSDelegationRepository = Depends(get_ctms_repository),
) -> SiteStartupService:
    return SiteStartupService(startup_repo=startup_repo, doa_repo=doa_repo)


# =========================================================================
# Country Prerequisite Rules Matrix Endpoints (PRD-SSU-001)
# =========================================================================


@router.get("/rules/presets", response_model=dict[str, list[dict[str, Any]]])
async def get_jurisdiction_rule_presets(
    service: SiteStartupService = Depends(get_site_startup_service),
    principal: Principal = Depends(get_principal),
) -> dict[str, list[dict[str, Any]]]:
    """Returns statutory startup prerequisite rule presets for US, EU, JP, UK, and Global jurisdictions."""
    return service.get_jurisdiction_presets()


@router.post(
    "/rules",
    response_model=CountryPrerequisiteRuleResponse,
    status_code=status.HTTP_201_CREATED,
)
async def create_or_update_country_prerequisite_rule(
    payload: CountryPrerequisiteRuleCreate,
    service: SiteStartupService = Depends(get_site_startup_service),
    principal: Principal = Depends(get_principal),
) -> CountryPrerequisiteRuleResponse:
    """Configures a country-specific prerequisite criterion for site activation."""
    if not (
        has_permission(principal, "ctms_study:create")
        or has_permission(principal, "ctms_site:update")
    ):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    entity = await service.create_or_update_country_prerequisite_rule(
        study_id=payload.study_id,
        country_code=payload.country_code,
        facet=payload.facet,
        criterion_code=payload.criterion_code,
        criterion_name=payload.criterion_name,
        is_mandatory=payload.is_mandatory,
        description=payload.description,
        user_id=principal.user_id,
        user_roles=",".join(principal.raw_roles),
        reason_for_change=principal.change_reason or "Prerequisite rule configuration",
    )
    return CountryPrerequisiteRuleResponse(
        id=entity.id or "",
        study_id=entity.study_id,
        country_code=entity.country_code,
        facet=entity.facet,
        criterion_code=entity.criterion_code,
        criterion_name=entity.criterion_name,
        is_mandatory=entity.is_mandatory,
        description=entity.description,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )


@router.get("/rules", response_model=list[CountryPrerequisiteRuleResponse])
async def list_country_prerequisite_rules(
    study_id: str,
    country_code: str | None = None,
    service: SiteStartupService = Depends(get_site_startup_service),
    principal: Principal = Depends(get_principal),
) -> list[CountryPrerequisiteRuleResponse]:
    """Retrieves country-specific prerequisite rules matrix for a study."""
    entities = await service.list_country_prerequisite_rules(study_id, country_code)
    return [
        CountryPrerequisiteRuleResponse(
            id=e.id or "",
            study_id=e.study_id,
            country_code=e.country_code,
            facet=e.facet,
            criterion_code=e.criterion_code,
            criterion_name=e.criterion_name,
            is_mandatory=e.is_mandatory,
            description=e.description,
            created_at=e.created_at,
            created_by=e.created_by,
            reason_for_change=e.reason_for_change,
            version_index=e.version_index,
        )
        for e in entities
    ]


@router.post(
    "/milestones",
    response_model=CountryMilestoneResponse,
    status_code=status.HTTP_201_CREATED,
)
async def create_or_update_country_milestone(
    payload: CountryMilestoneCreate,
    service: SiteStartupService = Depends(get_site_startup_service),
    principal: Principal = Depends(get_principal),
) -> CountryMilestoneResponse:
    if not (
        has_permission(principal, "ctms_study:create")
        or has_permission(principal, "ctms_site:update")
    ):
        raise HTTPException(status_code=403, detail="Forbidden: Access denied.")

    entity = await service.create_or_update_country_milestone(
        study_id=payload.study_id,
        country_code=payload.country_code,
        milestone_type=payload.milestone_type,
        status=payload.status,
        planned_date=payload.planned_date,
        actual_date=payload.actual_date,
        approval_number=payload.approval_number,
        authority_name=payload.authority_name,
        user_id=principal.user_id,
        user_roles=",".join(principal.raw_roles),
        reason_for_change=principal.change_reason or "Regulatory milestone update",
    )
    return CountryMilestoneResponse(
        id=entity.id or "",
        study_id=entity.study_id,
        country_code=entity.country_code,
        milestone_type=entity.milestone_type,
        status=entity.status,
        planned_date=entity.planned_date,
        actual_date=entity.actual_date,
        approval_number=entity.approval_number,
        authority_name=entity.authority_name,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )


@router.get("/milestones", response_model=list[CountryMilestoneResponse])
async def list_country_milestones(
    study_id: str,
    country_code: str | None = None,
    service: SiteStartupService = Depends(get_site_startup_service),
    principal: Principal = Depends(get_principal),
) -> list[CountryMilestoneResponse]:
    entities = await service.list_country_milestones(study_id, country_code)
    return [
        CountryMilestoneResponse(
            id=e.id or "",
            study_id=e.study_id,
            country_code=e.country_code,
            milestone_type=e.milestone_type,
            status=e.status,
            planned_date=e.planned_date,
            actual_date=e.actual_date,
            approval_number=e.approval_number,
            authority_name=e.authority_name,
            created_at=e.created_at,
            created_by=e.created_by,
            reason_for_change=e.reason_for_change,
            version_index=e.version_index,
        )
        for e in entities
    ]


@router.post(
    "/documents",
    response_model=EssentialDocumentResponse,
    status_code=status.HTTP_201_CREATED,
)
async def submit_essential_document(
    payload: EssentialDocumentCreate,
    service: SiteStartupService = Depends(get_site_startup_service),
    principal: Principal = Depends(get_principal),
) -> EssentialDocumentResponse:
    entity = await service.submit_essential_document(
        study_id=payload.study_id,
        site_id=payload.site_id,
        document_type=payload.document_type,
        file_name=payload.file_name,
        file_hash=payload.file_hash,
        expiration_date=payload.expiration_date,
        user_id=principal.user_id,
        user_roles=",".join(principal.raw_roles),
        reason_for_change=principal.change_reason or "Essential document submission",
    )
    return EssentialDocumentResponse(
        id=entity.id or "",
        study_id=entity.study_id,
        site_id=entity.site_id,
        document_type=entity.document_type,
        file_name=entity.file_name,
        file_hash=entity.file_hash,
        status=entity.status,
        expiration_date=entity.expiration_date,
        review_notes=entity.review_notes,
        reviewed_by=entity.reviewed_by,
        reviewed_at=entity.reviewed_at,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )


@router.put("/documents/{document_id}/review", response_model=EssentialDocumentResponse)
@router.post(
    "/documents/{document_id}/review", response_model=EssentialDocumentResponse
)
async def review_essential_document(
    document_id: str,
    payload: EssentialDocumentReview,
    service: SiteStartupService = Depends(get_site_startup_service),
    principal: Principal = Depends(get_principal),
) -> EssentialDocumentResponse:
    try:
        entity = await service.review_essential_document(
            document_id=document_id,
            status=payload.status,
            review_notes=payload.review_notes,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
            reason_for_change=principal.change_reason or "Essential document review",
        )
    except GreenlightPrerequisiteError as e:
        raise HTTPException(status_code=404, detail=str(e))

    return EssentialDocumentResponse(
        id=entity.id or "",
        study_id=entity.study_id,
        site_id=entity.site_id,
        document_type=entity.document_type,
        file_name=entity.file_name,
        file_hash=entity.file_hash,
        status=entity.status,
        expiration_date=entity.expiration_date,
        review_notes=entity.review_notes,
        reviewed_by=entity.reviewed_by,
        reviewed_at=entity.reviewed_at,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )


@router.get("/documents", response_model=list[EssentialDocumentResponse])
async def list_essential_documents(
    study_id: str,
    site_id: str | None = None,
    service: SiteStartupService = Depends(get_site_startup_service),
    principal: Principal = Depends(get_principal),
) -> list[EssentialDocumentResponse]:
    entities = await service.list_essential_documents(study_id, site_id)
    return [
        EssentialDocumentResponse(
            id=e.id or "",
            study_id=e.study_id,
            site_id=e.site_id,
            document_type=e.document_type,
            file_name=e.file_name,
            file_hash=e.file_hash,
            status=e.status,
            expiration_date=e.expiration_date,
            review_notes=e.review_notes,
            reviewed_by=e.reviewed_by,
            reviewed_at=e.reviewed_at,
            created_at=e.created_at,
            created_by=e.created_by,
            reason_for_change=e.reason_for_change,
            version_index=e.version_index,
        )
        for e in entities
    ]


@router.get("/sites/{site_id}/greenlight", response_model=SiteGreenlightGateResponse)
async def evaluate_site_greenlight(
    site_id: str,
    study_id: str,
    country_code: str = Query(
        "US", description="ISO-2 Country code or jurisdiction (e.g. US, EU, JP, GB)"
    ),
    service: SiteStartupService = Depends(get_site_startup_service),
    principal: Principal = Depends(get_principal),
) -> SiteGreenlightGateResponse:
    """Evaluates real-time site activation readiness across the 6 key operational facets."""
    entity = await service.evaluate_site_greenlight(
        study_id=study_id, site_id=site_id, country_code=country_code
    )
    return SiteGreenlightGateResponse(
        id=entity.id,
        study_id=entity.study_id,
        site_id=entity.site_id,
        country_code=entity.country_code,
        overall_status=entity.overall_status,
        facet_regulatory_irb=entity.facet_regulatory_irb,
        facet_cta_budget=entity.facet_cta_budget,
        facet_eisf_baseline=entity.facet_eisf_baseline,
        facet_doa_log=entity.facet_doa_log,
        facet_staff_training=entity.facet_staff_training,
        facet_ip_release=entity.facet_ip_release,
        contract_approved=entity.contract_approved,
        irb_approved=entity.irb_approved,
        form_1572_approved=entity.form_1572_approved,
        doa_signed_off=entity.doa_signed_off,
        ip_ready=entity.ip_ready,
        greenlight_token=entity.greenlight_token,
        greenlight_certified_by=entity.greenlight_certified_by,
        greenlight_certified_at=entity.greenlight_certified_at,
        is_overridden=entity.is_overridden,
        override_sponsor_user_id=entity.override_sponsor_user_id,
        override_medical_user_id=entity.override_medical_user_id,
        override_justification=entity.override_justification,
        override_authorized_at=entity.override_authorized_at,
        evaluation_details=entity.evaluation_details,
        rejection_reason=entity.rejection_reason,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )


@router.post(
    "/sites/{site_id}/greenlight/certify", response_model=SiteGreenlightGateResponse
)
async def certify_site_greenlight(
    site_id: str,
    study_id: str,
    country_code: str = Query("US", description="ISO-2 Country code or jurisdiction"),
    service: SiteStartupService = Depends(get_site_startup_service),
    principal: Principal = Depends(get_principal),
) -> SiteGreenlightGateResponse:
    """Formally certifies greenlight activation, issuing an HMAC-SHA256 token and un-gating trial execution."""
    try:
        entity = await service.certify_site_greenlight(
            study_id=study_id,
            site_id=site_id,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
            reason_for_change=principal.change_reason
            or "Formal greenlight certification",
            country_code=country_code,
        )
    except GreenlightPrerequisiteError as e:
        raise HTTPException(status_code=422, detail=str(e))

    return SiteGreenlightGateResponse(
        id=entity.id,
        study_id=entity.study_id,
        site_id=entity.site_id,
        country_code=entity.country_code,
        overall_status=entity.overall_status,
        facet_regulatory_irb=entity.facet_regulatory_irb,
        facet_cta_budget=entity.facet_cta_budget,
        facet_eisf_baseline=entity.facet_eisf_baseline,
        facet_doa_log=entity.facet_doa_log,
        facet_staff_training=entity.facet_staff_training,
        facet_ip_release=entity.facet_ip_release,
        contract_approved=entity.contract_approved,
        irb_approved=entity.irb_approved,
        form_1572_approved=entity.form_1572_approved,
        doa_signed_off=entity.doa_signed_off,
        ip_ready=entity.ip_ready,
        greenlight_token=entity.greenlight_token,
        greenlight_certified_by=entity.greenlight_certified_by,
        greenlight_certified_at=entity.greenlight_certified_at,
        is_overridden=entity.is_overridden,
        override_sponsor_user_id=entity.override_sponsor_user_id,
        override_medical_user_id=entity.override_medical_user_id,
        override_justification=entity.override_justification,
        override_authorized_at=entity.override_authorized_at,
        evaluation_details=entity.evaluation_details,
        rejection_reason=entity.rejection_reason,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )


@router.post(
    "/sites/{site_id}/greenlight/override", response_model=SiteGreenlightGateResponse
)
async def override_site_greenlight(
    site_id: str,
    payload: SiteGreenlightOverrideRequest,
    service: SiteStartupService = Depends(get_site_startup_service),
    principal: Principal = Depends(get_principal),
) -> SiteGreenlightGateResponse:
    """Executes dual-signature executive sponsor GxP override with mandatory Part 11 justification logging."""
    try:
        entity = await service.override_site_greenlight(
            study_id=payload.study_id,
            site_id=site_id,
            sponsor_user_id=payload.sponsor_user_id,
            medical_user_id=payload.medical_user_id,
            sponsor_signature=payload.sponsor_signature,
            medical_signature=payload.medical_signature,
            justification=payload.justification,
            user_roles=",".join(principal.raw_roles),
            reason_for_change=principal.change_reason
            or "Executive GxP Greenlight Override",
            country_code=payload.country_code,
        )
    except GreenlightPrerequisiteError as e:
        raise HTTPException(status_code=422, detail=str(e))

    return SiteGreenlightGateResponse(
        id=entity.id,
        study_id=entity.study_id,
        site_id=entity.site_id,
        country_code=entity.country_code,
        overall_status=entity.overall_status,
        facet_regulatory_irb=entity.facet_regulatory_irb,
        facet_cta_budget=entity.facet_cta_budget,
        facet_eisf_baseline=entity.facet_eisf_baseline,
        facet_doa_log=entity.facet_doa_log,
        facet_staff_training=entity.facet_staff_training,
        facet_ip_release=entity.facet_ip_release,
        contract_approved=entity.contract_approved,
        irb_approved=entity.irb_approved,
        form_1572_approved=entity.form_1572_approved,
        doa_signed_off=entity.doa_signed_off,
        ip_ready=entity.ip_ready,
        greenlight_token=entity.greenlight_token,
        greenlight_certified_by=entity.greenlight_certified_by,
        greenlight_certified_at=entity.greenlight_certified_at,
        is_overridden=entity.is_overridden,
        override_sponsor_user_id=entity.override_sponsor_user_id,
        override_medical_user_id=entity.override_medical_user_id,
        override_justification=entity.override_justification,
        override_authorized_at=entity.override_authorized_at,
        evaluation_details=entity.evaluation_details,
        rejection_reason=entity.rejection_reason,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )


@router.post(
    "/sites/{site_id}/greenlight/verify-token",
    response_model=GreenlightTokenVerifyResponse,
)
async def verify_site_greenlight_token_endpoint(
    site_id: str,
    payload: GreenlightTokenVerifyRequest,
    service: SiteStartupService = Depends(get_site_startup_service),
    principal: Principal = Depends(get_principal),
) -> GreenlightTokenVerifyResponse:
    """Verifies cryptographic validity and binding of a Site Greenlight Activation Token."""
    res = service.verify_greenlight_activation_token(
        token=payload.token,
        site_id=payload.site_id or site_id,
        study_id=payload.study_id,
    )
    if res is None:
        return GreenlightTokenVerifyResponse(
            is_valid=False,
            payload=None,
            error="Invalid, expired, or tampered cryptographic greenlight activation token.",
        )
    return GreenlightTokenVerifyResponse(is_valid=True, payload=res, error=None)
