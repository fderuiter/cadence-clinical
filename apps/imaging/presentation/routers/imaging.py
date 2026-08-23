"""FastAPI routers for Medical Imaging endpoints.

Owns: In-flight DICOM upload, PHI scrubbing, pixel masking, envelope queries,
file streaming, and cryptographic certificate verification.

Requirements: ADR-2199, PRD-IMG-001, PRD-IMG-002
"""

import hashlib
import json
import uuid
from typing import Any

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
from sqlalchemy import func, select
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy.orm import selectinload

from apps.imaging.adapters.database import get_db_session
from apps.imaging.adapters.storage import get_storage_adapter
from apps.imaging.infrastructure.models import (
    DeidentificationCertificateModel,
    ImagingAnnotationModel,
    ImagingAuditLogModel,
    ImagingInstanceEnvelopeModel,
    ImagingSeriesEnvelopeModel,
    ImagingStudyEnvelopeModel,
    TumorResponseAssessmentModel,
)
from apps.imaging.presentation.dtos import (
    CaliperAnnotationCreateDto,
    CaliperAnnotationResponseDto,
    CertificateVerificationResponse,
    DicomUploadResponse,
    InstanceEnvelopeSummaryDto,
    LuganoEvaluationRequestDto,
    LuganoEvaluationResponseDto,
    MprMetadataResponseDto,
    PointCoordinateDto,
    PresignedDownloadUrlResponse,
    RECISTEvaluationRequestDto,
    RECISTEvaluationResponseDto,
    SeriesEnvelopeSummaryDto,
    StudyEnvelopeDetailDto,
    StudyEnvelopeSummaryDto,
    SubjectTimepointEvaluateRequestDto,
    SubjectTimepointsTrajectoryResponseDto,
    TimepointTrajectorySummaryDto,
)
from apps.imaging.services.lugano_engine import LuganoEngine, LuganoLesionInput
from apps.imaging.services.mpr_engine import MprGeometryEngine
from apps.imaging.services.pipeline import ImagingIngestionPipeline
from apps.imaging.services.recist_engine import (
    LesionCategory,
    RECIST11Engine,
    RECISTLesionInput,
)
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


@router.get(
    "/series/{series_instance_uid}/mpr-metadata",
    response_model=MprMetadataResponseDto,
)
async def get_series_mpr_metadata(
    series_instance_uid: str,
    session: AsyncSession = Depends(get_db_session),
) -> MprMetadataResponseDto:
    """Retrieves 3D volume geometry, voxel spacing, and MPR orientation metadata.

    @req:PRD-IMG-001
    @req:PRD-IMG-004
    """
    stmt = (
        select(ImagingSeriesEnvelopeModel)
        .where(ImagingSeriesEnvelopeModel.series_instance_uid == series_instance_uid)
        .options(selectinload(ImagingSeriesEnvelopeModel.instances))
    )
    result = await session.execute(stmt)
    series = result.scalar_one_or_none()

    if series and series.instances:
        first_inst = series.instances[0]
        meta = MprGeometryEngine.generate_series_mpr_metadata(
            series_instance_uid=series.series_instance_uid,
            modality=series.modality,
            rows=first_inst.rows or 512,
            columns=first_inst.columns or 512,
            slice_count=len(series.instances),
            pixel_spacing=first_inst.pixel_spacing or [1.0, 1.0],
            slice_thickness=first_inst.slice_thickness or 2.5,
            window_center=first_inst.window_center,
            window_width=first_inst.window_width,
        )
    else:
        # Fallback default geometry for unpersisted or mock series
        meta = MprGeometryEngine.generate_series_mpr_metadata(
            series_instance_uid=series_instance_uid,
            modality="CT",
            rows=512,
            columns=512,
            slice_count=1,
            pixel_spacing=[0.75, 0.75],
            slice_thickness=2.5,
        )

    return MprMetadataResponseDto(
        series_instance_uid=meta.series_instance_uid,
        modality=meta.modality,
        rows=meta.rows,
        columns=meta.columns,
        slice_count=meta.slice_count,
        pixel_spacing=meta.pixel_spacing,
        slice_thickness=meta.slice_thickness,
        slice_spacing=meta.slice_spacing,
        volume_dimensions_mm=meta.volume_dimensions_mm,
        window_center=meta.window_center,
        window_width=meta.window_width,
        available_planes=[p.value for p in meta.available_planes],
        window_presets=meta.window_presets,
    )


@router.post(
    "/annotations",
    response_model=CaliperAnnotationResponseDto,
    status_code=status.HTTP_201_CREATED,
)
async def create_caliper_annotation(
    request: Request,
    payload: CaliperAnnotationCreateDto,
    session: AsyncSession = Depends(get_db_session),
) -> CaliperAnnotationResponseDto:
    """Creates a new oncologic caliper annotation with 21 CFR Part 11 audit logging.

    @req:PRD-IMG-004
    @req:PRD-SYS-001
    """
    user_id, _, _, reason = _get_request_identity(request)

    # Compute PPD if perpendicular diameter provided
    ppd = None
    if payload.perpendicular_diameter_mm is not None:
        ppd = round(payload.longest_diameter_mm * payload.perpendicular_diameter_mm, 2)

    # Calculate ellipsoid volume if not provided but perpendicular is available
    volume = payload.volume_mm3
    if volume is None and payload.perpendicular_diameter_mm is not None:
        volume = MprGeometryEngine.calculate_ellipsoid_volume(
            payload.longest_diameter_mm,
            payload.perpendicular_diameter_mm,
            payload.perpendicular_diameter_mm,
        )

    annotation_id = str(uuid.uuid4())
    model = ImagingAnnotationModel(
        id=annotation_id,
        study_envelope_id=payload.study_envelope_id,
        series_envelope_id=payload.series_envelope_id,
        sop_instance_uid=payload.sop_instance_uid,
        study_id=payload.study_id,
        site_id=payload.site_id,
        subject_id=payload.subject_id,
        timepoint_name=payload.timepoint_name,
        lesion_id=payload.lesion_id,
        lesion_type=payload.lesion_type,
        organ_site=payload.organ_site,
        measurement_criteria=payload.measurement_criteria,
        measurement_type=payload.measurement_type,
        longest_diameter_mm=payload.longest_diameter_mm,
        perpendicular_diameter_mm=payload.perpendicular_diameter_mm,
        product_perpendicular_mm2=ppd,
        volume_mm3=volume,
        slice_index=payload.slice_index,
        slice_location=payload.slice_location,
        deauville_score=payload.deauville_score,
        points=[p.model_dump() for p in payload.points],
        hounsfield_stats=payload.hounsfield_stats,
        reader_id=user_id,
        notes=payload.notes,
        created_by=user_id,
        reason_for_change=reason,
    )
    session.add(model)

    # 21 CFR Part 11 Audit Trail Record
    audit_entry = ImagingAuditLogModel(
        study_envelope_id=payload.study_envelope_id,
        action="CALIPER_ANNOTATION_CREATED",
        actor_id=user_id,
        reason_for_change=reason,
        details={
            "annotation_id": annotation_id,
            "lesion_id": payload.lesion_id,
            "lesion_type": payload.lesion_type,
            "organ_site": payload.organ_site,
            "longest_diameter_mm": payload.longest_diameter_mm,
            "perpendicular_diameter_mm": payload.perpendicular_diameter_mm,
            "timepoint_name": payload.timepoint_name,
        },
    )
    session.add(audit_entry)
    await session.commit()
    await session.refresh(model)

    return CaliperAnnotationResponseDto(
        id=model.id,
        study_envelope_id=model.study_envelope_id,
        series_envelope_id=model.series_envelope_id,
        sop_instance_uid=model.sop_instance_uid,
        study_id=model.study_id,
        site_id=model.site_id,
        subject_id=model.subject_id,
        timepoint_name=model.timepoint_name,
        lesion_id=model.lesion_id,
        lesion_type=model.lesion_type,
        organ_site=model.organ_site,
        measurement_criteria=model.measurement_criteria,
        measurement_type=model.measurement_type,
        longest_diameter_mm=model.longest_diameter_mm,
        perpendicular_diameter_mm=model.perpendicular_diameter_mm,
        product_perpendicular_mm2=model.product_perpendicular_mm2,
        volume_mm3=model.volume_mm3,
        slice_index=model.slice_index,
        slice_location=model.slice_location,
        deauville_score=model.deauville_score,
        points=[PointCoordinateDto(**p) for p in model.points],
        hounsfield_stats=model.hounsfield_stats,
        reader_id=model.reader_id,
        notes=model.notes,
        created_at=model.created_at,
        created_by=model.created_by,
        version_index=model.version_index,
    )


@router.get("/annotations", response_model=list[CaliperAnnotationResponseDto])
async def list_caliper_annotations(
    study_envelope_id: str | None = Query(default=None),
    series_envelope_id: str | None = Query(default=None),
    sop_instance_uid: str | None = Query(default=None),
    subject_id: str | None = Query(default=None),
    timepoint_name: str | None = Query(default=None),
    reader_id: str | None = Query(default=None),
    skip: int = Query(default=0, ge=0),
    limit: int = Query(default=100, ge=1, le=500),
    session: AsyncSession = Depends(get_db_session),
) -> list[CaliperAnnotationResponseDto]:
    """Lists caliper annotations matching criteria.

    @req:PRD-IMG-004
    """
    stmt = select(ImagingAnnotationModel)

    if study_envelope_id:
        stmt = stmt.where(ImagingAnnotationModel.study_envelope_id == study_envelope_id)
    if series_envelope_id:
        stmt = stmt.where(
            ImagingAnnotationModel.series_envelope_id == series_envelope_id
        )
    if sop_instance_uid:
        stmt = stmt.where(ImagingAnnotationModel.sop_instance_uid == sop_instance_uid)
    if subject_id:
        stmt = stmt.where(ImagingAnnotationModel.subject_id == subject_id)
    if timepoint_name:
        stmt = stmt.where(ImagingAnnotationModel.timepoint_name == timepoint_name)
    if reader_id:
        stmt = stmt.where(ImagingAnnotationModel.reader_id == reader_id)

    stmt = (
        stmt.order_by(ImagingAnnotationModel.created_at.asc()).offset(skip).limit(limit)
    )
    result = await session.execute(stmt)
    records = result.scalars().all()

    return [
        CaliperAnnotationResponseDto(
            id=r.id,
            study_envelope_id=r.study_envelope_id,
            series_envelope_id=r.series_envelope_id,
            sop_instance_uid=r.sop_instance_uid,
            study_id=r.study_id,
            site_id=r.site_id,
            subject_id=r.subject_id,
            timepoint_name=r.timepoint_name,
            lesion_id=r.lesion_id,
            lesion_type=r.lesion_type,
            organ_site=r.organ_site,
            measurement_criteria=r.measurement_criteria,
            measurement_type=r.measurement_type,
            longest_diameter_mm=r.longest_diameter_mm,
            perpendicular_diameter_mm=r.perpendicular_diameter_mm,
            product_perpendicular_mm2=r.product_perpendicular_mm2,
            volume_mm3=r.volume_mm3,
            slice_index=r.slice_index,
            slice_location=r.slice_location,
            deauville_score=r.deauville_score,
            points=[PointCoordinateDto(**p) for p in r.points],
            hounsfield_stats=r.hounsfield_stats,
            reader_id=r.reader_id,
            notes=r.notes,
            created_at=r.created_at,
            created_by=r.created_by,
            version_index=r.version_index,
        )
        for r in records
    ]


@router.delete("/annotations/{annotation_id}")
async def delete_caliper_annotation(
    request: Request,
    annotation_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> dict[str, Any]:
    """Deletes a caliper annotation with 21 CFR Part 11 reason for change.

    @req:PRD-IMG-004
    @req:PRD-SYS-001
    """
    user_id, _, _, reason = _get_request_identity(request)

    stmt = select(ImagingAnnotationModel).where(
        ImagingAnnotationModel.id == annotation_id
    )
    result = await session.execute(stmt)
    model = result.scalar_one_or_none()

    if model is None:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Caliper annotation '{annotation_id}' not found.",
        )

    # 21 CFR Part 11 Audit Trail
    audit_entry = ImagingAuditLogModel(
        study_envelope_id=model.study_envelope_id,
        action="CALIPER_ANNOTATION_DELETED",
        actor_id=user_id,
        reason_for_change=reason,
        details={
            "deleted_annotation_id": annotation_id,
            "lesion_id": model.lesion_id,
            "timepoint_name": model.timepoint_name,
        },
    )
    session.add(audit_entry)
    await session.delete(model)
    await session.commit()

    return {"deleted": True, "id": annotation_id}


@router.post(
    "/response-evaluations/recist",
    response_model=RECISTEvaluationResponseDto,
)
async def evaluate_recist_response(
    payload: RECISTEvaluationRequestDto,
) -> RECISTEvaluationResponseDto:
    """Computes automated RECIST 1.1 derivation for target and non-target lesions.

    @req:PRD-IMG-004
    """
    lesion_inputs = [
        RECISTLesionInput(
            lesion_id=lesion.lesion_id,
            organ_site=lesion.organ_site,
            lesion_type=LesionCategory(lesion.lesion_type),
            longest_diameter_mm=lesion.longest_diameter_mm,
            is_lymph_node=lesion.is_lymph_node,
            short_axis_mm=lesion.short_axis_mm,
            sop_instance_uid=lesion.sop_instance_uid,
        )
        for lesion in payload.current_lesions
    ]

    res = RECIST11Engine.evaluate_timepoint(
        current_lesions=lesion_inputs,
        baseline_sld=payload.baseline_sld_mm,
        prior_timepoints_sld=payload.prior_timepoints_sld,
        non_target_status=payload.non_target_status,
        new_lesions_present=payload.new_lesions_present,
        new_lesions_count=payload.new_lesions_count,
        is_baseline=payload.is_baseline,
    )

    return RECISTEvaluationResponseDto(
        criteria=res.criteria.value,
        baseline_sld_mm=res.baseline_sld_mm,
        nadir_sld_mm=res.nadir_sld_mm,
        current_sld_mm=res.current_sld_mm,
        pct_change_baseline=res.pct_change_baseline,
        pct_change_nadir=res.pct_change_nadir,
        absolute_change_nadir_mm=res.absolute_change_nadir_mm,
        target_response=res.target_response.value,
        non_target_status=res.non_target_status,
        new_lesions_present=res.new_lesions_present,
        new_lesions_count=res.new_lesions_count,
        overall_response=res.overall_response.value,
        is_nadir=res.is_nadir,
    )


@router.post(
    "/response-evaluations/lugano",
    response_model=LuganoEvaluationResponseDto,
)
async def evaluate_lugano_response(
    payload: LuganoEvaluationRequestDto,
) -> LuganoEvaluationResponseDto:
    """Computes automated Lugano 2014 & Cheson derivation for lymphoma.

    @req:PRD-IMG-004
    """
    lesion_inputs = [
        LuganoLesionInput(
            lesion_id=lesion.lesion_id,
            organ_site=lesion.organ_site,
            longest_diameter_mm=lesion.longest_diameter_mm,
            perpendicular_diameter_mm=lesion.perpendicular_diameter_mm,
            is_extranodal=lesion.is_extranodal,
            deauville_score=lesion.deauville_score,
        )
        for lesion in payload.current_lesions
    ]

    res = LuganoEngine.evaluate_timepoint(
        current_lesions=lesion_inputs,
        baseline_spd=payload.baseline_spd_mm2,
        prior_timepoints_spd=payload.prior_timepoints_spd,
        new_lesions_present=payload.new_lesions_present,
        single_lesion_progression=payload.single_lesion_progression,
        non_measured_progression=payload.non_measured_progression,
        is_baseline=payload.is_baseline,
    )

    return LuganoEvaluationResponseDto(
        criteria=res.criteria.value,
        baseline_spd_mm2=res.baseline_spd_mm2,
        nadir_spd_mm2=res.nadir_spd_mm2,
        current_spd_mm2=res.current_spd_mm2,
        pct_change_spd_baseline=res.pct_change_spd_baseline,
        pct_change_spd_nadir=res.pct_change_spd_nadir,
        ct_response=res.ct_response.value,
        pet_metabolic_response=(
            res.pet_metabolic_response.value if res.pet_metabolic_response else None
        ),
        max_deauville_score=res.max_deauville_score,
        new_lesions_present=res.new_lesions_present,
        overall_response=res.overall_response.value,
    )


@router.get(
    "/subjects/{subject_id}/timepoints",
    response_model=SubjectTimepointsTrajectoryResponseDto,
)
async def get_subject_timepoints_trajectory(
    subject_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> SubjectTimepointsTrajectoryResponseDto:
    """Retrieves full chronological trajectory of locked oncologic evaluations for a subject.

    @req:PRD-IMG-004
    """
    stmt = (
        select(TumorResponseAssessmentModel)
        .where(TumorResponseAssessmentModel.subject_id == subject_id)
        .order_by(TumorResponseAssessmentModel.created_at.asc())
    )
    result = await session.execute(stmt)
    assessments = result.scalars().all()

    # Query active annotations count for subject
    annot_count_stmt = select(func.count()).select_from(
        select(ImagingAnnotationModel)
        .where(ImagingAnnotationModel.subject_id == subject_id)
        .subquery()
    )
    annot_count_res = await session.execute(annot_count_stmt)
    annot_count = annot_count_res.scalar() or 0

    first_study_id = assessments[0].study_id if assessments else "STUDY-ONCOLOGY"
    first_site_id = assessments[0].site_id if assessments else "SITE-01"

    trajectory_dtos = [
        TimepointTrajectorySummaryDto(
            id=a.id,
            timepoint_name=a.timepoint_name,
            assessment_criteria=a.assessment_criteria,
            baseline_sld_mm=a.baseline_sld_mm,
            nadir_sld_mm=a.nadir_sld_mm,
            current_sld_mm=a.current_sld_mm,
            pct_change_baseline=a.pct_change_baseline,
            pct_change_nadir=a.pct_change_nadir,
            baseline_spd_mm2=a.baseline_spd_mm2,
            nadir_spd_mm2=a.nadir_spd_mm2,
            current_spd_mm2=a.current_spd_mm2,
            pct_change_spd_baseline=a.pct_change_spd_baseline,
            pct_change_spd_nadir=a.pct_change_spd_nadir,
            target_response=a.target_response,
            non_target_status=a.non_target_status,
            new_lesions_present=a.new_lesions_present,
            new_lesions_count=a.new_lesions_count,
            max_deauville_score=a.max_deauville_score,
            overall_response=a.overall_response,
            is_locked=a.is_locked,
            locked_at=a.locked_at,
            reader_id=a.reader_id,
            created_at=a.created_at,
        )
        for a in assessments
    ]

    return SubjectTimepointsTrajectoryResponseDto(
        study_id=first_study_id,
        site_id=first_site_id,
        subject_id=subject_id,
        trajectory=trajectory_dtos,
        active_annotations_count=annot_count,
    )


@router.post(
    "/subjects/{subject_id}/timepoints/evaluate",
    response_model=TimepointTrajectorySummaryDto,
    status_code=status.HTTP_201_CREATED,
)
async def evaluate_and_lock_timepoint(
    request: Request,
    subject_id: str,
    payload: SubjectTimepointEvaluateRequestDto,
    session: AsyncSession = Depends(get_db_session),
) -> TimepointTrajectorySummaryDto:
    """Evaluates, cryptographically signs, and locks an oncologic timepoint assessment.

    @req:PRD-IMG-004
    @req:PRD-SYS-001
    """
    user_id, _, _, reason = _get_request_identity(request)

    # 1. Fetch all annotations for this subject and timepoint
    annot_stmt = (
        select(ImagingAnnotationModel)
        .where(
            (ImagingAnnotationModel.subject_id == subject_id)
            & (ImagingAnnotationModel.timepoint_name == payload.timepoint_name)
        )
        .order_by(ImagingAnnotationModel.created_at.asc())
    )
    annot_res = await session.execute(annot_stmt)
    annotations = annot_res.scalars().all()

    # 2. Fetch prior assessments for this subject
    prior_stmt = (
        select(TumorResponseAssessmentModel)
        .where(
            (TumorResponseAssessmentModel.subject_id == subject_id)
            & (TumorResponseAssessmentModel.timepoint_name != payload.timepoint_name)
        )
        .order_by(TumorResponseAssessmentModel.created_at.asc())
    )
    prior_res = await session.execute(prior_stmt)
    prior_assessments = prior_res.scalars().all()

    assessment_id = str(uuid.uuid4())

    if payload.assessment_criteria.upper() == "LUGANO_2014":
        lugano_lesions = [
            LuganoLesionInput(
                lesion_id=a.lesion_id,
                organ_site=a.organ_site,
                longest_diameter_mm=a.longest_diameter_mm,
                perpendicular_diameter_mm=(
                    a.perpendicular_diameter_mm or a.longest_diameter_mm
                ),
                is_extranodal="EXTRANODAL" in a.organ_site.upper(),
                deauville_score=a.deauville_score,
            )
            for a in annotations
            if a.lesion_type == "TARGET"
        ]

        # Determine baseline SPD
        baseline_spd = 0.0
        prior_spds = []
        for p in prior_assessments:
            if p.current_spd_mm2 is not None:
                prior_spds.append(p.current_spd_mm2)
            if p.baseline_spd_mm2 is not None and baseline_spd == 0.0:
                baseline_spd = p.baseline_spd_mm2

        lugano_res = LuganoEngine.evaluate_timepoint(
            current_lesions=lugano_lesions,
            baseline_spd=baseline_spd,
            prior_timepoints_spd=prior_spds,
            new_lesions_present=payload.new_lesions_present,
            is_baseline=payload.is_baseline,
        )

        manifest = {
            "assessment_id": assessment_id,
            "subject_id": subject_id,
            "timepoint_name": payload.timepoint_name,
            "criteria": "LUGANO_2014",
            "current_spd": lugano_res.current_spd_mm2,
            "overall_response": lugano_res.overall_response.value,
        }
        manifest_hash = hashlib.sha256(
            json.dumps(manifest, sort_keys=True).encode()
        ).hexdigest()

        assessment_model = TumorResponseAssessmentModel(
            id=assessment_id,
            study_id=payload.study_id,
            site_id=payload.site_id,
            subject_id=subject_id,
            timepoint_name=payload.timepoint_name,
            assessment_criteria="LUGANO_2014",
            baseline_spd_mm2=lugano_res.baseline_spd_mm2,
            nadir_spd_mm2=lugano_res.nadir_spd_mm2,
            current_spd_mm2=lugano_res.current_spd_mm2,
            pct_change_spd_baseline=lugano_res.pct_change_spd_baseline,
            pct_change_spd_nadir=lugano_res.pct_change_spd_nadir,
            target_response=lugano_res.ct_response.value,
            non_target_status=payload.non_target_status,
            new_lesions_present=payload.new_lesions_present,
            new_lesions_count=payload.new_lesions_count,
            max_deauville_score=lugano_res.max_deauville_score,
            overall_response=lugano_res.overall_response.value,
            is_locked=True,
            locked_by=user_id,
            reader_id=user_id,
            manifest_sha256=manifest_hash,
            signature=payload.digital_signature or f"SIG-{manifest_hash[:16]}",
            created_by=user_id,
            reason_for_change=reason,
        )
    else:
        # Default RECIST 1.1
        recist_lesions = [
            RECISTLesionInput(
                lesion_id=a.lesion_id,
                organ_site=a.organ_site,
                lesion_type=LesionCategory(a.lesion_type),
                longest_diameter_mm=a.longest_diameter_mm,
                is_lymph_node="NODE" in a.organ_site.upper(),
                short_axis_mm=a.perpendicular_diameter_mm,
                sop_instance_uid=a.sop_instance_uid,
            )
            for a in annotations
        ]

        baseline_sld = 0.0
        prior_slds = []
        for p in prior_assessments:
            if p.current_sld_mm is not None:
                prior_slds.append(p.current_sld_mm)
            if p.baseline_sld_mm is not None and baseline_sld == 0.0:
                baseline_sld = p.baseline_sld_mm

        recist_res = RECIST11Engine.evaluate_timepoint(
            current_lesions=recist_lesions,
            baseline_sld=baseline_sld,
            prior_timepoints_sld=prior_slds,
            non_target_status=payload.non_target_status,
            new_lesions_present=payload.new_lesions_present,
            new_lesions_count=payload.new_lesions_count,
            is_baseline=payload.is_baseline,
        )

        manifest = {
            "assessment_id": assessment_id,
            "subject_id": subject_id,
            "timepoint_name": payload.timepoint_name,
            "criteria": "RECIST_1_1",
            "current_sld": recist_res.current_sld_mm,
            "overall_response": recist_res.overall_response.value,
        }
        manifest_hash = hashlib.sha256(
            json.dumps(manifest, sort_keys=True).encode()
        ).hexdigest()

        assessment_model = TumorResponseAssessmentModel(
            id=assessment_id,
            study_id=payload.study_id,
            site_id=payload.site_id,
            subject_id=subject_id,
            timepoint_name=payload.timepoint_name,
            assessment_criteria="RECIST_1_1",
            baseline_sld_mm=recist_res.baseline_sld_mm,
            nadir_sld_mm=recist_res.nadir_sld_mm,
            current_sld_mm=recist_res.current_sld_mm,
            pct_change_baseline=recist_res.pct_change_baseline,
            pct_change_nadir=recist_res.pct_change_nadir,
            absolute_change_nadir_mm=recist_res.absolute_change_nadir_mm,
            target_response=recist_res.target_response.value,
            non_target_status=payload.non_target_status,
            new_lesions_present=payload.new_lesions_present,
            new_lesions_count=payload.new_lesions_count,
            overall_response=recist_res.overall_response.value,
            is_locked=True,
            locked_by=user_id,
            reader_id=user_id,
            manifest_sha256=manifest_hash,
            signature=payload.digital_signature or f"SIG-{manifest_hash[:16]}",
            created_by=user_id,
            reason_for_change=reason,
        )

    session.add(assessment_model)

    audit_entry = ImagingAuditLogModel(
        action="TUMOR_RESPONSE_EVALUATED",
        actor_id=user_id,
        reason_for_change=reason,
        details={
            "assessment_id": assessment_id,
            "subject_id": subject_id,
            "timepoint_name": payload.timepoint_name,
            "overall_response": assessment_model.overall_response,
            "manifest_sha256": assessment_model.manifest_sha256,
        },
    )
    session.add(audit_entry)
    await session.commit()
    await session.refresh(assessment_model)

    return TimepointTrajectorySummaryDto(
        id=assessment_model.id,
        timepoint_name=assessment_model.timepoint_name,
        assessment_criteria=assessment_model.assessment_criteria,
        baseline_sld_mm=assessment_model.baseline_sld_mm,
        nadir_sld_mm=assessment_model.nadir_sld_mm,
        current_sld_mm=assessment_model.current_sld_mm,
        pct_change_baseline=assessment_model.pct_change_baseline,
        pct_change_nadir=assessment_model.pct_change_nadir,
        baseline_spd_mm2=assessment_model.baseline_spd_mm2,
        nadir_spd_mm2=assessment_model.nadir_spd_mm2,
        current_spd_mm2=assessment_model.current_spd_mm2,
        pct_change_spd_baseline=assessment_model.pct_change_spd_baseline,
        pct_change_spd_nadir=assessment_model.pct_change_spd_nadir,
        target_response=assessment_model.target_response,
        non_target_status=assessment_model.non_target_status,
        new_lesions_present=assessment_model.new_lesions_present,
        new_lesions_count=assessment_model.new_lesions_count,
        max_deauville_score=assessment_model.max_deauville_score,
        overall_response=assessment_model.overall_response,
        is_locked=assessment_model.is_locked,
        locked_at=assessment_model.locked_at,
        reader_id=assessment_model.reader_id,
        created_at=assessment_model.created_at,
    )
