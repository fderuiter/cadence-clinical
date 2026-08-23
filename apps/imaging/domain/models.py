"""Domain models and value objects for Medical Imaging microservice.

Requirements: ADR-2199, PRD-IMG-001, PRD-IMG-002
"""

from datetime import datetime
from enum import StrEnum
from typing import Any

from pydantic import BaseModel, Field, model_validator


class ImagingModality(StrEnum):
    """Supported standard DICOM imaging modalities."""

    CT = "CT"  # Computed Tomography
    MR = "MR"  # Magnetic Resonance
    US = "US"  # Ultrasound
    PT = "PT"  # Positron Emission Tomography (PET)
    DX = "DX"  # Digital Radiography
    CR = "CR"  # Computed Radiography
    NM = "NM"  # Nuclear Medicine
    MG = "MG"  # Mammography
    SC = "SC"  # Secondary Capture
    OT = "OT"  # Other


class ImagingStudyStatus(StrEnum):
    """Lifecycle status of an imaging study envelope."""

    UPLOADED = "UPLOADED"
    DEIDENTIFIED = "DEIDENTIFIED"
    STORED = "STORED"
    QUARANTINED = "QUARANTINED"
    FAILED = "FAILED"


class AdjudicationStatus(StrEnum):
    """Independent Review Charter (IRC) adjudication workflow status."""

    UNASSIGNED = "UNASSIGNED"
    PENDING_READ = "PENDING_READ"
    READ_COMPLETED = "READ_COMPLETED"
    DISCORDANT_ESCALATED = "DISCORDANT_ESCALATED"
    ADJUDICATED = "ADJUDICATED"


class ImagingInstanceEnvelope(BaseModel):
    """Domain representation of a single DICOM instance (slice/frame)."""

    id: str
    series_envelope_id: str
    sop_instance_uid: str
    sop_class_uid: str
    instance_number: int | None = None
    rows: int = 0
    columns: int = 0
    bits_allocated: int = 16
    pixel_spacing: list[float] = Field(default_factory=list)
    slice_thickness: float | None = None
    slice_location: float | None = None
    window_center: float | None = None
    window_width: float | None = None
    object_key: str
    checksum_sha256: str
    size_bytes: int = 0
    burned_in_phi_masked: bool = False
    certificate_id: str | None = None


class ImagingSeriesEnvelope(BaseModel):
    """Domain representation of a DICOM series."""

    id: str
    study_envelope_id: str
    series_instance_uid: str
    series_number: int | None = None
    modality: str
    body_part_examined: str | None = None
    series_description: str | None = None
    instance_count: int = 0
    protocol_name: str | None = None
    frame_of_reference_uid: str | None = None
    instances: list[ImagingInstanceEnvelope] = Field(default_factory=list)


class ImagingStudyEnvelope(BaseModel):
    """Domain representation of a de-identified clinical imaging study envelope."""

    id: str
    study_instance_uid: str
    study_id: str
    site_id: str
    subject_id: str
    modalities: list[str] = Field(default_factory=list)
    study_date: str | None = None
    study_description: str | None = None
    series_count: int = 0
    instance_count: int = 0
    storage_prefix: str | None = None
    status: ImagingStudyStatus = ImagingStudyStatus.STORED
    adjudication_status: AdjudicationStatus = AdjudicationStatus.UNASSIGNED
    tenant_id: str = "tenant_default"
    created_at: datetime | None = None
    created_by: str = "system"
    updated_at: datetime | None = None
    reason_for_change: str | None = None
    version_index: int = 1
    series: list[ImagingSeriesEnvelope] = Field(default_factory=list)


class DeidentificationCertificateRecord(BaseModel):
    """Cryptographic certificate manifest record stored in relational database."""

    id: str
    study_envelope_id: str | None = None
    source_sha256: str
    target_sha256: str
    study_id: str
    site_id: str
    subject_id: str
    original_study_instance_uid_hash: str
    original_sop_instance_uid_hash: str
    anonymized_study_instance_uid: str
    anonymized_sop_instance_uid: str
    scrubbed_tags_count: int
    scrubbed_tags: list[str] = Field(default_factory=list)
    pixel_mask_applied: bool = False
    pixel_masks: list[dict[str, Any]] = Field(default_factory=list)
    operator_identity: str
    timestamp: str
    signature: str
    created_at: datetime | None = None


# Type aliases for hexagonal port compatibility
ImagingStudyEnvelopeEntity = ImagingStudyEnvelope
DeidentificationCertificateEntity = DeidentificationCertificateRecord


class LesionMeasurementType(StrEnum):
    """Supported oncologic caliper measurement types."""

    RECIST_1D = "1D_RECIST"
    PERPENDICULAR_2D = "2D_PERPENDICULAR"
    ELLIPSOID_3D = "3D_ELLIPSOID"
    ROI_STATISTICS = "ROI_STATISTICS"


class LesionCategory(StrEnum):
    """Lesion categorization under oncologic response criteria."""

    TARGET = "TARGET"
    NON_TARGET = "NON_TARGET"
    NEW_LESION = "NEW_LESION"


class OncologicCriteria(StrEnum):
    """Supported oncologic response assessment criteria."""

    RECIST_1_1 = "RECIST_1_1"
    LUGANO_2014 = "LUGANO_2014"
    RANO = "RANO"


class RECISTResponseCode(StrEnum):
    """RECIST 1.1 Response classifications."""

    CR = "CR"  # Complete Response
    PR = "PR"  # Partial Response
    SD = "SD"  # Stable Disease
    PD = "PD"  # Progressive Disease
    NE = "NE"  # Not Evaluable


class LuganoResponseCode(StrEnum):
    """Lugano 2014 Response classifications for lymphoma."""

    CR = "CR"  # Complete Response
    PR = "PR"  # Partial Response
    SD = "SD"  # Stable Disease
    PD = "PD"  # Progressive Disease
    NE = "NE"  # Not Evaluable
    CMR = "CMR"  # Complete Metabolic Response
    PMR = "PMR"  # Partial Metabolic Response
    NMR = "NMR"  # No Metabolic Response
    PMD = "PMD"  # Progressive Metabolic Disease


class MprPlane(StrEnum):
    """Multi-Planar Reconstruction viewing planes."""

    AXIAL = "AXIAL"
    SAGITTAL = "SAGITTAL"
    CORONAL = "CORONAL"


class PointCoordinate(BaseModel):
    """2D or 3D point in pixel space or patient physical space."""

    x: float
    y: float
    z: float | None = None


class CaliperAnnotationEntity(BaseModel):
    """Domain model for a caliper annotation placed on a DICOM series/slice."""

    id: str
    study_envelope_id: str
    series_envelope_id: str
    sop_instance_uid: str
    study_id: str
    site_id: str
    subject_id: str
    timepoint_name: str
    lesion_id: str
    lesion_type: LesionCategory
    organ_site: str
    measurement_criteria: OncologicCriteria = OncologicCriteria.RECIST_1_1
    measurement_type: LesionMeasurementType = LesionMeasurementType.RECIST_1D
    longest_diameter_mm: float
    perpendicular_diameter_mm: float | None = None
    product_perpendicular_mm2: float | None = None
    volume_mm3: float | None = None
    slice_index: int | None = None
    slice_location: float | None = None
    deauville_score: int | None = None
    points: list[PointCoordinate] = Field(default_factory=list)
    hounsfield_stats: dict[str, float] | None = None
    reader_id: str = "system"
    notes: str | None = None
    created_at: datetime | None = None
    created_by: str = "system"
    updated_at: datetime | None = None
    reason_for_change: str | None = None
    version_index: int = 1


class RECISTLesionInput(BaseModel):
    """Input representation of a target or non-target lesion for RECIST evaluation."""

    lesion_id: str
    organ_site: str
    lesion_type: LesionCategory
    longest_diameter_mm: float
    is_lymph_node: bool = False
    short_axis_mm: float | None = None
    sop_instance_uid: str | None = None


class RECIST11ResponseResult(BaseModel):
    """Result of RECIST 1.1 oncologic response evaluation."""

    criteria: OncologicCriteria = OncologicCriteria.RECIST_1_1
    baseline_sld_mm: float
    nadir_sld_mm: float
    current_sld_mm: float
    pct_change_baseline: float
    pct_change_nadir: float
    absolute_change_nadir_mm: float
    target_response: RECISTResponseCode
    non_target_status: str
    new_lesions_present: bool
    new_lesions_count: int = 0
    overall_response: RECISTResponseCode
    is_nadir: bool = False
    details: dict[str, Any] = Field(default_factory=dict)


class LuganoLesionInput(BaseModel):
    """Input representation of a measurable target lesion for Lugano evaluation."""

    lesion_id: str
    organ_site: str
    longest_diameter_mm: float
    perpendicular_diameter_mm: float
    is_extranodal: bool = False
    deauville_score: int | None = None


class LuganoResponseResult(BaseModel):
    """Result of Lugano 2014 response evaluation."""

    criteria: OncologicCriteria = OncologicCriteria.LUGANO_2014
    baseline_spd_mm2: float
    nadir_spd_mm2: float
    current_spd_mm2: float
    pct_change_spd_baseline: float
    pct_change_spd_nadir: float
    ct_response: LuganoResponseCode
    pet_metabolic_response: LuganoResponseCode | None = None
    max_deauville_score: int | None = None
    new_lesions_present: bool = False
    overall_response: LuganoResponseCode
    details: dict[str, Any] = Field(default_factory=dict)


class TimepointEvaluationEntity(BaseModel):
    """Domain model of a finalized and signed timepoint response assessment."""

    id: str
    study_id: str
    site_id: str
    subject_id: str
    timepoint_name: str
    assessment_criteria: OncologicCriteria
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
    new_lesions_count: int = 0
    max_deauville_score: int | None = None
    overall_response: str
    is_locked: bool = False
    locked_at: datetime | None = None
    locked_by: str | None = None
    reader_id: str = "system"
    manifest_sha256: str = ""
    signature: str = ""
    created_at: datetime | None = None
    created_by: str = "system"
    updated_at: datetime | None = None
    reason_for_change: str | None = None
    version_index: int = 1


class MprVolumeMetadataEntity(BaseModel):
    """Domain model for Multi-Planar Reconstruction volume geometry."""

    series_instance_uid: str
    modality: str
    rows: int
    columns: int
    slice_count: int
    pixel_spacing: list[float]
    slice_thickness: float
    slice_spacing: float
    patient_orientation_row: list[float] = Field(
        default_factory=lambda: [1.0, 0.0, 0.0]
    )
    patient_orientation_col: list[float] = Field(
        default_factory=lambda: [0.0, 1.0, 0.0]
    )
    volume_dimensions_mm: list[float] = Field(default_factory=list)
    window_center: float | None = None
    window_width: float | None = None
    available_planes: list[MprPlane] = Field(
        default_factory=lambda: [MprPlane.AXIAL, MprPlane.SAGITTAL, MprPlane.CORONAL]
    )
    window_presets: dict[str, dict[str, float]] = Field(default_factory=dict)


class ReaderRole(StrEnum):
    """Blinded reader role assignments."""

    READER_1 = "READER_1"
    READER_2 = "READER_2"
    ADJUDICATOR = "ADJUDICATOR"
    CHAIR = "CHAIR"


class AdjudicationWorkflowStatus(StrEnum):
    """Lifecycle state of the blinded dual-read workflow."""

    UNASSIGNED = "UNASSIGNED"
    PENDING_PRIMARY_READS = "PENDING_PRIMARY_READS"
    PENDING_READ_1 = "PENDING_READ_1"
    PENDING_READ_2 = "PENDING_READ_2"
    CONSENSUS_REACHED = "CONSENSUS_REACHED"
    DISCORDANCE_DETECTED = "DISCORDANCE_DETECTED"
    PENDING_ADJUDICATION = "PENDING_ADJUDICATION"
    ADJUDICATED = "ADJUDICATED"


class DiscordanceReason(StrEnum):
    """Categorized triggers for adjudication tie-breaker escalation."""

    OVERALL_RESPONSE_MISMATCH = "OVERALL_RESPONSE_MISMATCH"
    SLD_DISCORDANCE_EXCEEDS_THRESHOLD = "SLD_DISCORDANCE_EXCEEDS_THRESHOLD"
    NEW_LESION_DISCORDANCE = "NEW_LESION_DISCORDANCE"
    NON_TARGET_STATUS_MISMATCH = "NON_TARGET_STATUS_MISMATCH"
    TARGET_LESION_COUNT_MISMATCH = "TARGET_LESION_COUNT_MISMATCH"


class LesionMeasurement(BaseModel):
    """Individual target lesion measurement record."""

    lesion_id: str
    organ_site: str
    longest_diameter_mm: float
    short_axis_mm: float | None = None
    slice_location: float | None = None
    slice_index: int | None = None
    sop_instance_uid: str
    deauville_score: int | None = None


class ReaderAssessment(BaseModel):
    """Blinded assessment submitted by a certified central reader/radiologist."""

    reader_id: str
    reader_name: str = "Reader"
    reader_role: ReaderRole
    timepoint_name: str
    target_lesions: list[LesionMeasurement] = Field(default_factory=list)
    non_target_status: str = "NON_CR_NON_PD"
    new_lesions_present: bool = False
    new_lesions_count: int = 0
    sum_longest_diameters_mm: float = 0.0
    baseline_sld_mm: float | None = None
    nadir_sld_mm: float | None = None
    pct_change_baseline: float | None = None
    pct_change_nadir: float | None = None
    overall_response: RECISTResponseCode | str
    assessment_notes: str | None = None
    submitted_at: str = Field(default_factory=lambda: datetime.now().isoformat())
    digital_signature: str = ""


class AdjudicationSessionEntity(BaseModel):
    """Independent Review Charter session encapsulating dual-read and tie-breaker decisions."""

    id: str
    study_envelope_id: str | None = None
    study_id: str
    site_id: str
    subject_id: str
    timepoint_name: str
    assessment_criteria: OncologicCriteria = OncologicCriteria.RECIST_1_1
    reader_1_id: str
    reader_1_name: str = "Reader 1"
    reader_1_status: str = "PENDING"
    reader_1_assessment: ReaderAssessment | None = None
    reader_2_id: str
    reader_2_name: str = "Reader 2"
    reader_2_status: str = "PENDING"
    reader_2_assessment: ReaderAssessment | None = None
    adjudicator_id: str | None = None
    adjudicator_name: str | None = "Adjudication Committee Chair"
    adjudicator_assessment: ReaderAssessment | None = None
    sld_discordance_delta: float = 0.0
    is_response_discordant: bool = False
    discordance_reasons: list[str] = Field(default_factory=list)
    workflow_status: AdjudicationWorkflowStatus = (
        AdjudicationWorkflowStatus.PENDING_PRIMARY_READS
    )
    final_consensus_response: str | None = None
    final_consensus_sld_mm: float | None = None
    binding_reader: str | None = None
    adjudication_rationale: str | None = None
    electronic_signature: dict[str, Any] | None = None
    locked_at: datetime | str | None = None
    locked_by: str | None = None
    session_digest: str = ""
    tenant_id: str = "tenant_default"
    created_at: datetime | None = None
    created_by: str = "system"
    updated_at: datetime | None = None
    reason_for_change: str | None = None
    version_index: int = 1


class SDTMTURecord(BaseModel):
    """CDISC SDTM Tumor Identification (TU) domain record."""

    STUDYID: str
    DOMAIN: str = "TU"
    USUBJID: str
    TUSEQ: int
    TULNKID: str
    TUTESTCD: str = "TUMIDENT"
    TUTEST: str = "Tumor Identification"
    TUORRES: str
    TUSTRESC: str
    TUMETHOD: str | None = "CT SCAN"
    TULOC: str | None = None
    VISITNUM: float | int | None = None
    VISIT: str | None = None
    TUDTC: str | None = None
    TUEVAL: str = "INDEPENDENT ASSESSOR"
    TUEVALID: str | None = None


class SDTMTRRecord(BaseModel):
    """CDISC SDTM Tumor Results / Measurements (TR) domain record."""

    STUDYID: str
    DOMAIN: str = "TR"
    USUBJID: str
    TRSEQ: int
    TRLNKID: str | None = None
    TRTESTCD: str
    TRTEST: str
    TRORRES: str | None = None
    TRORRESU: str | None = "mm"
    TRSTRESC: str | None = None
    TRSTRESN: float | None = None
    TRSTRESU: str | None = "mm"
    TRMETHOD: str | None = "CT SCAN"
    VISITNUM: float | int | None = None
    VISIT: str | None = None
    TRDTC: str | None = None
    TREVAL: str = "INDEPENDENT ASSESSOR"
    TREVALID: str | None = None


class SDTMRSRecord(BaseModel):
    """CDISC SDTM Disease Response (RS) domain record."""

    STUDYID: str
    DOMAIN: str = "RS"
    USUBJID: str
    RSSEQ: int
    RSTESTCD: str = "OVRLRESP"
    RSTEST: str = "Overall Response"
    RSORRES: str
    RSSTRESC: str
    RSMETHOD: str = "RECIST 1.1"
    VISITNUM: float | int | None = None
    VISIT: str | None = None
    RSDTC: str | None = None
    RSEVAL: str = "INDEPENDENT ASSESSOR"
    RSEVALID: str | None = None
    RSEVALSTAT: str | None = "FINAL"


class EDCTumorIdentificationRecord(BaseModel):
    """Direct EDC eCRF binding for Tumor Identification."""

    lesion_id: str
    lesion_type: str
    organ_site: str
    method: str
    sop_instance_uid: str | None = None
    slice_location: float | None = None


class EDCTumorAssessmentForm(BaseModel):
    """Direct EDC eCRF binding for Tumor Assessment form."""

    form_id: str = "eCRF_TUMOR_ASSESSMENT"
    study_id: str
    site_id: str
    subject_id: str
    timepoint_name: str
    assessment_criteria: str
    target_lesions: list[EDCTumorIdentificationRecord] = Field(default_factory=list)
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


class DualReaderAdjudicationSession(BaseModel):
    """Independent Review Charter session encapsulating dual-read and tie-breaker decisions."""

    id: str | None = None
    session_id: str | None = None
    study_envelope_id: str | None = None
    study_id: str
    site_id: str = "SITE-DEFAULT"
    subject_id: str
    timepoint_name: str
    assessment_criteria: OncologicCriteria = OncologicCriteria.RECIST_1_1
    reader_1_id: str = "rad_01"
    reader_1_name: str = "Reader 1"
    reader_1_status: str = "PENDING"
    reader_1_assessment: ReaderAssessment | None = None
    reader_2_id: str = "rad_02"
    reader_2_name: str = "Reader 2"
    reader_2_status: str = "PENDING"
    reader_2_assessment: ReaderAssessment | None = None
    adjudicator_id: str | None = None
    adjudicator_name: str | None = "Adjudication Committee Chair"
    adjudicator_assessment: ReaderAssessment | None = None
    sld_discordance_delta: float = 0.0
    is_response_discordant: bool = False
    discordance_reasons: list[str] = Field(default_factory=list)
    workflow_status: AdjudicationWorkflowStatus = (
        AdjudicationWorkflowStatus.PENDING_PRIMARY_READS
    )
    final_consensus_response: RECISTResponseCode | str | None = None
    final_consensus_sld_mm: float | None = None
    binding_reader: str | None = None
    adjudication_rationale: str | None = None
    electronic_signature: dict[str, Any] | None = None
    locked_at: str | None = None
    locked_by: str | None = None
    session_digest: str = ""
    tenant_id: str = "tenant_default"
    created_at: str | None = None
    created_by: str = "system"
    updated_at: str | None = None
    reason_for_change: str | None = None
    version_index: int = 1

    @model_validator(mode="after")
    def sync_id_and_session_id(self) -> DualReaderAdjudicationSession:
        if self.session_id and not self.id:
            self.id = self.session_id
        elif self.id and not self.session_id:
            self.session_id = self.id
        elif not self.id and not self.session_id:
            import uuid

            new_id = str(uuid.uuid4())
            self.id = new_id
            self.session_id = new_id
        return self
