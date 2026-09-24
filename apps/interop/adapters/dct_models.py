"""SQLAlchemy ORM models for DCT Telehealth Video, Pediatric Assent, DTP Logistics, and Home Health.

Requirements:
- PRD-DCT-003: Direct-to-Patient (DTP) Investigational Product Dispatch Tracking
- PRD-DCT-004: Decentralized In-Home Healthcare Nurse Visit Coordination
- PRD-DCT-005: GxP-Compliant Telehealth Video Consultation & Encrypted Recording
- PRD-DCT-006: Tiered Pediatric Assent & Legally Authorized Representative (LAR) Consent
- ADR-2199 / ADR-2207, 21 CFR Part 11
"""

from __future__ import annotations

import uuid
from datetime import datetime
from typing import Any

from sqlalchemy import JSON, Boolean, DateTime, Float, Integer, String, func
from sqlalchemy.orm import Mapped, mapped_column

from apps.interop.adapters.models import Base


class TelehealthSessionRecord(Base):
    """Persisted WebRTC telehealth video session and room metadata."""

    __tablename__ = "telehealth_sessions"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    room_id: Mapped[str] = mapped_column(
        String(100), nullable=False, unique=True, index=True
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    subject_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    title: Mapped[str] = mapped_column(
        String(255), default="Telehealth Clinical Consultation", nullable=False
    )
    scheduled_start_utc: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    scheduled_end_utc: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    status: Mapped[str] = mapped_column(
        String(50), default="SCHEDULED", nullable=False, index=True
    )
    room_secret: Mapped[str] = mapped_column(String(255), nullable=False)
    co_view_state: Mapped[dict[str, Any]] = mapped_column(
        JSON, default=dict, nullable=False
    )
    signaling_messages: Mapped[list[dict[str, Any]]] = mapped_column(
        JSON, default=list, nullable=False
    )
    participant_records: Mapped[list[dict[str, Any]]] = mapped_column(
        JSON, default=list, nullable=False
    )
    recording_seal_metadata: Mapped[dict[str, Any] | None] = mapped_column(
        JSON, nullable=True
    )

    # GxP 21 CFR Part 11 Audit Fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(
        String(255), default="system", nullable=False
    )
    reason_for_change: Mapped[str] = mapped_column(
        String(1000), default="Initial telehealth session scheduling", nullable=False
    )
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class PediatricAssentRecord(Base):
    """Persisted tiered pediatric assent and LAR consent manifest."""

    __tablename__ = "pediatric_assents"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    assent_id: Mapped[str] = mapped_column(
        String(100), nullable=False, unique=True, index=True
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    subject_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    tier: Mapped[str] = mapped_column(String(50), nullable=False, index=True)
    minor_age: Mapped[int] = mapped_column(Integer, nullable=False)
    comprehension_score_percent: Mapped[float] = mapped_column(
        Float, default=100.0, nullable=False
    )
    comprehension_passed: Mapped[bool] = mapped_column(
        Boolean, default=True, nullable=False
    )
    assent_signature: Mapped[str | None] = mapped_column(String(255), nullable=True)
    assent_signed_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    lar_name: Mapped[str | None] = mapped_column(String(255), nullable=True)
    lar_relationship: Mapped[str | None] = mapped_column(String(100), nullable=True)
    lar_legal_basis: Mapped[str | None] = mapped_column(String(255), nullable=True)
    lar_signature: Mapped[str | None] = mapped_column(String(255), nullable=True)
    lar_signed_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    investigator_name: Mapped[str | None] = mapped_column(String(255), nullable=True)
    investigator_signature: Mapped[str | None] = mapped_column(
        String(255), nullable=True
    )
    investigator_signed_at: Mapped[datetime | None] = mapped_column(
        DateTime, nullable=True
    )
    cryptographic_bundle_hash: Mapped[str] = mapped_column(
        String(64), nullable=False, index=True
    )

    # GxP 21 CFR Part 11 Audit Fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(
        String(255), default="system", nullable=False
    )
    reason_for_change: Mapped[str] = mapped_column(
        String(1000), default="Pediatric assent execution", nullable=False
    )
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class DTPShipmentRecord(Base):
    """Persisted Direct-to-Patient drug shipment and cold-chain temperature telemetry."""

    __tablename__ = "dtp_shipments"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    shipment_id: Mapped[str] = mapped_column(
        String(100), nullable=False, unique=True, index=True
    )
    tracking_number: Mapped[str] = mapped_column(
        String(100), nullable=False, unique=True, index=True
    )
    courier_name: Mapped[str] = mapped_column(String(100), nullable=False)
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    subject_pseudonym_id: Mapped[str] = mapped_column(
        String(255), nullable=False, index=True
    )
    destination_address_pseudonym: Mapped[str] = mapped_column(
        String(255), nullable=False
    )
    temperature_zone: Mapped[str] = mapped_column(
        String(50), nullable=False, index=True
    )
    kit_serials: Mapped[list[str]] = mapped_column(JSON, default=list, nullable=False)
    tamper_seal_id: Mapped[str] = mapped_column(String(100), nullable=False)
    status: Mapped[str] = mapped_column(
        String(50), default="ORDER_CREATED", nullable=False, index=True
    )
    current_temp_celsius: Mapped[float] = mapped_column(
        Float, default=4.0, nullable=False
    )
    min_temp_celsius: Mapped[float] = mapped_column(Float, default=4.0, nullable=False)
    max_temp_celsius: Mapped[float] = mapped_column(Float, default=4.0, nullable=False)
    mkt_celsius: Mapped[float] = mapped_column(Float, default=4.0, nullable=False)
    excursion_severity: Mapped[str] = mapped_column(
        String(50), default="NONE", nullable=False
    )
    custody_handoffs: Mapped[list[dict[str, Any]]] = mapped_column(
        JSON, default=list, nullable=False
    )
    temperature_readings: Mapped[list[dict[str, Any]]] = mapped_column(
        JSON, default=list, nullable=False
    )
    delivery_signature: Mapped[dict[str, Any] | None] = mapped_column(
        JSON, nullable=True
    )

    # GxP 21 CFR Part 11 Audit Fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(
        String(255), default="system", nullable=False
    )
    reason_for_change: Mapped[str] = mapped_column(
        String(1000), default="DTP shipment dispatch", nullable=False
    )
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class InHomeVisitRecord(Base):
    """Persisted in-home nurse clinical visits and mobile eCRF data entry records."""

    __tablename__ = "in_home_visits"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    visit_id: Mapped[str] = mapped_column(
        String(100), nullable=False, unique=True, index=True
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    subject_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    visit_number: Mapped[str] = mapped_column(String(50), nullable=False)
    visit_name: Mapped[str] = mapped_column(String(255), nullable=False)
    nurse_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    nurse_name: Mapped[str] = mapped_column(String(255), nullable=False)
    nurse_license_number: Mapped[str] = mapped_column(String(100), nullable=False)
    scheduled_start_utc: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    status: Mapped[str] = mapped_column(
        String(50), default="SCHEDULED", nullable=False, index=True
    )
    procedures: Mapped[list[dict[str, Any]]] = mapped_column(
        JSON, default=list, nullable=False
    )
    mobile_ecrf_data: Mapped[dict[str, Any]] = mapped_column(
        JSON, default=dict, nullable=False
    )
    offline_sync_id: Mapped[str | None] = mapped_column(String(100), nullable=True)
    sync_status: Mapped[str] = mapped_column(
        String(50), default="SYNCHRONIZED", nullable=False
    )

    # GxP 21 CFR Part 11 Audit Fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(
        String(255), default="system", nullable=False
    )
    reason_for_change: Mapped[str] = mapped_column(
        String(1000), default="In-home visit scheduling", nullable=False
    )
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class DCTAuditLogRecord(Base):
    """Immutable, chronological 21 CFR Part 11 GxP audit trail for all DCT actions."""

    __tablename__ = "dct_audit_logs"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    action: Mapped[str] = mapped_column(String(100), nullable=False, index=True)
    entity_type: Mapped[str] = mapped_column(String(100), nullable=False, index=True)
    entity_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    user_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    user_role: Mapped[str] = mapped_column(String(100), nullable=False)
    timestamp: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    change_reason: Mapped[str] = mapped_column(String(1000), nullable=False)
    details: Mapped[dict[str, Any]] = mapped_column(JSON, default=dict, nullable=False)
