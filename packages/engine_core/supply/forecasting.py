"""Predictive Demand and Resupply Simulation Engine.

Requirements: PRD-SCM-001, PRD-SCM-002, PRD-SYS-001, GMP Annex 13
"""

import math
from typing import Any

from packages.engine_core.supply.models import (
    DailyDemandProjection,
    DemandForecastParameters,
    PredictiveForecastResult,
    StockoutRiskLevel,
)


class PredictiveDemandForecastingEngine:
    """Zero-I/O stochastic and visit-based clinical supply demand forecasting runtime."""

    @classmethod
    def simulate_site_inventory(
        cls,
        params: DemandForecastParameters,
    ) -> PredictiveForecastResult:
        """Simulates longitudinal inventory run-down, lot expiry, and resupply triggers over a time horizon.

        Args:
            params: Parameters including initial on-hand, in-transit stock, recruitment rates, and visit schedules.

        Returns:
            PredictiveForecastResult with daily projections, days until stockout, and recommended reorder date/qty.
        """
        horizon = params.simulation_horizon_days
        daily_recruitment_rate = params.monthly_recruitment_rate / 30.0

        current_stock = params.current_on_hand
        in_transit_stock = params.current_in_transit
        in_transit_day = params.in_transit_arrival_day

        # Build map of scheduled visit dispensation intervals
        disp_map: dict[int, int] = {
            d.visit_day_offset: d.dispensation_qty for d in params.dispensation_schedule
        }

        # Track active cohorts of subjects by enrollment day offset
        # Day 0: start with params.active_subjects
        subject_cohorts: list[dict[str, float]] = [
            {"enrolled_day": 0, "subject_count": float(params.active_subjects)}
        ]

        # Track available lot quantities
        lot_stock: list[dict[str, Any]] = [
            {
                "lot_number": lot.lot_number,
                "expiry_day": lot.expiry_day_offset,
                "qty": lot.quantity,
            }
            for lot in params.active_lots
        ]

        daily_projections: list[DailyDemandProjection] = []
        days_until_stockout: int | None = None
        recommended_reorder_day: int | None = None
        reorder_triggered = False
        lot_expiry_warnings: list[str] = []
        total_projected_demand = 0

        lead_time_buffer_days = params.lead_time_days + params.safety_buffer_days

        for day in range(horizon + 1):
            # 1. Check in-transit arrivals
            arrived_qty = 0
            if in_transit_stock > 0 and day == in_transit_day:
                arrived_qty = in_transit_stock
                current_stock += arrived_qty
                in_transit_stock = 0

            # 2. Check and deduct expired lots
            expired_qty = 0
            for lot in lot_stock:
                if lot["expiry_day"] == day and lot["qty"] > 0:
                    expired_qty += lot["qty"]
                    warning_msg = (
                        f"Lot '{lot['lot_number']}' expired on day #{day} "
                        f"({lot['qty']} units removed from usable inventory)."
                    )
                    lot_expiry_warnings.append(warning_msg)
                    current_stock = max(0, current_stock - lot["qty"])
                    lot["qty"] = 0

            # 3. Simulate new accruals for the day (fractional or rounded)
            new_accruals = daily_recruitment_rate if day > 0 else 0.0
            if new_accruals > 0:
                subject_cohorts.append(
                    {"enrolled_day": day, "subject_count": new_accruals}
                )

            # 4. Calculate total expected dispensation demand across all active subject cohorts
            day_demand_float = 0.0
            for cohort in subject_cohorts:
                days_since_enrollment = day - int(cohort["enrolled_day"])
                if days_since_enrollment in disp_map:
                    qty_per_subject = disp_map[days_since_enrollment]
                    day_demand_float += cohort["subject_count"] * qty_per_subject

            day_demand = math.ceil(day_demand_float)
            total_projected_demand += day_demand

            # Deduct demand from stock
            current_stock -= day_demand

            # Check stockout
            if current_stock <= 0 and days_until_stockout is None:
                days_until_stockout = day

            # Calculate stockout risk
            total_active_subjects = int(
                sum(c["subject_count"] for c in subject_cohorts)
            )

            risk_level = StockoutRiskLevel.LOW
            if (
                current_stock <= 0
                or days_until_stockout is not None
                and (days_until_stockout - day) <= 7
            ):
                risk_level = StockoutRiskLevel.CRITICAL
            elif (
                days_until_stockout is not None
                and (days_until_stockout - day) <= lead_time_buffer_days
            ):
                risk_level = StockoutRiskLevel.HIGH
            elif current_stock <= (total_active_subjects * 2):
                risk_level = StockoutRiskLevel.MEDIUM

            # Check reorder trigger
            # Reorder if stockout is projected within lead_time_buffer_days and not already reordered
            is_reorder_day = False
            if (
                not reorder_triggered
                and days_until_stockout is not None
                and (days_until_stockout - day) <= lead_time_buffer_days
            ):
                is_reorder_day = True
                reorder_triggered = True
                recommended_reorder_day = day

            daily_projections.append(
                DailyDemandProjection(
                    day_index=day,
                    projected_active_subjects=total_active_subjects,
                    new_enrollments=int(round(new_accruals)),
                    expected_demand_kits=day_demand,
                    projected_on_hand=current_stock,
                    expired_kits_deducted=expired_qty,
                    in_transit_received=arrived_qty,
                    is_reorder_triggered=is_reorder_day,
                    stockout_risk=risk_level,
                )
            )

        # Calculate recommended reorder quantity (rounded up to resupply_lot_size)
        deficit = (
            max(0, -current_stock)
            if current_stock < 0
            else (total_projected_demand - params.current_on_hand)
        )
        needed_qty = max(
            params.resupply_lot_size, deficit + (params.active_subjects * 2)
        )
        resupply_lots = math.ceil(needed_qty / params.resupply_lot_size)
        recommended_qty = resupply_lots * params.resupply_lot_size

        # Overall risk level
        overall_risk = StockoutRiskLevel.LOW
        if days_until_stockout is not None:
            if days_until_stockout <= 7:
                overall_risk = StockoutRiskLevel.CRITICAL
            elif days_until_stockout <= lead_time_buffer_days:
                overall_risk = StockoutRiskLevel.HIGH
            else:
                overall_risk = StockoutRiskLevel.MEDIUM

        return PredictiveForecastResult(
            study_id=params.study_id,
            site_id=params.site_id,
            simulation_horizon_days=horizon,
            starting_on_hand=params.current_on_hand,
            total_projected_demand=total_projected_demand,
            days_until_stockout=days_until_stockout,
            recommended_reorder_day_offset=recommended_reorder_day,
            recommended_reorder_qty=recommended_qty,
            stockout_risk_level=overall_risk,
            lot_expiry_warnings=lot_expiry_warnings,
            daily_projections=daily_projections,
        )
