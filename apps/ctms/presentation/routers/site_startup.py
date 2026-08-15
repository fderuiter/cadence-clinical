from fastapi import APIRouter, Depends, HTTPException, status

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
    EssentialDocumentCreate,
    EssentialDocumentResponse,
    EssentialDocumentReview,
    SiteGreenlightGateResponse,
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
    service: SiteStartupService = Depends(get_site_startup_service),
    principal: Principal = Depends(get_principal),
) -> SiteGreenlightGateResponse:
    entity = await service.evaluate_site_greenlight(study_id, site_id)
    return SiteGreenlightGateResponse(
        id=entity.id,
        study_id=entity.study_id,
        site_id=entity.site_id,
        overall_status=entity.overall_status,
        contract_approved=entity.contract_approved,
        irb_approved=entity.irb_approved,
        form_1572_approved=entity.form_1572_approved,
        doa_signed_off=entity.doa_signed_off,
        ip_ready=entity.ip_ready,
        greenlight_certified_by=entity.greenlight_certified_by,
        greenlight_certified_at=entity.greenlight_certified_at,
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
    service: SiteStartupService = Depends(get_site_startup_service),
    principal: Principal = Depends(get_principal),
) -> SiteGreenlightGateResponse:
    try:
        entity = await service.certify_site_greenlight(
            study_id=study_id,
            site_id=site_id,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
            reason_for_change=principal.change_reason
            or "Formal greenlight certification",
        )
    except GreenlightPrerequisiteError as e:
        raise HTTPException(status_code=422, detail=str(e))

    return SiteGreenlightGateResponse(
        id=entity.id,
        study_id=entity.study_id,
        site_id=entity.site_id,
        overall_status=entity.overall_status,
        contract_approved=entity.contract_approved,
        irb_approved=entity.irb_approved,
        form_1572_approved=entity.form_1572_approved,
        doa_signed_off=entity.doa_signed_off,
        ip_ready=entity.ip_ready,
        greenlight_certified_by=entity.greenlight_certified_by,
        greenlight_certified_at=entity.greenlight_certified_at,
        rejection_reason=entity.rejection_reason,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )
