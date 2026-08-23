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
