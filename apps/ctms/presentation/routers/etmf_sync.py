from fastapi import APIRouter, Depends, status

from apps.ctms.adapters.clients import ETMFClient
from apps.ctms.adapters.repositories import (
    SQLAlchemCTMSDelegationRepository,
    SQLAlchemyETMFSyncRepository,
    get_ctms_repository,
    get_etmf_sync_repository,
)
from apps.ctms.application.etmf_sync_service import ETMFSyncService
from apps.ctms.presentation.dtos import (
    ETMFSyncRecordResponse,
    ETMFSyncRequest,
)
from packages.security.rbac import Principal, get_principal

router = APIRouter(prefix="/api/v1/ctms/etmf-sync", tags=["CTMS eTMF Sync"])


def get_etmf_sync_service(
    sync_repo: SQLAlchemyETMFSyncRepository = Depends(get_etmf_sync_repository),
    doa_repo: SQLAlchemCTMSDelegationRepository = Depends(get_ctms_repository),
) -> ETMFSyncService:
    return ETMFSyncService(
        sync_repo=sync_repo,
        etmf_client=ETMFClient(),
        doa_repo=doa_repo,
    )


@router.post(
    "", response_model=ETMFSyncRecordResponse, status_code=status.HTTP_201_CREATED
)
async def sync_artifact_to_etmf(
    payload: ETMFSyncRequest,
    service: ETMFSyncService = Depends(get_etmf_sync_service),
    principal: Principal = Depends(get_principal),
) -> ETMFSyncRecordResponse:
    entity = await service.sync_artifact_to_etmf(
        study_id=payload.study_id,
        site_id=payload.site_id,
        artifact_type=payload.artifact_type,
        source_record_id=payload.source_record_id,
        title=payload.title,
        content_text=payload.content_text,
        dia_zone=payload.dia_zone,
        dia_section=payload.dia_section,
        dia_artifact=payload.dia_artifact,
        user_id=principal.user_id,
        user_roles=",".join(principal.raw_roles),
        reason_for_change=principal.change_reason
        or "Automated eTMF artifact synchronization",
    )
    return ETMFSyncRecordResponse(
        id=entity.id or "",
        study_id=entity.study_id,
        site_id=entity.site_id,
        artifact_type=entity.artifact_type,
        source_record_id=entity.source_record_id,
        etmf_document_id=entity.etmf_document_id,
        dia_zone=entity.dia_zone,
        dia_section=entity.dia_section,
        dia_artifact=entity.dia_artifact,
        sync_status=entity.sync_status,
        error_message=entity.error_message,
        synced_at=entity.synced_at,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )


@router.get("", response_model=list[ETMFSyncRecordResponse])
async def list_etmf_sync_records(
    study_id: str,
    site_id: str | None = None,
    service: ETMFSyncService = Depends(get_etmf_sync_service),
    principal: Principal = Depends(get_principal),
) -> list[ETMFSyncRecordResponse]:
    entities = await service.list_sync_records(study_id, site_id)
    return [
        ETMFSyncRecordResponse(
            id=e.id or "",
            study_id=e.study_id,
            site_id=e.site_id,
            artifact_type=e.artifact_type,
            source_record_id=e.source_record_id,
            etmf_document_id=e.etmf_document_id,
            dia_zone=e.dia_zone,
            dia_section=e.dia_section,
            dia_artifact=e.dia_artifact,
            sync_status=e.sync_status,
            error_message=e.error_message,
            synced_at=e.synced_at,
            created_at=e.created_at,
            created_by=e.created_by,
            reason_for_change=e.reason_for_change,
            version_index=e.version_index,
        )
        for e in entities
    ]
