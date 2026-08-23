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


# =========================================================================
# Blinded Dual-Reader Charter & Tie-Breaker Adjudication DTOs
# Requirements: PRD-IMG-003, PRD-IMG-005, PRD-IMG-006, PRD-SYS-001
# =========================================================================


class CreateAdjudicationSessionDto(BaseModel):
    """Payload to instantiate a new Blinded Dual-Reader IRC adjudication session."""

    study_id: str = Field(..., description="Trial Protocol Identifier")
    site_id: str = Field(..., description="Clinical Trial Site ID")
    subject_id: str = Field(..., description="Trial Subject UUID")
    timepoint_name: str = Field(..., description="e.g. BASELINE, CYCLE_2_DAY_1")
    study_envelope_id: str | None = Field(
        default=None, description="Optional study envelope ID"
    )
    assessment_criteria: str = Field(
        default="RECIST_1_1", description="RECIST_1_1, LUGANO_2014, RANO"
    )
    reader_1_id: str = Field(..., description="Certified Radiologist 1 User ID")
    reader_1_name: str = Field(default="Reader 1", description="Reader 1 Full Name")
    reader_2_id: str = Field(..., description="Certified Radiologist 2 User ID")
    reader_2_name: str = Field(default="Reader 2", description="Reader 2 Full Name")
    adjudicator_id: str | None = Field(
        default=None, description="Adjudication Committee Chair User ID"
    )
    adjudicator_name: str | None = Field(
        default="Adjudication Committee Chair", description="Adjudicator Full Name"
    )


class LesionMeasurementDto(BaseModel):
    """Individual target or non-target lesion caliper measurement."""

    lesion_id: str = Field(..., description="Lesion tag, e.g. TL1, TL2, NTL1")
    organ_site: str = Field(..., description="Anatomical organ location")
    longest_diameter_mm: float = Field(..., description="Longest axis diameter in mm")
    short_axis_mm: float | None = Field(
        default=None, description="Short / perpendicular axis in mm"
    )
    slice_location: float | None = Field(default=None, description="Slice Z-position")
    slice_index: int | None = Field(default=None, description="Instance slice index")
    sop_instance_uid: str = Field(default="", description="DICOM SOP Instance UID")
    deauville_score: int | None = Field(
        default=None, description="Deauville 1-5 score for PET"
    )


class SubmitReaderAssessmentDto(BaseModel):
    """Blinded reader submission payload."""

    reader_id: str = Field(..., description="Radiologist User ID")
    reader_role: str = Field(..., description="READER_1, READER_2, or ADJUDICATOR")
    reader_name: str = Field(default="Reader", description="Radiologist Display Name")
    target_lesions: list[LesionMeasurementDto] = Field(default_factory=list)
    non_target_status: str = Field(
        default="NON_CR_NON_PD", description="CR, NON_CR_NON_PD, PD, NE"
    )
    new_lesions_present: bool = Field(default=False)
    new_lesions_count: int = Field(default=0)
    sum_longest_diameters_mm: float = Field(default=0.0)
    baseline_sld_mm: float | None = Field(default=None)
    nadir_sld_mm: float | None = Field(default=None)
    overall_response: str = Field(..., description="CR, PR, SD, PD, NE")
    assessment_notes: str | None = Field(default=None)
    digital_signature: str = Field(default="")


class StepUpAdjudicationSignOffDto(BaseModel):
    """21 CFR Part 11 step-up electronic signature sign-off payload for Adjudication Chair."""

    signer_id: str = Field(..., description="Adjudicator User ID")
    signer_name: str = Field(..., description="Full Legal Name of Adjudicator")
    password: str | None = Field(
        default=None, description="Step-up credential / authentication secret"
    )
    step_up_token: str | None = Field(
        default=None, description="Cryptographic step-up token"
    )
    signing_reason: str = Field(
        default="ADJUDICATION_SIGN_OFF",
        description="21 CFR Part 11 signature reason (e.g. ADJUDICATION_SIGN_OFF, APPROVAL)",
    )
    binding_reader: str = Field(
        default="ADJUDICATOR",
        description="Source of binding measurements: READER_1, READER_2, or ADJUDICATOR",
    )
    rationale: str = Field(
        ..., description="Clinical justification for tie-breaker determination"
    )
    adjudicator_assessment: SubmitReaderAssessmentDto | None = Field(
        default=None, description="Optional independent adjudicator measurement set"
    )


class BlindedReaderAssessmentDto(BaseModel):
    """Blinded reader assessment representation."""

    reader_id: str
    reader_name: str
    reader_role: str
    timepoint_name: str
    target_lesions: list[LesionMeasurementDto] = Field(default_factory=list)
    non_target_status: str
    new_lesions_present: bool
    new_lesions_count: int
    sum_longest_diameters_mm: float
    overall_response: str
    assessment_notes: str | None = None
    submitted_at: str
    digital_signature: str


class AdjudicationSessionResponseDto(BaseModel):
    """Dual-reader adjudication session response (dynamically filtered for blinding)."""

    session_id: str
    study_envelope_id: str | None = None
    study_id: str
    site_id: str
    subject_id: str
    timepoint_name: str
    assessment_criteria: str
    workflow_status: str
    reader_1_status: str
    reader_2_status: str
    reader_1_assessment: BlindedReaderAssessmentDto | None = None
    reader_2_assessment: BlindedReaderAssessmentDto | None = None
    adjudicator_assessment: BlindedReaderAssessmentDto | None = None
    sld_discordance_delta: float = 0.0
    is_response_discordant: bool = False
    discordance_reasons: list[str] = Field(default_factory=list)
    final_consensus_response: str | None = None
    final_consensus_sld_mm: float | None = None
    binding_reader: str | None = None
    adjudication_rationale: str | None = None
    electronic_signature: dict | None = None
    locked_at: datetime | str | None = None
    locked_by: str | None = None
    session_digest: str = ""
    version_index: int = 1


class EDCTumorBindingResponseDto(BaseModel):
    """Direct EDC eCRF binding payload."""

    form_id: str = "eCRF_TUMOR_ASSESSMENT"
    study_id: str
    site_id: str
    subject_id: str
    timepoint_name: str
    assessment_criteria: str
    target_lesions: list[dict] = Field(default_factory=list)
    sum_longest_diameters_mm: float | None = None
    baseline_sld_mm: float | None = None
    nadir_sld_mm: float | None = None
    pct_change_baseline: float | None = None
    pct_change_nadir: float | None = None
    non_target_status: str
    new_lesions_present: bool
    overall_response: str
    adjudication_status: str
    binding_source: str
    manifest_sha256: str
    signed_by: str | None = None
    signed_at: str | None = None


class SDTMDatasetExportResponseDto(BaseModel):
    """CDISC SDTM TU, TR, RS dataset package."""

    study_id: str
    subject_id: str
    timepoint_name: str
    tu_records: list[dict] = Field(default_factory=list)
    tr_records: list[dict] = Field(default_factory=list)
    rs_records: list[dict] = Field(default_factory=list)
    total_records: int = 0
