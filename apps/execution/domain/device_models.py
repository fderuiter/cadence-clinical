"""Domain Models, Pydantic v2 DTOs, and Enums for Medical Device & IVD Investigation.

Requirements: PRD-SYS-126, PRD-DEV-001, PRD-DEV-002, PRD-DEV-003, PRD-DEV-004,
ISO 14155:2020, EU MDR 2017/745, EU IVDR 2017/746, 21 CFR Part 11, ADR-2209.
"""

from datetime import UTC, datetime
from enum import StrEnum

from pydantic import BaseModel, Field, field_validator


class DeviceType(StrEnum):
    """Classification of device modality according to ISO 14155 and EU MDR/IVDR."""

    THERAPEUTIC = "THERAPEUTIC"
    DIAGNOSTIC_IVD = "DIAGNOSTIC_IVD"
    IMPLANTABLE = "IMPLANTABLE"
    SAMD = "SAMD"  # Software as a Medical Device
    COMBINATION = "COMBINATION"


class DeviceClassification(StrEnum):
    """Regulatory risk classification per EU MDR (Classes I, IIa, IIb, III) and IVDR (Classes A-D)."""

    CLASS_I = "CLASS_I"
    CLASS_IIA = "CLASS_IIA"
    CLASS_IIB = "CLASS_IIB"
    CLASS_III = "CLASS_III"
    IVD_CLASS_A = "IVD_CLASS_A"
    IVD_CLASS_B = "IVD_CLASS_B"
    IVD_CLASS_C = "IVD_CLASS_C"
    IVD_CLASS_D = "IVD_CLASS_D"


class UDICarrierFormat(StrEnum):
    """Barcode and UDI standard formats."""

    GS1_128 = "GS1_128"
    GS1_DATAMATRIX = "GS1_DATAMATRIX"
    HIBCC_128 = "HIBCC_128"
    HIBCC_DATAMATRIX = "HIBCC_DATAMATRIX"
    RAW_UDI = "RAW_UDI"


class DeviceInventoryStatus(StrEnum):
    """Accountability status for investigational medical device serialized inventory."""

    AVAILABLE = "AVAILABLE"
    ASSIGNED = "ASSIGNED"
    DISPENSED = "DISPENSED"
    IMPLANTED = "IMPLANTED"
    QUARANTINED = "QUARANTINED"
    RETURNED = "RETURNED"
    DEFICIENT = "DEFICIENT"
    DESTROYED = "DESTROYED"
    DECOMMISSIONED = "DECOMMISSIONED"


class DeviceDeficiencyType(StrEnum):
    """Standardized device deficiency types per ISO 14155:2020 Clause 3.15."""

    MALFUNCTION = "MALFUNCTION"
    USABILITY_ERROR = "USABILITY_ERROR"
    INADEQUATE_LABELING = "INADEQUATE_LABELING"
    DESIGN_DEFECT = "DESIGN_DEFECT"
    PACKAGING_BREACH = "PACKAGING_BREACH"
    SOFTWARE_FAULT = "SOFTWARE_FAULT"


class HealthImpact(StrEnum):
    """Clinical severity and patient consequence per ISO 14155."""

    NONE = "NONE"
    MILD_TRANSIENT = "MILD_TRANSIENT"
    SERIOUS_DETERIORATION = "SERIOUS_DETERIORATION"
    PERMANENT_IMPAIRMENT = "PERMANENT_IMPAIRMENT"
    LIFE_THREATENING = "LIFE_THREATENING"
    DEATH = "DEATH"


class ActionTakenDevice(StrEnum):
    """Action taken with the investigational medical device upon deficiency."""

    QUARANTINED_AT_SITE = "QUARANTINED_AT_SITE"
    RETURNED_TO_SPONSOR = "RETURNED_TO_SPONSOR"
    REPLACED = "REPLACED"
    DISPOSED = "DISPOSED"
    REPAIRED = "REPAIRED"
    NONE = "NONE"


class QuarantineScope(StrEnum):
    """Scope of quarantine trigger."""

    DEVICE_SERIAL = "DEVICE_SERIAL"
    SITE_LOT = "SITE_LOT"
    STUDY_GLOBAL_LOT = "STUDY_GLOBAL_LOT"


class DeviceSafetyClassification(StrEnum):
    """Safety event classification for Adverse Device Effects."""

    ADE = "ADE"  # Adverse Device Effect (related, non-serious)
    SADE = "SADE"  # Serious Adverse Device Effect
    USADE = "USADE"  # Unanticipated Serious Adverse Device Effect
    DEVICE_DEFICIENCY_INCIDENT = "DEVICE_DEFICIENCY_INCIDENT"  # Could have led to SADE


class InvestigationStatus(StrEnum):
    """Lifecycle state of an ADE/SADE/USADE safety investigation."""

    OPEN = "OPEN"
    TRIAGED = "TRIAGED"
    UNDER_INVESTIGATION = "UNDER_INVESTIGATION"
    CAPA_INITIATED = "CAPA_INITIATED"
    RESOLVED = "RESOLVED"
    REPORTED_TO_AUTHORITY = "REPORTED_TO_AUTHORITY"
    CLOSED = "CLOSED"


# =========================================================================
# Pydantic v2 DTOs
# =========================================================================


class ParsedUDI(BaseModel):
    """Result of UDI barcode parsing and validation."""

    raw_barcode: str
    udi_carrier_format: UDICarrierFormat = UDICarrierFormat.GS1_128
    udi_di: str
    lot_number: str | None = None
    serial_number: str | None = None
    expiration_date: str | None = None
    manufacturing_date: str | None = None
    software_version: str | None = None
    is_valid: bool = True
    validation_errors: list[str] = Field(default_factory=list)


class UDIParseRequest(BaseModel):
    """Request payload for barcode parsing."""

    raw_barcode: str = Field(
        ..., min_length=3, description="Raw barcode string (GS1 / HIBCC / Raw UDI)"
    )


class DeviceInventoryCreateRequest(BaseModel):
    """Request payload for registering investigational devices in RTSM."""

    study_id: str = Field(..., min_length=1)
    site_id: str = Field(..., min_length=1)
    device_identifier: str | None = None
    brand_name: str = Field(..., min_length=1)
    device_model: str | None = None
    device_type: DeviceType = DeviceType.THERAPEUTIC
    device_classification: DeviceClassification = DeviceClassification.CLASS_IIA
    raw_barcode: str | None = None
    udi_di: str = Field(..., min_length=3)
    lot_number: str = Field(..., min_length=1)
    serial_number: str | None = None
    expiration_date: datetime | None = None
    manufacturing_date: datetime | None = None
    software_version: str | None = None
    quantity: int = Field(default=1, ge=1, le=1000)
    reason_for_change: str = Field(
        default="Investigational medical device inventory intake", min_length=5
    )


class DeviceInventoryResponse(BaseModel):
    """Detailed response model for investigational device inventory item."""

    id: str
    study_id: str
    site_id: str
    device_identifier: str
    brand_name: str
    device_model: str | None
    device_type: str
    device_classification: str
    udi_di: str
    udi_pi: str | None
    udi_carrier_format: str
    lot_number: str
    serial_number: str | None
    expiration_date: datetime | None
    manufacturing_date: datetime | None
    software_version: str | None
    status: str
    is_dispensation_blocked: bool
    allocated_subject_id: str | None
    dispensed_at: datetime | None
    dispensed_by: str | None
    quarantined_at: datetime | None
    quarantined_by: str | None
    quarantine_reason: str | None
    created_at: datetime
    created_by: str
    reason_for_change: str
    version_index: int


class DeviceDispensationRequest(BaseModel):
    """Request payload for dispensing a device to a clinical study participant."""

    study_id: str = Field(..., min_length=1)
    site_id: str = Field(..., min_length=1)
    subject_id: str = Field(..., min_length=1)
    device_inventory_id: str | None = None
    serial_number: str | None = None
    scanned_barcode: str | None = None
    reason_for_change: str = Field(
        default="Investigational device dispensation to participant", min_length=5
    )


class DeviceDispensationResponse(BaseModel):
    """Response model for device dispensation."""

    status: str = "SUCCESS"
    message: str
    device: DeviceInventoryResponse
    dispensed_at: datetime


class DeviceDeficiencyCreateRequest(BaseModel):
    """Request payload for reporting a device deficiency or malfunction."""

    study_id: str = Field(..., min_length=1)
    site_id: str = Field(..., min_length=1)
    subject_id: str | None = None
    device_inventory_id: str | None = None
    udi_di: str = Field(..., min_length=3)
    lot_number: str = Field(..., min_length=1)
    serial_number: str | None = None
    device_name: str = Field(default="Investigational Medical Device", min_length=1)
    deficiency_type: DeviceDeficiencyType = DeviceDeficiencyType.MALFUNCTION
    deficiency_description: str = Field(..., min_length=5)
    incident_date: datetime
    awareness_date: datetime = Field(default_factory=lambda: datetime.now(UTC))
    could_lead_to_sade: bool = False
    patient_involved: bool = False
    health_impact: HealthImpact = HealthImpact.NONE
    action_taken_device: ActionTakenDevice = ActionTakenDevice.QUARANTINED_AT_SITE
    device_returned_to_sponsor: bool = False
    return_tracking_number: str | None = None
    quarantine_scope: QuarantineScope = QuarantineScope.DEVICE_SERIAL
    reason_for_change: str = Field(
        default="Report investigational medical device deficiency / malfunction",
        min_length=5,
    )
    signature_token: str | None = None

    @field_validator("reason_for_change")
    @classmethod
    def validate_reason(cls, v: str) -> str:
        if not v or not v.strip():
            raise ValueError("Reason for change cannot be blank.")
        return v


class DeviceDeficiencyResponse(BaseModel):
    """Response model for reported device deficiency."""

    id: str
    study_id: str
    site_id: str
    subject_id: str | None
    device_inventory_id: str | None
    udi_di: str
    lot_number: str
    serial_number: str | None
    device_name: str
    deficiency_type: str
    deficiency_description: str
    incident_date: datetime
    awareness_date: datetime
    could_lead_to_sade: bool
    patient_involved: bool
    health_impact: str
    action_taken_device: str
    device_returned_to_sponsor: bool
    return_tracking_number: str | None
    root_cause_analysis: str | None
    sponsor_evaluation: str | None
    quarantine_triggered: bool
    quarantine_scope: str
    safety_case_id: str | None
    safety_classification: str | None
    investigation_status: str
    quarantine_record_ids: list[str] = Field(default_factory=list)
    created_at: datetime
    created_by: str
    reason_for_change: str
    version_index: int


class DeviceQuarantineCreateRequest(BaseModel):
    """Request payload for manual or programmatic device quarantine."""

    study_id: str = Field(..., min_length=1)
    site_id: str = Field(..., min_length=1)
    scope: QuarantineScope = QuarantineScope.DEVICE_SERIAL
    device_inventory_id: str | None = None
    lot_number: str | None = None
    serial_number: str | None = None
    reason: str = Field(..., min_length=5)
    reason_for_change: str = Field(
        default="Initiate medical device quarantine", min_length=5
    )


class DeviceQuarantineReleaseRequest(BaseModel):
    """Request payload for releasing a device from quarantine under Part 11 signature."""

    quarantine_id: str = Field(..., min_length=1)
    release_justification: str = Field(..., min_length=10)
    signature_token: str = Field(..., min_length=10)
    reason_for_change: str = Field(
        default="Authorized medical device quarantine release", min_length=5
    )


class DeviceQuarantineResponse(BaseModel):
    """Response model for quarantine records."""

    id: str
    study_id: str
    site_id: str
    deficiency_id: str | None
    device_inventory_id: str | None
    lot_number: str
    serial_number: str | None
    scope: str
    status: str
    reason: str
    quarantined_at: datetime
    quarantined_by: str
    released_at: datetime | None
    released_by: str | None
    release_justification: str | None
    impacted_device_count: int = 1
    created_at: datetime
    created_by: str
    reason_for_change: str
    version_index: int


class DeviceInvestigationSummary(BaseModel):
    """Dashboard metrics and counts for device investigations."""

    study_id: str
    total_devices: int = 0
    available_devices: int = 0
    dispensed_devices: int = 0
    quarantined_devices: int = 0
    total_deficiencies: int = 0
    open_safety_cases: int = 0
    sade_count: int = 0
    active_quarantines: int = 0


# =========================================================================
# GUDID (Global Unique Device Identification Database) Models
# =========================================================================


class GUDIDDeviceRecord(BaseModel):
    """FDA AccessGUDID reference record attributes."""

    primary_di: str
    brand_name: str
    version_model_number: str
    device_description: str
    commercial_distribution_status: str = "In Commercial Distribution"
    device_count: int = 1
    fda_listing_number: str | None = None
    premarket_submission_number: str | None = None  # 510(k), PMA, De Novo
    gmdn_code: str | None = None
    gmdn_name: str | None = None
    device_class: str = "Class II"
    is_single_use: bool = True
    is_prescription_only: bool = True
    is_otc: bool = False
    is_sterile: bool = True
    sterilization_methods: list[str] = Field(default_factory=lambda: ["Ethylene Oxide"])
    mri_safety_status: str = "MR Conditional"  # MR Safe, MR Conditional, MR Unsafe
    has_lot_number: bool = True
    has_serial_number: bool = True
    has_expiration_date: bool = True
    has_donation_id: bool = False


class GUDIDLookupRequest(BaseModel):
    """Request payload for looking up a device in the GUDID database."""

    udi_di: str = Field(
        ..., min_length=3, description="Device Identifier (GTIN / HIBCC DI)"
    )


class GUDIDVerificationResult(BaseModel):
    """Result of validating physical/scanned UDI data against the GUDID database."""

    udi_di: str
    is_in_gudid: bool
    is_checksum_valid: bool
    is_verified: bool
    gudid_record: GUDIDDeviceRecord | None = None
    pi_validation: dict[str, bool] = Field(default_factory=dict)
    discrepancies: list[str] = Field(default_factory=list)
    verification_timestamp: str = Field(
        default_factory=lambda: datetime.now(UTC).isoformat()
    )


# =========================================================================
# IVD Specimen Models & Chain-of-Custody DTOs
# =========================================================================


class IVDSpecimenType(StrEnum):
    """Specimen biological matrix types for IVD investigations."""

    WHOLE_BLOOD = "WHOLE_BLOOD"
    SERUM = "SERUM"
    PLASMA = "PLASMA"
    URINE = "URINE"
    SALIVA = "SALIVA"
    CSF = "CSF"
    TISSUE_BIOPSY = "TISSUE_BIOPSY"
    NASOPHARYNGEAL_SWAB = "NASOPHARYNGEAL_SWAB"
    CELL_CULTURE = "CELL_CULTURE"


class IVDContainerType(StrEnum):
    """Specimen collection and transport container formats."""

    EDTA_TUBE = "EDTA_TUBE"
    SST_TUBE = "SST_TUBE"
    CRYOBIO_VIAL = "CRYOBIO_VIAL"
    HEPARIN_TUBE = "HEPARIN_TUBE"
    STERILE_CUP = "STERILE_CUP"
    SWAB_TRANSPORT_MEDIA = "SWAB_TRANSPORT_MEDIA"


class IVDSpecimenStatus(StrEnum):
    """Lifecycle tracking status for an IVD specimen."""

    COLLECTED = "COLLECTED"
    IN_TRANSIT = "IN_TRANSIT"
    ACCESSIONED = "ACCESSIONED"
    PROCESSING = "PROCESSING"
    ANALYZED = "ANALYZED"
    BIOBANKED = "BIOBANKED"
    QUARANTINED = "QUARANTINED"
    REJECTED = "REJECTED"
    DISPOSED = "DISPOSED"


class IVDIntegrityStatus(StrEnum):
    """Integrity assessment outcome for an IVD specimen."""

    VALID = "VALID"
    COMPROMISED = "COMPROMISED"
    REJECTED = "REJECTED"
    FLAGGED_FOR_REVIEW = "FLAGGED_FOR_REVIEW"


class IVDSpecimenCreateRequest(BaseModel):
    """Request payload for logging an IVD specimen collection."""

    study_id: str = Field(..., min_length=1)
    site_id: str = Field(..., min_length=1)
    subject_id: str = Field(..., min_length=1)
    specimen_identifier: str | None = None
    specimen_type: IVDSpecimenType = IVDSpecimenType.SERUM
    collection_date: datetime = Field(default_factory=lambda: datetime.now(UTC))
    collector_user_id: str | None = None
    container_type: IVDContainerType = IVDContainerType.SST_TUBE
    initial_volume_ul: float = Field(default=1000.0, gt=0)
    storage_temperature_celsius: float = Field(default=-80.0)
    storage_location: str = Field(default="FREEZER_ULT_01_RACK_A")
    max_allowed_freeze_thaw: int = Field(default=3, ge=0, le=10)
    reason_for_change: str = Field(
        default="Log IVD biospecimen collection", min_length=5
    )


class IVDSpecimenCustodyEventRequest(BaseModel):
    """Request payload for logging a chain-of-custody transfer event."""

    from_user_id: str = Field(..., min_length=1)
    to_user_id: str = Field(..., min_length=1)
    from_location: str = Field(..., min_length=1)
    to_location: str = Field(..., min_length=1)
    transport_condition: str = Field(default="DRY_ICE_-80C")
    temperature_at_transfer_celsius: float | None = None
    seal_intact: bool = True
    signature_token: str | None = None
    reason_for_change: str = Field(
        default="IVD Specimen custody transfer", min_length=5
    )


class IVDSpecimenCustodyEventResponse(BaseModel):
    """Response model for a chain-of-custody transfer event."""

    id: str
    specimen_identifier: str
    transfer_timestamp: datetime
    from_user_id: str
    to_user_id: str
    from_location: str
    to_location: str
    transport_condition: str
    temperature_at_transfer_celsius: float | None
    seal_intact: bool
    signature_token: str | None


class IVDSpecimenFreezeThawEventRequest(BaseModel):
    """Request payload for logging a freeze-thaw thermal cycle."""

    thaw_start_timestamp: datetime = Field(default_factory=lambda: datetime.now(UTC))
    thaw_end_timestamp: datetime | None = None
    thaw_duration_minutes: float = Field(default=15.0, ge=0)
    thaw_temperature_celsius: float = Field(default=22.0)
    operator_user_id: str = Field(..., min_length=1)
    purpose: str = Field(..., min_length=3)
    is_excursion: bool = False
    reason_for_change: str = Field(
        default="Record IVD specimen freeze-thaw cycle", min_length=5
    )


class IVDSpecimenFreezeThawEventResponse(BaseModel):
    """Response model for a freeze-thaw event."""

    id: str
    specimen_identifier: str
    cycle_number: int
    thaw_start_timestamp: datetime
    thaw_end_timestamp: datetime | None
    thaw_duration_minutes: float
    thaw_temperature_celsius: float
    operator_user_id: str
    purpose: str
    is_excursion: bool


class IVDSampleIntegrityCheckRequest(BaseModel):
    """Request payload for validating IVD sample integrity (HIL indices & volume)."""

    hemolysis_index_mg_dl: float = Field(default=0.0, ge=0)
    lipemia_index_mg_dl: float = Field(default=0.0, ge=0)
    icterus_index_mg_dl: float = Field(default=0.0, ge=0)
    clot_detected: bool = False
    temperature_excursion: bool = False
    volume_adequate: bool = True
    integrity_notes: str | None = None
    reason_for_change: str = Field(
        default="Assess IVD specimen sample integrity", min_length=5
    )


class IVDSampleIntegrityCheckResponse(BaseModel):
    """Response model for sample integrity assessment."""

    specimen_identifier: str
    integrity_status: str
    hemolysis_index_mg_dl: float
    lipemia_index_mg_dl: float
    icterus_index_mg_dl: float
    clot_detected: bool
    temperature_excursion: bool
    is_quarantined: bool
    quarantine_reason: str | None
    validated_at: datetime
    validated_by: str


class IVDSpecimenResponse(BaseModel):
    """Detailed response model for an IVD biospecimen record."""

    id: str
    study_id: str
    site_id: str
    subject_id: str
    specimen_identifier: str
    specimen_type: str
    collection_date: datetime
    collector_user_id: str
    container_type: str
    initial_volume_ul: float
    current_volume_ul: float
    storage_temperature_celsius: float
    storage_location: str
    status: str
    is_quarantined: bool
    quarantine_reason: str | None
    freeze_thaw_count: int
    max_allowed_freeze_thaw: int
    hemolysis_index_mg_dl: float
    lipemia_index_mg_dl: float
    icterus_index_mg_dl: float
    clot_detected: bool
    temperature_excursion: bool
    integrity_status: str
    integrity_validated_at: datetime | None
    integrity_validated_by: str | None
    custody_events: list[IVDSpecimenCustodyEventResponse] = Field(default_factory=list)
    freeze_thaw_events: list[IVDSpecimenFreezeThawEventResponse] = Field(
        default_factory=list
    )
    created_at: datetime
    created_by: str
    reason_for_change: str
    version_index: int


class IVDAssayPerformanceRequest(BaseModel):
    """Request payload for computing and recording IVD assay analytical and clinical performance."""

    study_id: str = Field(..., min_length=1)
    assay_name: str = Field(..., min_length=1)
    assay_lot_number: str = Field(..., min_length=1)
    device_udi_di: str | None = None
    true_positives: int = Field(..., ge=0)
    false_positives: int = Field(..., ge=0)
    true_negatives: int = Field(..., ge=0)
    false_negatives: int = Field(..., ge=0)
    replicate_measurements: list[float] = Field(default_factory=list)
    inter_assay_runs: list[list[float]] = Field(default_factory=list)
    blank_measurements: list[float] = Field(default_factory=list)
    low_standard_measurements: list[float] = Field(default_factory=list)
    linearity_concentrations: list[float] = Field(default_factory=list)
    linearity_signals: list[float] = Field(default_factory=list)
    reason_for_change: str = Field(
        default="Compute and validate IVD assay performance metrics", min_length=5
    )


class IVDAssayPerformanceResponse(BaseModel):
    """Response model with computed IVD assay performance metrics."""

    id: str
    study_id: str
    assay_name: str
    assay_lot_number: str
    device_udi_di: str | None
    true_positives: int
    false_positives: int
    true_negatives: int
    false_negatives: int
    sensitivity_percent: float
    specificity_percent: float
    ppv_percent: float
    npv_percent: float
    accuracy_percent: float
    cohens_kappa: float
    intra_assay_cv_percent: float
    inter_assay_cv_percent: float
    limit_of_detection: float | None
    limit_of_quantitation: float | None
    linearity_r_squared: float | None
    status: str
    passes_acceptance_criteria: bool
    created_at: datetime
    created_by: str
