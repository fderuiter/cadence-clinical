"""Medical Device and In-Vitro Diagnostic (IVD) Database Models.

Requirements: PRD-SYS-126, PRD-DEV-001, PRD-DEV-002, PRD-DEV-003, PRD-DEV-004,
ISO 14155:2020, EU MDR 2017/745, EU IVDR 2017/746, 21 CFR Part 11, ADR-2209.
"""

from datetime import datetime

from sqlalchemy import (
    Boolean,
    DateTime,
    Float,
    Index,
    Integer,
    String,
    func,
)
from sqlalchemy.orm import Mapped, mapped_column

from .audit import AuditedModel


class InvestigationalDeviceInventory(AuditedModel):
    """Represents serialized investigational medical device and IVD inventory in RTSM.

    Tracks device accountability, Unique Device Identifier (UDI: DI + PI),
    and active quarantine/dispensation block status.
    """

    __tablename__ = "investigational_device_inventories"
    __table_args__ = (
        Index("idx_device_inv_study_site", "study_id", "site_id"),
        Index("idx_device_inv_lot_serial", "lot_number", "serial_number"),
    )

    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    device_identifier: Mapped[str] = mapped_column(
        String(255), nullable=False, index=True
    )
    brand_name: Mapped[str] = mapped_column(String(255), nullable=False)
    device_model: Mapped[str | None] = mapped_column(String(255), nullable=True)
    device_type: Mapped[str] = mapped_column(
        String(100), default="THERAPEUTIC", nullable=False
    )
    device_classification: Mapped[str] = mapped_column(
        String(50), default="CLASS_IIA", nullable=False
    )
    udi_di: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    udi_pi: Mapped[str | None] = mapped_column(String(512), nullable=True)
    udi_carrier_format: Mapped[str] = mapped_column(
        String(50), default="GS1_128", nullable=False
    )
    lot_number: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    serial_number: Mapped[str | None] = mapped_column(
        String(255), nullable=True, index=True
    )
    expiration_date: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    manufacturing_date: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    software_version: Mapped[str | None] = mapped_column(String(100), nullable=True)
    status: Mapped[str] = mapped_column(
        String(50), default="AVAILABLE", nullable=False, index=True
    )
    is_dispensation_blocked: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )
    allocated_subject_id: Mapped[str | None] = mapped_column(
        String(255), nullable=True, index=True
    )
    dispensed_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    dispensed_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    quarantined_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    quarantined_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    quarantine_reason: Mapped[str | None] = mapped_column(String(1000), nullable=True)


class DeviceDeficiencyReport(AuditedModel):
    """Represents a structured eCRF log for medical device deficiencies and malfunctions.

    Captures malfunctions, usability issues, and inadequate labeling with automated
    RTSM quarantine and Safety Adverse Device Effect (ADE / SADE / USADE) linkages.
    """

    __tablename__ = "device_deficiency_reports"
    __table_args__ = (
        Index("idx_device_defic_study_site", "study_id", "site_id"),
        Index("idx_device_defic_udi_lot", "udi_di", "lot_number"),
    )

    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    subject_id: Mapped[str | None] = mapped_column(
        String(255), nullable=True, index=True
    )
    device_inventory_id: Mapped[str | None] = mapped_column(
        String(36), nullable=True, index=True
    )
    udi_di: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    lot_number: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    serial_number: Mapped[str | None] = mapped_column(
        String(255), nullable=True, index=True
    )
    device_name: Mapped[str] = mapped_column(String(255), nullable=False)
    deficiency_type: Mapped[str] = mapped_column(
        String(100), nullable=False, index=True
    )
    deficiency_description: Mapped[str] = mapped_column(String(4000), nullable=False)
    incident_date: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    awareness_date: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    could_lead_to_sade: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )
    patient_involved: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )
    health_impact: Mapped[str] = mapped_column(
        String(100), default="NONE", nullable=False, index=True
    )
    action_taken_device: Mapped[str] = mapped_column(
        String(100), default="QUARANTINED_AT_SITE", nullable=False
    )
    device_returned_to_sponsor: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )
    return_tracking_number: Mapped[str | None] = mapped_column(
        String(255), nullable=True
    )
    root_cause_analysis: Mapped[str | None] = mapped_column(String(4000), nullable=True)
    sponsor_evaluation: Mapped[str | None] = mapped_column(String(4000), nullable=True)
    quarantine_triggered: Mapped[bool] = mapped_column(
        Boolean, default=True, nullable=False
    )
    quarantine_scope: Mapped[str] = mapped_column(
        String(50), default="DEVICE_SERIAL", nullable=False
    )
    safety_case_id: Mapped[str | None] = mapped_column(
        String(255), nullable=True, index=True
    )
    safety_classification: Mapped[str | None] = mapped_column(String(50), nullable=True)
    investigation_status: Mapped[str] = mapped_column(
        String(50), default="OPEN", nullable=False, index=True
    )
    esignature_token: Mapped[str | None] = mapped_column(String(512), nullable=True)
    signed_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    signed_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)


class DeviceQuarantineRecord(AuditedModel):
    """Audit-tracked device inventory quarantine record with Part 11 release controls."""

    __tablename__ = "device_quarantine_records"
    __table_args__ = (
        Index("idx_device_quar_study_lot", "study_id", "lot_number"),
        Index("idx_device_quar_status", "status"),
    )

    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    deficiency_id: Mapped[str | None] = mapped_column(
        String(36), nullable=True, index=True
    )
    device_inventory_id: Mapped[str | None] = mapped_column(
        String(36), nullable=True, index=True
    )
    lot_number: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    serial_number: Mapped[str | None] = mapped_column(
        String(255), nullable=True, index=True
    )
    scope: Mapped[str] = mapped_column(
        String(50), default="DEVICE_SERIAL", nullable=False, index=True
    )
    status: Mapped[str] = mapped_column(
        String(50), default="ACTIVE", nullable=False, index=True
    )
    reason: Mapped[str] = mapped_column(String(2000), nullable=False)
    quarantined_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    quarantined_by: Mapped[str] = mapped_column(String(255), nullable=False)
    released_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    released_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    release_justification: Mapped[str | None] = mapped_column(
        String(2000), nullable=True
    )
    release_esignature_token: Mapped[str | None] = mapped_column(
        String(512), nullable=True
    )


class IVDSpecimenRecord(AuditedModel):
    """Represents an In-Vitro Diagnostic (IVD) biospecimen with chain-of-custody,

    freeze-thaw history, and sample integrity tracking.
    """

    __tablename__ = "ivd_specimen_records"
    __table_args__ = (
        Index("idx_ivd_specimen_study_site", "study_id", "site_id"),
        Index("idx_ivd_specimen_subject", "subject_id"),
    )

    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    subject_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    specimen_identifier: Mapped[str] = mapped_column(
        String(255), nullable=False, unique=True, index=True
    )
    specimen_type: Mapped[str] = mapped_column(String(100), nullable=False, index=True)
    collection_date: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    collector_user_id: Mapped[str] = mapped_column(String(255), nullable=False)
    container_type: Mapped[str] = mapped_column(String(100), nullable=False)
    initial_volume_ul: Mapped[float] = mapped_column(
        Float, nullable=False, default=1000.0
    )
    current_volume_ul: Mapped[float] = mapped_column(
        Float, nullable=False, default=1000.0
    )
    storage_temperature_celsius: Mapped[float] = mapped_column(
        Float, nullable=False, default=-80.0
    )
    storage_location: Mapped[str] = mapped_column(
        String(255), nullable=False, default="FREEZER_ULT_01"
    )
    status: Mapped[str] = mapped_column(
        String(50), default="COLLECTED", nullable=False, index=True
    )
    is_quarantined: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    quarantine_reason: Mapped[str | None] = mapped_column(String(1000), nullable=True)

    # Freeze-Thaw Controls
    freeze_thaw_count: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    max_allowed_freeze_thaw: Mapped[int] = mapped_column(
        Integer, default=3, nullable=False
    )

    # Sample Integrity Attributes (Hemolysis, Lipemia, Icterus)
    hemolysis_index_mg_dl: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    lipemia_index_mg_dl: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    icterus_index_mg_dl: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    clot_detected: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    temperature_excursion: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )
    integrity_status: Mapped[str] = mapped_column(
        String(50), default="VALID", nullable=False, index=True
    )
    integrity_validated_at: Mapped[datetime | None] = mapped_column(
        DateTime, nullable=True
    )
    integrity_validated_by: Mapped[str | None] = mapped_column(
        String(255), nullable=True
    )
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str | None] = mapped_column(String(255), nullable=True)


class IVDSpecimenFreezeThawEvent(AuditedModel):
    """Represents a discrete freeze-thaw thermal cycle log for an IVD specimen."""

    __tablename__ = "ivd_specimen_freeze_thaw_events"
    __table_args__ = (Index("idx_ivd_ft_specimen", "specimen_identifier"),)

    specimen_identifier: Mapped[str] = mapped_column(
        String(255), nullable=False, index=True
    )
    cycle_number: Mapped[int] = mapped_column(Integer, nullable=False)
    thaw_start_timestamp: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    thaw_end_timestamp: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    thaw_duration_minutes: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    thaw_temperature_celsius: Mapped[float] = mapped_column(
        Float, default=22.0, nullable=False
    )
    operator_user_id: Mapped[str] = mapped_column(String(255), nullable=False)
    purpose: Mapped[str] = mapped_column(String(500), nullable=False)
    is_excursion: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str | None] = mapped_column(String(255), nullable=True)


class IVDSpecimenCustodyEvent(AuditedModel):
    """Represents an auditable chain-of-custody transfer log for an IVD specimen."""

    __tablename__ = "ivd_specimen_custody_events"
    __table_args__ = (Index("idx_ivd_custody_specimen", "specimen_identifier"),)

    specimen_identifier: Mapped[str] = mapped_column(
        String(255), nullable=False, index=True
    )
    transfer_timestamp: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    from_user_id: Mapped[str] = mapped_column(String(255), nullable=False)
    to_user_id: Mapped[str] = mapped_column(String(255), nullable=False)
    from_location: Mapped[str] = mapped_column(String(255), nullable=False)
    to_location: Mapped[str] = mapped_column(String(255), nullable=False)
    transport_condition: Mapped[str] = mapped_column(
        String(100), default="DRY_ICE_-80C", nullable=False
    )
    temperature_at_transfer_celsius: Mapped[float | None] = mapped_column(
        Float, nullable=True
    )
    seal_intact: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)
    signature_token: Mapped[str | None] = mapped_column(String(512), nullable=True)
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str | None] = mapped_column(String(255), nullable=True)


class IVDAssayPerformanceRun(AuditedModel):
    """Represents statistical assay validation metrics for an IVD diagnostic device/assay."""

    __tablename__ = "ivd_assay_performance_runs"
    __table_args__ = (Index("idx_ivd_assay_study", "study_id", "assay_name"),)

    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    assay_name: Mapped[str] = mapped_column(String(255), nullable=False)
    assay_lot_number: Mapped[str] = mapped_column(String(255), nullable=False)
    device_udi_di: Mapped[str | None] = mapped_column(String(255), nullable=True)
    true_positives: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    false_positives: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    true_negatives: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    false_negatives: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    sensitivity_percent: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    specificity_percent: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    ppv_percent: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    npv_percent: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    accuracy_percent: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    intra_assay_cv_percent: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    inter_assay_cv_percent: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    limit_of_detection: Mapped[float | None] = mapped_column(Float, nullable=True)
    limit_of_quantitation: Mapped[float | None] = mapped_column(Float, nullable=True)
    linearity_r_squared: Mapped[float | None] = mapped_column(Float, nullable=True)
    status: Mapped[str] = mapped_column(String(50), default="PASSED", nullable=False)
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
