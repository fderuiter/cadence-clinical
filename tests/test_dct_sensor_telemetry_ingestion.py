"""Root forwarding test suite for Continuous Biometric IoT Sensor Telemetry Ingestion.

Requirements: PRD-DCT-002, PRD-DCT-001, ADR-2199, 21 CFR Part 11
"""

from apps.interop.tests.test_dct_sensor_telemetry_ingestion import (
    TestClockDriftCorrection,
    TestDailySummaryEndpointsCalculation,
    TestOpenMHealthAndFHIRNormalization,
    TestPseudonymizationAndDeviceMapping,
    TestSafetyThresholdAlertsEngine,
    TestSensorServiceIntegration,
    TestSensorTelemetryRestApi,
    setup_db,
)

__all__ = [
    "TestClockDriftCorrection",
    "TestDailySummaryEndpointsCalculation",
    "TestOpenMHealthAndFHIRNormalization",
    "TestPseudonymizationAndDeviceMapping",
    "TestSafetyThresholdAlertsEngine",
    "TestSensorServiceIntegration",
    "TestSensorTelemetryRestApi",
    "setup_db",
]
