"""FastAPI routers for Medical Imaging endpoints.

Owns: In-flight DICOM upload, PHI scrubbing, pixel masking, envelope queries,
file streaming, and cryptographic certificate verification.

Requirements: ADR-2199, PRD-IMG-001, PRD-IMG-002
"""

import json

from fastapi import (
    APIRouter,
    Depends,
    File,
    Form,
    HTTPException,
    Query,
    Request,
    UploadFile,
    status,
)
from fastapi.responses import Response
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy.orm import selectinload

from apps.imaging.adapters.database import get_db_session
from apps.imaging.adapters.storage import get_storage_adapter
from apps.imaging.infrastructure.models import (
    DeidentificationCertificateModel,
    ImagingInstanceEnvelopeModel,
    ImagingSeriesEnvelopeModel,
    ImagingStudyEnvelopeModel,
)
from apps.imaging.presentation.dtos import (
    CertificateVerificationResponse,
    DicomUploadResponse,
    InstanceEnvelopeSummaryDto,
    PresignedDownloadUrlResponse,
    SeriesEnvelopeSummaryDto,
    StudyEnvelopeDetailDto,
    StudyEnvelopeSummaryDto,
)
from apps.imaging.services.pipeline import ImagingIngestionPipeline
from packages.deid.dicom import (
    DicomDeidentifier,
    PixelMaskBoundingBox,
)
from packages.security.context import (
    current_site_id,
    current_tenant_id,
    current_user_id,
)

router = APIRouter(prefix="/api/v1/imaging", tags=["Medical Imaging"])


def _get_request_identity(request: Request) -> tuple[str, str, str, str]:
    """Resolves authenticated user_id, tenant_id, site_id, and change_reason from request."""
    user_id = (
        current_user_id.get()
        or request.headers.get("X-User-Id")
        or request.state._state.get("user_id", "system.imaging.user")
        if hasattr(request.state, "_state")
        else request.headers.get("X-User-Id", "system.imaging.user")
    )
    tenant_id = (
        current_tenant_id.get()
        or request.headers.get("X-Tenant-Id")
        or "tenant_default"
    )
    site_id = current_site_id.get() or request.headers.get("X-Site-Id") or ""
    change_reason = (
        request.headers.get("X-Change-Reason")
        or "In-Flight DICOM Ingestion and De-Identification"
    )
    return user_id, tenant_id, site_id, change_reason


@router.post(
    "/upload", response_model=DicomUploadResponse, status_code=status.HTTP_201_CREATED
)
async def upload_dicom(
    request: Request,
    file: UploadFile = File(...),
    study_id: str = Form(..., description="Clinical Trial Protocol ID"),
    site_id: str = Form(..., description="Clinical Site ID"),
    subject_id: str = Form(..., description="Trial Subject UUID"),
    date_shift_days: int = Form(default=0, description="Optional date shift in days"),
    apply_pixel_mask: bool = Form(
        default=True, description="Whether to mask burned-in pixel text"
    ),
    custom_pixel_masks_json: str | None = Form(
        default=None, description="Optional JSON array of custom pixel boxes"
    ),
    session: AsyncSession = Depends(get_db_session),
) -> DicomUploadResponse:
    """Uploads and de-identifies DICOM file(s) or ZIP archives in-flight before persisting to S3.

    @req:PRD-IMG-001
    @req:PRD-IMG-002
    """
    user_id, tenant_id, header_site_id, reason = _get_request_identity(request)
    effective_site_id = site_id or header_site_id or "SITE-DEFAULT"

    custom_masks: list[PixelMaskBoundingBox] | None = None
    if custom_pixel_masks_json:
        try:
            boxes_raw = json.loads(custom_pixel_masks_json)
            custom_masks = [PixelMaskBoundingBox(**b) for b in boxes_raw]
        except Exception as e:
            raise HTTPException(
                status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
                detail=f"Invalid custom_pixel_masks_json format: {e}",
            ) from e

    payload_bytes = await file.read()
    if not payload_bytes:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Uploaded file payload is empty.",
        )

    storage_port = get_storage_adapter()
    pipeline = ImagingIngestionPipeline(storage_port=storage_port)

    try:
        study_model, certs = await pipeline.ingest_archive_or_file(
            payload_bytes=payload_bytes,
            filename=file.filename or "upload.dcm",
            study_id=study_id,
            site_id=effective_site_id,
            subject_id=subject_id,
            user_id=user_id,
            reason_for_change=reason,
            session=session,
            tenant_id=tenant_id,
            date_shift_days=date_shift_days,
            apply_pixel_mask=apply_pixel_mask,
            custom_pixel_masks=custom_masks,
        )
        await session.commit()
    except ValueError as val_err:
        await session.rollback()
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail=str(val_err),
        ) from val_err
    except Exception as exc:
        await session.rollback()
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=f"De-identification pipeline error: {exc}",
        ) from exc

    return DicomUploadResponse(
        study_envelope_id=study_model.id,
        study_instance_uid=study_model.study_instance_uid,
        study_id=study_model.study_id,
        site_id=study_model.site_id,
        subject_id=study_model.subject_id,
        processed_instances_count=len(certs),
        processed_series_count=study_model.series_count,
        modalities=study_model.modalities,
        certificates=certs,
    )


@router.post("/deidentify")
async def deidentify_in_flight(
    request: Request,
    file: UploadFile = File(...),
    study_id: str = Form(...),
    site_id: str = Form(...),
    subject_id: str = Form(...),
    date_shift_days: int = Form(default=0),
    apply_pixel_mask: bool = Form(default=True),
) -> Response:
    """Performs in-memory de-identification on an uploaded DICOM file without persistent storage.

    Returns the clean, de-identified DICOM bytes with certificate metadata headers.

    @req:PRD-IMG-002
    """
    user_id, _, _, _ = _get_request_identity(request)
    payload_bytes = await file.read()
    if not payload_bytes:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="File content is empty.",
        )

    deidentifier = DicomDeidentifier()
    try:
        res = deidentifier.deidentify(
            dicom_bytes=payload_bytes,
            subject_id=subject_id,
            site_id=site_id,
            study_id=study_id,
            date_shift_days=date_shift_days,
            apply_pixel_mask=apply_pixel_mask,
            operator_identity=user_id,
        )
    except Exception as exc:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail=f"Unable to parse or de-identify DICOM payload: {exc}",
        ) from exc

    headers = {
        "Content-Disposition": f'attachment; filename="deidentified_{res.anonymized_sop_uid}.dcm"',
        "X-Deid-Certificate-Id": res.certificate.certificate_id,
        "X-Source-SHA256": res.certificate.source_sha256,
        "X-Target-SHA256": res.certificate.target_sha256,
        "X-Anonymized-Study-UID": res.anonymized_study_uid,
        "X-Anonymized-Series-UID": res.anonymized_series_uid,
        "X-Anonymized-SOP-UID": res.anonymized_sop_uid,
    }

    return Response(
        content=res.deidentified_bytes,
        media_type="application/dicom",
        headers=headers,
    )


@router.get("/studies", response_model=list[StudyEnvelopeSummaryDto])
async def list_studies(
    study_id: str | None = Query(default=None, description="Filter by Protocol ID"),
    site_id: str | None = Query(default=None, description="Filter by Site ID"),
    subject_id: str | None = Query(default=None, description="Filter by Subject UUID"),
    modality: str | None = Query(default=None, description="Filter by Modality"),
    status_filter: str | None = Query(default=None, alias="status"),
    skip: int = Query(default=0, ge=0),
    limit: int = Query(default=50, ge=1, le=200),
    session: AsyncSession = Depends(get_db_session),
) -> list[StudyEnvelopeSummaryDto]:
    """Lists de-identified study metadata envelopes matching query filters.

    @req:PRD-IMG-001
    """
    stmt = select(ImagingStudyEnvelopeModel)

    if study_id:
        stmt = stmt.where(ImagingStudyEnvelopeModel.study_id == study_id)
    if site_id:
        stmt = stmt.where(ImagingStudyEnvelopeModel.site_id == site_id)
    if subject_id:
        stmt = stmt.where(ImagingStudyEnvelopeModel.subject_id == subject_id)
    if status_filter:
        stmt = stmt.where(ImagingStudyEnvelopeModel.status == status_filter)

    stmt = (
        stmt.order_by(ImagingStudyEnvelopeModel.created_at.desc())
        .offset(skip)
        .limit(limit)
    )
    result = await session.execute(stmt)
    studies = result.scalars().all()

    # Filter in-memory for modality JSON array if requested
    if modality:
        studies = [
            s for s in studies if modality.upper() in [m.upper() for m in s.modalities]
        ]

    return [
        StudyEnvelopeSummaryDto(
            id=s.id,
            study_instance_uid=s.study_instance_uid,
            study_id=s.study_id,
            site_id=s.site_id,
            subject_id=s.subject_id,
            modalities=s.modalities,
            study_date=s.study_date,
            study_description=s.study_description,
            series_count=s.series_count,
            instance_count=s.instance_count,
            status=s.status,
            adjudication_status=s.adjudication_status,
            tenant_id=s.tenant_id,
            created_at=s.created_at,
            created_by=s.created_by,
            version_index=s.version_index,
        )
        for s in studies
    ]


@router.get("/studies/{study_uid}", response_model=StudyEnvelopeDetailDto)
async def get_study_detail(
    study_uid: str,
    session: AsyncSession = Depends(get_db_session),
) -> StudyEnvelopeDetailDto:
    """Retrieves full study envelope hierarchy including series and instance envelopes.

    @req:PRD-IMG-001
    """
    stmt = (
        select(ImagingStudyEnvelopeModel)
        .where(
            (ImagingStudyEnvelopeModel.study_instance_uid == study_uid)
            | (ImagingStudyEnvelopeModel.id == study_uid)
        )
        .options(
            selectinload(ImagingStudyEnvelopeModel.series).selectinload(
                ImagingSeriesEnvelopeModel.instances
            ),
            selectinload(ImagingStudyEnvelopeModel.certificates),
        )
    )
    result = await session.execute(stmt)
    study = result.scalar_one_or_none()

    if study is None:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Imaging study '{study_uid}' not found.",
        )

    series_dtos: list[SeriesEnvelopeSummaryDto] = []
    for ser in study.series:
        instance_dtos = [
            InstanceEnvelopeSummaryDto(
                id=inst.id,
                sop_instance_uid=inst.sop_instance_uid,
                sop_class_uid=inst.sop_class_uid,
                instance_number=inst.instance_number,
                rows=inst.rows,
                columns=inst.columns,
                bits_allocated=inst.bits_allocated,
                pixel_spacing=inst.pixel_spacing,
                slice_thickness=inst.slice_thickness,
                slice_location=inst.slice_location,
                object_key=inst.object_key,
                checksum_sha256=inst.checksum_sha256,
                size_bytes=inst.size_bytes,
                burned_in_phi_masked=inst.burned_in_phi_masked,
                certificate_id=inst.certificate_id,
            )
            for inst in ser.instances
        ]
        series_dtos.append(
            SeriesEnvelopeSummaryDto(
                id=ser.id,
                series_instance_uid=ser.series_instance_uid,
                series_number=ser.series_number,
                modality=ser.modality,
                body_part_examined=ser.body_part_examined,
                series_description=ser.series_description,
                instance_count=ser.instance_count,
                protocol_name=ser.protocol_name,
                instances=instance_dtos,
            )
        )

    cert_ids = [c.id for c in study.certificates]

    return StudyEnvelopeDetailDto(
        id=study.id,
        study_instance_uid=study.study_instance_uid,
        study_id=study.study_id,
        site_id=study.site_id,
        subject_id=study.subject_id,
        modalities=study.modalities,
        study_date=study.study_date,
        study_description=study.study_description,
        series_count=study.series_count,
        instance_count=study.instance_count,
        status=study.status,
        adjudication_status=study.adjudication_status,
        tenant_id=study.tenant_id,
        created_at=study.created_at,
        created_by=study.created_by,
        version_index=study.version_index,
        series=series_dtos,
        certificate_ids=cert_ids,
    )


@router.get("/instances/{sop_instance_uid}/file")
async def download_instance_file(
    sop_instance_uid: str,
    session: AsyncSession = Depends(get_db_session),
) -> Response:
    """Streams the stored de-identified DICOM binary file.

    @req:PRD-IMG-001
    """
    stmt = select(ImagingInstanceEnvelopeModel).where(
        ImagingInstanceEnvelopeModel.sop_instance_uid == sop_instance_uid
    )
    result = await session.execute(stmt)
    inst = result.scalar_one_or_none()

    if inst is None:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"DICOM Instance '{sop_instance_uid}' not found.",
        )

    storage_port = get_storage_adapter()
    try:
        content, calculated_sha = await storage_port.get_object(inst.object_key)
    except Exception as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Storage object for instance '{sop_instance_uid}' not found: {exc}",
        ) from exc

    return Response(
        content=content,
        media_type="application/dicom",
        headers={
            "Content-Disposition": f'attachment; filename="{sop_instance_uid}.dcm"',
            "X-Checksum-SHA256": calculated_sha,
        },
    )


@router.get(
    "/instances/{sop_instance_uid}/presigned-url",
    response_model=PresignedDownloadUrlResponse,
)
async def get_presigned_download_url(
    sop_instance_uid: str,
    session: AsyncSession = Depends(get_db_session),
) -> PresignedDownloadUrlResponse:
    """Generates a secure, short-lived presigned URL for direct object storage download.

    @req:PRD-IMG-001
    """
    stmt = select(ImagingInstanceEnvelopeModel).where(
        ImagingInstanceEnvelopeModel.sop_instance_uid == sop_instance_uid
    )
    result = await session.execute(stmt)
    inst = result.scalar_one_or_none()

    if inst is None:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"DICOM Instance '{sop_instance_uid}' not found.",
        )

    storage_port = get_storage_adapter()
    try:
        url = await storage_port.generate_presigned_get_url(
            key=inst.object_key,
            expires_in=3600,
            response_content_disposition=f'attachment; filename="{sop_instance_uid}.dcm"',
        )
    except Exception:
        # If storage backend is local filesystem, fallback to direct endpoint path
        url = f"/api/v1/imaging/instances/{sop_instance_uid}/file"

    return PresignedDownloadUrlResponse(
        sop_instance_uid=sop_instance_uid,
        download_url=url,
        expires_in_seconds=3600,
    )


@router.get(
    "/certificates/{certificate_id}/verify",
    response_model=CertificateVerificationResponse,
)
async def verify_certificate(
    certificate_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> CertificateVerificationResponse:
    """Verifies the cryptographic HMAC-SHA256 signature of a de-identification certificate.

    @req:PRD-IMG-002
    """
    stmt = select(DeidentificationCertificateModel).where(
        DeidentificationCertificateModel.id == certificate_id
    )
    result = await session.execute(stmt)
    cert_model = result.scalar_one_or_none()

    if cert_model is None:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Certificate '{certificate_id}' not found.",
        )

    deidentifier = DicomDeidentifier()
    from packages.deid.dicom import DicomDeidCertificate

    cert_obj = DicomDeidCertificate(
        certificate_id=cert_model.id,
        source_sha256=cert_model.source_sha256,
        target_sha256=cert_model.target_sha256,
        study_id=cert_model.study_id,
        site_id=cert_model.site_id,
        subject_id=cert_model.subject_id,
        original_study_instance_uid_hash=cert_model.original_study_instance_uid_hash,
        original_sop_instance_uid_hash=cert_model.original_sop_instance_uid_hash,
        anonymized_study_instance_uid=cert_model.anonymized_study_instance_uid,
        anonymized_sop_instance_uid=cert_model.anonymized_sop_instance_uid,
        scrubbed_tags_count=cert_model.scrubbed_tags_count,
        scrubbed_tags=cert_model.scrubbed_tags,
        pixel_mask_applied=cert_model.pixel_mask_applied,
        pixel_masks=[PixelMaskBoundingBox(**m) for m in cert_model.pixel_masks],
        operator_identity=cert_model.operator_identity,
        timestamp=cert_model.timestamp,
        signature=cert_model.signature,
    )

    is_valid = deidentifier.verify_certificate(cert_obj)

    return CertificateVerificationResponse(
        certificate_id=cert_model.id,
        is_valid=is_valid,
        source_sha256=cert_model.source_sha256,
        target_sha256=cert_model.target_sha256,
        subject_id=cert_model.subject_id,
        site_id=cert_model.site_id,
        study_id=cert_model.study_id,
        scrubbed_tags_count=cert_model.scrubbed_tags_count,
        pixel_mask_applied=cert_model.pixel_mask_applied,
        timestamp=cert_model.timestamp,
        operator_identity=cert_model.operator_identity,
        message=(
            "Cryptographic signature verified: manifest is authentic and untampered."
            if is_valid
            else "TAMPER WARNING: Cryptographic signature verification failed."
        ),
    )
