"""Pydantic v2 DTOs for Continuous Biometric IoT Sensor Telemetry REST API.

Requirements: PRD-DCT-002, PRD-DCT-001, ADR-2199, 21 CFR Part 11
"""

from __future__ import annotations

from pydantic import BaseModel, ConfigDict, Field

from apps.interop.domain.sensor_models import (
    DailySummaryEndpoints,
    DeviceRegistration,
    SensorDashboardMetrics,
    SensorIngestResult,
    SensorSafetyAlert,
)


class DeviceRegistrationResponse(BaseModel):
    """Response returned upon device registration."""

    model_config = ConfigDict(extra="forbid")

    success: bool = True
    device: DeviceRegistration = Field(..., description="Registered device details")
    message: str = Field(
        "Device registered and mapped successfully", description="Status message"
    )


class DeviceListResponse(BaseModel):
    """List response of registered medical IoT devices."""

    model_config = ConfigDict(extra="forbid")

    study_id: str = Field(..., description="Study protocol ID")
    total_count: int = Field(..., ge=0, description="Total registered devices")
    devices: list[DeviceRegistration] = Field(
        default_factory=list, description="Registered device entities"
    )


class SensorIngestResponse(BaseModel):
    """Response returned upon continuous sensor telemetry ingestion."""

    model_config = ConfigDict(extra="forbid")

    success: bool = True
    result: SensorIngestResult = Field(
        ..., description="Telemetry ingestion processing result"
    )
    message: str = Field(
        "Biometric sensor telemetry ingested and normalized successfully",
        description="Status message",
    )


class DailySummariesResponse(BaseModel):
    """Response containing calculated daily summary endpoints."""

    model_config = ConfigDict(extra="forbid")

    study_id: str = Field(..., description="Study ID")
    total_count: int = Field(..., ge=0, description="Total daily summaries")
    summaries: list[DailySummaryEndpoints] = Field(
        default_factory=list, description="List of daily summary endpoints"
    )


class SafetyAlertsResponse(BaseModel):
    """Response containing automated safety threshold alerts."""

    model_config = ConfigDict(extra="forbid")

    study_id: str = Field(..., description="Study ID")
    total_count: int = Field(..., ge=0, description="Total alerts count")
    unacknowledged_count: int = Field(
        ..., ge=0, description="Pending unacknowledged count"
    )
    alerts: list[SensorSafetyAlert] = Field(
        default_factory=list, description="Safety threshold alert entities"
    )


class AcknowledgeAlertRequest(BaseModel):
    """Request payload to acknowledge a safety threshold alert."""

    model_config = ConfigDict(extra="forbid")

    notes: str = Field(
        ..., min_length=1, description="Clinical evaluation and justification notes"
    )
    audit_signature: str | None = Field(
        None, description="21 CFR Part 11 electronic signature token"
    )


class AcknowledgeAlertResponse(BaseModel):
    """Response returned upon acknowledging a safety alert."""

    model_config = ConfigDict(extra="forbid")

    success: bool = True
    alert: SensorSafetyAlert = Field(
        ..., description="Updated safety alert with acknowledgment details"
    )
    message: str = Field(
        "Safety alert acknowledged successfully", description="Status message"
    )


class SensorMetricsResponse(BaseModel):
    """Response containing real-time IoT sensor telemetry metrics."""

    model_config = ConfigDict(extra="forbid")

    metrics: SensorDashboardMetrics = Field(
        ..., description="Aggregated study-wide sensor metrics"
    )
