"""Unit tests for the Predictive Supply Chain and DTP Logistics Engine.

@req:PRD-SCM-001, PRD-SCM-002, PRD-SYS-001
"""

import pytest

from packages.engine_core.supply.dtp import (
    calculate_mean_kinetic_temperature,
)
from packages.engine_core.supply.engine import (
    PredictiveSupplyEngine,
)
from packages.engine_core.supply.hierarchy import (
    InvalidHierarchyError,
    MultiEchelonHierarchyEngine,
)
from packages.engine_core.supply.models import (
    DemandForecastParameters,
    EmergencyUnblindingRequest,
    LotExpiryInfo,
    MultiEchelonNode,
    SupplyNodeType,
    TemperatureReading,
)


def test_multi_echelon_hierarchy_rollup_and_cycle_detection():
    """Verify multi-echelon hierarchy tree validation, stock rollups, and cycle detection.

    @req:PRD-SCM-001
    """
    # 1. Valid 4-tier hierarchy
    nodes = [
        MultiEchelonNode(
            node_id="MFR-01",
            study_id="STUDY-301",
            node_type=SupplyNodeType.PRIMARY_MANUFACTURER,
            name="Primary Manufacturing Plant",
            on_hand_qty=10000,
            reserved_qty=2000,
            in_transit_qty=0,
        ),
        MultiEchelonNode(
            node_id="DEPOT-CENTRAL",
            study_id="STUDY-301",
            node_type=SupplyNodeType.CENTRAL_DEPOT,
            name="Global Central Depot",
            parent_node_id="MFR-01",
            on_hand_qty=5000,
            reserved_qty=500,
            in_transit_qty=1000,
        ),
        MultiEchelonNode(
            node_id="DEPOT-REGIONAL",
            study_id="STUDY-301",
            node_type=SupplyNodeType.REGIONAL_DEPOT,
            name="US Regional Depot",
            parent_node_id="DEPOT-CENTRAL",
            on_hand_qty=1200,
            reserved_qty=200,
            in_transit_qty=300,
        ),
        MultiEchelonNode(
            node_id="SITE-101",
            study_id="STUDY-301",
            node_type=SupplyNodeType.CLINICAL_SITE,
            name="Site 101 Mayo Clinic",
            parent_node_id="DEPOT-REGIONAL",
            on_hand_qty=50,
            reserved_qty=10,
            in_transit_qty=20,
        ),
    ]

    rollup = PredictiveSupplyEngine.compute_hierarchy_rollup("STUDY-301", nodes)
    assert rollup.total_nodes == 4
    assert rollup.total_on_hand == 10000 + 5000 + 1200 + 50
    assert rollup.total_reserved == 2000 + 500 + 200 + 10
    assert rollup.total_in_transit == 0 + 1000 + 300 + 20
    assert rollup.tier_counts[SupplyNodeType.CLINICAL_SITE.value] == 1

    # 2. Cycle detection rejection
    cyclic_nodes = [
        MultiEchelonNode(
            node_id="NODE-A",
            study_id="STUDY-301",
            node_type=SupplyNodeType.REGIONAL_DEPOT,
            name="Node A",
            parent_node_id="NODE-B",
        ),
        MultiEchelonNode(
            node_id="NODE-B",
            study_id="STUDY-301",
            node_type=SupplyNodeType.REGIONAL_DEPOT,
            name="Node B",
            parent_node_id="NODE-A",
        ),
    ]
    with pytest.raises(InvalidHierarchyError, match="Cycle detected"):
        MultiEchelonHierarchyEngine.validate_hierarchy_dag(cyclic_nodes)


def test_predictive_demand_forecasting_and_expiry_warnings():
    """Verify predictive demand projection, lead-time buffer, and lot expiry detection.

    @req:PRD-SCM-001
    """
    params = DemandForecastParameters(
        study_id="STUDY-401",
        site_id="SITE-201",
        active_subjects=5,
        monthly_recruitment_rate=3.0,
        simulation_horizon_days=60,
        lead_time_days=10,
        safety_buffer_days=4,
        resupply_lot_size=10,
        current_on_hand=15,
        current_in_transit=10,
        in_transit_arrival_day=5,
        active_lots=[
            LotExpiryInfo(lot_number="LOT-ALPHA", expiry_day_offset=20, quantity=5)
        ],
    )

    forecast = PredictiveSupplyEngine.forecast_site_demand(params)
    assert forecast.study_id == "STUDY-401"
    assert forecast.site_id == "SITE-201"
    assert len(forecast.daily_projections) == 61  # Day 0 to Day 60
    assert len(forecast.lot_expiry_warnings) > 0
    assert "LOT-ALPHA" in forecast.lot_expiry_warnings[0]
    assert forecast.recommended_reorder_qty % 10 == 0


def test_mean_kinetic_temperature_and_dtp_cold_chain_gating():
    """Verify Haynes MKT calculation and Direct-to-Patient cold-chain dispatch gating.

    @req:PRD-SCM-002, PRD-SYS-001
    """
    # 1. Normal refrigerated readings (4.0 C to 6.0 C)
    normal_telemetry = [
        TemperatureReading(temperature_celsius=4.0),
        TemperatureReading(temperature_celsius=4.5),
        TemperatureReading(temperature_celsius=5.0),
        TemperatureReading(temperature_celsius=5.5),
    ]
    mkt_normal = calculate_mean_kinetic_temperature(normal_telemetry)
    assert 4.0 <= mkt_normal <= 6.0

    # 2. Eligible DTP dispatch
    auth_ok = PredictiveSupplyEngine.evaluate_dtp_dispatch(
        study_id="STUDY-501",
        site_id="SITE-301",
        subject_id="SUBJ-101",
        kit_number="KIT-99901",
        lot_number="LOT-2026",
        days_until_expiry=45,
        is_quarantined=False,
        is_recalled=False,
        temperature_readings=normal_telemetry,
    )
    assert auth_ok.is_eligible_for_dispatch is True
    assert auth_ok.rejection_reasons == []
    assert auth_ok.tamper_seal_id is not None
    assert auth_ok.tamper_seal_id.startswith("SEAL-")

    # 3. Quarantined kit rejection
    auth_quarantine = PredictiveSupplyEngine.evaluate_dtp_dispatch(
        study_id="STUDY-501",
        site_id="SITE-301",
        subject_id="SUBJ-102",
        kit_number="KIT-99902",
        lot_number="LOT-2026",
        days_until_expiry=45,
        is_quarantined=True,
    )
    assert auth_quarantine.is_eligible_for_dispatch is False
    assert any("quarantine" in r.lower() for r in auth_quarantine.rejection_reasons)
    assert auth_quarantine.tamper_seal_id is None

    # 4. Temperature excursion rejection (MKT = 15.0 C > 8.0 C)
    hot_telemetry = [
        TemperatureReading(temperature_celsius=14.0),
        TemperatureReading(temperature_celsius=16.0),
    ]
    auth_excursion = PredictiveSupplyEngine.evaluate_dtp_dispatch(
        study_id="STUDY-501",
        site_id="SITE-301",
        subject_id="SUBJ-103",
        kit_number="KIT-99903",
        lot_number="LOT-2026",
        days_until_expiry=45,
        temperature_readings=hot_telemetry,
    )
    assert auth_excursion.is_eligible_for_dispatch is False
    assert any("excursion" in r.lower() for r in auth_excursion.rejection_reasons)


def test_21cfr_part11_emergency_unblinding():
    """Verify 21 CFR Part 11 Emergency Unblinding role authorization and digital sealing.

    @req:PRD-SYS-001, PRD-RTSM-001
    """
    # 1. Authorized Safety Physician request with valid justification
    valid_req = EmergencyUnblindingRequest(
        study_id="STUDY-601",
        subject_id="SUBJ-888",
        requester_user_id="dr.smith",
        requester_role="safety_physician",
        reason_for_change="Severe Grade 4 anaphylactic reaction requiring unblinded emergency treatment.",
        subject_actual_arm="ACTIVE_EXPERIMENTAL_ARM",
    )

    unblind_res = PredictiveSupplyEngine.execute_emergency_unblinding(valid_req)
    assert unblind_res.is_authorized is True
    assert unblind_res.unblinded_arm == "ACTIVE_EXPERIMENTAL_ARM"
    assert unblind_res.authorized_by_user_id == "dr.smith"
    assert unblind_res.digital_seal is not None
    assert unblind_res.safety_notification_required is True

    # 2. Unauthorized role rejection
    unauthorized_req = EmergencyUnblindingRequest(
        study_id="STUDY-601",
        subject_id="SUBJ-888",
        requester_user_id="crc.user",
        requester_role="site_crc",
        reason_for_change="Curiosity about arm assignment.",
        subject_actual_arm="ACTIVE_EXPERIMENTAL_ARM",
    )
    with pytest.raises(PermissionError, match="not authorized"):
        PredictiveSupplyEngine.execute_emergency_unblinding(unauthorized_req)

    # 3. Missing / insufficient change justification rejection (Pydantic validation)
    with pytest.raises(Exception):
        EmergencyUnblindingRequest(
            study_id="STUDY-601",
            subject_id="SUBJ-888",
            requester_user_id="dr.smith",
            requester_role="safety_physician",
            reason_for_change="Too short",
            subject_actual_arm="ACTIVE_EXPERIMENTAL_ARM",
        )
