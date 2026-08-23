"""Domain models and canonical schemas for the Multi-Source Clinical Lakehouse.

Supports ingestion and normalization across 7 heterogeneous clinical streams:
1. Transactional EDC eCRFs
2. Subject-Facing ePRO / eCOA Questionnaires
3. Central Laboratory Feeds (HL7 / CSV)
4. Local Laboratory Feeds
5. Connected Sensors & Wearable IoT streams
6. EHR HL7 FHIR bundles
7. Blinded Medical Imaging Adjudication Results

Enforces version-pinned data snapshot immutability, cryptographic SHA-256 digests,
unblinded data partitioning, and 21 CFR Part 11 audit trails.

Requirements: PRD-CDR-001, ADR-2199, 21 CFR Part 11
"""

from __future__ import annotations

import uuid
from datetime import UTC, datetime
from enum import StrEnum
from typing import Any

from pydantic import BaseModel, ConfigDict, Field


class DataStreamType(StrEnum):
    """Supported heterogeneous clinical data stream types."""

    EDC_ECRF = "EDC_ECRF"
    EPRO_ECOA = "EPRO_ECOA"
    CENTRAL_LAB = "CENTRAL_LAB"
    LOCAL_LAB = "LOCAL_LAB"
    IOT_WEARABLE = "IOT_WEARABLE"
    EHR_FHIR = "EHR_FHIR"
    MEDICAL_IMAGING = "MEDICAL_IMAGING"


class LockMilestoneType(StrEnum):
    """Clinical database lock and interim analysis milestones."""

    INTERIM_ANALYSIS_1 = "INTERIM_ANALYSIS_1"
    INTERIM_ANALYSIS_2 = "INTERIM_ANALYSIS_2"
    DATABASE_LOCK = "DATABASE_LOCK"
    SAFETY_REVIEW = "SAFETY_REVIEW"
    REGULATORY_SUBMISSION = "REGULATORY_SUBMISSION"
    PROTOCOL_FREEZE = "PROTOCOL_FREEZE"
    ADJUDICATION_MILESTONE = "ADJUDICATION_MILESTONE"
    CUSTOM = "CUSTOM"


class LakehouseAction(StrEnum):
    """21 CFR Part 11 auditable lakehouse operations."""

    INGEST = "INGEST"
    BATCH_INGEST = "BATCH_INGEST"
    SNAPSHOT_CREATE = "SNAPSHOT_CREATE"
    SNAPSHOT_QUERY = "SNAPSHOT_QUERY"
    SNAPSHOT_VERIFY = "SNAPSHOT_VERIFY"
    POOL_EXPORT = "POOL_EXPORT"
    UNBLINDED_ACCESS = "UNBLINDED_ACCESS"
    INTEGRITY_CHECK = "INTEGRITY_CHECK"


class ObservationDomain(StrEnum):
    """Standard CDISC observation domains."""

    DM = "DM"  # Demographics
    AE = "AE"  # Adverse Events
    CM = "CM"  # Concomitant Medications
    VS = "VS"  # Vital Signs
    LB = "LB"  # Laboratory Tests
    EX = "EX"  # Exposure / Treatment
    DS = "DS"  # Disposition
    QS = "QS"  # Questionnaires / PRO
    PR = "PR"  # Procedures
    TR = "TR"  # Tumor Response / Lesion Measurements
    TU = "TU"  # Tumor Identification
    RS = "RS"  # Disease Response
    TI = "TI"  # Trial Inclusion / Exclusion
    CO = "CO"  # Comments
    OTHER = "OTHER"


# =========================================================================
# Ingest Item Models for Each Stream Type
# =========================================================================


class EDCEcrfIngestItem(BaseModel):
    """Raw ingestion payload for transactional EDC eCRFs."""

    model_config = ConfigDict(extra="forbid")

    subject_id: str = Field(
        ..., min_length=1, description="Unique subject/participant identifier"
    )
    visit_id: str = Field(
        ..., min_length=1, description="Visit identifier (e.g. VIS_01, SCREENING)"
    )
    form_oid: str = Field(
        ...,
        min_length=1,
        description="Form OID or form identifier (e.g. FORM_VS, FORM_AE)",
    )
    field_name: str = Field(
        ..., min_length=1, description="Item or field name (e.g. SYSBP, HR, AETERM)"
    )
    value: Any = Field(
        ..., description="Captured field value (numeric, string, boolean)"
    )
    unit: str | None = Field(None, description="Measurement unit (e.g. mmHg, bpm)")
    recorded_at: datetime = Field(
        ..., description="Timestamp of clinical observation or entry"
    )
    status: str = Field(
        "COMPLETED", description="Form entry status (e.g. DRAFT, COMPLETED, SIGNED)"
    )
    investigator_id: str | None = Field(
        None, description="Investigator or user identifier"
    )
    site_id: str | None = Field(None, description="Clinical site identifier")
    domain: str | None = Field(
        None, description="Optional CDISC domain override (e.g. VS, AE, LB)"
    )
    is_unblinded: bool = Field(
        False,
        description="Flag if field contains unblinded treatment/randomization data",
    )
    metadata: dict[str, Any] = Field(
        default_factory=dict, description="Additional EDC provenance metadata"
    )


class EPROEcoaIngestItem(BaseModel):
    """Raw ingestion payload for subject-facing ePRO / eCOA questionnaires."""

    model_config = ConfigDict(extra="forbid")

    subject_id: str = Field(..., min_length=1, description="Unique subject identifier")
    diary_id: str = Field(
        ..., min_length=1, description="Questionnaire / diary form identifier"
    )
    questionnaire_name: str = Field(
        ...,
        min_length=1,
        description="Name of instrument (e.g. PROMIS-29, SF-36, EQ-5D)",
    )
    answers: dict[str, Any] = Field(
        ..., description="Map of question IDs to participant responses"
    )
    total_score: float | None = Field(
        None, description="Computed questionnaire total or index score"
    )
    device_timestamp: datetime = Field(
        ..., description="Timestamp recorded by client device"
    )
    completed_at: datetime | None = Field(
        None, description="Timestamp questionnaire was submitted"
    )
    compliance_flag: bool = Field(
        True, description="Whether completion was within the scheduled time window"
    )
    site_id: str | None = Field(None, description="Associated clinical site identifier")
    metadata: dict[str, Any] = Field(
        default_factory=dict, description="Device and sensor sync metadata"
    )


class CentralLabIngestItem(BaseModel):
    """Raw ingestion payload for Central Diagnostic Laboratory feeds (HL7 / CSV)."""

    model_config = ConfigDict(extra="forbid")

    subject_id: str = Field(..., min_length=1, description="Subject identifier")
    site_id: str | None = Field(None, description="Clinical site identifier")
    specimen_id: str = Field(
        ..., min_length=1, description="Lab specimen barcode or identifier"
    )
    test_code: str = Field(
        ..., min_length=1, description="Standard LOINC or lab-specific test code"
    )
    test_name: str = Field(
        ...,
        min_length=1,
        description="Descriptive laboratory test name (e.g. Hemoglobin, ALT)",
    )
    numeric_value: float | None = Field(None, description="Quantitative test result")
    string_value: str | None = Field(
        None, description="Qualitative test result or text finding"
    )
    unit: str | None = Field(None, description="Unit of measurement (e.g. g/dL, U/L)")
    reference_low: float | None = Field(
        None, description="Normal lower reference range bound"
    )
    reference_high: float | None = Field(
        None, description="Normal upper reference range bound"
    )
    abnormal_flag: str | None = Field(
        None, description="Diagnostic flag (e.g. NORMAL, HIGH, LOW, CRITICAL)"
    )
    collection_timestamp: datetime = Field(
        ..., description="Specimen collection timestamp"
    )
    accession_number: str | None = Field(
        None, description="Laboratory accession number"
    )
    is_unblinded: bool = Field(
        False, description="Whether this lab test unblinds treatment arm"
    )
    metadata: dict[str, Any] = Field(
        default_factory=dict, description="Lab instrument and method metadata"
    )


class LocalLabIngestItem(BaseModel):
    """Raw ingestion payload for site local laboratory feeds."""

    model_config = ConfigDict(extra="forbid")

    subject_id: str = Field(..., min_length=1, description="Subject identifier")
    site_id: str = Field(..., min_length=1, description="Local site identifier")
    test_code: str = Field(..., min_length=1, description="Local laboratory test code")
    test_name: str = Field(..., min_length=1, description="Local lab test name")
    value: float | str = Field(..., description="Test result value (numeric or text)")
    unit: str | None = Field(None, description="Result unit")
    reference_low: float | None = Field(
        None, description="Site reference range lower bound"
    )
    reference_high: float | None = Field(
        None, description="Site reference range upper bound"
    )
    abnormal_flag: str | None = Field(
        None, description="Abnormal indicator (NORMAL, HIGH, LOW)"
    )
    collection_timestamp: datetime = Field(
        ..., description="Sample collection timestamp"
    )
    is_unblinded: bool = Field(
        False, description="Flag for unblinded specialized tests"
    )
    metadata: dict[str, Any] = Field(
        default_factory=dict, description="Site lab technician and kit metadata"
    )


class IoTTelemetryReading(BaseModel):
    """Single time-series point from a connected sensor or wearable device."""

    model_config = ConfigDict(extra="forbid")

    timestamp: datetime = Field(..., description="Observation timestamp")
    metric_name: str = Field(
        ...,
        min_length=1,
        description="Metric name (e.g. glucose, step_count, heart_rate, fev1)",
    )
    value: float = Field(..., description="Numeric telemetry value")
    unit: str | None = Field(
        None, description="Metric unit (e.g. mg/dL, steps, bpm, L)"
    )
    flag: str | None = Field(
        None, description="Telemetry quality flag (e.g. VALID, ARTIFACT, EXCURSION)"
    )


class IOTWearableIngestItem(BaseModel):
    """Raw ingestion payload for connected sensors and wearable IoT streams."""

    model_config = ConfigDict(extra="forbid")

    subject_id: str = Field(..., min_length=1, description="Subject identifier")
    device_id: str = Field(
        ..., min_length=1, description="Unique medical device serial or UUID"
    )
    device_type: str = Field(
        ...,
        min_length=1,
        description="Device model / type (e.g. CGM, ACTIGRAPHY, SPIROMETER, ECG)",
    )
    observation_type: str = Field(
        ..., min_length=1, description="Continuous observation stream category"
    )
    telemetry_readings: list[IoTTelemetryReading] = Field(
        ..., min_length=1, description="Batched time-series sensor readings"
    )
    duration_seconds: float | None = Field(
        None, description="Aggregate recording duration"
    )
    sample_rate_hz: float | None = Field(None, description="Device sampling rate in Hz")
    calibration_status: str = Field(
        "CALIBRATED", description="Device calibration state"
    )
    site_id: str | None = Field(None, description="Site identifier")
    metadata: dict[str, Any] = Field(
        default_factory=dict, description="Sensor firmware and battery status metadata"
    )


class EHRFHIRIngestItem(BaseModel):
    """Raw ingestion payload for Electronic Health Record (EHR) HL7 FHIR bundles."""

    model_config = ConfigDict(extra="forbid")

    subject_id: str = Field(
        ..., min_length=1, description="Clinical trial subject identifier"
    )
    resource_type: str = Field(
        ...,
        min_length=1,
        description="FHIR resource type (Observation, Condition, Encounter, MedicationRequest)",
    )
    fhir_id: str = Field(..., min_length=1, description="FHIR resource ID")
    resource_payload: dict[str, Any] = Field(
        ..., description="Complete FHIR JSON resource object"
    )
    coding_code: str | None = Field(
        None,
        description="Primary clinical coding (e.g. SNOMED-CT, LOINC, RxNorm, ICD-10)",
    )
    coding_display: str | None = Field(
        None, description="Coding human-readable display string"
    )
    effective_datetime: datetime = Field(
        ..., description="Clinical occurrence or effective timestamp"
    )
    site_id: str | None = Field(None, description="Site identifier")
    metadata: dict[str, Any] = Field(
        default_factory=dict, description="FHIR bundle header and source EHR metadata"
    )


class MedicalImagingLesion(BaseModel):
    """RECIST 1.1 or imaging lesion evaluation."""

    model_config = ConfigDict(extra="forbid")

    lesion_id: str = Field(
        ...,
        min_length=1,
        description="Target/non-target lesion identifier (e.g. T1, NT1)",
    )
    lesion_type: str = Field(
        "TARGET", description="Lesion category: TARGET, NON_TARGET, NEW_LESION"
    )
    site_location: str = Field(
        ...,
        min_length=1,
        description="Anatomical site location (e.g. Liver, Right Lung, Lymph Node)",
    )
    longest_diameter_mm: float | None = Field(
        None, description="Longest diameter measurement in mm"
    )
    short_axis_mm: float | None = Field(
        None, description="Short axis measurement in mm for lymph nodes"
    )
    response_category: str | None = Field(
        None, description="Lesion status: PRESENT, ABSENT, NOT_EVALUATED"
    )


class MedicalImagingIngestItem(BaseModel):
    """Raw ingestion payload for Blinded Medical Imaging and IRC adjudication results."""

    model_config = ConfigDict(extra="forbid")

    subject_id: str = Field(..., min_length=1, description="Subject identifier")
    site_id: str | None = Field(None, description="Site identifier")
    study_instance_uid: str = Field(
        ..., min_length=1, description="DICOM Study Instance UID"
    )
    series_instance_uid: str | None = Field(
        None, description="DICOM Series Instance UID"
    )
    modality: str = Field(
        ..., min_length=1, description="Imaging modality (CT, MR, PET, PT, DX)"
    )
    timepoint: str = Field(
        ...,
        min_length=1,
        description="Assessment timepoint (e.g. BASELINE, WEEK_6, WEEK_12)",
    )
    lesions: list[MedicalImagingLesion] = Field(
        default_factory=list, description="Assessed target and non-target lesions"
    )
    overall_response: str | None = Field(
        None, description="RECIST 1.1 overall response (CR, PR, SD, PD, NE)"
    )
    sum_of_diameters_mm: float | None = Field(
        None, description="Sum of longest diameters for target lesions in mm"
    )
    reader_id: str = Field(
        ...,
        min_length=1,
        description="Blinded independent central review (BICR) reader identifier",
    )
    adjudication_tier: str = Field(
        "PRIMARY_READER_1",
        description="Adjudication tier: PRIMARY_READER_1, PRIMARY_READER_2, TIE_BREAKER",
    )
    scan_datetime: datetime = Field(..., description="DICOM acquisition timestamp")
    is_unblinded: bool = Field(
        False, description="Whether this adjudication unblinds arm"
    )
    metadata: dict[str, Any] = Field(
        default_factory=dict, description="Imaging charter and PACS archive metadata"
    )


# =========================================================================
# Unified Ingestion Envelope
# =========================================================================


class MultiStreamIngestPayload(BaseModel):
    """Unified payload container for ingesting one or more heterogeneous streams."""

    model_config = ConfigDict(extra="forbid")

    study_id: str = Field(
        ..., min_length=1, description="Clinical trial protocol ID (e.g. STUDY-901)"
    )
    stream_type: DataStreamType = Field(..., description="Stream type being ingested")
    edc_items: list[EDCEcrfIngestItem] | None = Field(
        None, description="EDC eCRF items when stream_type=EDC_ECRF"
    )
    epro_items: list[EPROEcoaIngestItem] | None = Field(
        None, description="ePRO items when stream_type=EPRO_ECOA"
    )
    central_lab_items: list[CentralLabIngestItem] | None = Field(
        None, description="Central lab items when stream_type=CENTRAL_LAB"
    )
    local_lab_items: list[LocalLabIngestItem] | None = Field(
        None, description="Local lab items when stream_type=LOCAL_LAB"
    )
    wearable_items: list[IOTWearableIngestItem] | None = Field(
        None, description="IoT wearable items when stream_type=IOT_WEARABLE"
    )
    fhir_items: list[EHRFHIRIngestItem] | None = Field(
        None, description="FHIR items when stream_type=EHR_FHIR"
    )
    imaging_items: list[MedicalImagingIngestItem] | None = Field(
        None, description="Imaging items when stream_type=MEDICAL_IMAGING"
    )
    reason_for_ingest: str = Field(
        "Routine stream ingestion into CDR Lakehouse",
        description="GxP justification for ingestion",
    )


# =========================================================================
# Canonical Lakehouse Observation & Snapshot Models
# =========================================================================


class CanonicalClinicalRecord(BaseModel):
    """Harmonized, canonical observation record stored in the Lakehouse repository."""

    model_config = ConfigDict(extra="forbid")

    record_id: str = Field(
        default_factory=lambda: str(uuid.uuid4()),
        description="Unique canonical record UUID",
    )
    study_id: str = Field(..., description="Study identifier")
    site_id: str | None = Field(None, description="Site identifier")
    subject_id: str = Field(..., description="Subject identifier")
    stream_type: DataStreamType = Field(..., description="Originating data stream type")
    domain: str = Field(
        ..., description="Normalized CDISC domain (e.g. DM, AE, VS, LB, QS, TR)"
    )
    item_code: str = Field(..., description="Canonical item code or test identifier")
    item_name: str = Field(..., description="Human-readable item name")
    observation_datetime: datetime = Field(
        ..., description="Effective timestamp of observation"
    )
    numeric_value: float | None = Field(
        None, description="Quantitative value if applicable"
    )
    text_value: str | None = Field(None, description="Categorical or textual finding")
    unit: str | None = Field(None, description="Normalized standard unit")
    reference_low: float | None = Field(None, description="Reference range lower bound")
    reference_high: float | None = Field(
        None, description="Reference range upper bound"
    )
    abnormal_flag: str | None = Field(None, description="Abnormality flag")
    is_unblinded: bool = Field(
        False, description="Flag indicating if record contains unblinded data"
    )
    raw_payload: dict[str, Any] = Field(
        default_factory=dict, description="Original raw stream payload"
    )
    normalized_attributes: dict[str, Any] = Field(
        default_factory=dict, description="Harmonized contextual attributes"
    )
    data_hash: str = Field(
        ..., description="SHA-256 digest of normalized record fields"
    )
    created_at: datetime = Field(
        default_factory=lambda: datetime.now(UTC),
        description="Lakehouse ingestion timestamp",
    )


class ClinicalSnapshot(BaseModel):
    """Immutable, version-pinned snapshot pool tied to a database lock milestone."""

    model_config = ConfigDict(extra="forbid")

    snapshot_id: str = Field(
        default_factory=lambda: f"SNAP-{uuid.uuid4().hex[:12].upper()}",
        description="Snapshot identifier",
    )
    study_id: str = Field(..., description="Clinical study identifier")
    version_tag: str = Field(
        ..., min_length=1, description="Version tag (e.g. v1.0.0-INTERIM-1)"
    )
    milestone: LockMilestoneType = Field(
        ..., description="Associated database lock milestone"
    )
    lock_id: str | None = Field(
        None, description="Reference to relational DataLock record"
    )
    record_count: int = Field(
        0, ge=0, description="Total records pooled in this snapshot"
    )
    stream_counts: dict[str, int] = Field(
        default_factory=dict, description="Breakdown of record counts by stream type"
    )
    domain_counts: dict[str, int] = Field(
        default_factory=dict, description="Breakdown of record counts by CDISC domain"
    )
    sha256_digest: str = Field(
        ..., description="Cryptographic SHA-256 digest sealing this snapshot pool"
    )
    created_at: datetime = Field(
        default_factory=lambda: datetime.now(UTC),
        description="Snapshot creation timestamp",
    )
    created_by: str = Field(..., description="User ID or service creating snapshot")
    reason_for_snapshot: str = Field(
        ...,
        min_length=1,
        description="GxP rationale / justification for snapshot creation",
    )
    is_locked: bool = Field(
        True, description="Immutability flag (sealed snapshots cannot be modified)"
    )
    blinded_record_count: int = Field(
        0, ge=0, description="Count of blinded records in pool"
    )
    unblinded_record_count: int = Field(
        0, ge=0, description="Count of unblinded records in pool"
    )
    snapshot_manifest: dict[str, Any] = Field(
        default_factory=dict, description="Cryptographic manifest metadata"
    )


class SnapshotFilterCriteria(BaseModel):
    """Filter criteria used when generating or querying a version-pinned snapshot."""

    model_config = ConfigDict(extra="forbid")

    subject_ids: list[str] | None = Field(
        None, description="Optional filter by subject IDs"
    )
    site_ids: list[str] | None = Field(None, description="Optional filter by site IDs")
    stream_types: list[DataStreamType] | None = Field(
        None, description="Filter by stream types"
    )
    domains: list[str] | None = Field(None, description="Filter by CDISC domains")
    cutoff_datetime: datetime | None = Field(
        None, description="Upper bound timestamp cutoff for snapshot pooling"
    )
    include_unblinded: bool = Field(
        False,
        description="Whether to request unblinded records (requires unblinded role)",
    )


class SnapshotVerificationResult(BaseModel):
    """Result of cryptographic SHA-256 integrity verification against a sealed snapshot."""

    model_config = ConfigDict(extra="forbid")

    snapshot_id: str = Field(..., description="Verified snapshot identifier")
    version_tag: str = Field(..., description="Snapshot version tag")
    stored_digest: str = Field(
        ..., description="SHA-256 digest recorded at snapshot sealing"
    )
    computed_digest: str = Field(
        ..., description="SHA-256 digest dynamically recomputed over pool"
    )
    is_valid: bool = Field(
        ..., description="Whether digests match bit-for-bit (True = untampered)"
    )
    record_count: int = Field(..., description="Total verified record count")
    verified_at: datetime = Field(
        default_factory=lambda: datetime.now(UTC), description="Verification timestamp"
    )
    verified_by: str = Field(..., description="User ID performing verification")
    verification_message: str = Field(
        ..., description="Detailed integrity verification message"
    )


class LakehouseMetrics(BaseModel):
    """Aggregated real-time metrics for a study in the Lakehouse repository."""

    model_config = ConfigDict(extra="forbid")

    study_id: str = Field(..., description="Study identifier")
    total_records: int = Field(
        0, ge=0, description="Total canonical clinical records ingested"
    )
    total_snapshots: int = Field(0, ge=0, description="Total sealed snapshots created")
    stream_breakdown: dict[str, int] = Field(
        default_factory=dict, description="Record counts per stream type"
    )
    domain_breakdown: dict[str, int] = Field(
        default_factory=dict, description="Record counts per CDISC domain"
    )
    subject_count: int = Field(0, ge=0, description="Distinct subject count")
    site_count: int = Field(0, ge=0, description="Distinct site count")
    blinded_record_count: int = Field(0, ge=0, description="Total blinded records")
    unblinded_record_count: int = Field(0, ge=0, description="Total unblinded records")
    latest_snapshot_digest: str | None = Field(
        None, description="SHA-256 digest of latest sealed snapshot"
    )
    last_ingested_at: datetime | None = Field(
        None, description="Timestamp of most recent ingestion"
    )


# Type aliases for backward-compatibility across Lakehouse and SCE subsystems
CanonicalObservationRecord = CanonicalClinicalRecord
VersionPinnedSnapshot = ClinicalSnapshot
