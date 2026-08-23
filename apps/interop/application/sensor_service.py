"""Application service orchestrating Continuous Biometric IoT Sensor Telemetry Ingestion.

Handles:
- Medical IoT device registration, UDI parsing, and MAC masking
- High-frequency biometric stream ingestion with clock drift correction
- Automated pseudonymization & de-identification
- Open mHealth (OMH) and HL7 FHIR normalization
- Daily summary endpoint calculations (TIR, mean glucose, steps, resting HR, FEV1)
- Automated safety threshold alerts triggering and acknowledgment
- Feeding validated sensor endpoints into the CDR Lakehouse

Requirements: PRD-DCT-002, PRD-DCT-001, ADR-2199, 21 CFR Part 11
"""

from __future__ import annotations

import json
import uuid
from datetime import UTC, datetime

from apps.interop.domain.ports import (
    ISensorRepositoryPort,
    LakehouseRepositoryPort,
)
from apps.interop.domain.sensor_models import (
    ActigraphyDailySummary,
    BloodPressureDailySummary,
    CGMDailySummary,
    DailySummaryEndpoints,
    DeviceRegistration,
    DeviceRegistrationRequest,
    SensorDashboardMetrics,
    SensorDeviceType,
    SensorIngestResult,
    SensorIngestStreamPayload,
    SensorSafetyAlert,
    SpirometryDailySummary,
)
from apps.interop.domain.sensor_processor import (
    calculate_daily_summary_endpoints,
    compute_clock_drift_offset_ms,
    convert_sensor_telemetry_to_lakehouse_records,
    correct_reading_timestamps,
    evaluate_safety_threshold_alerts,
    generate_device_token,
    pseudonymize_subject_id,
    register_device_entity,
    transform_to_fhir_observations,
    transform_to_omh_packets,
)


class SensorService:
    """Core application service for continuous biometric IoT sensor stream ingestion and processing."""

    def __init__(
        self,
        repository: ISensorRepositoryPort,
        lakehouse_repo: LakehouseRepositoryPort | None = None,
    ) -> None:
        self.repository = repository
        self.lakehouse_repo = lakehouse_repo

    # =========================================================================
    # Device Registration & Management
    # =========================================================================

    async def register_device(
        self,
        req: DeviceRegistrationRequest,
        user_id: str = "system",
        user_role: str = "crc",
    ) -> DeviceRegistration:
        """Register and map an approved medical IoT sensor device to a subject."""
        domain_device = register_device_entity(req, user_id=user_id)

        # Check for existing device with same token
        existing = await self.repository.get_device_by_token(domain_device.device_token)
        if existing:
            return DeviceRegistration(
                device_id=existing.id,
                study_id=existing.study_id,
                site_id=existing.site_id,
                subject_id=existing.subject_id,
                device_type=SensorDeviceType(existing.device_type),
                vendor=existing.vendor,
                model_name=existing.model_name,
                udi_di=existing.udi_di,
                udi_pi_serial=existing.udi_pi_serial,
                udi_pi_lot=existing.udi_pi_lot,
                mac_address_masked=existing.mac_address_masked,
                device_token=existing.device_token,
                firmware_version=existing.firmware_version,
                calibration_status=existing.calibration_status,
                calibration_date=existing.calibration_date,
                is_active=existing.is_active,
                created_at=existing.created_at,
                created_by=existing.created_by,
                reason_for_change=existing.reason_for_change,
                version_index=existing.version_index,
            )

        # Persist new registration record
        await self.repository.save_device(domain_device)
        return domain_device

    async def list_devices(
        self,
        study_id: str,
        site_id: str | None = None,
        subject_id: str | None = None,
        device_type: str | None = None,
    ) -> list[DeviceRegistration]:
        """List registered medical IoT devices matching criteria."""
        records = await self.repository.list_devices(
            study_id=study_id,
            site_id=site_id,
            subject_id=subject_id,
            device_type=device_type,
        )
        return [
            DeviceRegistration(
                device_id=r.id,
                study_id=r.study_id,
                site_id=r.site_id,
                subject_id=r.subject_id,
                device_type=SensorDeviceType(r.device_type),
                vendor=r.vendor,
                model_name=r.model_name,
                udi_di=r.udi_di,
                udi_pi_serial=r.udi_pi_serial,
                udi_pi_lot=r.udi_pi_lot,
                mac_address_masked=r.mac_address_masked,
                device_token=r.device_token,
                firmware_version=r.firmware_version,
                calibration_status=r.calibration_status,
                calibration_date=r.calibration_date,
                is_active=r.is_active,
                created_at=r.created_at,
                created_by=r.created_by,
                reason_for_change=r.reason_for_change,
                version_index=r.version_index,
            )
            for r in records
        ]

    # =========================================================================
    # Continuous Biometric Telemetry Ingestion Pipeline
    # =========================================================================

    async def ingest_telemetry_stream(
        self,
        stream: SensorIngestStreamPayload,
        user_id: str = "system",
        user_role: str = "data_manager",
    ) -> SensorIngestResult:
        """Ingest, calibrate, normalize, evaluate, and persist continuous sensor telemetry."""
        # 1. Automated Pseudonymization
        pseudo_subject = (
            stream.subject_id
            if stream.subject_id.startswith("SUBJ-PSEUDO-")
            else pseudonymize_subject_id(stream.subject_id)
        )

        # 2. Device Token Mapping
        raw_ident = (
            stream.serial_number
            or stream.mac_address
            or stream.device_udi
            or f"{stream.vendor}-{stream.model_name}"
        )
        device_token = generate_device_token(
            stream.vendor, stream.model_name, raw_ident
        )

        # 3. Clock Drift Correction
        drift_ms = compute_clock_drift_offset_ms(
            stream.device_sync_timestamp,
            stream.gateway_receive_timestamp,
        )
        corrected_readings = correct_reading_timestamps(stream.readings, drift_ms)

        # 4. Standard Schema Normalization: Open mHealth (OMH)
        omh_packets = transform_to_omh_packets(
            stream, pseudo_subject, device_token, corrected_readings
        )

        # 5. Standard Schema Normalization: HL7 FHIR Observations
        fhir_observations = transform_to_fhir_observations(
            stream, pseudo_subject, device_token, corrected_readings
        )

        # 6. Calculate Aggregate Daily Summary Endpoints
        daily_summary = calculate_daily_summary_endpoints(
            stream, pseudo_subject, device_token, corrected_readings
        )

        # 7. Evaluate Automated Safety Threshold Alerts
        safety_alerts = evaluate_safety_threshold_alerts(
            stream,
            pseudo_subject,
            device_token,
            corrected_readings,
            drift_offset_ms=drift_ms,
        )

        # 8. Feed Validated Sensor Endpoints into CDR Lakehouse if requested
        lakehouse_record_ids: list[str] = []
        if stream.feed_to_lakehouse:
            lakehouse_records = convert_sensor_telemetry_to_lakehouse_records(
                stream,
                pseudo_subject,
                device_token,
                corrected_readings,
                daily_summary,
            )
            lakehouse_record_ids = [r.record_id for r in lakehouse_records]

            # If Lakehouse repository is provided, persist canonical records directly
            if self.lakehouse_repo is not None and lakehouse_records:
                await self.lakehouse_repo.save_canonical_records(lakehouse_records)

        # 9. Persist Telemetry Stream Batch Record
        stream_id = str(uuid.uuid4())
        raw_hash_data = {
            "study_id": stream.study_id,
            "subject_id": pseudo_subject,
            "device_token": device_token,
            "reading_count": len(corrected_readings),
            "clock_drift_ms": drift_ms,
        }
        stream_hash = json.dumps(raw_hash_data, sort_keys=True)

        stream_data = {
            "id": stream_id,
            "study_id": stream.study_id,
            "site_id": stream.site_id,
            "subject_id": pseudo_subject,
            "device_token": device_token,
            "device_type": stream.device_type.value,
            "batch_id": f"BATCH-{uuid.uuid4().hex[:10].upper()}",
            "received_at": stream.gateway_receive_timestamp,
            "clock_drift_offset_ms": drift_ms,
            "reading_count": len(corrected_readings),
            "start_timestamp": corrected_readings[0].timestamp,
            "end_timestamp": corrected_readings[-1].timestamp,
            "omh_payload": [p.model_dump(mode="json") for p in omh_packets],
            "fhir_observations": [o.model_dump(mode="json") for o in fhir_observations],
            "raw_data_hash": stream_hash,
            "feed_lakehouse_record_ids": lakehouse_record_ids,
            "created_at": datetime.now(UTC),
        }
        await self.repository.save_telemetry_stream(stream_data)

        # 10. Persist Daily Summary Record
        if daily_summary:
            await self.repository.save_daily_summary(daily_summary)

        # 11. Persist Safety Alerts
        if safety_alerts:
            await self.repository.save_safety_alerts(safety_alerts)

        return SensorIngestResult(
            stream_id=stream_id,
            study_id=stream.study_id,
            subject_id_pseudonymized=pseudo_subject,
            device_token=device_token,
            device_type=stream.device_type,
            readings_ingested=len(corrected_readings),
            clock_drift_offset_ms=drift_ms,
            drift_corrected=abs(drift_ms) >= 1.0,
            omh_data_points_count=len(omh_packets),
            fhir_observations_count=len(fhir_observations),
            safety_alerts_triggered=safety_alerts,
            daily_summary=daily_summary,
            lakehouse_record_ids=lakehouse_record_ids,
            data_hash=stream_hash,
            ingested_at=datetime.now(UTC),
        )

    # =========================================================================
    # Queries & Alerts Workflow
    # =========================================================================

    async def list_daily_summaries(
        self,
        study_id: str,
        subject_id: str | None = None,
        device_type: str | None = None,
        start_date: datetime | None = None,
        end_date: datetime | None = None,
    ) -> list[DailySummaryEndpoints]:
        """Query calculated daily summary endpoints."""
        records = await self.repository.list_daily_summaries(
            study_id=study_id,
            subject_id=subject_id,
            device_type=device_type,
            start_date=start_date,
            end_date=end_date,
        )
        summaries: list[DailySummaryEndpoints] = []
        for r in records:
            dev_type = SensorDeviceType(r.device_type)
            rc = max(1, r.readings_count or 1)
            cgm = (
                CGMDailySummary(
                    tir_percentage=r.tir_percentage or 0.0,
                    tbr_level1_percentage=r.tbr_level1_percentage or 0.0,
                    tbr_level2_percentage=r.tbr_level2_percentage or 0.0,
                    tar_level1_percentage=r.tar_level1_percentage or 0.0,
                    tar_level2_percentage=r.tar_level2_percentage or 0.0,
                    mean_glucose_mg_dl=r.mean_glucose_mg_dl or 0.0,
                    glucose_sd=r.glucose_sd or 0.0,
                    glucose_cv_percent=r.glucose_cv_percent or 0.0,
                    glucose_management_indicator=r.glucose_management_indicator or 0.0,
                    readings_count=rc,
                    active_wear_percentage=round(
                        (r.active_wear_hours / 24.0) * 100.0, 1
                    ),
                )
                if dev_type == SensorDeviceType.CGM
                else None
            )
            act = (
                ActigraphyDailySummary(
                    total_steps=r.total_steps or 0,
                    mean_cadence_spm=r.mean_cadence_spm or 0.0,
                    peak_cadence_spm=r.peak_cadence_spm or 0.0,
                    active_minutes=r.active_minutes or 0.0,
                    sedentary_minutes=r.sedentary_minutes or 0.0,
                    sleep_duration_minutes=r.sleep_duration_minutes or 0.0,
                    sleep_efficiency_percent=r.sleep_efficiency_percent or 0.0,
                    resting_heart_rate_bpm=r.resting_heart_rate_bpm or 0.0,
                    readings_count=rc,
                )
                if dev_type == SensorDeviceType.ACTIGRAPHY
                else None
            )
            spiro = (
                SpirometryDailySummary(
                    best_fev1_liters=r.best_fev1_liters or 0.0,
                    best_fvc_liters=r.best_fvc_liters or 0.0,
                    fev1_fvc_ratio=r.fev1_fvc_ratio or 0.0,
                    fev1_percent_predicted=r.fev1_percent_predicted or 0.0,
                    peak_expiratory_flow_l_min=r.peak_expiratory_flow_l_min or 0.0,
                    maneuver_count=rc,
                    session_quality_grade="A",
                )
                if dev_type == SensorDeviceType.SPIROMETRY
                else None
            )
            bp = (
                BloodPressureDailySummary(
                    mean_systolic_mmhg=r.mean_systolic_mmhg or 0.0,
                    mean_diastolic_mmhg=r.mean_diastolic_mmhg or 0.0,
                    mean_arterial_pressure_mmhg=r.mean_arterial_pressure_mmhg or 0.0,
                    readings_count=rc,
                )
                if dev_type == SensorDeviceType.BLOOD_PRESSURE
                else None
            )

            summaries.append(
                DailySummaryEndpoints(
                    summary_id=r.id,
                    study_id=r.study_id,
                    site_id=r.site_id,
                    subject_id=r.subject_id,
                    summary_date=r.summary_date,
                    device_type=dev_type,
                    device_token=r.device_token,
                    cgm=cgm,
                    actigraphy=act,
                    spirometry=spiro,
                    blood_pressure=bp,
                    lakehouse_record_id=r.lakehouse_record_id,
                    data_hash=r.sha256_hash,
                    created_at=r.created_at,
                )
            )

        return summaries

    async def list_safety_alerts(
        self,
        study_id: str,
        subject_id: str | None = None,
        severity: str | None = None,
        alert_type: str | None = None,
        is_acknowledged: bool | None = None,
    ) -> list[SensorSafetyAlert]:
        """List triggered clinical safety threshold alerts."""
        records = await self.repository.list_safety_alerts(
            study_id=study_id,
            subject_id=subject_id,
            severity=severity,
            alert_type=alert_type,
            is_acknowledged=is_acknowledged,
        )
        return [
            SensorSafetyAlert(
                alert_id=r.alert_id,
                study_id=r.study_id,
                site_id=r.site_id,
                subject_id=r.subject_id,
                device_type=SensorDeviceType(r.device_type),
                device_token=r.device_token,
                alert_type=r.alert_type,
                severity=r.severity,
                parameter_name=r.parameter_name,
                observed_value=r.observed_value,
                threshold_value=r.threshold_value,
                unit=r.unit,
                triggered_at=r.triggered_at,
                is_acknowledged=r.is_acknowledged,
                acknowledged_by=r.acknowledged_by,
                acknowledged_at=r.acknowledged_at,
                acknowledgment_notes=r.acknowledgment_notes,
                audit_signature=r.audit_signature,
            )
            for r in records
        ]

    async def acknowledge_safety_alert(
        self,
        alert_id: str,
        user_id: str,
        notes: str,
        audit_signature: str | None = None,
    ) -> SensorSafetyAlert | None:
        """Acknowledge a safety threshold alert with 21 CFR Part 11 electronic sign-off."""
        r = await self.repository.acknowledge_safety_alert(
            alert_id=alert_id,
            acknowledged_by=user_id,
            notes=notes,
            audit_signature=audit_signature,
        )
        if not r:
            return None
        return SensorSafetyAlert(
            alert_id=r.alert_id,
            study_id=r.study_id,
            site_id=r.site_id,
            subject_id=r.subject_id,
            device_type=SensorDeviceType(r.device_type),
            device_token=r.device_token,
            alert_type=r.alert_type,
            severity=r.severity,
            parameter_name=r.parameter_name,
            observed_value=r.observed_value,
            threshold_value=r.threshold_value,
            unit=r.unit,
            triggered_at=r.triggered_at,
            is_acknowledged=r.is_acknowledged,
            acknowledged_by=r.acknowledged_by,
            acknowledged_at=r.acknowledged_at,
            acknowledgment_notes=r.acknowledgment_notes,
            audit_signature=r.audit_signature,
        )

    async def get_dashboard_metrics(self, study_id: str) -> SensorDashboardMetrics:
        """Compute real-time IoT sensor telemetry metrics across a study."""
        raw_metrics = await self.repository.get_sensor_metrics(study_id)
        return SensorDashboardMetrics(
            study_id=raw_metrics["study_id"],
            total_streams_ingested=raw_metrics["total_streams_ingested"],
            total_readings_ingested=raw_metrics["total_readings_ingested"],
            active_devices_count=raw_metrics["active_devices_count"],
            device_breakdown=raw_metrics["device_breakdown"],
            total_safety_alerts=raw_metrics["total_safety_alerts"],
            unacknowledged_alerts=raw_metrics["unacknowledged_alerts"],
            mean_time_in_range_percent=raw_metrics["mean_time_in_range_percent"],
            mean_daily_steps=raw_metrics["mean_daily_steps"],
            mean_resting_hr_bpm=raw_metrics["mean_resting_hr_bpm"],
            mean_fev1_liters=raw_metrics["mean_fev1_liters"],
            last_telemetry_received_at=raw_metrics["last_telemetry_received_at"],
        )
