"""Unified Predictive Supply Chain & DTP Logistics Engine Facade.

Requirements: PRD-SCM-001, PRD-SCM-002, PRD-SYS-001, GMP Annex 13, 21 CFR Part 11
"""

from collections.abc import Sequence

from packages.engine_core.supply.dtp import (
    DTPLogisticsEngine,
    calculate_mean_kinetic_temperature,
)
from packages.engine_core.supply.forecasting import (
    PredictiveDemandForecastingEngine,
)
from packages.engine_core.supply.hierarchy import (
    MultiEchelonHierarchyEngine,
)
from packages.engine_core.supply.models import (
    DemandForecastParameters,
    DTPDispatchAuthorization,
    EmergencyUnblindingRequest,
    EmergencyUnblindingResult,
    MultiEchelonNode,
    MultiEchelonRollup,
    PredictiveForecastResult,
    TemperatureReading,
)
from packages.engine_core.supply.unblinding import (
    EmergencyUnblindingEngine,
)


class PredictiveSupplyEngine:
    """Unified zero-I/O entrypoint for clinical supply hierarchy, forecasting, DTP, and emergency unblinding."""

    @staticmethod
    def compute_hierarchy_rollup(
        study_id: str,
        nodes: list[MultiEchelonNode],
    ) -> MultiEchelonRollup:
        """Validates multi-echelon DAG and computes aggregated inventory rollups."""
        return MultiEchelonHierarchyEngine.compute_rollup(study_id, nodes)

    @staticmethod
    def forecast_site_demand(
        params: DemandForecastParameters,
    ) -> PredictiveForecastResult:
        """Simulates site inventory run-out and calculates optimal reorder triggers."""
        return PredictiveDemandForecastingEngine.simulate_site_inventory(params)

    @staticmethod
    def evaluate_dtp_dispatch(
        study_id: str,
        site_id: str,
        subject_id: str,
        kit_number: str,
        lot_number: str,
        days_until_expiry: int,
        is_quarantined: bool = False,
        is_recalled: bool = False,
        temperature_readings: Sequence[TemperatureReading] | None = None,
        min_allowed_temp: float = 2.0,
        max_allowed_temp: float = 8.0,
        min_shelf_life_days: int = 14,
    ) -> DTPDispatchAuthorization:
        """Evaluates Direct-to-Patient home shipment eligibility and cold-chain compliance."""
        return DTPLogisticsEngine.evaluate_dtp_dispatch(
            study_id=study_id,
            site_id=site_id,
            subject_id=subject_id,
            kit_number=kit_number,
            lot_number=lot_number,
            days_until_expiry=days_until_expiry,
            is_quarantined=is_quarantined,
            is_recalled=is_recalled,
            temperature_readings=temperature_readings,
            min_allowed_temp=min_allowed_temp,
            max_allowed_temp=max_allowed_temp,
            min_shelf_life_days=min_shelf_life_days,
        )

    @staticmethod
    def calculate_mkt(readings: Sequence[TemperatureReading]) -> float:
        """Calculates Mean Kinetic Temperature in Celsius."""
        return calculate_mean_kinetic_temperature(readings)

    @staticmethod
    def execute_emergency_unblinding(
        request: EmergencyUnblindingRequest,
    ) -> EmergencyUnblindingResult:
        """Evaluates and seals an authorized 21 CFR Part 11 emergency unblinding manifest."""
        return EmergencyUnblindingEngine.execute_unblinding(request)
