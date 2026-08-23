"""Medical Device and In-Vitro Diagnostic (IVD) Database Models.

Requirements: PRD-SYS-126, PRD-DEV-001, PRD-DEV-002, PRD-DEV-003, PRD-DEV-004,
ISO 14155:2020, EU MDR 2017/745, EU IVDR 2017/746, 21 CFR Part 11, ADR-2209.
"""

from datetime import datetime

from sqlalchemy import (
    Boolean,
    DateTime,
    Index,
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
