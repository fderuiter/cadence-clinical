"""Data Transfer Objects (DTOs) for Medical Imaging microservice.

Requirements: ADR-2199, PRD-IMG-001, PRD-IMG-002
"""

from datetime import datetime

from pydantic import BaseModel, Field

from packages.deid.dicom import DicomDeidCertificate


class PixelMaskInputDto(BaseModel):
    """Bounding box coordinates for manual pixel masking."""

    x_min: int
    y_min: int
    x_max: int
    y_max: int


class InFlightDeidentifyRequest(BaseModel):
    """Request payload for in-memory DICOM de-identification."""

    study_id: str = Field(..., description="Clinical Trial Protocol ID")
    site_id: str = Field(..., description="Clinical Site Code / ID")
    subject_id: str = Field(..., description="Subject UUID")
    date_shift_days: int = Field(default=0, description="Optional date shift in days")
    custom_pixel_masks: list[PixelMaskInputDto] | None = Field(
        default=None, description="Optional custom pixel mask bounding boxes"
    )


class InstanceEnvelopeSummaryDto(BaseModel):
    """Summary of a stored de-identified instance envelope."""

    id: str
    sop_instance_uid: str
    sop_class_uid: str
    instance_number: int | None = None
    rows: int
    columns: int
    bits_allocated: int
    pixel_spacing: list[float] | None = None
    slice_thickness: float | None = None
    slice_location: float | None = None
    object_key: str
    checksum_sha256: str
    size_bytes: int
    burned_in_phi_masked: bool
    certificate_id: str | None = None


class SeriesEnvelopeSummaryDto(BaseModel):
    """Summary of a stored de-identified series envelope."""

    id: str
    series_instance_uid: str
    series_number: int | None = None
    modality: str
    body_part_examined: str | None = None
    series_description: str | None = None
    instance_count: int
    protocol_name: str | None = None
    instances: list[InstanceEnvelopeSummaryDto] = Field(default_factory=list)


class StudyEnvelopeSummaryDto(BaseModel):
    """Summary of a stored de-identified study envelope."""

    id: str
    study_instance_uid: str
    study_id: str
    site_id: str
    subject_id: str
    modalities: list[str]
    study_date: str | None = None
    study_description: str | None = None
    series_count: int
    instance_count: int
    status: str
    adjudication_status: str
    tenant_id: str
    created_at: datetime | None = None
    created_by: str
    version_index: int


class StudyEnvelopeDetailDto(StudyEnvelopeSummaryDto):
    """Full detail of a stored de-identified study envelope including nested series."""

    series: list[SeriesEnvelopeSummaryDto] = Field(default_factory=list)
    certificate_ids: list[str] = Field(default_factory=list)


class DicomUploadResponse(BaseModel):
    """Response returned upon successful upload and de-identification of DICOM files."""

    study_envelope_id: str
    study_instance_uid: str
    study_id: str
    site_id: str
    subject_id: str
    processed_instances_count: int
    processed_series_count: int
    modalities: list[str]
    certificates: list[DicomDeidCertificate]


class CertificateVerificationResponse(BaseModel):
    """Response returned when validating a cryptographic de-identification certificate."""

    certificate_id: str
    is_valid: bool
    source_sha256: str
    target_sha256: str
    subject_id: str
    site_id: str
    study_id: str
    scrubbed_tags_count: int
    pixel_mask_applied: bool
    timestamp: str
    operator_identity: str
    message: str


class PresignedDownloadUrlResponse(BaseModel):
    """Response containing a short-lived presigned URL for secure instance download."""

    sop_instance_uid: str
    download_url: str
    expires_in_seconds: int = 3600
