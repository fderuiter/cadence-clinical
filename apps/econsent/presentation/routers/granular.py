"""FastAPI sub-router for granular & tiered optional consent choices."""

from fastapi import APIRouter, Depends, Request
from sqlalchemy.ext.asyncio import AsyncSession

from apps.econsent.adapters.database import db_manager
from apps.econsent.adapters.repositories import (
    SQLConsentAuditRepository,
    SQLGranularOptionRepository,
)
from apps.econsent.application.use_cases import GranularOptionService
from apps.econsent.presentation.dtos import (
    GranularOptionCreate,
    GranularOptionResponse,
)
from packages.database import DatabaseSessionDependency

router = APIRouter(prefix="/api/v1/econsent/options", tags=["Granular Options"])
get_db_session = DatabaseSessionDependency(db_manager)


@router.post(
    "/{template_id}/{version_index}",
    response_model=GranularOptionResponse,
    status_code=201,
)
async def create_granular_option(
    request: Request,
    template_id: str,
    version_index: int,
    payload: GranularOptionCreate,
    session: AsyncSession = Depends(get_db_session),
) -> GranularOptionResponse:
    """Configures a granular optional research choice on a template version."""
    user_id = getattr(request.state, "user_id", "study_designer")
    change_reason = getattr(request.state, "change_reason", payload.reason_for_change)

    granular_repo = SQLGranularOptionRepository(session)
    audit_repo = SQLConsentAuditRepository(session)
    svc = GranularOptionService(granular_repo, audit_repo)

    opt = await svc.create_option(
        template_id=template_id,
        version_index=version_index,
        option_code=payload.option_code,
        title=payload.title,
        description=payload.description,
        category=payload.category,
        is_mandatory=payload.is_mandatory,
        default_selected=payload.default_selected,
        created_by=user_id,
        reason_for_change=change_reason,
    )
    return GranularOptionResponse(
        id=opt.id,
        template_id=opt.template_id,
        version_index=opt.version_index,
        option_code=opt.option_code,
        title=opt.title,
        description=opt.description,
        category=opt.category,
        is_mandatory=opt.is_mandatory,
        default_selected=opt.default_selected,
        created_at=opt.created_at,
        created_by=opt.created_by,
        reason_for_change=opt.reason_for_change,
    )


@router.get(
    "/{template_id}/{version_index}",
    response_model=list[GranularOptionResponse],
)
async def list_granular_options(
    template_id: str,
    version_index: int,
    session: AsyncSession = Depends(get_db_session),
) -> list[GranularOptionResponse]:
    """Lists all configured granular research options for a template version."""
    granular_repo = SQLGranularOptionRepository(session)
    audit_repo = SQLConsentAuditRepository(session)
    svc = GranularOptionService(granular_repo, audit_repo)

    options = await svc.list_options(template_id, version_index)
    return [
        GranularOptionResponse(
            id=o.id,
            template_id=o.template_id,
            version_index=o.version_index,
            option_code=o.option_code,
            title=o.title,
            description=o.description,
            category=o.category,
            is_mandatory=o.is_mandatory,
            default_selected=o.default_selected,
            created_at=o.created_at,
            created_by=o.created_by,
            reason_for_change=o.reason_for_change,
        )
        for o in options
    ]
