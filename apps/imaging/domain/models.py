"""Domain models and value objects for Medical Imaging microservice.

Requirements: ADR-2199, PRD-IMG-001, PRD-IMG-002
"""

from datetime import datetime
from enum import StrEnum
from typing import Any

from pydantic import BaseModel, Field


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
