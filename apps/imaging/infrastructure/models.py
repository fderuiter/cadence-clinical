"""SQLAlchemy ORM models for Medical Imaging microservice envelopes and manifests.

Requirements: ADR-2199, PRD-IMG-001, PRD-IMG-002
"""

from __future__ import annotations

import uuid
from datetime import datetime
from typing import Any

from sqlalchemy import JSON, Boolean, Float, ForeignKey, Integer, String, func
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column, relationship

from packages.database.datetime_helpers import UTCDateTime


class Base(DeclarativeBase):
    """Declarative Base for Imaging microservice tables."""

    pass


class ImagingStudyEnvelopeModel(Base):
    """Database table representing de-identified study-level metadata envelopes."""

    __tablename__ = "imaging_study_envelopes"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_instance_uid: Mapped[str] = mapped_column(
        String(64), nullable=False, unique=True, index=True
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    subject_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    modalities: Mapped[list[str]] = mapped_column(JSON, default=list, nullable=False)
    study_date: Mapped[str | None] = mapped_column(String(8), nullable=True)
    study_description: Mapped[str | None] = mapped_column(String(500), nullable=True)
    series_count: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    instance_count: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    storage_prefix: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    status: Mapped[str] = mapped_column(
        String(50), default="STORED", nullable=False, index=True
    )
    adjudication_status: Mapped[str] = mapped_column(
        String(50), default="UNASSIGNED", nullable=False, index=True
    )
    tenant_id: Mapped[str] = mapped_column(
        String(255), default="tenant_default", nullable=False, index=True
    )

    # 21 CFR Part 11 GxP Audit Columns
    created_at: Mapped[datetime] = mapped_column(
        UTCDateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(
        String(255), default="system", nullable=False, index=True
    )
    updated_at: Mapped[datetime] = mapped_column(
        UTCDateTime, default=func.now(), onupdate=func.now(), nullable=False
    )
    reason_for_change: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # Relationships
    series: Mapped[list[ImagingSeriesEnvelopeModel]] = relationship(
        "ImagingSeriesEnvelopeModel",
        back_populates="study",
        cascade="all, delete-orphan",
        lazy="selectin",
    )
    certificates: Mapped[list[DeidentificationCertificateModel]] = relationship(
        "DeidentificationCertificateModel",
        back_populates="study",
        cascade="all, delete-orphan",
        lazy="selectin",
    )
    annotations: Mapped[list[ImagingAnnotationModel]] = relationship(
        "ImagingAnnotationModel",
        back_populates="study",
        cascade="all, delete-orphan",
        lazy="selectin",
    )


class ImagingSeriesEnvelopeModel(Base):
    """Database table representing de-identified series-level metadata envelopes."""

    __tablename__ = "imaging_series_envelopes"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_envelope_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("imaging_study_envelopes.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
    )
    series_instance_uid: Mapped[str] = mapped_column(
        String(64), nullable=False, index=True
    )
    series_number: Mapped[int | None] = mapped_column(Integer, nullable=True)
    modality: Mapped[str] = mapped_column(String(20), nullable=False, index=True)
    body_part_examined: Mapped[str | None] = mapped_column(String(100), nullable=True)
    series_description: Mapped[str | None] = mapped_column(String(500), nullable=True)
    instance_count: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    protocol_name: Mapped[str | None] = mapped_column(String(255), nullable=True)
    frame_of_reference_uid: Mapped[str | None] = mapped_column(
        String(64), nullable=True
    )

    # Relationships
    study: Mapped[ImagingStudyEnvelopeModel] = relationship(
        "ImagingStudyEnvelopeModel", back_populates="series"
    )
    instances: Mapped[list[ImagingInstanceEnvelopeModel]] = relationship(
        "ImagingInstanceEnvelopeModel",
        back_populates="series",
        cascade="all, delete-orphan",
        lazy="selectin",
    )
    annotations: Mapped[list[ImagingAnnotationModel]] = relationship(
        "ImagingAnnotationModel",
        back_populates="series",
        cascade="all, delete-orphan",
        lazy="selectin",
    )


class ImagingInstanceEnvelopeModel(Base):
    """Database table representing de-identified individual DICOM instance envelopes."""

    __tablename__ = "imaging_instance_envelopes"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    series_envelope_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("imaging_series_envelopes.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
    )
    sop_instance_uid: Mapped[str] = mapped_column(
        String(64), nullable=False, unique=True, index=True
    )
    sop_class_uid: Mapped[str] = mapped_column(String(64), nullable=False, index=True)
    instance_number: Mapped[int | None] = mapped_column(Integer, nullable=True)
    rows: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    columns: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    bits_allocated: Mapped[int] = mapped_column(Integer, default=16, nullable=False)
    pixel_spacing: Mapped[list[float] | None] = mapped_column(JSON, nullable=True)
    slice_thickness: Mapped[float | None] = mapped_column(Float, nullable=True)
    slice_location: Mapped[float | None] = mapped_column(Float, nullable=True)
    window_center: Mapped[float | None] = mapped_column(Float, nullable=True)
    window_width: Mapped[float | None] = mapped_column(Float, nullable=True)

    # Object storage mapping
    object_key: Mapped[str] = mapped_column(
        String(1000), nullable=False, unique=True, index=True
    )
    checksum_sha256: Mapped[str] = mapped_column(String(64), nullable=False, index=True)
    size_bytes: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    burned_in_phi_masked: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )
    certificate_id: Mapped[str | None] = mapped_column(
        String(36),
        ForeignKey("imaging_deid_certificates.id", ondelete="SET NULL"),
        nullable=True,
        index=True,
    )

    # Relationships
    series: Mapped[ImagingSeriesEnvelopeModel] = relationship(
        "ImagingSeriesEnvelopeModel", back_populates="instances"
    )
    certificate: Mapped[DeidentificationCertificateModel | None] = relationship(
        "DeidentificationCertificateModel", lazy="selectin"
    )


class DeidentificationCertificateModel(Base):
    """Database table representing immutable, signed de-identification certificates."""

    __tablename__ = "imaging_deid_certificates"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_envelope_id: Mapped[str | None] = mapped_column(
        String(36),
        ForeignKey("imaging_study_envelopes.id", ondelete="CASCADE"),
        nullable=True,
        index=True,
    )
    source_sha256: Mapped[str] = mapped_column(String(64), nullable=False, index=True)
    target_sha256: Mapped[str] = mapped_column(String(64), nullable=False, index=True)
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    subject_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    original_study_instance_uid_hash: Mapped[str] = mapped_column(
        String(64), nullable=False
    )
    original_sop_instance_uid_hash: Mapped[str] = mapped_column(
        String(64), nullable=False
    )
    anonymized_study_instance_uid: Mapped[str] = mapped_column(
        String(64), nullable=False, index=True
    )
    anonymized_sop_instance_uid: Mapped[str] = mapped_column(
        String(64), nullable=False, index=True
    )
    scrubbed_tags_count: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    scrubbed_tags: Mapped[list[str]] = mapped_column(JSON, default=list, nullable=False)
    pixel_mask_applied: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )
    pixel_masks: Mapped[list[dict[str, Any]]] = mapped_column(
        JSON, default=list, nullable=False
    )
    operator_identity: Mapped[str] = mapped_column(
        String(255), default="cadence.imaging.pipeline", nullable=False
    )
    timestamp: Mapped[str] = mapped_column(String(50), nullable=False)
    signature: Mapped[str] = mapped_column(String(255), nullable=False)

    created_at: Mapped[datetime] = mapped_column(
        UTCDateTime, default=func.now(), nullable=False
    )

    # Relationships
    study: Mapped[ImagingStudyEnvelopeModel | None] = relationship(
        "ImagingStudyEnvelopeModel", back_populates="certificates"
    )


class ImagingAuditLogModel(Base):
    """Database table recording 21 CFR Part 11 audit trails for imaging operations."""

    __tablename__ = "imaging_audit_logs"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_envelope_id: Mapped[str | None] = mapped_column(
        String(36), nullable=True, index=True
    )
    action: Mapped[str] = mapped_column(String(100), nullable=False, index=True)
    actor_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    details: Mapped[dict[str, Any] | None] = mapped_column(JSON, nullable=True)
    created_at: Mapped[datetime] = mapped_column(
        UTCDateTime, default=func.now(), nullable=False
    )


class ImagingAnnotationModel(Base):
    """Database table recording oncologic caliper annotations and ROI metrics."""

    __tablename__ = "imaging_caliper_annotations"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_envelope_id: Mapped[str | None] = mapped_column(
        String(36),
        ForeignKey("imaging_study_envelopes.id", ondelete="CASCADE"),
        nullable=True,
        index=True,
    )
    series_envelope_id: Mapped[str | None] = mapped_column(
        String(36),
        ForeignKey("imaging_series_envelopes.id", ondelete="CASCADE"),
        nullable=True,
        index=True,
    )
    sop_instance_uid: Mapped[str | None] = mapped_column(
        String(64), nullable=True, index=True
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    subject_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    timepoint_name: Mapped[str] = mapped_column(String(100), nullable=False, index=True)
    lesion_id: Mapped[str] = mapped_column(String(50), nullable=False, index=True)
    lesion_type: Mapped[str] = mapped_column(
        String(50), default="TARGET", nullable=False
    )
    organ_site: Mapped[str] = mapped_column(String(100), nullable=False)
    measurement_criteria: Mapped[str] = mapped_column(
        String(50), default="RECIST_1_1", nullable=False
    )
    measurement_type: Mapped[str] = mapped_column(
        String(50), default="1D_RECIST", nullable=False
    )
    longest_diameter_mm: Mapped[float] = mapped_column(Float, nullable=False)
    perpendicular_diameter_mm: Mapped[float | None] = mapped_column(
        Float, nullable=True
    )
    product_perpendicular_mm2: Mapped[float | None] = mapped_column(
        Float, nullable=True
    )
    volume_mm3: Mapped[float | None] = mapped_column(Float, nullable=True)
    slice_index: Mapped[int | None] = mapped_column(Integer, nullable=True)
    slice_location: Mapped[float | None] = mapped_column(Float, nullable=True)
    deauville_score: Mapped[int | None] = mapped_column(Integer, nullable=True)
    points: Mapped[list[dict[str, Any]]] = mapped_column(
        JSON, default=list, nullable=False
    )
    hounsfield_stats: Mapped[dict[str, Any] | None] = mapped_column(JSON, nullable=True)
    reader_id: Mapped[str] = mapped_column(
        String(255), default="system", nullable=False, index=True
    )
    notes: Mapped[str | None] = mapped_column(String(1000), nullable=True)

    # 21 CFR Part 11 Audit Columns
    created_at: Mapped[datetime] = mapped_column(
        UTCDateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(
        String(255), default="system", nullable=False
    )
    updated_at: Mapped[datetime] = mapped_column(
        UTCDateTime, default=func.now(), onupdate=func.now(), nullable=False
    )
    reason_for_change: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # Relationships
    study: Mapped[ImagingStudyEnvelopeModel] = relationship(
        "ImagingStudyEnvelopeModel", back_populates="annotations"
    )
    series: Mapped[ImagingSeriesEnvelopeModel] = relationship(
        "ImagingSeriesEnvelopeModel", back_populates="annotations"
    )


class TumorResponseAssessmentModel(Base):
    """Database table recording timepoint-level oncologic tumor response assessments."""

    __tablename__ = "imaging_tumor_response_assessments"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    subject_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    timepoint_name: Mapped[str] = mapped_column(String(100), nullable=False, index=True)
    assessment_criteria: Mapped[str] = mapped_column(
        String(50), default="RECIST_1_1", nullable=False
    )
    baseline_sld_mm: Mapped[float | None] = mapped_column(Float, nullable=True)
    nadir_sld_mm: Mapped[float | None] = mapped_column(Float, nullable=True)
    current_sld_mm: Mapped[float | None] = mapped_column(Float, nullable=True)
    pct_change_baseline: Mapped[float | None] = mapped_column(Float, nullable=True)
    pct_change_nadir: Mapped[float | None] = mapped_column(Float, nullable=True)
    absolute_change_nadir_mm: Mapped[float | None] = mapped_column(Float, nullable=True)
    baseline_spd_mm2: Mapped[float | None] = mapped_column(Float, nullable=True)
    nadir_spd_mm2: Mapped[float | None] = mapped_column(Float, nullable=True)
    current_spd_mm2: Mapped[float | None] = mapped_column(Float, nullable=True)
    pct_change_spd_baseline: Mapped[float | None] = mapped_column(Float, nullable=True)
    pct_change_spd_nadir: Mapped[float | None] = mapped_column(Float, nullable=True)
    target_response: Mapped[str] = mapped_column(
        String(50), default="SD", nullable=False
    )
    non_target_status: Mapped[str] = mapped_column(
        String(50), default="NON_CR_NON_PD", nullable=False
    )
    new_lesions_present: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )
    new_lesions_count: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    max_deauville_score: Mapped[int | None] = mapped_column(Integer, nullable=True)
    overall_response: Mapped[str] = mapped_column(
        String(50), nullable=False, index=True
    )
    is_locked: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    locked_at: Mapped[datetime | None] = mapped_column(UTCDateTime, nullable=True)
    locked_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    reader_id: Mapped[str] = mapped_column(
        String(255), default="system", nullable=False, index=True
    )
    manifest_sha256: Mapped[str] = mapped_column(String(64), default="", nullable=False)
    signature: Mapped[str] = mapped_column(String(255), default="", nullable=False)

    # 21 CFR Part 11 Audit Columns
    created_at: Mapped[datetime] = mapped_column(
        UTCDateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(
        String(255), default="system", nullable=False
    )
    updated_at: Mapped[datetime] = mapped_column(
        UTCDateTime, default=func.now(), onupdate=func.now(), nullable=False
    )
    reason_for_change: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)
