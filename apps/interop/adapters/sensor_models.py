"""SQLAlchemy ORM models for Continuous Biometric IoT Sensor Telemetry and Safety Alerts.

Maps:
- device_registrations: Approved medical IoT sensor catalog, MAC/UDI mapping & calibration
- sensor_telemetry_streams: Ingested biometric streams with OMH/FHIR representations & clock drift metrics
- sensor_daily_summaries: Calculated aggregate daily endpoints (Time-in-Range TIR, mean glucose, steps, FEV1)
- sensor_safety_alerts: Automated clinical threshold safety alerts with 21 CFR Part 11 electronic sign-off

Requirements: PRD-DCT-002, PRD-DCT-001, ADR-2199, 21 CFR Part 11
"""

from __future__ import annotations

import uuid
from datetime import datetime
from typing import Any

from sqlalchemy import JSON, Boolean, DateTime, Float, Integer, String, func
from sqlalchemy.orm import Mapped, mapped_column

from apps.interop.adapters.models import Base


class DeviceRegistrationRecord(Base):
    """Persisted approved medical IoT sensor device registry and subject mapping."""

    __tablename__ = "device_registrations"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    subject_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    device_type: Mapped[str] = mapped_column(String(50), nullable=False, index=True)
    vendor: Mapped[str] = mapped_column(String(100), nullable=False)
    model_name: Mapped[str] = mapped_column(String(100), nullable=False)
    udi_di: Mapped[str] = mapped_column(String(100), nullable=False, index=True)
    udi_pi_serial: Mapped[str | None] = mapped_column(String(100), nullable=True)
    udi_pi_lot: Mapped[str | None] = mapped_column(String(100), nullable=True)
    mac_address_masked: Mapped[str | None] = mapped_column(String(50), nullable=True)
    device_token: Mapped[str] = mapped_column(String(100), nullable=False, index=True)
    firmware_version: Mapped[str | None] = mapped_column(String(50), nullable=True)
    calibration_status: Mapped[str] = mapped_column(
        String(50), default="CALIBRATED", nullable=False
    )
    calibration_date: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    is_active: Mapped[bool] = mapped_column(
        Boolean, default=True, nullable=False, index=True
    )
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(
        String(255), default="system", nullable=False
    )
    reason_for_change: Mapped[str] = mapped_column(
        String(1000), default="Initial device registration", nullable=False
    )
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class SensorTelemetryStreamRecord(Base):
    """Persisted continuous time-series biometric telemetry stream batch."""

    __tablename__ = "sensor_telemetry_streams"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    subject_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    device_token: Mapped[str] = mapped_column(String(100), nullable=False, index=True)
    device_type: Mapped[str] = mapped_column(String(50), nullable=False, index=True)
    batch_id: Mapped[str] = mapped_column(String(100), nullable=False, index=True)
    received_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False, index=True
    )
    clock_drift_offset_ms: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    reading_count: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    start_timestamp: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    end_timestamp: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    omh_payload: Mapped[list[dict[str, Any]]] = mapped_column(JSON, nullable=False)
    fhir_observations: Mapped[list[dict[str, Any]]] = mapped_column(
        JSON, nullable=False
    )
    raw_data_hash: Mapped[str] = mapped_column(String(64), nullable=False, index=True)
    feed_lakehouse_record_ids: Mapped[list[str]] = mapped_column(
        JSON, default=list, nullable=False
    )
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )


class SensorDailySummaryRecord(Base):
    """Persisted daily summary endpoints calculated from continuous IoT sensors."""

    __tablename__ = "sensor_daily_summaries"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    subject_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    summary_date: Mapped[datetime] = mapped_column(DateTime, nullable=False, index=True)
    device_type: Mapped[str] = mapped_column(String(50), nullable=False, index=True)
    device_token: Mapped[str] = mapped_column(String(100), nullable=False, index=True)
    readings_count: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    active_wear_hours: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)

    # CGM daily metrics
    tir_percentage: Mapped[float | None] = mapped_column(Float, nullable=True)
    tbr_level1_percentage: Mapped[float | None] = mapped_column(Float, nullable=True)
    tbr_level2_percentage: Mapped[float | None] = mapped_column(Float, nullable=True)
    tar_level1_percentage: Mapped[float | None] = mapped_column(Float, nullable=True)
    tar_level2_percentage: Mapped[float | None] = mapped_column(Float, nullable=True)
    mean_glucose_mg_dl: Mapped[float | None] = mapped_column(Float, nullable=True)
    glucose_sd: Mapped[float | None] = mapped_column(Float, nullable=True)
    glucose_cv_percent: Mapped[float | None] = mapped_column(Float, nullable=True)
    glucose_management_indicator: Mapped[float | None] = mapped_column(
        Float, nullable=True
    )

    # Actigraphy daily metrics
    total_steps: Mapped[int | None] = mapped_column(Integer, nullable=True)
    mean_cadence_spm: Mapped[float | None] = mapped_column(Float, nullable=True)
    peak_cadence_spm: Mapped[float | None] = mapped_column(Float, nullable=True)
    active_minutes: Mapped[float | None] = mapped_column(Float, nullable=True)
    sedentary_minutes: Mapped[float | None] = mapped_column(Float, nullable=True)
    sleep_duration_minutes: Mapped[float | None] = mapped_column(Float, nullable=True)
    sleep_efficiency_percent: Mapped[float | None] = mapped_column(Float, nullable=True)
    resting_heart_rate_bpm: Mapped[float | None] = mapped_column(Float, nullable=True)

    # Spirometry daily metrics
    best_fev1_liters: Mapped[float | None] = mapped_column(Float, nullable=True)
    best_fvc_liters: Mapped[float | None] = mapped_column(Float, nullable=True)
    fev1_fvc_ratio: Mapped[float | None] = mapped_column(Float, nullable=True)
    fev1_percent_predicted: Mapped[float | None] = mapped_column(Float, nullable=True)
    peak_expiratory_flow_l_min: Mapped[float | None] = mapped_column(
        Float, nullable=True
    )

    # Blood Pressure daily metrics
    mean_systolic_mmhg: Mapped[float | None] = mapped_column(Float, nullable=True)
    mean_diastolic_mmhg: Mapped[float | None] = mapped_column(Float, nullable=True)
    mean_arterial_pressure_mmhg: Mapped[float | None] = mapped_column(
        Float, nullable=True
    )

    # Provenance & Lakehouse link
    summary_manifest: Mapped[dict[str, Any]] = mapped_column(
        JSON, default=dict, nullable=False
    )
    lakehouse_record_id: Mapped[str | None] = mapped_column(
        String(255), nullable=True, index=True
    )
    sha256_hash: Mapped[str] = mapped_column(String(64), nullable=False, index=True)
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )


class SensorSafetyAlertRecord(Base):
    """Persisted clinical safety threshold alert with 21 CFR Part 11 electronic sign-off."""

    __tablename__ = "sensor_safety_alerts"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    alert_id: Mapped[str] = mapped_column(String(100), nullable=False, index=True)
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    subject_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    device_type: Mapped[str] = mapped_column(String(50), nullable=False, index=True)
    device_token: Mapped[str] = mapped_column(String(100), nullable=False, index=True)
    alert_type: Mapped[str] = mapped_column(String(50), nullable=False, index=True)
    severity: Mapped[str] = mapped_column(String(50), nullable=False, index=True)
    parameter_name: Mapped[str] = mapped_column(String(100), nullable=False)
    observed_value: Mapped[float] = mapped_column(Float, nullable=False)
    threshold_value: Mapped[float] = mapped_column(Float, nullable=False)
    unit: Mapped[str] = mapped_column(String(50), nullable=False)
    triggered_at: Mapped[datetime] = mapped_column(DateTime, nullable=False, index=True)
    is_acknowledged: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False, index=True
    )
    acknowledged_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    acknowledged_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    acknowledgment_notes: Mapped[str | None] = mapped_column(
        String(1000), nullable=True
    )
    audit_signature: Mapped[str | None] = mapped_column(String(255), nullable=True)
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )


__all__ = [
    "DeviceRegistrationRecord",
    "SensorDailySummaryRecord",
    "SensorSafetyAlertRecord",
    "SensorTelemetryStreamRecord",
]
