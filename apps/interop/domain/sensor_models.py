"""Domain models and canonical schemas for Continuous Biometric IoT Sensor Telemetry Ingestion.

Supports high-frequency biometric streams from approved medical IoT devices:
1. Continuous Glucose Monitors (CGM)
2. Actigraphy / Accelerometry (steps, cadence, sleep, heart rate)
3. Digital Spirometry (FEV1, FVC, PEF)
4. Bluetooth Blood Pressure Cuffs (Systolic, Diastolic, MAP)
5. Pulse Oximeters & Continuous ECG

Enforces automated pseudonymization, device MAC/UDI mapping, clock drift correction,
Open mHealth (OMH) & HL7 FHIR Observation schemas, aggregate daily summary endpoints
(Time-in-Range TIR, mean glucose, steps/cadence, resting HR, FEV1), and automated
safety threshold alerts.

Requirements: PRD-DCT-002, PRD-DCT-001, ADR-2199, 21 CFR Part 11
"""

from __future__ import annotations

import uuid
from datetime import UTC, datetime
from enum import StrEnum
from typing import Any

from pydantic import BaseModel, ConfigDict, Field


class SensorDeviceType(StrEnum):
    """Supported medical IoT sensor device categories."""

    CGM = "CGM"
    ACTIGRAPHY = "ACTIGRAPHY"
    SPIROMETRY = "SPIROMETRY"
    BLOOD_PRESSURE = "BLOOD_PRESSURE"
    PULSE_OXIMETER = "PULSE_OXIMETER"
    ECG = "ECG"


class AlertSeverity(StrEnum):
    """Clinical alert severity classification."""

    INFO = "INFO"
    WARNING = "WARNING"
    CRITICAL = "CRITICAL"
    LIFE_THREATENING = "LIFE_THREATENING"


class SensorAlertType(StrEnum):
    """Automated safety threshold alert types."""

    SEVERE_HYPOGLYCEMIA = "SEVERE_HYPOGLYCEMIA"
    HYPOGLYCEMIA = "HYPOGLYCEMIA"
    SEVERE_HYPERGLYCEMIA = "SEVERE_HYPERGLYCEMIA"
    HYPERGLYCEMIA = "HYPERGLYCEMIA"
    EXTREME_TACHYCARDIA = "EXTREME_TACHYCARDIA"
    TACHYCARDIA = "TACHYCARDIA"
    SEVERE_BRADYCARDIA = "SEVERE_BRADYCARDIA"
    BRADYCARDIA = "BRADYCARDIA"
    HYPERTENSIVE_CRISIS = "HYPERTENSIVE_CRISIS"
    STAGE2_HYPERTENSION = "STAGE2_HYPERTENSION"
    SEVERE_HYPOTENSION = "SEVERE_HYPOTENSION"
    ACUTE_BRONCHOCONSTRICTION = "ACUTE_BRONCHOCONSTRICTION"
    DEVICE_CLOCK_DRIFT_EXCESSIVE = "DEVICE_CLOCK_DRIFT_EXCESSIVE"
    BATTERY_CRITICAL = "BATTERY_CRITICAL"


class CalibrationState(StrEnum):
    """Medical device calibration status."""

    CALIBRATED = "CALIBRATED"
    PENDING_CALIBRATION = "PENDING_CALIBRATION"
    EXPIRED = "EXPIRED"
    FACTORY_CALIBRATED = "FACTORY_CALIBRATED"


class GlucoseUnit(StrEnum):
    """Blood/interstitial glucose measurement units."""

    MG_DL = "mg/dL"
    MMOL_L = "mmol/L"


class PressureUnit(StrEnum):
    """Blood pressure measurement units."""

    MMHG = "mmHg"
    KPA = "kPa"


# =========================================================================
# Ingestion & Raw Reading Models
# =========================================================================


class RawSensorReading(BaseModel):
    """Single high-frequency biometric measurement data point."""

    model_config = ConfigDict(extra="forbid")

    timestamp: datetime = Field(
        ..., description="Raw device observation timestamp (pre-drift correction)"
    )
    metric_name: str = Field(
        ...,
        min_length=1,
        description="Metric name (e.g. glucose, step_count, heart_rate, fev1, fvc, systolic_bp, diastolic_bp)",
    )
    value: float = Field(..., description="Numeric measurement value")
    unit: str = Field(
        ...,
        min_length=1,
        description="Standard measurement unit (e.g. mg/dL, mmol/L, steps, bpm, L, mmHg)",
    )
    flag: str | None = Field(
        None,
        description="Raw sensor quality or status flag (e.g. VALID, NOISE, CAL_ERROR)",
    )
    extra_attributes: dict[str, Any] = Field(
        default_factory=dict, description="Additional device-specific telemetry tags"
    )


class SensorIngestStreamPayload(BaseModel):
    """High-frequency continuous biometric telemetry ingestion stream payload."""

    model_config = ConfigDict(extra="forbid")

    study_id: str = Field(
        ..., min_length=1, description="Clinical trial protocol ID (e.g. STUDY-901)"
    )
    site_id: str | None = Field(None, description="Clinical site identifier")
    subject_id: str = Field(
        ..., min_length=1, description="Participant identifier (raw or pseudonymized)"
    )
    device_type: SensorDeviceType = Field(
        ...,
        description="Medical IoT device category (CGM, ACTIGRAPHY, SPIROMETRY, BLOOD_PRESSURE)",
    )
    vendor: str = Field(
        ...,
        min_length=1,
        description="Device manufacturer/vendor (e.g. Dexcom, Abbott, ActiGraph, MIR, Omron)",
    )
    model_name: str = Field(
        ...,
        min_length=1,
        description="Device model name (e.g. G7, FreeStyle Libre 3, wGT3X-BT, Spirobank Smart)",
    )
    device_udi: str | None = Field(
        None, description="FDA Unique Device Identifier (UDI) barcode/string"
    )
    mac_address: str | None = Field(
        None,
        description="Bluetooth Low Energy (BLE) MAC address (e.g. 00:1A:7D:DA:71:13)",
    )
    serial_number: str | None = Field(None, description="Device hardware serial number")
    firmware_version: str | None = Field(
        None, description="Device firmware build version"
    )
    device_sync_timestamp: datetime = Field(
        ...,
        description="Timestamp reported by device RTC at the synchronization moment",
    )
    gateway_receive_timestamp: datetime = Field(
        ..., description="Authoritative UTC timestamp when gateway received the payload"
    )
    readings: list[RawSensorReading] = Field(
        ..., min_length=1, description="Batch of high-frequency sensor readings"
    )
    feed_to_lakehouse: bool = Field(
        True,
        description="Whether to automatically feed normalized records to CDR Lakehouse",
    )
    reason_for_change: str = Field(
        "Continuous IoT biometric sensor stream ingestion",
        description="GxP audit trail justification",
    )


# =========================================================================
# Device Registration & Mapping Models
# =========================================================================


class DeviceRegistrationRequest(BaseModel):
    """Payload to register and map an approved medical IoT sensor device."""

    model_config = ConfigDict(extra="forbid")

    study_id: str = Field(..., min_length=1, description="Study protocol ID")
    site_id: str | None = Field(None, description="Site ID")
    subject_id: str = Field(..., min_length=1, description="Assigned subject ID")
    device_type: SensorDeviceType = Field(..., description="Device category")
    vendor: str = Field(..., min_length=1, description="Device manufacturer")
    model_name: str = Field(..., min_length=1, description="Device model")
    device_udi: str | None = Field(
        None, description="FDA Unique Device Identifier (UDI)"
    )
    mac_address: str | None = Field(None, description="Bluetooth MAC address")
    serial_number: str | None = Field(None, description="Device serial number")
    firmware_version: str | None = Field(None, description="Firmware version")
    calibration_status: CalibrationState = Field(
        CalibrationState.CALIBRATED, description="Initial calibration state"
    )
    calibration_date: datetime | None = Field(
        None, description="Date of last calibration"
    )
    reason_for_change: str = Field(
        "Initial device registration and subject mapping",
        description="GxP justification",
    )


class DeviceRegistration(BaseModel):
    """Registered and mapped medical IoT sensor device."""

    model_config = ConfigDict(extra="forbid")

    device_id: str = Field(
        default_factory=lambda: str(uuid.uuid4()), description="Device entity UUID"
    )
    study_id: str = Field(..., description="Study protocol ID")
    site_id: str | None = Field(None, description="Site ID")
    subject_id: str = Field(..., description="Pseudonymized subject ID")
    device_type: SensorDeviceType = Field(..., description="Device category")
    vendor: str = Field(..., description="Device manufacturer")
    model_name: str = Field(..., description="Device model")
    udi_di: str = Field(..., description="UDI Device Identifier")
    udi_pi_serial: str | None = Field(None, description="UDI Production Serial")
    udi_pi_lot: str | None = Field(None, description="UDI Production Lot")
    mac_address_masked: str | None = Field(
        None, description="Cryptographically masked MAC address"
    )
    device_token: str = Field(
        ..., description="Deterministic GxP device identifier token"
    )
    firmware_version: str | None = Field(None, description="Firmware version")
    calibration_status: CalibrationState = Field(..., description="Calibration state")
    calibration_date: datetime | None = Field(None, description="Calibration date")
    is_active: bool = Field(True, description="Active status")
    created_at: datetime = Field(
        default_factory=lambda: datetime.now(UTC), description="Creation timestamp"
    )
    created_by: str = Field("system", description="Author user ID")
    reason_for_change: str = Field(..., description="GxP reason")
    version_index: int = Field(1, description="Record version")


# =========================================================================
# Standard Interoperability Schema Representations (OMH & FHIR)
# =========================================================================


class OpenMHealthDataPoint(BaseModel):
    """Normalized Open mHealth (OMH) standard data point."""

    model_config = ConfigDict(extra="forbid")

    header: dict[str, Any] = Field(
        ..., description="OMH Header with schema_id, creation_date_time, and provenance"
    )
    body: dict[str, Any] = Field(
        ..., description="OMH Body matching schema specification"
    )


class FHIRSensorObservation(BaseModel):
    """Normalized HL7 FHIR R4/R5 Observation resource representation."""

    model_config = ConfigDict(extra="forbid")

    resourceType: str = Field("Observation", description="FHIR Resource Type")
    id: str = Field(
        default_factory=lambda: str(uuid.uuid4()), description="FHIR resource ID"
    )
    identifier: list[dict[str, Any]] = Field(
        default_factory=list, description="Business identifiers"
    )
    status: str = Field("final", description="Observation status (final, amended)")
    category: list[dict[str, Any]] = Field(
        default_factory=list,
        description="Observation category (vital-signs, laboratory)",
    )
    code: dict[str, Any] = Field(..., description="LOINC / SNOMED concept coding")
    subject: dict[str, str] = Field(
        ..., description="Reference to pseudonymized Subject/Patient"
    )
    device: dict[str, Any] = Field(
        ..., description="Reference to Device entity and token"
    )
    effectiveDateTime: str | None = Field(
        None, description="Single observation timestamp"
    )
    effectivePeriod: dict[str, str] | None = Field(
        None, description="Observation time window"
    )
    valueQuantity: dict[str, Any] | None = Field(None, description="Quantitative value")
    component: list[dict[str, Any]] | None = Field(
        None, description="Multi-component observations"
    )
    interpretation: list[dict[str, Any]] | None = Field(
        None, description="Clinical interpretation"
    )


# =========================================================================
# Daily Summary Aggregate Endpoints Models
# =========================================================================


class CGMDailySummary(BaseModel):
    """Standard clinical Continuous Glucose Monitoring daily endpoints."""

    model_config = ConfigDict(extra="forbid")

    tir_percentage: float = Field(
        ...,
        ge=0.0,
        le=100.0,
        description="Time-in-Range percentage (70-180 mg/dL / 3.9-10.0 mmol/L)",
    )
    tbr_level1_percentage: float = Field(
        ...,
        ge=0.0,
        le=100.0,
        description="Time-Below-Range Level 1 (54-69 mg/dL / 3.0-3.8 mmol/L)",
    )
    tbr_level2_percentage: float = Field(
        ...,
        ge=0.0,
        le=100.0,
        description="Time-Below-Range Level 2 / Severe Hypoglycemia (<54 mg/dL / <3.0 mmol/L)",
    )
    tar_level1_percentage: float = Field(
        ...,
        ge=0.0,
        le=100.0,
        description="Time-Above-Range Level 1 (181-250 mg/dL / 10.1-13.9 mmol/L)",
    )
    tar_level2_percentage: float = Field(
        ...,
        ge=0.0,
        le=100.0,
        description="Time-Above-Range Level 2 / Severe Hyperglycemia (>250 mg/dL / >13.9 mmol/L)",
    )
    mean_glucose_mg_dl: float = Field(
        ..., ge=0.0, description="Mean daily glucose in mg/dL"
    )
    glucose_sd: float = Field(
        ..., ge=0.0, description="Standard deviation of glucose in mg/dL"
    )
    glucose_cv_percent: float = Field(
        ...,
        ge=0.0,
        description="Glucose Coefficient of Variation (CV% = SD/mean * 100)",
    )
    glucose_management_indicator: float = Field(
        ...,
        ge=0.0,
        description="Estimated HbA1c proxy: GMI = 3.31 + (0.02392 * mean_glucose)",
    )
    readings_count: int = Field(..., ge=1, description="Total CGM readings analyzed")
    active_wear_percentage: float = Field(
        ..., ge=0.0, le=100.0, description="Sensor active wear compliance percentage"
    )


class ActigraphyDailySummary(BaseModel):
    """Standard actigraphy and activity tracking daily endpoints."""

    model_config = ConfigDict(extra="forbid")

    total_steps: int = Field(..., ge=0, description="Total daily step count")
    mean_cadence_spm: float = Field(
        ..., ge=0.0, description="Average active cadence in steps/minute"
    )
    peak_cadence_spm: float = Field(
        ..., ge=0.0, description="Peak 1-minute cadence in steps/minute"
    )
    active_minutes: float = Field(
        ..., ge=0.0, description="Total moderate-to-vigorous active minutes"
    )
    sedentary_minutes: float = Field(..., ge=0.0, description="Total sedentary minutes")
    sleep_duration_minutes: float = Field(
        ..., ge=0.0, description="Total sleep duration in minutes"
    )
    sleep_efficiency_percent: float = Field(
        ...,
        ge=0.0,
        le=100.0,
        description="Sleep efficiency percentage (time asleep / time in bed)",
    )
    resting_heart_rate_bpm: float = Field(
        ..., ge=0.0, description="Resting heart rate in beats per minute"
    )
    readings_count: int = Field(
        ..., ge=1, description="Total telemetry epochs analyzed"
    )


class SpirometryDailySummary(BaseModel):
    """Standard digital spirometry daily summary endpoints."""

    model_config = ConfigDict(extra="forbid")

    best_fev1_liters: float = Field(
        ..., ge=0.0, description="Highest Forced Expiratory Volume in 1 second (Liters)"
    )
    best_fvc_liters: float = Field(
        ..., ge=0.0, description="Highest Forced Vital Capacity (Liters)"
    )
    fev1_fvc_ratio: float = Field(
        ..., ge=0.0, le=1.0, description="FEV1 / FVC ratio (Tiffeneau-Pinelli index)"
    )
    fev1_percent_predicted: float = Field(
        ..., ge=0.0, description="FEV1 percent of predicted reference baseline (%)"
    )
    peak_expiratory_flow_l_min: float = Field(
        ..., ge=0.0, description="Peak Expiratory Flow in Liters per minute"
    )
    maneuver_count: int = Field(
        ..., ge=1, description="Total acceptable spirometry maneuvers"
    )
    session_quality_grade: str = Field(
        "A", description="ATS/ERS session quality grade (A, B, C, D, F)"
    )


class BloodPressureDailySummary(BaseModel):
    """Standard blood pressure daily summary endpoints."""

    model_config = ConfigDict(extra="forbid")

    mean_systolic_mmhg: float = Field(
        ..., ge=0.0, description="Mean Systolic Blood Pressure in mmHg"
    )
    mean_diastolic_mmhg: float = Field(
        ..., ge=0.0, description="Mean Diastolic Blood Pressure in mmHg"
    )
    mean_arterial_pressure_mmhg: float = Field(
        ..., ge=0.0, description="Mean Arterial Pressure in mmHg: MAP = (2*DBP + SBP)/3"
    )
    morning_systolic_mmhg: float | None = Field(
        None, description="Morning mean systolic BP"
    )
    evening_systolic_mmhg: float | None = Field(
        None, description="Evening mean systolic BP"
    )
    readings_count: int = Field(
        ..., ge=1, description="Total blood pressure cuff readings"
    )


class DailySummaryEndpoints(BaseModel):
    """Aggregated daily biometric endpoints for a study subject and device."""

    model_config = ConfigDict(extra="forbid")

    summary_id: str = Field(
        default_factory=lambda: f"SUM-{uuid.uuid4().hex[:12].upper()}",
        description="Daily summary UUID",
    )
    study_id: str = Field(..., description="Study ID")
    site_id: str | None = Field(None, description="Site ID")
    subject_id: str = Field(..., description="Pseudonymized subject ID")
    summary_date: datetime = Field(..., description="Date of aggregate summary")
    device_type: SensorDeviceType = Field(..., description="Device category")
    device_token: str = Field(..., description="Device token")
    cgm: CGMDailySummary | None = Field(
        None, description="CGM summary if device_type=CGM"
    )
    actigraphy: ActigraphyDailySummary | None = Field(
        None, description="Actigraphy summary if device_type=ACTIGRAPHY"
    )
    spirometry: SpirometryDailySummary | None = Field(
        None, description="Spirometry summary if device_type=SPIROMETRY"
    )
    blood_pressure: BloodPressureDailySummary | None = Field(
        None, description="Blood pressure summary if device_type=BLOOD_PRESSURE"
    )
    lakehouse_record_id: str | None = Field(
        None, description="Reference to generated Lakehouse canonical record"
    )
    data_hash: str = Field(..., description="SHA-256 integrity seal")
    created_at: datetime = Field(
        default_factory=lambda: datetime.now(UTC), description="Creation timestamp"
    )


# =========================================================================
# Safety Threshold Alerts & Ingestion Results
# =========================================================================


class SensorSafetyAlert(BaseModel):
    """Automated biometric safety threshold alert."""

    model_config = ConfigDict(extra="forbid")

    alert_id: str = Field(
        default_factory=lambda: f"ALT-{uuid.uuid4().hex[:10].upper()}",
        description="Unique alert identifier",
    )
    study_id: str = Field(..., description="Study ID")
    site_id: str | None = Field(None, description="Site ID")
    subject_id: str = Field(..., description="Pseudonymized subject ID")
    device_type: SensorDeviceType = Field(..., description="Device category")
    device_token: str = Field(..., description="Device token")
    alert_type: SensorAlertType = Field(..., description="Alert classification")
    severity: AlertSeverity = Field(..., description="Alert severity level")
    parameter_name: str = Field(
        ...,
        description="Breached parameter (e.g. glucose, heart_rate, fev1, systolic_bp)",
    )
    observed_value: float = Field(..., description="Actual observed measurement")
    threshold_value: float = Field(..., description="Clinical safety limit breached")
    unit: str = Field(..., description="Measurement unit")
    triggered_at: datetime = Field(
        ..., description="Timestamp of breach (drift-corrected UTC)"
    )
    is_acknowledged: bool = Field(False, description="Investigator acknowledgment flag")
    acknowledged_by: str | None = Field(
        None, description="User ID of acknowledging clinician"
    )
    acknowledged_at: datetime | None = Field(
        None, description="Timestamp of acknowledgment"
    )
    acknowledgment_notes: str | None = Field(
        None, description="Clinical justification notes"
    )
    audit_signature: str | None = Field(
        None, description="Cryptographic Part 11 signature seal"
    )
    notified_roles: list[str] = Field(
        default_factory=lambda: ["site_investigator", "crc", "safety_physician"],
        description="Clinical roles alerted",
    )


class SensorIngestResult(BaseModel):
    """Result of continuous IoT sensor telemetry ingestion."""

    model_config = ConfigDict(extra="forbid")

    stream_id: str = Field(
        default_factory=lambda: str(uuid.uuid4()), description="Ingestion stream UUID"
    )
    study_id: str = Field(..., description="Study protocol ID")
    subject_id_pseudonymized: str = Field(..., description="Pseudonymized subject ID")
    device_token: str = Field(..., description="GxP device identifier token")
    device_type: SensorDeviceType = Field(..., description="Device category")
    readings_ingested: int = Field(..., ge=0, description="Total readings processed")
    clock_drift_offset_ms: float = Field(
        ..., description="Computed clock drift between device RTC and server (ms)"
    )
    drift_corrected: bool = Field(
        ..., description="Whether timestamps were drift-adjusted"
    )
    omh_data_points_count: int = Field(
        ..., description="Generated OMH standard packets count"
    )
    fhir_observations_count: int = Field(
        ..., description="Generated FHIR Observations count"
    )
    safety_alerts_triggered: list[SensorSafetyAlert] = Field(
        default_factory=list, description="Automated safety threshold alerts triggered"
    )
    daily_summary: DailySummaryEndpoints | None = Field(
        None, description="Calculated aggregate daily summary endpoints"
    )
    lakehouse_record_ids: list[str] = Field(
        default_factory=list,
        description="Canonical clinical record IDs fed to CDR Lakehouse",
    )
    data_hash: str = Field(..., description="SHA-256 cryptographic stream seal")
    ingested_at: datetime = Field(
        default_factory=lambda: datetime.now(UTC), description="Ingestion timestamp"
    )


class SensorDashboardMetrics(BaseModel):
    """Aggregate real-time metrics for connected IoT sensors across a clinical study."""

    model_config = ConfigDict(extra="forbid")

    study_id: str = Field(..., description="Study ID")
    total_streams_ingested: int = Field(
        0, ge=0, description="Total stream batches ingested"
    )
    total_readings_ingested: int = Field(
        0, ge=0, description="Total individual readings"
    )
    active_devices_count: int = Field(
        0, ge=0, description="Registered active devices count"
    )
    device_breakdown: dict[str, int] = Field(
        default_factory=dict, description="Active devices count per device type"
    )
    total_safety_alerts: int = Field(0, ge=0, description="Total alerts triggered")
    unacknowledged_alerts: int = Field(
        0, ge=0, description="Pending unacknowledged alerts"
    )
    mean_time_in_range_percent: float | None = Field(
        None, description="Study-wide mean CGM Time-in-Range percentage"
    )
    mean_daily_steps: float | None = Field(
        None, description="Study-wide mean daily step count"
    )
    mean_resting_hr_bpm: float | None = Field(
        None, description="Study-wide mean resting heart rate"
    )
    mean_fev1_liters: float | None = Field(
        None, description="Study-wide mean FEV1 in Liters"
    )
    last_telemetry_received_at: datetime | None = Field(
        None, description="Most recent telemetry ingestion timestamp"
    )
