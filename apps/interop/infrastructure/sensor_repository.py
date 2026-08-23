"""SQLAlchemy async repository for IoT Sensor Telemetry, Device Registry, and Safety Alerts.

Requirements: PRD-DCT-002, PRD-DCT-001, ADR-2199, 21 CFR Part 11
"""

from __future__ import annotations

import uuid
from datetime import UTC, datetime
from typing import Any

from sqlalchemy import case, func, select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.interop.domain.ports import ISensorRepositoryPort
from apps.interop.infrastructure.sensor_models import (
    DeviceRegistrationRecord,
    SensorDailySummaryRecord,
    SensorSafetyAlertRecord,
    SensorTelemetryStreamRecord,
)


class SensorRepository(ISensorRepositoryPort):
    """Async repository providing GxP compliant persistence for medical IoT sensor streams."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    async def get_by_id(self, entity_id: str) -> Any | None:
        return await self.get_device_by_token(entity_id)

    async def save(self, entity: Any) -> Any:
        return await self.save_device(entity)

    # =========================================================================
    # Device Registration & Mapping
    # =========================================================================

    async def save_device(self, device: Any) -> Any:
        """Persist a new or updated medical IoT device registration."""
        if isinstance(device, DeviceRegistrationRecord):
            record = device
        else:
            d_type = (
                device.device_type.value
                if hasattr(device.device_type, "value")
                else str(device.device_type)
            )
            cal_stat = (
                device.calibration_status.value
                if hasattr(device.calibration_status, "value")
                else str(device.calibration_status)
            )
            record = DeviceRegistrationRecord(
                id=getattr(device, "device_id", None)
                or (
                    device.get("id") if isinstance(device, dict) else str(uuid.uuid4())
                ),
                study_id=getattr(device, "study_id", None)
                or (device.get("study_id") if isinstance(device, dict) else ""),
                site_id=getattr(device, "site_id", None)
                or (device.get("site_id") if isinstance(device, dict) else None),
                subject_id=getattr(device, "subject_id", None)
                or (device.get("subject_id") if isinstance(device, dict) else ""),
                device_type=d_type,
                vendor=getattr(device, "vendor", None)
                or (device.get("vendor") if isinstance(device, dict) else ""),
                model_name=getattr(device, "model_name", None)
                or (device.get("model_name") if isinstance(device, dict) else ""),
                udi_di=getattr(device, "udi_di", None)
                or (device.get("udi_di") if isinstance(device, dict) else None),
                udi_pi_serial=getattr(device, "udi_pi_serial", None)
                or (device.get("udi_pi_serial") if isinstance(device, dict) else None),
                udi_pi_lot=getattr(device, "udi_pi_lot", None)
                or (device.get("udi_pi_lot") if isinstance(device, dict) else None),
                mac_address_masked=getattr(device, "mac_address_masked", None)
                or (
                    device.get("mac_address_masked")
                    if isinstance(device, dict)
                    else None
                ),
                device_token=getattr(device, "device_token", None)
                or (device.get("device_token") if isinstance(device, dict) else ""),
                firmware_version=getattr(device, "firmware_version", None)
                or (
                    device.get("firmware_version") if isinstance(device, dict) else None
                ),
                calibration_status=cal_stat,
                calibration_date=getattr(device, "calibration_date", None)
                or (
                    device.get("calibration_date") if isinstance(device, dict) else None
                ),
                is_active=getattr(device, "is_active", True)
                if not isinstance(device, dict)
                else device.get("is_active", True),
                created_at=getattr(device, "created_at", None)
                or (
                    device.get("created_at")
                    if isinstance(device, dict)
                    else datetime.now(UTC)
                ),
                created_by=getattr(device, "created_by", "system")
                if not isinstance(device, dict)
                else device.get("created_by", "system"),
                reason_for_change=getattr(device, "reason_for_change", None)
                or (
                    device.get("reason_for_change")
                    if isinstance(device, dict)
                    else None
                ),
                version_index=getattr(device, "version_index", 1)
                if not isinstance(device, dict)
                else device.get("version_index", 1),
            )
        self.session.add(record)
        await self.session.commit()
        await self.session.refresh(record)
        return record

    async def get_device_by_token(
        self, device_token: str
    ) -> DeviceRegistrationRecord | None:
        """Find an active device registration by its GxP device token."""
        stmt = select(DeviceRegistrationRecord).where(
            DeviceRegistrationRecord.device_token == device_token,
            DeviceRegistrationRecord.is_active.is_(True),
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    async def get_device_by_udi(
        self, udi_di: str, serial: str | None = None
    ) -> DeviceRegistrationRecord | None:
        """Find a device registration by UDI Device Identifier and serial number."""
        stmt = select(DeviceRegistrationRecord).where(
            DeviceRegistrationRecord.udi_di == udi_di,
            DeviceRegistrationRecord.is_active.is_(True),
        )
        if serial:
            stmt = stmt.where(DeviceRegistrationRecord.udi_pi_serial == serial)
        result = await self.session.execute(stmt)
        return result.scalars().first()

    async def list_devices(
        self,
        study_id: str,
        site_id: str | None = None,
        subject_id: str | None = None,
        device_type: str | None = None,
    ) -> list[DeviceRegistrationRecord]:
        """List registered medical IoT devices matching filter criteria."""
        stmt = (
            select(DeviceRegistrationRecord)
            .where(
                DeviceRegistrationRecord.study_id == study_id,
                DeviceRegistrationRecord.is_active.is_(True),
            )
            .order_by(DeviceRegistrationRecord.created_at.desc())
        )
        if site_id:
            stmt = stmt.where(DeviceRegistrationRecord.site_id == site_id)
        if subject_id:
            stmt = stmt.where(DeviceRegistrationRecord.subject_id == subject_id)
        if device_type:
            stmt = stmt.where(DeviceRegistrationRecord.device_type == device_type)

        result = await self.session.execute(stmt)
        return list(result.scalars().all())

    # =========================================================================
    # Telemetry Streams
    # =========================================================================

    async def save_telemetry_stream(self, stream_record: Any) -> Any:
        """Persist an ingested continuous biometric telemetry stream batch."""
        if isinstance(stream_record, SensorTelemetryStreamRecord):
            record = stream_record
        elif isinstance(stream_record, dict):
            record = SensorTelemetryStreamRecord(**stream_record)
        else:
            record = SensorTelemetryStreamRecord(
                id=getattr(stream_record, "stream_id", str(uuid.uuid4())),
                study_id=stream_record.study_id,
                site_id=getattr(stream_record, "site_id", None),
                subject_id=stream_record.subject_id,
                device_token=stream_record.device_token,
                device_type=stream_record.device_type.value
                if hasattr(stream_record.device_type, "value")
                else str(stream_record.device_type),
                batch_id=getattr(
                    stream_record, "batch_id", f"BATCH-{uuid.uuid4().hex[:10].upper()}"
                ),
                received_at=getattr(
                    stream_record, "gateway_receive_timestamp", datetime.now(UTC)
                ),
                clock_drift_offset_ms=getattr(
                    stream_record, "clock_drift_offset_ms", 0
                ),
                reading_count=getattr(stream_record, "reading_count", 0),
                start_timestamp=getattr(
                    stream_record, "start_timestamp", datetime.now(UTC)
                ),
                end_timestamp=getattr(
                    stream_record, "end_timestamp", datetime.now(UTC)
                ),
                omh_payload=getattr(stream_record, "omh_payload", []),
                fhir_observations=getattr(stream_record, "fhir_observations", []),
                raw_data_hash=getattr(stream_record, "raw_data_hash", ""),
                feed_lakehouse_record_ids=getattr(
                    stream_record, "feed_lakehouse_record_ids", []
                ),
                created_at=datetime.now(UTC),
            )
        self.session.add(record)
        await self.session.commit()
        await self.session.refresh(record)
        return record

    async def list_telemetry_streams(
        self,
        study_id: str,
        subject_id: str | None = None,
        device_token: str | None = None,
        device_type: str | None = None,
        limit: int = 100,
    ) -> list[SensorTelemetryStreamRecord]:
        """List telemetry stream batches for a study or subject."""
        stmt = (
            select(SensorTelemetryStreamRecord)
            .where(SensorTelemetryStreamRecord.study_id == study_id)
            .order_by(SensorTelemetryStreamRecord.received_at.desc())
            .limit(limit)
        )
        if subject_id:
            stmt = stmt.where(SensorTelemetryStreamRecord.subject_id == subject_id)
        if device_token:
            stmt = stmt.where(SensorTelemetryStreamRecord.device_token == device_token)
        if device_type:
            stmt = stmt.where(SensorTelemetryStreamRecord.device_type == device_type)

        result = await self.session.execute(stmt)
        return list(result.scalars().all())

    # =========================================================================
    # Daily Summary Endpoints
    # =========================================================================

    async def save_daily_summary(self, summary: Any) -> Any:
        """Persist calculated daily summary aggregate endpoints."""
        if isinstance(summary, SensorDailySummaryRecord):
            record = summary
        elif isinstance(summary, dict):
            record = SensorDailySummaryRecord(**summary)
        else:
            cgm = getattr(summary, "cgm", None)
            act = getattr(summary, "actigraphy", None)
            spiro = getattr(summary, "spirometry", None)
            bp = getattr(summary, "blood_pressure", None)
            rc = getattr(summary, "readings_count", None)
            if rc is None or rc < 1:
                if cgm and hasattr(cgm, "readings_count"):
                    rc = cgm.readings_count
                elif act and hasattr(act, "readings_count"):
                    rc = act.readings_count
                elif spiro and hasattr(spiro, "readings_count"):
                    rc = spiro.readings_count
                elif bp and hasattr(bp, "readings_count"):
                    rc = bp.readings_count
                else:
                    rc = 1
            record = SensorDailySummaryRecord(
                id=getattr(summary, "summary_id", str(uuid.uuid4())),
                study_id=summary.study_id,
                site_id=getattr(summary, "site_id", None),
                subject_id=summary.subject_id,
                summary_date=summary.summary_date,
                device_type=summary.device_type.value
                if hasattr(summary.device_type, "value")
                else str(summary.device_type),
                device_token=summary.device_token,
                readings_count=rc,
                active_wear_hours=(cgm.active_wear_percentage / 100.0 * 24.0)
                if cgm
                else 24.0,
                tir_percentage=cgm.tir_percentage if cgm else None,
                tbr_level1_percentage=cgm.tbr_level1_percentage if cgm else None,
                tbr_level2_percentage=cgm.tbr_level2_percentage if cgm else None,
                tar_level1_percentage=cgm.tar_level1_percentage if cgm else None,
                tar_level2_percentage=cgm.tar_level2_percentage if cgm else None,
                mean_glucose_mg_dl=cgm.mean_glucose_mg_dl if cgm else None,
                glucose_sd=cgm.glucose_sd if cgm else None,
                glucose_cv_percent=cgm.glucose_cv_percent if cgm else None,
                glucose_management_indicator=cgm.glucose_management_indicator
                if cgm
                else None,
                total_steps=act.total_steps if act else None,
                mean_cadence_spm=act.mean_cadence_spm if act else None,
                peak_cadence_spm=act.peak_cadence_spm if act else None,
                active_minutes=act.active_minutes if act else None,
                sedentary_minutes=act.sedentary_minutes if act else None,
                sleep_duration_minutes=act.sleep_duration_minutes if act else None,
                sleep_efficiency_percent=act.sleep_efficiency_percent if act else None,
                resting_heart_rate_bpm=act.resting_heart_rate_bpm if act else None,
                best_fev1_liters=spiro.best_fev1_liters if spiro else None,
                best_fvc_liters=spiro.best_fvc_liters if spiro else None,
                fev1_fvc_ratio=spiro.fev1_fvc_ratio if spiro else None,
                fev1_percent_predicted=spiro.fev1_percent_predicted if spiro else None,
                peak_expiratory_flow_l_min=spiro.peak_expiratory_flow_l_min
                if spiro
                else None,
                mean_systolic_mmhg=bp.mean_systolic_mmhg if bp else None,
                mean_diastolic_mmhg=bp.mean_diastolic_mmhg if bp else None,
                mean_arterial_pressure_mmhg=bp.mean_arterial_pressure_mmhg
                if bp
                else None,
                summary_manifest=summary.model_dump(mode="json")
                if hasattr(summary, "model_dump")
                else {},
                lakehouse_record_id=getattr(summary, "summary_id", None),
                sha256_hash=getattr(summary, "data_hash", ""),
                created_at=getattr(summary, "created_at", datetime.now(UTC)),
            )
        self.session.add(record)
        await self.session.commit()
        await self.session.refresh(record)
        return record

    async def list_daily_summaries(
        self,
        study_id: str,
        subject_id: str | None = None,
        device_type: str | None = None,
        start_date: datetime | None = None,
        end_date: datetime | None = None,
    ) -> list[SensorDailySummaryRecord]:
        """Query daily summary endpoints with filtering."""
        stmt = (
            select(SensorDailySummaryRecord)
            .where(SensorDailySummaryRecord.study_id == study_id)
            .order_by(SensorDailySummaryRecord.summary_date.desc())
        )
        if subject_id:
            stmt = stmt.where(SensorDailySummaryRecord.subject_id == subject_id)
        if device_type:
            stmt = stmt.where(SensorDailySummaryRecord.device_type == device_type)
        if start_date:
            stmt = stmt.where(SensorDailySummaryRecord.summary_date >= start_date)
        if end_date:
            stmt = stmt.where(SensorDailySummaryRecord.summary_date <= end_date)

        result = await self.session.execute(stmt)
        return list(result.scalars().all())

    # =========================================================================
    # Automated Safety Threshold Alerts
    # =========================================================================

    async def save_safety_alerts(self, alerts: list[Any]) -> list[Any]:
        """Bulk persist automated safety threshold alerts."""
        if not alerts:
            return []
        records = []
        for alert in alerts:
            if isinstance(alert, SensorSafetyAlertRecord):
                rec = alert
            elif isinstance(alert, dict):
                rec = SensorSafetyAlertRecord(**alert)
            else:
                rec = SensorSafetyAlertRecord(
                    id=str(uuid.uuid4()),
                    alert_id=alert.alert_id,
                    study_id=alert.study_id,
                    site_id=alert.site_id,
                    subject_id=alert.subject_id,
                    device_type=alert.device_type.value
                    if hasattr(alert.device_type, "value")
                    else str(alert.device_type),
                    device_token=alert.device_token,
                    alert_type=alert.alert_type.value
                    if hasattr(alert.alert_type, "value")
                    else str(alert.alert_type),
                    severity=alert.severity.value
                    if hasattr(alert.severity, "value")
                    else str(alert.severity),
                    parameter_name=alert.parameter_name,
                    observed_value=alert.observed_value,
                    threshold_value=alert.threshold_value,
                    unit=alert.unit,
                    triggered_at=alert.triggered_at,
                    is_acknowledged=alert.is_acknowledged,
                    created_at=datetime.now(UTC),
                )
            self.session.add(rec)
            records.append(rec)
        await self.session.commit()
        for r in records:
            await self.session.refresh(r)
        return records

    async def get_safety_alert_by_id(
        self, alert_id: str
    ) -> SensorSafetyAlertRecord | None:
        """Find a safety alert by its unique alert identifier."""
        stmt = select(SensorSafetyAlertRecord).where(
            SensorSafetyAlertRecord.alert_id == alert_id
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    async def list_safety_alerts(
        self,
        study_id: str,
        subject_id: str | None = None,
        severity: str | None = None,
        alert_type: str | None = None,
        is_acknowledged: bool | None = None,
        limit: int = 100,
    ) -> list[SensorSafetyAlertRecord]:
        """List safety threshold alerts with filtering."""
        stmt = (
            select(SensorSafetyAlertRecord)
            .where(SensorSafetyAlertRecord.study_id == study_id)
            .order_by(SensorSafetyAlertRecord.triggered_at.desc())
            .limit(limit)
        )
        if subject_id:
            stmt = stmt.where(SensorSafetyAlertRecord.subject_id == subject_id)
        if severity:
            stmt = stmt.where(SensorSafetyAlertRecord.severity == severity)
        if alert_type:
            stmt = stmt.where(SensorSafetyAlertRecord.alert_type == alert_type)
        if is_acknowledged is not None:
            stmt = stmt.where(
                SensorSafetyAlertRecord.is_acknowledged.is_(is_acknowledged)
            )

        result = await self.session.execute(stmt)
        return list(result.scalars().all())

    async def acknowledge_safety_alert(
        self,
        alert_id: str,
        acknowledged_by: str,
        notes: str | None = None,
        audit_signature: str | None = None,
        action_taken: str | None = None,
        investigator_notes: str | None = None,
    ) -> SensorSafetyAlertRecord | None:
        """Acknowledge a safety alert with 21 CFR Part 11 sign-off."""
        alert = await self.get_safety_alert_by_id(alert_id)
        if not alert:
            return None

        final_notes = notes or investigator_notes or action_taken or "Acknowledged"
        alert.is_acknowledged = True
        alert.acknowledged_by = acknowledged_by
        alert.acknowledged_at = datetime.now(UTC)
        alert.acknowledgment_notes = final_notes
        alert.audit_signature = (
            audit_signature
            or f"SIG-ACK-{alert_id}-{datetime.now(UTC).strftime('%Y%m%d%H%M%S')}"
        )

        await self.session.commit()
        await self.session.refresh(alert)
        return alert

    # =========================================================================
    # Aggregate Metrics & Reporting
    # =========================================================================

    async def get_sensor_metrics(self, study_id: str) -> dict[str, Any]:
        """Compute aggregated metrics for connected IoT sensors across a clinical study."""
        # 1. Total streams & readings
        streams_stmt = select(
            func.count(SensorTelemetryStreamRecord.id),
            func.sum(SensorTelemetryStreamRecord.reading_count),
            func.max(SensorTelemetryStreamRecord.received_at),
        ).where(SensorTelemetryStreamRecord.study_id == study_id)
        streams_res = await self.session.execute(streams_stmt)
        total_streams, total_readings, last_received = streams_res.first() or (
            0,
            0,
            None,
        )

        # 2. Registered active devices & breakdown
        dev_stmt = (
            select(
                DeviceRegistrationRecord.device_type,
                func.count(DeviceRegistrationRecord.id),
            )
            .where(
                DeviceRegistrationRecord.study_id == study_id,
                DeviceRegistrationRecord.is_active.is_(True),
            )
            .group_by(DeviceRegistrationRecord.device_type)
        )
        dev_res = await self.session.execute(dev_stmt)
        dev_breakdown = {row[0]: row[1] for row in dev_res.all()}
        active_devices = sum(dev_breakdown.values())

        # 3. Safety alerts count & unacknowledged
        alert_stmt = select(
            func.count(SensorSafetyAlertRecord.id),
            func.sum(
                case((SensorSafetyAlertRecord.is_acknowledged.is_(False), 1), else_=0)
            ),
        ).where(SensorSafetyAlertRecord.study_id == study_id)
        alert_res = await self.session.execute(alert_stmt)
        total_alerts, unack_alerts = alert_res.first() or (0, 0)

        # 4. Study-wide clinical averages from daily summaries
        cgm_avg_stmt = select(func.avg(SensorDailySummaryRecord.tir_percentage)).where(
            SensorDailySummaryRecord.study_id == study_id,
            SensorDailySummaryRecord.device_type == "CGM",
            SensorDailySummaryRecord.tir_percentage.isnot(None),
        )
        cgm_avg_res = await self.session.execute(cgm_avg_stmt)
        mean_tir = cgm_avg_res.scalar_one_or_none()

        act_avg_stmt = select(
            func.avg(SensorDailySummaryRecord.total_steps),
            func.avg(SensorDailySummaryRecord.resting_heart_rate_bpm),
        ).where(
            SensorDailySummaryRecord.study_id == study_id,
            SensorDailySummaryRecord.device_type == "ACTIGRAPHY",
        )
        act_avg_res = await self.session.execute(act_avg_stmt)
        mean_steps, mean_rhr = act_avg_res.first() or (None, None)

        spiro_avg_stmt = select(
            func.avg(SensorDailySummaryRecord.best_fev1_liters)
        ).where(
            SensorDailySummaryRecord.study_id == study_id,
            SensorDailySummaryRecord.device_type == "SPIROMETRY",
            SensorDailySummaryRecord.best_fev1_liters.isnot(None),
        )
        spiro_avg_res = await self.session.execute(spiro_avg_stmt)
        mean_fev1 = spiro_avg_res.scalar_one_or_none()

        return {
            "study_id": study_id,
            "total_streams_ingested": int(total_streams or 0),
            "total_readings_ingested": int(total_readings or 0),
            "active_devices_count": int(active_devices),
            "device_breakdown": dev_breakdown,
            "total_safety_alerts": int(total_alerts or 0),
            "unacknowledged_alerts": int(unack_alerts or 0),
            "mean_time_in_range_percent": (
                round(float(mean_tir), 1) if mean_tir is not None else None
            ),
            "mean_daily_steps": (
                round(float(mean_steps), 0) if mean_steps is not None else None
            ),
            "mean_resting_hr_bpm": (
                round(float(mean_rhr), 1) if mean_rhr is not None else None
            ),
            "mean_fev1_liters": (
                round(float(mean_fev1), 3) if mean_fev1 is not None else None
            ),
            "last_telemetry_received_at": last_received,
        }

    async def get_sensor_dashboard_metrics(self, study_id: str) -> dict[str, Any]:
        """Aggregate metrics for connected IoT sensors across a clinical study."""
        return await self.get_sensor_metrics(study_id)
