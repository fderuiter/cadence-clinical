"""FastAPI router for clinical file operations, uploads, downloads, and grants.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002, PRD-DOC-003
"""

from fastapi import APIRouter, Depends, HTTPException, Request, Response, status
from fastapi.responses import JSONResponse
from sqlalchemy.ext.asyncio import AsyncSession

from apps.fileshare.adapters.clamav_adapter import get_scan_adapter
from apps.fileshare.adapters.conversion_adapter import get_conversion_adapter
from apps.fileshare.adapters.credential_verifier import get_credential_verifier
from apps.fileshare.adapters.database import get_db_session
from apps.fileshare.adapters.repositories import (
    SqlAlchemyFileAuditLogRepository,
    SqlAlchemyFileRecordRepository,
    SqlAlchemyGuestLinkRepository,
    SqlAlchemyPlaybackAuditRepository,
    SqlAlchemyShareGrantRepository,
)
from apps.fileshare.adapters.storage import get_storage_adapter
from apps.fileshare.application.fileshare_service import FileShareService
from apps.fileshare.domain.exceptions import (
    FileImmutableError,
    FileNotFoundError,
    FileOnHoldError,
    FileQuarantinedError,
    FileSharePermissionDeniedError,
    InvalidCredentialError,
)
from apps.fileshare.domain.models import TranscodeStatus
from apps.fileshare.presentation.dtos import (
    FileApproveRequest,
    FileAuditLogResponse,
    FileDeleteRequest,
    FileDownloadUrlResponse,
    FileHoldClearRequest,
    FileHoldRequest,
    FileRecordResponse,
    FileRejectRequest,
    FileRescindApprovalRequest,
    FileUploadUrlRequest,
    FileUploadUrlResponse,
    GuestLinkCreateRequest,
    GuestLinkResponse,
    PlaybackAuditCreateRequest,
    PlaybackAuditResponse,
    ShareGrantCreateRequest,
    ShareGrantResponse,
    StreamUrlResponse,
    UploadCompleteRequest,
    UploadRevisionRequest,
)
from packages.security.context import (
    current_site_id,
    current_tenant_id,
    current_user_id,
)

router = APIRouter(prefix="/api/v1/fileshare/files", tags=["Fileshare"])
files_alias_router = APIRouter(prefix="/api/v1/files", tags=["Fileshare"])


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
    audit_repo = SqlAlchemyPlaybackAuditRepository(session)
    file_audit_repo = SqlAlchemyFileAuditLogRepository(session)
    storage_port = get_storage_adapter()
    scan_port = get_scan_adapter()
    credential_verifier = get_credential_verifier()
    conversion_port = get_conversion_adapter(storage_port)
    return FileShareService(
        file_repo=file_repo,
        grant_repo=grant_repo,
        guest_repo=guest_repo,
        storage_port=storage_port,
        audit_repo=audit_repo,
        scan_port=scan_port,
        file_audit_repo=file_audit_repo,
        credential_verifier=credential_verifier,
        conversion_port=conversion_port,
    )


@router.post(
    "/upload-url",
    response_model=FileUploadUrlResponse,
    status_code=status.HTTP_201_CREATED,
)
@router.post(
    "/upload/presigned-url",
    response_model=FileUploadUrlResponse,
    status_code=status.HTTP_201_CREATED,
)
@files_alias_router.post(
    "/upload-url",
    response_model=FileUploadUrlResponse,
    status_code=status.HTTP_201_CREATED,
)
@files_alias_router.post(
    "/upload/presigned-url",
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
        checksum_sha256=payload.checksum_sha256,
    )
    return FileUploadUrlResponse(
        file_id=session.file_id,
        object_key=session.object_key,
        upload_id=session.upload_id,
        upload_url=session.upload_url,
        upload_urls=session.upload_urls,
        expires_in=session.expires_in,
        checksum_sha256=session.checksum_sha256,
        required_headers=session.required_headers or {},
    )


@router.post(
    "/{file_id}/complete-upload",
    response_model=FileRecordResponse,
    status_code=status.HTTP_200_OK,
)
@router.post(
    "/{file_id}/upload-complete",
    response_model=FileRecordResponse,
    status_code=status.HTTP_200_OK,
)
@files_alias_router.post(
    "/{file_id}/complete-upload",
    response_model=FileRecordResponse,
    status_code=status.HTTP_200_OK,
)
@files_alias_router.post(
    "/{file_id}/upload-complete",
    response_model=FileRecordResponse,
    status_code=status.HTTP_200_OK,
)
async def complete_file_upload(
    file_id: str,
    payload: UploadCompleteRequest | None = None,
    service: FileShareService = Depends(get_fileshare_service),
) -> FileRecordResponse:
    """Finalize singlepart or multipart upload and trigger asynchronous video transcoding if applicable.

    Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002
    """
    user_id = current_user_id.get() or "system_user"
    parts = payload.parts if payload else None
    checksum = payload.checksum_sha256 if payload else None
    reason = (
        payload.reason_for_change
        if payload and payload.reason_for_change
        else "Completed file upload"
    )

    try:
        record = await service.complete_upload(
            file_id=file_id,
            caller_user_id=user_id,
            parts=parts,
            checksum_sha256=checksum,
            reason_for_change=reason,
        )
        return FileRecordResponse.model_validate(record)
    except FileNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc


@router.post(
    "/{file_id}/abort-upload",
    response_model=FileRecordResponse,
    status_code=status.HTTP_200_OK,
)
@files_alias_router.post(
    "/{file_id}/abort-upload",
    response_model=FileRecordResponse,
    status_code=status.HTTP_200_OK,
)
async def abort_file_upload(
    file_id: str,
    upload_id: str | None = None,
    service: FileShareService = Depends(get_fileshare_service),
) -> FileRecordResponse:
    """Abort multipart upload session and mark file record deleted.

    Requirements: PRD-SYS-001, PRD-DOC-001
    """
    user_id = current_user_id.get() or "system_user"
    try:
        record = await service.abort_upload(
            file_id=file_id,
            caller_user_id=user_id,
            upload_id=upload_id,
        )
        return FileRecordResponse.model_validate(record)
    except FileNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc


@router.get(
    "/{file_id}/stream-url",
    response_model=StreamUrlResponse,
    status_code=status.HTTP_200_OK,
    responses={
        200: {
            "description": "HLS master manifest presigned URL is ready for streaming."
        },
        202: {"description": "Video transcoding is pending or in progress."},
        400: {"description": "Transcoding is not required for non-video file."},
        409: {"description": "Video transcoding failed."},
    },
)
@files_alias_router.get(
    "/{file_id}/stream-url",
    response_model=StreamUrlResponse,
    status_code=status.HTTP_200_OK,
    responses={
        200: {
            "description": "HLS master manifest presigned URL is ready for streaming."
        },
        202: {"description": "Video transcoding is pending or in progress."},
        400: {"description": "Transcoding is not required for non-video file."},
        409: {"description": "Video transcoding failed."},
    },
)
async def get_stream_url(
    file_id: str,
    request: Request,
    response: Response,
    service: FileShareService = Depends(get_fileshare_service),
) -> StreamUrlResponse:
    """Issue short-lived presigned HLS master manifest stream URL for video playback.

    Returns HTTP 200 with presigned URL when transcoding is READY,
    HTTP 202 Accepted when transcoding is PENDING or IN_PROGRESS,
    HTTP 409 Conflict when transcoding has FAILED,
    and HTTP 400 Bad Request when transcoding was NOT_REQUIRED.

    Requirements: PRD-SYS-001, PRD-DOC-001
    """
    user_id = current_user_id.get() or "anonymous_user"
    roles = extract_caller_roles(request)
    site_id = current_site_id.get()

    try:
        record, stream_url = await service.generate_stream_url(
            file_id=file_id,
            caller_user_id=user_id,
            caller_roles=roles,
            caller_site_id=site_id,
        )
    except FileNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
    except FileQuarantinedError as exc:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail={"message": str(exc), "reason": exc.reason},
        ) from exc
    except FileSharePermissionDeniedError as exc:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN, detail=str(exc)
        ) from exc

    if record.transcode_status == TranscodeStatus.READY and stream_url:
        return StreamUrlResponse(
            file_id=record.id,
            stream_url=stream_url,
            expires_in=300,
            transcode_status=record.transcode_status,
            message="Video stream is ready",
        )
    if record.transcode_status in (
        TranscodeStatus.PENDING,
        TranscodeStatus.IN_PROGRESS,
    ):
        response.status_code = status.HTTP_202_ACCEPTED
        return StreamUrlResponse(
            file_id=record.id,
            stream_url=None,
            expires_in=300,
            transcode_status=record.transcode_status,
            message=f"Video transcoding is {record.transcode_status.value.lower()}",
        )
    if record.transcode_status == TranscodeStatus.FAILED:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail=f"Video transcoding failed: {record.transcode_error or 'Unknown error'}",
        )
    raise HTTPException(
        status_code=status.HTTP_400_BAD_REQUEST,
        detail="File is not a video or video transcoding was not required.",
    )


@router.get(
    "/{file_id}/download-url",
    response_model=FileDownloadUrlResponse,
    status_code=status.HTTP_200_OK,
)
@files_alias_router.get(
    "/{file_id}/download-url",
    response_model=FileDownloadUrlResponse,
    status_code=status.HTTP_200_OK,
)
async def get_download_url(
    file_id: str,
    request: Request,
    response: Response,
    original: bool = False,
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
            original=original,
        )
        if session.is_rendition:
            response.headers["X-Rendition"] = "pdf"
        else:
            response.headers["X-Rendition"] = "original"

        return FileDownloadUrlResponse(
            file_id=session.file_id,
            filename=session.filename,
            mime_type=session.mime_type,
            download_url=session.download_url,
            expires_in=session.expires_in,
            is_watermarked=session.is_watermarked,
            is_rendition=session.is_rendition,
            rendition_key=session.rendition_key,
        )
    except FileNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
    except FileQuarantinedError as exc:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail={"message": str(exc), "reason": exc.reason},
        ) from exc
    except FileSharePermissionDeniedError as exc:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN, detail=str(exc)
        ) from exc


@router.get(
    "/{file_id}/preview-url",
    response_model=FileDownloadUrlResponse,
    status_code=status.HTTP_200_OK,
)
@files_alias_router.get(
    "/{file_id}/preview-url",
    response_model=FileDownloadUrlResponse,
    status_code=status.HTTP_200_OK,
)
async def get_preview_url(
    file_id: str,
    request: Request,
    response: Response,
    original: bool = False,
    service: FileShareService = Depends(get_fileshare_service),
) -> FileDownloadUrlResponse:
    """Retrieve short-lived presigned preview URL (PDF rendition if available, otherwise original)."""
    return await get_download_url(
        file_id=file_id,
        request=request,
        response=response,
        original=original,
        service=service,
    )


@router.get(
    "/{file_id}",
    response_model=FileRecordResponse,
    status_code=status.HTTP_200_OK,
)
@files_alias_router.get(
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


@router.post(
    "/{file_id}/hold",
    response_model=FileRecordResponse,
    status_code=status.HTTP_200_OK,
)
@files_alias_router.post(
    "/{file_id}/hold",
    response_model=FileRecordResponse,
    status_code=status.HTTP_200_OK,
)
async def place_file_on_hold(
    file_id: str,
    payload: FileHoldRequest,
    request: Request,
    service: FileShareService = Depends(get_fileshare_service),
) -> FileRecordResponse:
    """Place a file under retention/legal hold preventing deletion or archival.

    Requires super_admin or data_manager role.
    Requirements: PRD-SYS-001, PRD-DOC-001, 21 CFR Part 11
    """
    user_id = current_user_id.get() or "system_user"
    roles = extract_caller_roles(request)

    try:
        record = await service.set_hold(
            file_id=file_id,
            caller_user_id=user_id,
            caller_roles=roles,
            reason_for_change=payload.reason_for_change,
            hold_reason=payload.hold_reason,
        )
        return FileRecordResponse.model_validate(record)
    except FileNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
    except FileSharePermissionDeniedError as exc:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN, detail=str(exc)
        ) from exc


@router.delete(
    "/{file_id}/hold",
    response_model=FileRecordResponse,
    status_code=status.HTTP_200_OK,
)
@files_alias_router.delete(
    "/{file_id}/hold",
    response_model=FileRecordResponse,
    status_code=status.HTTP_200_OK,
)
async def remove_file_from_hold(
    file_id: str,
    request: Request,
    payload: FileHoldClearRequest | None = None,
    service: FileShareService = Depends(get_fileshare_service),
) -> FileRecordResponse:
    """Remove retention/legal hold from a file record.

    Requires super_admin or data_manager role.
    Requirements: PRD-SYS-001, PRD-DOC-001, 21 CFR Part 11
    """
    user_id = current_user_id.get() or "system_user"
    roles = extract_caller_roles(request)
    justification = (
        (payload.reason_for_change if payload else None)
        or request.headers.get("X-Change-Reason")
        or request.query_params.get("reason_for_change")
        or "Removing legal/retention hold"
    )

    try:
        record = await service.clear_hold(
            file_id=file_id,
            caller_user_id=user_id,
            caller_roles=roles,
            reason_for_change=justification,
        )
        return FileRecordResponse.model_validate(record)
    except FileNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
    except FileSharePermissionDeniedError as exc:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN, detail=str(exc)
        ) from exc


@router.delete(
    "/{file_id}",
    response_model=FileRecordResponse,
    status_code=status.HTTP_200_OK,
)
@files_alias_router.delete(
    "/{file_id}",
    response_model=FileRecordResponse,
    status_code=status.HTTP_200_OK,
)
async def delete_file_record(
    file_id: str,
    request: Request,
    payload: FileDeleteRequest | None = None,
    service: FileShareService = Depends(get_fileshare_service),
) -> Response:
    """Soft delete a file record.

    Gated on FileRecord.is_on_hold being False; returns 409 if file is on hold.
    Requirements: PRD-SYS-001, PRD-DOC-001, 21 CFR Part 11
    """
    user_id = current_user_id.get() or "system_user"
    roles = extract_caller_roles(request)
    justification = (
        (payload.reason_for_change if payload else None)
        or request.headers.get("X-Change-Reason")
        or request.query_params.get("reason_for_change")
        or "Deleting file record"
    )

    try:
        record = await service.delete_file(
            file_id=file_id,
            caller_user_id=user_id,
            caller_roles=roles,
            reason_for_change=justification,
        )
        return FileRecordResponse.model_validate(record)
    except FileNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
    except FileSharePermissionDeniedError as exc:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN, detail=str(exc)
        ) from exc
    except FileOnHoldError as exc:
        return JSONResponse(
            status_code=status.HTTP_409_CONFLICT,
            content={"reason": "FILE_ON_HOLD", "detail": str(exc.message or exc)},
        )


@router.get(
    "/{file_id}/audit-logs",
    response_model=list[FileAuditLogResponse],
    status_code=status.HTTP_200_OK,
)
@files_alias_router.get(
    "/{file_id}/audit-logs",
    response_model=list[FileAuditLogResponse],
    status_code=status.HTTP_200_OK,
)
async def get_file_audit_logs(
    file_id: str,
    request: Request,
    service: FileShareService = Depends(get_fileshare_service),
) -> list[FileAuditLogResponse]:
    """Retrieve immutable Part 11 audit log entries for a file record.

    Requirements: PRD-SYS-001, PRD-DOC-001, 21 CFR Part 11
    """
    user_id = current_user_id.get() or "system_user"
    roles = extract_caller_roles(request)

    try:
        entries = await service.list_audit_logs(
            file_id=file_id,
            caller_user_id=user_id,
            caller_roles=roles,
        )
        return [FileAuditLogResponse.model_validate(e) for e in entries]
    except FileNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc


@router.post(
    "/{file_id}/approve",
    response_model=FileRecordResponse,
    status_code=status.HTTP_200_OK,
)
@files_alias_router.post(
    "/{file_id}/approve",
    response_model=FileRecordResponse,
    status_code=status.HTTP_200_OK,
)
async def approve_document_endpoint(
    file_id: str,
    payload: FileApproveRequest,
    request: Request,
    service: FileShareService = Depends(get_fileshare_service),
) -> Response:
    """Execute 21 CFR Part 11 electronic signature document approval with in-product re-authentication.

    Requirements: PRD-SYS-001, PRD-DOC-001, 21 CFR Part 11
    """
    user_id = current_user_id.get() or "system_user"
    roles = extract_caller_roles(request)

    client_ip = getattr(request.state, "ip_address", None)
    if not client_ip:
        client_ip = request.headers.get("x-forwarded-for") or (
            request.client.host if request.client else "127.0.0.1"
        )
        if "," in client_ip:
            client_ip = client_ip.split(",")[0].strip()

    user_agent = request.headers.get("user-agent") or "Fileshare Service"

    try:
        record = await service.approve_file(
            file_id=file_id,
            caller_user_id=user_id,
            caller_roles=roles,
            signing_reason=payload.signing_reason,
            credential=payload.credential,
            client_ip=client_ip,
            user_agent=user_agent,
        )
        return FileRecordResponse.model_validate(record)
    except FileNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
    except InvalidCredentialError as exc:
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail=str(exc) or "Invalid re-authentication credentials.",
        ) from exc
    except FileQuarantinedError as exc:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail={"message": str(exc), "reason": exc.reason},
        ) from exc
    except FileSharePermissionDeniedError as exc:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN, detail=str(exc)
        ) from exc
    except FileImmutableError as exc:
        return JSONResponse(
            status_code=status.HTTP_409_CONFLICT,
            content={"reason": exc.reason, "detail": str(exc)},
        )


@router.post(
    "/{file_id}/reject",
    response_model=FileRecordResponse,
    status_code=status.HTTP_200_OK,
)
@files_alias_router.post(
    "/{file_id}/reject",
    response_model=FileRecordResponse,
    status_code=status.HTTP_200_OK,
)
async def reject_document_endpoint(
    file_id: str,
    payload: FileRejectRequest,
    request: Request,
    service: FileShareService = Depends(get_fileshare_service),
) -> FileRecordResponse:
    """Reject a document for approval.

    Requirements: PRD-SYS-001, PRD-DOC-001, 21 CFR Part 11
    """
    user_id = current_user_id.get() or "system_user"
    roles = extract_caller_roles(request)

    try:
        record = await service.reject_file(
            file_id=file_id,
            caller_user_id=user_id,
            caller_roles=roles,
            reason_for_change=payload.reason_for_change,
        )
        return FileRecordResponse.model_validate(record)
    except FileNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
    except FileQuarantinedError as exc:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail={"message": str(exc), "reason": exc.reason},
        ) from exc
    except FileSharePermissionDeniedError as exc:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN, detail=str(exc)
        ) from exc


@router.post(
    "/{file_id}/rescind-approval",
    response_model=FileRecordResponse,
    status_code=status.HTTP_200_OK,
)
@files_alias_router.post(
    "/{file_id}/rescind-approval",
    response_model=FileRecordResponse,
    status_code=status.HTTP_200_OK,
)
async def rescind_approval_endpoint(
    file_id: str,
    payload: FileRescindApprovalRequest,
    request: Request,
    service: FileShareService = Depends(get_fileshare_service),
) -> FileRecordResponse:
    """Rescind a previous document approval. Requires super_admin.

    Requirements: PRD-SYS-001, PRD-DOC-001, 21 CFR Part 11
    """
    user_id = current_user_id.get() or "system_user"
    roles = extract_caller_roles(request)

    try:
        record = await service.rescind_approval(
            file_id=file_id,
            caller_user_id=user_id,
            caller_roles=roles,
            reason_for_change=payload.reason_for_change,
        )
        return FileRecordResponse.model_validate(record)
    except FileNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
    except FileSharePermissionDeniedError as exc:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN, detail=str(exc)
        ) from exc


@router.post(
    "/{file_id}/upload-revision",
    response_model=FileUploadUrlResponse,
    status_code=status.HTTP_201_CREATED,
)
@files_alias_router.post(
    "/{file_id}/upload-revision",
    response_model=FileUploadUrlResponse,
    status_code=status.HTTP_201_CREATED,
)
async def upload_revision_endpoint(
    file_id: str,
    payload: UploadRevisionRequest,
    request: Request,
    service: FileShareService = Depends(get_fileshare_service),
) -> Response:
    """Initiate upload of a new version/revision for a file record. Blocks if approved and immutable.

    Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002, 21 CFR Part 11
    """
    user_id = current_user_id.get() or "system_user"
    roles = extract_caller_roles(request)
    tenant_id = current_tenant_id.get() or "tenant_default"

    try:
        session = await service.upload_revision(
            file_id=file_id,
            caller_user_id=user_id,
            caller_roles=roles,
            filename=payload.filename,
            mime_type=payload.mime_type,
            size_bytes=payload.size_bytes,
            reason_for_change=payload.reason_for_change,
            tenant_id=tenant_id,
            is_multipart=payload.is_multipart,
            parts_count=payload.parts_count,
            checksum_sha256=payload.checksum_sha256,
        )
        return FileUploadUrlResponse(
            file_id=session.file_id,
            object_key=session.object_key,
            upload_id=session.upload_id,
            upload_url=session.upload_url,
            upload_urls=session.upload_urls,
            expires_in=session.expires_in,
            checksum_sha256=session.checksum_sha256,
            required_headers=session.required_headers or {},
        )
    except FileNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
    except FileSharePermissionDeniedError as exc:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN, detail=str(exc)
        ) from exc
    except FileOnHoldError as exc:
        return JSONResponse(
            status_code=status.HTTP_409_CONFLICT,
            content={"reason": "FILE_ON_HOLD", "detail": str(exc.message or exc)},
        )
    except FileImmutableError as exc:
        return JSONResponse(
            status_code=status.HTTP_409_CONFLICT,
            content={"reason": exc.reason, "detail": str(exc)},
        )


@router.get(
    "",
    response_model=list[FileRecordResponse],
    status_code=status.HTTP_200_OK,
)
@files_alias_router.get(
    "",
    response_model=list[FileRecordResponse],
    status_code=status.HTTP_200_OK,
)
async def list_files(
    study_id: str | None = None,
    site_id: str | None = None,
    q: str | None = None,
    page: int = 1,
    page_size: int = 50,
    service: FileShareService = Depends(get_fileshare_service),
) -> list[FileRecordResponse]:
    """List active file records for an optional study, site, and search query.

    Requirements: PRD-SYS-001, PRD-DOC-001
    """
    records = await service.file_repo.list_by_study(study_id=study_id, site_id=site_id)
    if q and q.strip():
        query_str = q.strip().lower()
        records = [
            r
            for r in records
            if query_str in r.filename.lower()
            or query_str in r.study_id.lower()
            or query_str in r.uploaded_by.lower()
            or (r.site_id and query_str in r.site_id.lower())
        ]
    start_idx = max(0, (page - 1) * page_size)
    paged = records[start_idx : start_idx + page_size]
    return [FileRecordResponse.model_validate(r) for r in paged]


@router.get(
    "/{file_id}/grants",
    response_model=list[ShareGrantResponse],
    status_code=status.HTTP_200_OK,
)
@files_alias_router.get(
    "/{file_id}/grants",
    response_model=list[ShareGrantResponse],
    status_code=status.HTTP_200_OK,
)
async def list_share_grants(
    file_id: str,
    service: FileShareService = Depends(get_fileshare_service),
) -> list[ShareGrantResponse]:
    """Retrieve active internal share grants for a file record.

    Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-003
    """
    grants = await service.grant_repo.list_by_file_id(file_id, active_only=True)
    return [ShareGrantResponse.model_validate(g) for g in grants]


@router.get(
    "/{file_id}/guest-links",
    response_model=list[GuestLinkResponse],
    status_code=status.HTTP_200_OK,
)
@files_alias_router.get(
    "/{file_id}/guest-links",
    response_model=list[GuestLinkResponse],
    status_code=status.HTTP_200_OK,
)
async def list_guest_links(
    file_id: str,
    service: FileShareService = Depends(get_fileshare_service),
) -> list[GuestLinkResponse]:
    """Retrieve active guest links for a file record.

    Requirements: PRD-SYS-001, PRD-DOC-001
    """
    links = await service.guest_repo.list_by_file_id(file_id, active_only=True)
    return [
        GuestLinkResponse(
            id=link.id,
            file_record_id=link.file_record_id,
            guest_url="",
            expires_at=link.expires_at,
            created_by=link.created_by,
            access_count=link.access_count,
            is_valid=link.is_valid,
        )
        for link in links
    ]


@router.post(
    "/{file_id}/grants",
    response_model=ShareGrantResponse,
    status_code=status.HTTP_201_CREATED,
)
@files_alias_router.post(
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
    except FileQuarantinedError as exc:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail={"message": str(exc), "reason": exc.reason},
        ) from exc
    except FileSharePermissionDeniedError as exc:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN, detail=str(exc)
        ) from exc


@router.delete(
    "/{file_id}/grants/{grant_id}",
    response_model=ShareGrantResponse,
    status_code=status.HTTP_200_OK,
)
@files_alias_router.delete(
    "/{file_id}/grants/{grant_id}",
    response_model=ShareGrantResponse,
    status_code=status.HTTP_200_OK,
)
async def delete_share_grant(
    file_id: str,
    grant_id: str,
    request: Request,
    reason_for_change: str = "Revoking share grant delegation",
    service: FileShareService = Depends(get_fileshare_service),
) -> ShareGrantResponse:
    """Revoke an internal share grant delegation.

    Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-003
    """
    user_id = current_user_id.get() or "system_user"
    roles = extract_caller_roles(request)
    justification = (
        request.headers.get("X-Change-Reason")
        or request.query_params.get("reason_for_change")
        or reason_for_change
    )

    try:
        grant = await service.revoke_share_grant(
            file_id=file_id,
            grant_id=grant_id,
            caller_user_id=user_id,
            caller_roles=roles,
            reason_for_change=justification,
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
@files_alias_router.post(
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
    except FileQuarantinedError as exc:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail={"message": str(exc), "reason": exc.reason},
        ) from exc


@router.delete(
    "/{file_id}/guest-links/{guest_link_id}",
    response_model=GuestLinkResponse,
    status_code=status.HTTP_200_OK,
)
@files_alias_router.delete(
    "/{file_id}/guest-links/{guest_link_id}",
    response_model=GuestLinkResponse,
    status_code=status.HTTP_200_OK,
)
async def revoke_guest_link(
    file_id: str,
    guest_link_id: str,
    request: Request,
    reason_for_change: str = "Revoking external guest link",
    service: FileShareService = Depends(get_fileshare_service),
) -> GuestLinkResponse:
    """Revoke an active external guest link.

    Requirements: PRD-SYS-001, PRD-DOC-001
    """
    user_id = current_user_id.get() or "system_user"
    roles = extract_caller_roles(request)
    justification = (
        request.headers.get("X-Change-Reason")
        or request.query_params.get("reason_for_change")
        or reason_for_change
    )

    try:
        revoked = await service.revoke_guest_link(
            file_id=file_id,
            guest_link_id=guest_link_id,
            caller_user_id=user_id,
            caller_roles=roles,
            reason_for_change=justification,
        )
        return GuestLinkResponse(
            id=revoked.id,
            file_record_id=revoked.file_record_id,
            guest_url="",
            expires_at=revoked.expires_at,
            created_by=revoked.created_by,
            access_count=revoked.access_count,
            is_valid=revoked.is_valid,
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
    "/guest/{token}",
    response_model=FileDownloadUrlResponse,
    status_code=status.HTTP_200_OK,
)
@files_alias_router.get(
    "/guest/{token}",
    response_model=FileDownloadUrlResponse,
    status_code=status.HTTP_200_OK,
)
async def get_guest_file_download(
    token: str,
    service: FileShareService = Depends(get_fileshare_service),
) -> FileDownloadUrlResponse:
    """Resolve an external guest link token and issue presigned GET URL.

    Requirements: PRD-SYS-001, PRD-DOC-001
    """
    try:
        session = await service.access_guest_link(raw_secret_token=token)
        return FileDownloadUrlResponse(
            file_id=session.file_id,
            filename=session.filename,
            mime_type=session.mime_type,
            download_url=session.download_url,
            expires_in=session.expires_in,
            is_watermarked=session.is_watermarked,
            access_count=session.access_count,
        )
    except FileNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
    except FileQuarantinedError as exc:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail={"message": str(exc), "reason": exc.reason},
        ) from exc
    except FileSharePermissionDeniedError as exc:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN, detail=str(exc)
        ) from exc


# Dedicated router for top-level guest links (/api/v1/fileshare/guest/{token} and /api/v1/files/guest/{token})
guest_router = APIRouter(tags=["Fileshare Guest Access"])


@guest_router.get(
    "/api/v1/fileshare/guest/{token}",
    response_model=FileDownloadUrlResponse,
    status_code=status.HTTP_200_OK,
)
@guest_router.get(
    "/api/v1/files/guest/{token}",
    response_model=FileDownloadUrlResponse,
    status_code=status.HTTP_200_OK,
)
async def get_external_guest_file_download(
    token: str,
    service: FileShareService = Depends(get_fileshare_service),
) -> FileDownloadUrlResponse:
    """Public resolution endpoint for external guest access tokens.

    Requirements: PRD-SYS-001, PRD-DOC-001
    """
    return await get_guest_file_download(token=token, service=service)


# Dedicated router for playback audit logging (/api/v1/fileshare/audit/playback and /api/v1/files/audit/playback)
audit_router = APIRouter(prefix="/api/v1/fileshare/audit", tags=["Fileshare Audit"])
audit_alias_router = APIRouter(prefix="/api/v1/files/audit", tags=["Fileshare Audit"])


@audit_router.post(
    "/playback",
    response_model=PlaybackAuditResponse,
    status_code=status.HTTP_201_CREATED,
)
@audit_alias_router.post(
    "/playback",
    response_model=PlaybackAuditResponse,
    status_code=status.HTTP_201_CREATED,
)
async def record_playback_checkpoint(
    payload: PlaybackAuditCreateRequest,
    request: Request,
    service: FileShareService = Depends(get_fileshare_service),
) -> PlaybackAuditResponse:
    """Record a viewer session video playback checkpoint audit event.

    Requirements: PRD-SYS-001, PRD-DOC-001, 21 CFR Part 11
    """
    user_id = (
        request.headers.get("X-User-Id")
        or getattr(request.state, "user_id", None)
        or (current_user_id.get() if current_user_id.get() != "system" else None)
        or "guest_viewer"
    )
    try:
        event = await service.record_playback_audit(
            file_id=payload.file_record_id,
            event=payload.event,
            source=payload.source,
            session_id=payload.session_id,
            user_id=user_id,
            guest_token=payload.guest_token,
            reason_for_change=payload.reason_for_change,
        )
        return PlaybackAuditResponse.model_validate(event)
    except FileNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
