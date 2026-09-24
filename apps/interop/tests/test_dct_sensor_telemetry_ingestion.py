"""Comprehensive unit and integration test suite for Continuous Biometric IoT Sensor Telemetry Ingestion.

Validates:
- Automated HMAC-SHA256 pseudonymization & device MAC/UDI mapping
- Bidirectional clock drift computation and sample timestamp calibration
- Open mHealth (OMH) & HL7 FHIR Observation schemas normalization
- Aggregate daily summary calculations (Time-in-Range TIR, mean glucose, steps/cadence, resting HR, FEV1)
- Automated clinical safety threshold alert evaluation & Part 11 electronic acknowledgment
- Feeding validated sensor endpoints into CDR Lakehouse
- REST API endpoint contracts and gateway security

Requirements: PRD-DCT-002, PRD-DCT-001, ADR-2199, 21 CFR Part 11
"""

from __future__ import annotations

from datetime import UTC, datetime, timedelta

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.interop.adapters.database import db_manager
from apps.interop.adapters.lakehouse_models import LakehouseCanonicalRecord
from apps.interop.adapters.lakehouse_repository import LakehouseRepository
from apps.interop.adapters.models import Base
from apps.interop.adapters.sensor_repository import SensorRepository
from apps.interop.application.sensor_service import SensorService
from apps.interop.domain.lakehouse_models import (
    DataStreamType,
)
from apps.interop.domain.sensor_models import (
    AlertSeverity,
    CalibrationState,
    DeviceRegistrationRequest,
    RawSensorReading,
    SensorAlertType,
    SensorDeviceType,
    SensorIngestStreamPayload,
)
from apps.interop.domain.sensor_processor import (
    calculate_daily_summary_endpoints,
    compute_clock_drift_offset_ms,
    correct_reading_timestamps,
    evaluate_safety_threshold_alerts,
    generate_device_token,
    mask_mac_address,
    parse_udi,
    pseudonymize_subject_id,
    register_device_entity,
    transform_to_fhir_observations,
    transform_to_omh_packets,
)
from apps.interop.main import app
from packages.security.rbac_helpers import build_gateway_headers


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """Setup clean in-memory SQLite database for DCT sensor telemetry tests."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_auth_headers(
    roles: str = "data_manager",
    user_id: str = "user_dct_crc_01",
    change_reason: str = "GxP DCT sensor telemetry testing",
) -> dict[str, str]:
    """Helper to generate valid gateway signed headers."""
    return build_gateway_headers(
        user_id=user_id,
        roles=roles,
        change_reason=change_reason,
    )


# =========================================================================
# Unit Tests: Pseudonymization, UDI & MAC Mapping
# =========================================================================


class TestPseudonymizationAndDeviceMapping:
    """Test automated pseudonymization, MAC masking, and UDI mapping.

    @req:PRD-DCT-002
    @req:PRD-DCT-001
    @req:ADR-2199
    """

    def test_pseudonymize_subject_id(self) -> None:
        """Verify deterministic, irreversible pseudonymization of subject identifiers.

        @req:PRD-DCT-002
        @req:ADR-2199
        """
        raw_id = "SUBJ-BOSTON-101"
        p1 = pseudonymize_subject_id(raw_id)
        p2 = pseudonymize_subject_id(raw_id)

        assert p1.startswith("SUBJ-PSEUDO-")
        assert p1 == p2
        assert raw_id not in p1

        p_other = pseudonymize_subject_id("SUBJ-BOSTON-102")
        assert p1 != p_other

    def test_generate_device_token(self) -> None:
        """Verify device token generation masks physical MAC / serial while maintaining GxP lineage.

        @req:PRD-DCT-002
        @req:ADR-2199
        """
        token1 = generate_device_token("Dexcom", "G7", "SN-987654321")
        token2 = generate_device_token("Dexcom", "G7", "SN-987654321")
        assert token1.startswith("DEV-DEX-")
        assert token1 == token2

        token_diff = generate_device_token("Abbott", "Libre3", "SN-987654321")
        assert token1 != token_diff

    def test_mask_mac_address(self) -> None:
        """Verify Bluetooth MAC address masking protects hardware identifier while keeping diagnostic octets.

        @req:PRD-DCT-002
        @req:ADR-2199
        """
        raw_mac = "00:1A:7D:DA:71:13"
        masked = mask_mac_address(raw_mac)
        assert masked == "XX:XX:XX:DA:71:13"
        assert mask_mac_address(None) is None

    def test_parse_udi(self) -> None:
        """Verify parsing of FDA Unique Device Identifier (UDI) barcodes.

        @req:PRD-DCT-002
        @req:ADR-2199
        """
        udi_str = "(01)00850012345678(21)SN556677(17)281231(10)LOT900"
        parsed = parse_udi(udi_str)
        assert parsed["udi_di"] == "00850012345678"
        assert parsed["serial"] == "SN556677"
        assert parsed["expiration"] == "281231"
        assert parsed["lot"] == "LOT900"

    def test_register_device_entity(self) -> None:
        """Verify creation of normalized device registration entity.

        @req:PRD-DCT-002
        @req:ADR-2199
        """
        req = DeviceRegistrationRequest(
            study_id="STUDY-901",
            site_id="SITE-101",
            subject_id="PATIENT-001",
            device_type=SensorDeviceType.CGM,
            vendor="Dexcom",
            model_name="G7",
            device_udi="(01)00850012345678(21)SN123",
            mac_address="AA:BB:CC:DD:EE:FF",
            serial_number="SN123",
            firmware_version="v2.1.0",
            calibration_status=CalibrationState.CALIBRATED,
            reason_for_change="Baseline enrollment device allocation",
        )
        dev = register_device_entity(req, user_id="crc_sarah")
        assert dev.study_id == "STUDY-901"
        assert dev.subject_id.startswith("SUBJ-PSEUDO-")
        assert dev.mac_address_masked == "XX:XX:XX:DD:EE:FF"
        assert dev.device_token.startswith("DEV-DEX-")
        assert dev.calibration_status == CalibrationState.CALIBRATED


# =========================================================================
# Unit Tests: Clock Drift Correction
# =========================================================================


class TestClockDriftCorrection:
    """Test clock drift calculation and timestamp compensation.

    @req:PRD-DCT-002
    @req:ADR-2199
    """

    def test_compute_clock_drift_offset(self) -> None:
        """Verify computation of clock drift between device RTC and server clock.

        @req:PRD-DCT-002
        @req:ADR-2199
        """
        base_time = datetime(2026, 8, 23, 12, 0, 0, tzinfo=UTC)
        device_time = base_time - timedelta(seconds=12.5)  # Device is 12.5s behind
        gateway_time = base_time

        offset_ms = compute_clock_drift_offset_ms(device_time, gateway_time)
        assert abs(offset_ms - 12500.0) < 0.001

    def test_correct_reading_timestamps(self) -> None:
        """Verify timestamp calibration across a batch of high-frequency samples.

        @req:PRD-DCT-002
        @req:ADR-2199
        """
        t0 = datetime(2026, 8, 23, 10, 0, 0, tzinfo=UTC)
        readings = [
            RawSensorReading(
                timestamp=t0,
                metric_name="glucose",
                value=110.0,
                unit="mg/dL",
            ),
            RawSensorReading(
                timestamp=t0 + timedelta(minutes=5),
                metric_name="glucose",
                value=115.0,
                unit="mg/dL",
            ),
        ]

        drift_ms = 5000.0  # Device was 5 seconds behind
        calibrated = correct_reading_timestamps(readings, drift_ms)

        assert len(calibrated) == 2
        assert calibrated[0].timestamp == t0 + timedelta(seconds=5)
        assert calibrated[1].timestamp == t0 + timedelta(minutes=5, seconds=5)
        assert calibrated[0].extra_attributes["clock_drift_offset_ms"] == 5000.0


# =========================================================================
# Unit Tests: Open mHealth & HL7 FHIR Normalization
# =========================================================================


class TestOpenMHealthAndFHIRNormalization:
    """Test transformation into Open mHealth standard schemas and HL7 FHIR Observations.

    @req:PRD-DCT-002
    @req:PRD-DCT-001
    @req:ADR-2199
    """

    def test_transform_cgm_to_omh_and_fhir(self) -> None:
        """Verify CGM stream normalization into OMH blood-glucose and FHIR Observation with LOINC 2339-0.

        @req:PRD-DCT-002
        @req:ADR-2199
        """
        now = datetime.now(UTC)
        payload = SensorIngestStreamPayload(
            study_id="STUDY-901",
            subject_id="SUBJ-001",
            device_type=SensorDeviceType.CGM,
            vendor="Dexcom",
            model_name="G7",
            device_sync_timestamp=now,
            gateway_receive_timestamp=now,
            readings=[
                RawSensorReading(
                    timestamp=now,
                    metric_name="glucose",
                    value=125.0,
                    unit="mg/dL",
                )
            ],
        )

        omh = transform_to_omh_packets(
            payload, "SUBJ-PSEUDO-123", "DEV-DEX-ABC", payload.readings
        )
        assert len(omh) == 1
        assert omh[0].header["schema_id"]["name"] == "blood-glucose"
        assert omh[0].body["blood_glucose"]["value"] == 125.0
        assert omh[0].body["blood_glucose"]["unit"] == "mg/dL"
        assert omh[0].body["specimen_source"] == "interstitial fluid"

        fhir = transform_to_fhir_observations(
            payload, "SUBJ-PSEUDO-123", "DEV-DEX-ABC", payload.readings
        )
        assert len(fhir) == 1
        assert fhir[0].resourceType == "Observation"
        assert fhir[0].status == "final"
        assert fhir[0].code["coding"][0]["code"] == "2339-0"
        assert fhir[0].subject["reference"] == "Patient/SUBJ-PSEUDO-123"
        assert fhir[0].device["reference"] == "Device/DEV-DEX-ABC"
        assert fhir[0].valueQuantity["value"] == 125.0

    def test_transform_actigraphy_to_omh_and_fhir(self) -> None:
        """Verify actigraphy steps and heart rate normalization into OMH and FHIR.

        @req:PRD-DCT-002
        @req:ADR-2199
        """
        now = datetime.now(UTC)
        payload = SensorIngestStreamPayload(
            study_id="STUDY-901",
            subject_id="SUBJ-001",
            device_type=SensorDeviceType.ACTIGRAPHY,
            vendor="ActiGraph",
            model_name="wGT3X-BT",
            device_sync_timestamp=now,
            gateway_receive_timestamp=now,
            readings=[
                RawSensorReading(
                    timestamp=now,
                    metric_name="step_count",
                    value=450.0,
                    unit="steps",
                ),
                RawSensorReading(
                    timestamp=now,
                    metric_name="heart_rate",
                    value=72.0,
                    unit="bpm",
                ),
            ],
        )

        omh = transform_to_omh_packets(
            payload, "SUBJ-PSEUDO-123", "DEV-ACT-XYZ", payload.readings
        )
        assert len(omh) == 2
        assert omh[0].header["schema_id"]["name"] == "step-count"
        assert omh[0].body["step_count"] == 450
        assert omh[1].header["schema_id"]["name"] == "heart-rate"
        assert omh[1].body["heart_rate"]["value"] == 72.0

        fhir = transform_to_fhir_observations(
            payload, "SUBJ-PSEUDO-123", "DEV-ACT-XYZ", payload.readings
        )
        assert len(fhir) == 2
        assert fhir[0].code["coding"][0]["code"] == "55423-8"  # Steps LOINC
        assert fhir[1].code["coding"][0]["code"] == "8867-4"  # Heart rate LOINC

    def test_transform_blood_pressure_to_fhir_components(self) -> None:
        """Verify blood pressure panel multi-component FHIR Observation generation.

        @req:PRD-DCT-002
        @req:ADR-2199
        """
        now = datetime.now(UTC)
        payload = SensorIngestStreamPayload(
            study_id="STUDY-901",
            subject_id="SUBJ-001",
            device_type=SensorDeviceType.BLOOD_PRESSURE,
            vendor="Omron",
            model_name="Platinum",
            device_sync_timestamp=now,
            gateway_receive_timestamp=now,
            readings=[
                RawSensorReading(
                    timestamp=now,
                    metric_name="blood_pressure",
                    value=124.0,
                    unit="mmHg",
                    extra_attributes={"systolic_bp": 124.0, "diastolic_bp": 82.0},
                )
            ],
        )

        fhir = transform_to_fhir_observations(
            payload, "SUBJ-PSEUDO-123", "DEV-OMR-999", payload.readings
        )
        assert len(fhir) == 1
        assert fhir[0].code["coding"][0]["code"] == "85354-9"  # BP panel
        assert fhir[0].component is not None
        assert len(fhir[0].component) == 2
        assert fhir[0].component[0]["code"]["coding"][0]["code"] == "8480-6"  # SBP
        assert fhir[0].component[0]["valueQuantity"]["value"] == 124.0
        assert fhir[0].component[1]["code"]["coding"][0]["code"] == "8462-4"  # DBP
        assert fhir[0].component[1]["valueQuantity"]["value"] == 82.0


# =========================================================================
# Unit Tests: Daily Summary Endpoints & Math Calculations
# =========================================================================


class TestDailySummaryEndpointsCalculation:
    """Test clinical calculation of daily summary endpoints.

    @req:PRD-DCT-002
    @req:ADR-2199
    """

    def test_cgm_time_in_range_and_metrics(self) -> None:
        """Verify CGM Time-in-Range (TIR), TBR, TAR, CV%, and GMI calculations.

        @req:PRD-DCT-002
        @req:ADR-2199
        """
        now = datetime.now(UTC)
        # Create distribution of glucose values:
        # 1 reading in TBR L2 (<54): 45.0
        # 1 reading in TBR L1 (54-69): 60.0
        # 6 readings in Target (70-180): 100, 110, 120, 130, 140, 150
        # 1 reading in TAR L1 (181-250): 200.0
        # 1 reading in TAR L2 (>250): 280.0
        # Total = 10 readings
        vals = [45.0, 60.0, 100.0, 110.0, 120.0, 130.0, 140.0, 150.0, 200.0, 280.0]
        readings = [
            RawSensorReading(
                timestamp=now + timedelta(minutes=i * 5),
                metric_name="glucose",
                value=v,
                unit="mg/dL",
            )
            for i, v in enumerate(vals)
        ]

        payload = SensorIngestStreamPayload(
            study_id="STUDY-901",
            subject_id="SUBJ-001",
            device_type=SensorDeviceType.CGM,
            vendor="Dexcom",
            model_name="G7",
            device_sync_timestamp=now,
            gateway_receive_timestamp=now,
            readings=readings,
        )

        summary = calculate_daily_summary_endpoints(
            payload, "SUBJ-PSEUDO-123", "DEV-DEX-ABC", readings
        )
        assert summary.cgm is not None
        cgm = summary.cgm

        # Assert percentages out of 10
        assert cgm.tir_percentage == 60.0  # 6 / 10
        assert cgm.tbr_level1_percentage == 10.0  # 1 / 10
        assert cgm.tbr_level2_percentage == 10.0  # 1 / 10
        assert cgm.tar_level1_percentage == 10.0  # 1 / 10
        assert cgm.tar_level2_percentage == 10.0  # 1 / 10

        # Mean = sum(vals)/10 = 133.5
        assert cgm.mean_glucose_mg_dl == 133.5
        assert cgm.glucose_sd > 0
        assert cgm.glucose_cv_percent > 0
        # GMI = 3.31 + 0.02392 * 133.5 = 6.50
        expected_gmi = round(3.31 + (0.02392 * 133.5), 2)
        assert cgm.glucose_management_indicator == expected_gmi

    def test_cgm_mmol_unit_conversion(self) -> None:
        """Verify CGM automatically converts mmol/L to mg/dL before computing endpoints.

        @req:PRD-DCT-002
        @req:ADR-2199
        """
        now = datetime.now(UTC)
        # 6.0 mmol/L ~= 108.1 mg/dL (In target)
        readings = [
            RawSensorReading(
                timestamp=now,
                metric_name="glucose",
                value=6.0,
                unit="mmol/L",
            )
        ]
        payload = SensorIngestStreamPayload(
            study_id="STUDY-901",
            subject_id="SUBJ-001",
            device_type=SensorDeviceType.CGM,
            vendor="Abbott",
            model_name="Libre3",
            device_sync_timestamp=now,
            gateway_receive_timestamp=now,
            readings=readings,
        )

        summary = calculate_daily_summary_endpoints(
            payload, "SUBJ-PSEUDO-123", "DEV-ABB-123", readings
        )
        assert summary.cgm is not None
        assert summary.cgm.tir_percentage == 100.0
        assert abs(summary.cgm.mean_glucose_mg_dl - 108.11) < 0.1

    def test_spirometry_daily_summary(self) -> None:
        """Verify spirometry daily endpoints (best FEV1, FVC, ratio, % predicted).

        @req:PRD-DCT-002
        @req:ADR-2199
        """
        now = datetime.now(UTC)
        readings = [
            RawSensorReading(
                timestamp=now,
                metric_name="fev1",
                value=3.20,
                unit="L",
                extra_attributes={"fvc": 4.00, "pef": 420.0},
            ),
            RawSensorReading(
                timestamp=now + timedelta(minutes=2),
                metric_name="fev1",
                value=3.45,
                unit="L",
                extra_attributes={"fvc": 4.25, "pef": 450.0},
            ),
        ]
        payload = SensorIngestStreamPayload(
            study_id="STUDY-901",
            subject_id="SUBJ-001",
            device_type=SensorDeviceType.SPIROMETRY,
            vendor="MIR",
            model_name="Spirobank",
            device_sync_timestamp=now,
            gateway_receive_timestamp=now,
            readings=readings,
        )

        summary = calculate_daily_summary_endpoints(
            payload, "SUBJ-PSEUDO-123", "DEV-MIR-123", readings
        )
        assert summary.spirometry is not None
        s = summary.spirometry
        assert s.best_fev1_liters == 3.45
        assert s.best_fvc_liters == 4.25
        assert abs(s.fev1_fvc_ratio - (3.45 / 4.25)) < 0.005
        assert s.fev1_percent_predicted == round((3.45 / 4.0) * 100.0, 1)

    def test_blood_pressure_daily_summary(self) -> None:
        """Verify blood pressure mean SBP/DBP and Mean Arterial Pressure (MAP).

        @req:PRD-DCT-002
        @req:ADR-2199
        """
        now = datetime.now(UTC)
        readings = [
            RawSensorReading(
                timestamp=now,
                metric_name="blood_pressure",
                value=120.0,
                unit="mmHg",
                extra_attributes={"systolic_bp": 120.0, "diastolic_bp": 80.0},
            ),
            RawSensorReading(
                timestamp=now + timedelta(hours=10),
                metric_name="blood_pressure",
                value=130.0,
                unit="mmHg",
                extra_attributes={"systolic_bp": 130.0, "diastolic_bp": 86.0},
            ),
        ]
        payload = SensorIngestStreamPayload(
            study_id="STUDY-901",
            subject_id="SUBJ-001",
            device_type=SensorDeviceType.BLOOD_PRESSURE,
            vendor="Withings",
            model_name="BPM Core",
            device_sync_timestamp=now,
            gateway_receive_timestamp=now,
            readings=readings,
        )

        summary = calculate_daily_summary_endpoints(
            payload, "SUBJ-PSEUDO-123", "DEV-WIT-123", readings
        )
        assert summary.blood_pressure is not None
        bp = summary.blood_pressure
        assert bp.mean_systolic_mmhg == 125.0
        assert bp.mean_diastolic_mmhg == 83.0
        # MAP = (2*83 + 125) / 3 = 291 / 3 = 97.0
        assert bp.mean_arterial_pressure_mmhg == 97.0


# =========================================================================
# Unit Tests: Automated Safety Threshold Alerts
# =========================================================================


class TestSafetyThresholdAlertsEngine:
    """Test automated evaluation of clinical safety threshold alerts.

    @req:PRD-DCT-002
    @req:ADR-2199
    """

    def test_severe_hypoglycemia_alert(self) -> None:
        """Verify severe hypoglycemia (<54 mg/dL) triggers CRITICAL alert.

        @req:PRD-DCT-002
        @req:ADR-2199
        """
        now = datetime.now(UTC)
        readings = [
            RawSensorReading(
                timestamp=now,
                metric_name="glucose",
                value=48.0,  # Severe hypo
                unit="mg/dL",
            )
        ]
        payload = SensorIngestStreamPayload(
            study_id="STUDY-901",
            subject_id="SUBJ-001",
            device_type=SensorDeviceType.CGM,
            vendor="Dexcom",
            model_name="G7",
            device_sync_timestamp=now,
            gateway_receive_timestamp=now,
            readings=readings,
        )

        alerts = evaluate_safety_threshold_alerts(
            payload, "SUBJ-PSEUDO-123", "DEV-DEX-ABC", readings
        )
        assert len(alerts) == 1
        assert alerts[0].alert_type == SensorAlertType.SEVERE_HYPOGLYCEMIA
        assert alerts[0].severity == AlertSeverity.CRITICAL
        assert alerts[0].observed_value == 48.0

    def test_extreme_tachycardia_and_bradycardia_alerts(self) -> None:
        """Verify heart rate extremes trigger tachycardia/bradycardia alerts.

        @req:PRD-DCT-002
        @req:ADR-2199
        """
        now = datetime.now(UTC)
        readings = [
            RawSensorReading(
                timestamp=now,
                metric_name="heart_rate",
                value=135.0,  # Extreme tachy
                unit="bpm",
            ),
            RawSensorReading(
                timestamp=now + timedelta(minutes=10),
                metric_name="heart_rate",
                value=38.0,  # Severe brady
                unit="bpm",
            ),
        ]
        payload = SensorIngestStreamPayload(
            study_id="STUDY-901",
            subject_id="SUBJ-001",
            device_type=SensorDeviceType.ACTIGRAPHY,
            vendor="Garmin",
            model_name="Health",
            device_sync_timestamp=now,
            gateway_receive_timestamp=now,
            readings=readings,
        )

        alerts = evaluate_safety_threshold_alerts(
            payload, "SUBJ-PSEUDO-123", "DEV-GAR-123", readings
        )
        assert len(alerts) == 2
        assert alerts[0].alert_type == SensorAlertType.EXTREME_TACHYCARDIA
        assert alerts[0].severity == AlertSeverity.CRITICAL
        assert alerts[1].alert_type == SensorAlertType.SEVERE_BRADYCARDIA
        assert alerts[1].severity == AlertSeverity.CRITICAL

    def test_hypertensive_crisis_alert(self) -> None:
        """Verify BP >= 180/120 triggers HYPERTENSIVE_CRISIS critical alert.

        @req:PRD-DCT-002
        @req:ADR-2199
        """
        now = datetime.now(UTC)
        readings = [
            RawSensorReading(
                timestamp=now,
                metric_name="blood_pressure",
                value=185.0,
                unit="mmHg",
                extra_attributes={"systolic_bp": 185.0, "diastolic_bp": 122.0},
            )
        ]
        payload = SensorIngestStreamPayload(
            study_id="STUDY-901",
            subject_id="SUBJ-001",
            device_type=SensorDeviceType.BLOOD_PRESSURE,
            vendor="Omron",
            model_name="Platinum",
            device_sync_timestamp=now,
            gateway_receive_timestamp=now,
            readings=readings,
        )

        alerts = evaluate_safety_threshold_alerts(
            payload, "SUBJ-PSEUDO-123", "DEV-OMR-123", readings
        )
        assert len(alerts) == 1
        assert alerts[0].alert_type == SensorAlertType.HYPERTENSIVE_CRISIS
        assert alerts[0].severity == AlertSeverity.CRITICAL


# =========================================================================
# Integration Tests: Ingestion Service & CDR Lakehouse Feed
# =========================================================================


class TestSensorServiceIntegration:
    """Integration test suite for SensorService with database persistence and Lakehouse feeding.

    @req:PRD-DCT-002
    @req:PRD-DCT-001
    @req:ADR-2199
    """

    @pytest.mark.asyncio
    async def test_full_ingestion_pipeline_with_lakehouse_feed(self) -> None:
        """Verify complete ingestion workflow feeds validated endpoints into Lakehouse repository.

        @req:PRD-DCT-002
        @req:ADR-2199
        """
        async with db_manager.session_maker() as session:
            sensor_repo = SensorRepository(session)
            lakehouse_repo = LakehouseRepository(session)
            service = SensorService(sensor_repo, lakehouse_repo=lakehouse_repo)

            now = datetime.now(UTC)
            # CGM Batch with a severe hypo alert
            readings = [
                RawSensorReading(
                    timestamp=now - timedelta(minutes=15),
                    metric_name="glucose",
                    value=110.0,
                    unit="mg/dL",
                ),
                RawSensorReading(
                    timestamp=now - timedelta(minutes=10),
                    metric_name="glucose",
                    value=49.0,  # Critical hypo
                    unit="mg/dL",
                ),
                RawSensorReading(
                    timestamp=now - timedelta(minutes=5),
                    metric_name="glucose",
                    value=85.0,
                    unit="mg/dL",
                ),
            ]

            payload = SensorIngestStreamPayload(
                study_id="STUDY-901",
                site_id="SITE-101",
                subject_id="PATIENT-999",
                device_type=SensorDeviceType.CGM,
                vendor="Dexcom",
                model_name="G7",
                serial_number="SN-DEX-999",
                device_sync_timestamp=now - timedelta(seconds=2),
                gateway_receive_timestamp=now,
                readings=readings,
                feed_to_lakehouse=True,
            )

            result = await service.ingest_telemetry_stream(
                payload, user_id="crc_user", user_role="crc"
            )

            assert result.readings_ingested == 3
            assert result.subject_id_pseudonymized.startswith("SUBJ-PSEUDO-")
            assert len(result.safety_alerts_triggered) == 1
            assert (
                result.safety_alerts_triggered[0].alert_type
                == SensorAlertType.SEVERE_HYPOGLYCEMIA
            )
            assert len(result.lakehouse_record_ids) > 0

            # Verify Lakehouse canonical records were persisted in DB
            stmt = select(LakehouseCanonicalRecord).where(
                LakehouseCanonicalRecord.study_id == "STUDY-901",
                LakehouseCanonicalRecord.stream_type
                == DataStreamType.IOT_WEARABLE.value,
            )
            lh_res = await session.execute(stmt)
            lh_records = lh_res.scalars().all()
            assert len(lh_records) >= 3

            # Verify Daily Summaries
            summaries = await service.list_daily_summaries("STUDY-901")
            assert len(summaries) == 1
            assert summaries[0].cgm is not None
            assert summaries[0].cgm.readings_count == 3

            # Verify Safety Alerts & Acknowledgment
            alerts = await service.list_safety_alerts("STUDY-901")
            assert len(alerts) == 1
            alert_id = alerts[0].alert_id

            ack_result = await service.acknowledge_safety_alert(
                alert_id=alert_id,
                user_id="dr_investigator",
                notes="Patient consumed fast-acting carbohydrate oral glucose gel; recovered to 105 mg/dL.",
            )
            assert ack_result is not None
            assert ack_result.is_acknowledged is True
            assert ack_result.acknowledged_by == "dr_investigator"

            # Verify Dashboard Metrics
            metrics = await service.get_dashboard_metrics("STUDY-901")
            assert metrics.total_streams_ingested == 1
            assert metrics.total_readings_ingested == 3
            assert metrics.total_safety_alerts == 1
            assert metrics.unacknowledged_alerts == 0


# =========================================================================
# Integration Tests: REST API Endpoints & Gateway Security
# =========================================================================


class TestSensorTelemetryRestApi:
    """Integration test suite for FastAPI REST endpoints.

    @req:PRD-DCT-002
    @req:PRD-DCT-001
    @req:ADR-2199
    """

    def test_device_registration_and_list_api(self) -> None:
        """Verify POST and GET /api/v1/interop/sensors/devices endpoints.

        @req:PRD-DCT-002
        @req:ADR-2199
        """
        client = TestClient(app)
        headers = get_auth_headers(roles="crc", user_id="crc_user_01")

        reg_payload = {
            "study_id": "STUDY-API-01",
            "site_id": "SITE-01",
            "subject_id": "SUBJ-TEST-01",
            "device_type": "CGM",
            "vendor": "Dexcom",
            "model_name": "G7",
            "device_udi": "(01)00850012345678(21)SN7788",
            "mac_address": "00:1A:7D:DA:71:13",
            "serial_number": "SN7788",
            "firmware_version": "v1.0.4",
            "calibration_status": "CALIBRATED",
            "reason_for_change": "Initial device mapping test",
        }

        # 1. Register Device
        resp = client.post(
            "/api/v1/interop/sensors/devices", json=reg_payload, headers=headers
        )
        assert resp.status_code == 201
        data = resp.json()
        assert data["success"] is True
        assert data["device"]["vendor"] == "Dexcom"
        assert data["device"]["mac_address_masked"] == "XX:XX:XX:DA:71:13"

        # 2. List Devices
        list_resp = client.get(
            "/api/v1/interop/sensors/devices?study_id=STUDY-API-01",
            headers=headers,
        )
        assert list_resp.status_code == 200
        list_data = list_resp.json()
        assert list_data["total_count"] == 1
        assert list_data["devices"][0]["model_name"] == "G7"

    def test_telemetry_stream_ingest_api(self) -> None:
        """Verify POST /api/v1/interop/sensors/ingest with safety alert and daily summary.

        @req:PRD-DCT-002
        @req:ADR-2199
        """
        client = TestClient(app)
        headers = get_auth_headers(roles="data_manager", user_id="dm_user_01")
        now_iso = datetime.now(UTC).isoformat()

        ingest_payload = {
            "study_id": "STUDY-API-02",
            "site_id": "SITE-02",
            "subject_id": "PATIENT-API-02",
            "device_type": "ACTIGRAPHY",
            "vendor": "ActiGraph",
            "model_name": "wGT3X-BT",
            "serial_number": "ACT-8899",
            "device_sync_timestamp": now_iso,
            "gateway_receive_timestamp": now_iso,
            "readings": [
                {
                    "timestamp": now_iso,
                    "metric_name": "step_count",
                    "value": 1500.0,
                    "unit": "steps",
                },
                {
                    "timestamp": now_iso,
                    "metric_name": "heart_rate",
                    "value": 140.0,  # Extreme tachycardia
                    "unit": "bpm",
                },
            ],
            "feed_to_lakehouse": True,
            "reason_for_change": "Continuous actigraphy ingestion test",
        }

        resp = client.post(
            "/api/v1/interop/sensors/ingest", json=ingest_payload, headers=headers
        )
        assert resp.status_code == 201
        res = resp.json()["result"]
        assert res["readings_ingested"] == 2
        assert res["omh_data_points_count"] == 2
        assert res["fhir_observations_count"] == 2
        assert len(res["safety_alerts_triggered"]) == 1
        assert res["safety_alerts_triggered"][0]["alert_type"] == "EXTREME_TACHYCARDIA"

        # Query Daily Summaries API
        sum_resp = client.get(
            "/api/v1/interop/sensors/daily-summaries?study_id=STUDY-API-02",
            headers=headers,
        )
        assert sum_resp.status_code == 200
        sum_data = sum_resp.json()
        assert sum_data["total_count"] == 1
        assert sum_data["summaries"][0]["actigraphy"]["total_steps"] == 1500

        # Query Safety Alerts API
        alert_resp = client.get(
            "/api/v1/interop/sensors/safety-alerts?study_id=STUDY-API-02",
            headers=headers,
        )
        assert alert_resp.status_code == 200
        alert_data = alert_resp.json()
        assert alert_data["total_count"] == 1
        alert_id = alert_data["alerts"][0]["alert_id"]

        # Acknowledge Safety Alert API
        ack_resp = client.post(
            f"/api/v1/interop/sensors/safety-alerts/{alert_id}/acknowledge",
            json={
                "notes": "Patient was performing vigorous treadmill protocol procedure.",
                "audit_signature": "SIG-TREADMILL-TEST",
            },
            headers=headers,
        )
        assert ack_resp.status_code == 200
        ack_data = ack_resp.json()
        assert ack_data["success"] is True
        assert ack_data["alert"]["is_acknowledged"] is True

        # Query Metrics API
        metrics_resp = client.get(
            "/api/v1/interop/sensors/metrics/STUDY-API-02",
            headers=headers,
        )
        assert metrics_resp.status_code == 200
        m = metrics_resp.json()["metrics"]
        assert m["total_streams_ingested"] == 1
        assert m["total_readings_ingested"] == 2
        assert m["total_safety_alerts"] == 1
        assert m["unacknowledged_alerts"] == 0
