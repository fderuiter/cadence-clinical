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


class PointCoordinateDto(BaseModel):
    """2D or 3D point in pixel space or patient physical space."""

    x: float
    y: float
    z: float | None = None


class CaliperAnnotationCreateDto(BaseModel):
    """Request payload to create a new oncologic caliper annotation."""

    study_envelope_id: str | None = None
    series_envelope_id: str | None = None
    sop_instance_uid: str | None = None
    study_id: str
    site_id: str
    subject_id: str
    timepoint_name: str
    lesion_id: str
    lesion_type: str = "TARGET"  # TARGET, NON_TARGET, NEW_LESION
    organ_site: str
    measurement_criteria: str = "RECIST_1_1"  # RECIST_1_1, LUGANO_2014, RANO
    measurement_type: str = (
        "1D_RECIST"  # 1D_RECIST, 2D_PERPENDICULAR, 3D_ELLIPSOID, ROI_STATISTICS
    )
    longest_diameter_mm: float
    perpendicular_diameter_mm: float | None = None
    volume_mm3: float | None = None
    slice_index: int | None = None
    slice_location: float | None = None
    deauville_score: int | None = None
    points: list[PointCoordinateDto] = Field(default_factory=list)
    hounsfield_stats: dict[str, float] | None = None
    notes: str | None = None


class CaliperAnnotationResponseDto(BaseModel):
    """Response representing a stored caliper annotation."""

    id: str
    study_envelope_id: str | None = None
    series_envelope_id: str | None = None
    sop_instance_uid: str | None = None
    study_id: str
    site_id: str
    subject_id: str
    timepoint_name: str
    lesion_id: str
    lesion_type: str
    organ_site: str
    measurement_criteria: str
    measurement_type: str
    longest_diameter_mm: float
    perpendicular_diameter_mm: float | None = None
    product_perpendicular_mm2: float | None = None
    volume_mm3: float | None = None
    slice_index: int | None = None
    slice_location: float | None = None
    deauville_score: int | None = None
    points: list[PointCoordinateDto] = Field(default_factory=list)
    hounsfield_stats: dict[str, float] | None = None
    reader_id: str
    notes: str | None = None
    created_at: datetime | None = None
    created_by: str
    version_index: int


class RECISTLesionInputDto(BaseModel):
    """Input representation of a target lesion for RECIST derivation."""

    lesion_id: str
    organ_site: str
    lesion_type: str = "TARGET"
    longest_diameter_mm: float
    is_lymph_node: bool = False
    short_axis_mm: float | None = None
    sop_instance_uid: str | None = None


class RECISTEvaluationRequestDto(BaseModel):
    """Request payload to derive RECIST 1.1 tumor response."""

    current_lesions: list[RECISTLesionInputDto]
    baseline_sld_mm: float = 0.0
    prior_timepoints_sld: list[float] = Field(default_factory=list)
    non_target_status: str = "NON_CR_NON_PD"
    new_lesions_present: bool = False
    new_lesions_count: int = 0
    is_baseline: bool = False


class RECISTEvaluationResponseDto(BaseModel):
    """Response containing derived RECIST 1.1 parameters and overall response classification."""

    criteria: str = "RECIST_1_1"
    baseline_sld_mm: float
    nadir_sld_mm: float
    current_sld_mm: float
    pct_change_baseline: float
    pct_change_nadir: float
    absolute_change_nadir_mm: float
    target_response: str
    non_target_status: str
    new_lesions_present: bool
    new_lesions_count: int
    overall_response: str
    is_nadir: bool


class LuganoLesionInputDto(BaseModel):
    """Input representation of a target lesion for Lugano derivation."""

    lesion_id: str
    organ_site: str
    longest_diameter_mm: float
    perpendicular_diameter_mm: float
    is_extranodal: bool = False
    deauville_score: int | None = None


class LuganoEvaluationRequestDto(BaseModel):
    """Request payload to derive Lugano 2014 / Cheson response."""

    current_lesions: list[LuganoLesionInputDto]
    baseline_spd_mm2: float = 0.0
    prior_timepoints_spd: list[float] = Field(default_factory=list)
    new_lesions_present: bool = False
    single_lesion_progression: bool = False
    non_measured_progression: bool = False
    is_baseline: bool = False


class LuganoEvaluationResponseDto(BaseModel):
    """Response containing derived Lugano 2014 parameters."""

    criteria: str = "LUGANO_2014"
    baseline_spd_mm2: float
    nadir_spd_mm2: float
    current_spd_mm2: float
    pct_change_spd_baseline: float
    pct_change_spd_nadir: float
    ct_response: str
    pet_metabolic_response: str | None = None
    max_deauville_score: int | None = None
    new_lesions_present: bool
    overall_response: str


class MprMetadataResponseDto(BaseModel):
    """Response containing 3D volume geometry and MPR slice configuration."""

    series_instance_uid: str
    modality: str
    rows: int
    columns: int
    slice_count: int
    pixel_spacing: list[float]
    slice_thickness: float
    slice_spacing: float
    volume_dimensions_mm: list[float]
    window_center: float | None = None
    window_width: float | None = None
    available_planes: list[str]
    window_presets: dict[str, dict[str, float]]


class SubjectTimepointEvaluateRequestDto(BaseModel):
    """Request to compute, lock, and persist a timepoint evaluation for a subject."""

    study_id: str
    site_id: str
    timepoint_name: str
    assessment_criteria: str = "RECIST_1_1"  # RECIST_1_1, LUGANO_2014
    non_target_status: str = "NON_CR_NON_PD"
    new_lesions_present: bool = False
    new_lesions_count: int = 0
    is_baseline: bool = False
    digital_signature: str | None = None


class TimepointTrajectorySummaryDto(BaseModel):
    """Chronological timepoint summary in a subject's oncology tumor trajectory."""

    id: str
    timepoint_name: str
    assessment_criteria: str
    baseline_sld_mm: float | None = None
    nadir_sld_mm: float | None = None
    current_sld_mm: float | None = None
    pct_change_baseline: float | None = None
    pct_change_nadir: float | None = None
    baseline_spd_mm2: float | None = None
    nadir_spd_mm2: float | None = None
    current_spd_mm2: float | None = None
    pct_change_spd_baseline: float | None = None
    pct_change_spd_nadir: float | None = None
    target_response: str
    non_target_status: str
    new_lesions_present: bool
    new_lesions_count: int
    max_deauville_score: int | None = None
    overall_response: str
    is_locked: bool
    locked_at: datetime | None = None
    reader_id: str
    created_at: datetime | None = None


class SubjectTimepointsTrajectoryResponseDto(BaseModel):
    """Full chronological trajectory of oncologic measurements for a subject."""

    study_id: str
    site_id: str
    subject_id: str
    trajectory: list[TimepointTrajectorySummaryDto] = Field(default_factory=list)
    active_annotations_count: int = 0
