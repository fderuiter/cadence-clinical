"""FastAPI router for clinical file operations, uploads, downloads, and grants.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002, PRD-DOC-003
"""

from fastapi import APIRouter, Depends, HTTPException, Request, status
from sqlalchemy.ext.asyncio import AsyncSession

from apps.fileshare.adapters.database import get_db_session
from apps.fileshare.adapters.repositories import (
    SqlAlchemyFileRecordRepository,
    SqlAlchemyGuestLinkRepository,
    SqlAlchemyShareGrantRepository,
)
from apps.fileshare.adapters.storage import get_storage_adapter
from apps.fileshare.application.fileshare_service import FileShareService
from apps.fileshare.domain.exceptions import (
    FileNotFoundError,
    FileSharePermissionDeniedError,
)
from apps.fileshare.presentation.dtos import (
    FileDownloadUrlResponse,
    FileRecordResponse,
    FileUploadUrlRequest,
    FileUploadUrlResponse,
    GuestLinkCreateRequest,
    GuestLinkResponse,
    ShareGrantCreateRequest,
    ShareGrantResponse,
)
from packages.security.context import (
    current_site_id,
    current_tenant_id,
    current_user_id,
)

router = APIRouter(prefix="/api/v1/fileshare/files", tags=["Fileshare"])


def extract_caller_roles(request: Request) -> list[str]:
    """Extract authenticated caller roles from request state or gateway headers."""
    raw_roles = (
        getattr(request.state, "roles", None)
        or request.headers.get("X-User-Roles")
        or ""
    )
    return [r.strip() for r in str(raw_roles).split(",") if r.strip()]


def get_fileshare_service(
    session: AsyncSession = Depends(get_db_session),
) -> FileShareService:
    """Dependency provider for FileShareService."""
    file_repo = SqlAlchemyFileRecordRepository(session)
    grant_repo = SqlAlchemyShareGrantRepository(session)
    guest_repo = SqlAlchemyGuestLinkRepository(session)
    storage_port = get_storage_adapter()
    return FileShareService(
        file_repo=file_repo,
        grant_repo=grant_repo,
        guest_repo=guest_repo,
        storage_port=storage_port,
    )


@router.post(
    "/upload-url",
    response_model=FileUploadUrlResponse,
    status_code=status.HTTP_201_CREATED,
)
async def get_upload_url(
    payload: FileUploadUrlRequest,
    service: FileShareService = Depends(get_fileshare_service),
) -> FileUploadUrlResponse:
    """Allocate draft file record and generate presigned PUT or multipart upload URLs.

    Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002
    """
    user_id = current_user_id.get() or "system_user"
    tenant_id = current_tenant_id.get() or "tenant_default"

    session = await service.generate_upload_url(
        study_id=payload.study_id,
        filename=payload.filename,
        mime_type=payload.mime_type,
        size_bytes=payload.size_bytes,
        uploader_id=user_id,
        reason_for_change=payload.reason_for_change,
        tenant_id=tenant_id,
        site_id=payload.site_id,
        is_multipart=payload.is_multipart,
        parts_count=payload.parts_count,
    )
    return FileUploadUrlResponse(
        file_id=session.file_id,
        object_key=session.object_key,
        upload_id=session.upload_id,
        upload_url=session.upload_url,
        upload_urls=session.upload_urls,
        expires_in=session.expires_in,
    )


@router.get(
    "/{file_id}/download-url",
    response_model=FileDownloadUrlResponse,
    status_code=status.HTTP_200_OK,
)
async def get_download_url(
    file_id: str,
    request: Request,
    service: FileShareService = Depends(get_fileshare_service),
) -> FileDownloadUrlResponse:
    """Retrieve short-lived presigned GET download URL with permission and watermark verification.

    Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-003
    """
    user_id = current_user_id.get() or "anonymous_user"
    roles = extract_caller_roles(request)
    site_id = current_site_id.get()

    try:
        session = await service.generate_download_url(
            file_id=file_id,
            caller_user_id=user_id,
            caller_roles=roles,
            caller_site_id=site_id,
        )
        return FileDownloadUrlResponse(
            file_id=session.file_id,
            filename=session.filename,
            mime_type=session.mime_type,
            download_url=session.download_url,
            expires_in=session.expires_in,
            is_watermarked=session.is_watermarked,
        )
    except FileNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
    except FileSharePermissionDeniedError as exc:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN, detail=str(exc)
        ) from exc


@router.get(
    "/{file_id}",
    response_model=FileRecordResponse,
    status_code=status.HTTP_200_OK,
)
async def get_file_record(
    file_id: str,
    service: FileShareService = Depends(get_fileshare_service),
) -> FileRecordResponse:
    """Retrieve file record metadata.

    Requirements: PRD-SYS-001, PRD-DOC-001
    """
    record = await service.file_repo.get_by_id(file_id)
    if not record:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"File record '{file_id}' not found.",
        )
    return FileRecordResponse.model_validate(record)


@router.get(
    "",
    response_model=list[FileRecordResponse],
    status_code=status.HTTP_200_OK,
)
async def list_files(
    study_id: str,
    site_id: str | None = None,
    service: FileShareService = Depends(get_fileshare_service),
) -> list[FileRecordResponse]:
    """List active file records for a study and site.

    Requirements: PRD-SYS-001, PRD-DOC-001
    """
    records = await service.file_repo.list_by_study(study_id=study_id, site_id=site_id)
    return [FileRecordResponse.model_validate(r) for r in records]


@router.post(
    "/{file_id}/grants",
    response_model=ShareGrantResponse,
    status_code=status.HTTP_201_CREATED,
)
async def create_share_grant(
    file_id: str,
    payload: ShareGrantCreateRequest,
    request: Request,
    service: FileShareService = Depends(get_fileshare_service),
) -> ShareGrantResponse:
    """Delegate file permissions via an internal share grant.

    Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-003
    """
    user_id = current_user_id.get() or "system_user"
    roles = extract_caller_roles(request)

    try:
        grant = await service.create_share_grant(
            file_id=file_id,
            grantor_user_id=user_id,
            grantor_roles=roles,
            granted_to_user_id=payload.granted_to_user_id,
            scope=payload.scope,
            permission_level=payload.permission_level,
            reason_for_change=payload.reason_for_change,
            expires_at=payload.expires_at,
        )
        return ShareGrantResponse.model_validate(grant)
    except FileNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
    except FileSharePermissionDeniedError as exc:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN, detail=str(exc)
        ) from exc


@router.post(
    "/{file_id}/guest-links",
    response_model=GuestLinkResponse,
    status_code=status.HTTP_201_CREATED,
)
async def create_guest_link(
    file_id: str,
    payload: GuestLinkCreateRequest,
    service: FileShareService = Depends(get_fileshare_service),
) -> GuestLinkResponse:
    """Generate a time-bounded external guest link.

    Requirements: PRD-SYS-001, PRD-DOC-001
    """
    user_id = current_user_id.get() or "system_user"

    try:
        link = await service.create_guest_link(
            file_id=file_id,
            creator_user_id=user_id,
            reason_for_change=payload.reason_for_change,
            expires_in_hours=payload.expires_in_hours,
        )
        return GuestLinkResponse(
            id=link.id,
            file_record_id=link.file_record_id,
            guest_url=link.guest_url,
            expires_at=link.expires_at,
            created_by=link.created_by,
            access_count=link.access_count,
            is_valid=link.is_valid,
        )
    except FileNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
