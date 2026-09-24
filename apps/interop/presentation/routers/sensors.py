"""FastAPI Router for Continuous Biometric IoT Sensor Telemetry Ingestion.

Endpoints:
- POST /api/v1/interop/sensors/devices: Register & map approved medical IoT sensor device (MAC/UDI)
- GET  /api/v1/interop/sensors/devices: List registered devices for a study/site/subject
- POST /api/v1/interop/sensors/ingest: Ingest continuous biometric telemetry with pseudonymization, drift correction, OMH/FHIR transformation, daily summary calculation, safety alerting, and Lakehouse feed
- GET  /api/v1/interop/sensors/daily-summaries: Query calculated daily summary endpoints (TIR, glucose, steps, FEV1)
- GET  /api/v1/interop/sensors/safety-alerts: Query active and historical safety threshold alerts
- POST /api/v1/interop/sensors/safety-alerts/{alert_id}/acknowledge: Acknowledge safety alert with GxP 21 CFR Part 11 electronic sign-off
- GET  /api/v1/interop/sensors/metrics/{study_id}: Dashboard summary metrics

Requirements: PRD-DCT-002, PRD-DCT-001, ADR-2199, 21 CFR Part 11
"""

from __future__ import annotations

from datetime import datetime

from fastapi import APIRouter, Depends, HTTPException, Query, Request, status
from sqlalchemy.ext.asyncio import AsyncSession

from apps.interop.adapters.database import db_manager
from apps.interop.adapters.lakehouse_repository import LakehouseRepository
from apps.interop.adapters.sensor_repository import SensorRepository
from apps.interop.application.sensor_service import SensorService
from apps.interop.domain.sensor_models import (
    DeviceRegistrationRequest,
    SensorDeviceType,
    SensorIngestStreamPayload,
)
from apps.interop.presentation.sensor_dtos import (
    AcknowledgeAlertRequest,
    AcknowledgeAlertResponse,
    DailySummariesResponse,
    DeviceListResponse,
    DeviceRegistrationResponse,
    SafetyAlertsResponse,
    SensorIngestResponse,
    SensorMetricsResponse,
)
from packages.database import DatabaseSessionDependency
from packages.security.trial_roles import get_normalized_request_roles

router = APIRouter(prefix="/api/v1/interop/sensors", tags=["Biometric Sensors & DCT"])
get_db_session = DatabaseSessionDependency(db_manager)


def _extract_principal(request: Request) -> tuple[str, str]:
    """Extract user ID and primary role from request headers/state."""
    user_id = (
        getattr(request.state, "user_id", None)
        or request.headers.get("X-User-Id")
        or request.headers.get("x-user-id")
        or "system"
    )
    roles = get_normalized_request_roles(request)
    primary_role = roles[0] if roles else "crc"
    return user_id, primary_role


def _get_sensor_service(session: AsyncSession) -> SensorService:
    """Instantiate SensorService with both SensorRepository and LakehouseRepository."""
    sensor_repo = SensorRepository(session)
    lakehouse_repo = LakehouseRepository(session)
    return SensorService(sensor_repo, lakehouse_repo=lakehouse_repo)


# =========================================================================
# Device Registration Endpoints
# =========================================================================


@router.post(
    "/devices",
    response_model=DeviceRegistrationResponse,
    status_code=status.HTTP_201_CREATED,
    summary="Register & map approved medical IoT sensor device",
)
async def register_device(
    payload: DeviceRegistrationRequest,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> DeviceRegistrationResponse:
    """Register and map an approved medical IoT sensor device (CGM, Actigraphy, Spirometry, BP).

    @req:PRD-DCT-002
    @req:ADR-2199
    """
    user_id, role = _extract_principal(request)
    service = _get_sensor_service(session)
    device = await service.register_device(payload, user_id=user_id, user_role=role)
    return DeviceRegistrationResponse(
        success=True,
        device=device,
        message="Device registered and mapped successfully",
    )


@router.get(
    "/devices",
    response_model=DeviceListResponse,
    status_code=status.HTTP_200_OK,
    summary="List registered medical IoT devices",
)
async def list_devices(
    study_id: str = Query(..., description="Study protocol ID"),
    site_id: str | None = Query(None, description="Optional site ID filter"),
    subject_id: str | None = Query(None, description="Optional subject ID filter"),
    device_type: SensorDeviceType | None = Query(
        None, description="Optional device type filter"
    ),
    session: AsyncSession = Depends(get_db_session),
) -> DeviceListResponse:
    """List registered medical IoT sensor devices for a clinical study.

    @req:PRD-DCT-002
    @req:ADR-2199
    """
    service = _get_sensor_service(session)
    devices = await service.list_devices(
        study_id=study_id,
        site_id=site_id,
        subject_id=subject_id,
        device_type=device_type.value if device_type else None,
    )
    return DeviceListResponse(
        study_id=study_id,
        total_count=len(devices),
        devices=devices,
    )


# =========================================================================
# Telemetry Stream Ingestion
# =========================================================================


@router.post(
    "/ingest",
    response_model=SensorIngestResponse,
    status_code=status.HTTP_201_CREATED,
    summary="Ingest continuous biometric IoT sensor telemetry",
)
async def ingest_telemetry_stream(
    payload: SensorIngestStreamPayload,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> SensorIngestResponse:
    """Ingest continuous time-series telemetry streams from approved medical IoT devices.

    Applies:
    1. Automated HMAC-SHA256 pseudonymization & device MAC/UDI mapping
    2. Clock drift correction between device RTC and gateway UTC
    3. Normalization into Open mHealth (OMH) & HL7 FHIR Observation schemas
    4. Aggregate daily summary endpoints calculation (Time-in-Range TIR, mean glucose, steps, FEV1)
    5. Automated safety threshold alert evaluation
    6. Automated feeding into the Clinical Data Repository (CDR) Lakehouse

    @req:PRD-DCT-002
    @req:PRD-DCT-001
    @req:ADR-2199
    """
    user_id, role = _extract_principal(request)
    service = _get_sensor_service(session)
    result = await service.ingest_telemetry_stream(
        payload, user_id=user_id, user_role=role
    )
    return SensorIngestResponse(
        success=True,
        result=result,
        message=f"Successfully ingested {result.readings_ingested} readings from {payload.vendor} {payload.device_type.value}",
    )


# =========================================================================
# Daily Summary Endpoints Queries
# =========================================================================


@router.get(
    "/daily-summaries",
    response_model=DailySummariesResponse,
    status_code=status.HTTP_200_OK,
    summary="Query calculated daily summary endpoints",
)
async def list_daily_summaries(
    study_id: str = Query(..., description="Study ID"),
    subject_id: str | None = Query(
        None, description="Optional pseudonymized subject ID"
    ),
    device_type: SensorDeviceType | None = Query(
        None, description="Device type filter"
    ),
    start_date: datetime | None = Query(None, description="Start date filter"),
    end_date: datetime | None = Query(None, description="End date filter"),
    session: AsyncSession = Depends(get_db_session),
) -> DailySummariesResponse:
    """Retrieve aggregate daily summary endpoints (TIR, glucose, steps, cadence, RHR, FEV1).

    @req:PRD-DCT-002
    @req:ADR-2199
    """
    service = _get_sensor_service(session)
    summaries = await service.list_daily_summaries(
        study_id=study_id,
        subject_id=subject_id,
        device_type=device_type.value if device_type else None,
        start_date=start_date,
        end_date=end_date,
    )
    return DailySummariesResponse(
        study_id=study_id,
        total_count=len(summaries),
        summaries=summaries,
    )


# =========================================================================
# Safety Threshold Alerts & Acknowledgment
# =========================================================================


@router.get(
    "/safety-alerts",
    response_model=SafetyAlertsResponse,
    status_code=status.HTTP_200_OK,
    summary="Query automated safety threshold alerts",
)
async def list_safety_alerts(
    study_id: str = Query(..., description="Study protocol ID"),
    subject_id: str | None = Query(
        None, description="Optional pseudonymized subject filter"
    ),
    severity: str | None = Query(
        None, description="Severity filter (CRITICAL, WARNING)"
    ),
    alert_type: str | None = Query(None, description="Alert type filter"),
    is_acknowledged: bool | None = Query(None, description="Acknowledgment filter"),
    session: AsyncSession = Depends(get_db_session),
) -> SafetyAlertsResponse:
    """Retrieve triggered biometric safety threshold alerts for clinical investigation.

    @req:PRD-DCT-002
    @req:ADR-2199
    """
    service = _get_sensor_service(session)
    alerts = await service.list_safety_alerts(
        study_id=study_id,
        subject_id=subject_id,
        severity=severity,
        alert_type=alert_type,
        is_acknowledged=is_acknowledged,
    )
    unack = sum(1 for a in alerts if not a.is_acknowledged)
    return SafetyAlertsResponse(
        study_id=study_id,
        total_count=len(alerts),
        unacknowledged_count=unack,
        alerts=alerts,
    )


@router.post(
    "/safety-alerts/{alert_id}/acknowledge",
    response_model=AcknowledgeAlertResponse,
    status_code=status.HTTP_200_OK,
    summary="Acknowledge safety alert with electronic signature",
)
async def acknowledge_safety_alert(
    alert_id: str,
    payload: AcknowledgeAlertRequest,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> AcknowledgeAlertResponse:
    """Acknowledge a safety threshold alert with 21 CFR Part 11 electronic sign-off and clinical notes.

    @req:PRD-DCT-002
    @req:ADR-2199
    """
    user_id, _ = _extract_principal(request)
    service = _get_sensor_service(session)
    alert = await service.acknowledge_safety_alert(
        alert_id=alert_id,
        user_id=user_id,
        notes=payload.notes,
        audit_signature=payload.audit_signature,
    )
    if not alert:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Safety alert '{alert_id}' not found",
        )
    return AcknowledgeAlertResponse(
        success=True,
        alert=alert,
        message=f"Safety alert {alert_id} acknowledged by {user_id}",
    )


# =========================================================================
# Dashboard Summary Metrics
# =========================================================================


@router.get(
    "/metrics/{study_id}",
    response_model=SensorMetricsResponse,
    status_code=status.HTTP_200_OK,
    summary="Get connected IoT sensor dashboard metrics",
)
async def get_sensor_dashboard_metrics(
    study_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> SensorMetricsResponse:
    """Retrieve real-time IoT sensor telemetry metrics across a clinical study.

    @req:PRD-DCT-002
    @req:ADR-2199
    """
    service = _get_sensor_service(session)
    metrics = await service.get_dashboard_metrics(study_id=study_id)
    return SensorMetricsResponse(metrics=metrics)
