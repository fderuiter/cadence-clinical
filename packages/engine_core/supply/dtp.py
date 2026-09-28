"""Direct-to-Patient (DTP) Logistics and Cold-Chain Temperature Excursion Gating.

Requirements: PRD-SCM-002, GMP Annex 13, 21 CFR Part 11
"""

import hashlib
import math
from collections.abc import Sequence

from packages.engine_core.supply.models import (
    DTPDispatchAuthorization,
    TemperatureReading,
)

# Universal gas constant R and activation energy for pharmaceutical MKT
GAS_CONSTANT_R = 0.008314472  # kJ / (mol * K)
ACTIVATION_ENERGY_DH = 83.144  # kJ / mol


def calculate_mean_kinetic_temperature(readings: Sequence[TemperatureReading]) -> float:
    """Calculates Mean Kinetic Temperature (MKT) in Celsius using the Haynes equation.

    T_mkt = (DH / R) / -ln( (1/n) * sum( exp( -DH / (R * T_k) ) ) ) - 273.15
    """
    if not readings:
        raise ValueError("Cannot calculate MKT with empty temperature readings.")

    n = len(readings)
    sum_exp = 0.0

    for r in readings:
        temp_kelvin = r.temperature_celsius + 273.15
        if temp_kelvin <= 0:
            raise ValueError(f"Invalid temperature reading: {r.temperature_celsius} C")
        exponent = -ACTIVATION_ENERGY_DH / (GAS_CONSTANT_R * temp_kelvin)
        sum_exp += math.exp(exponent)

    avg_exp = sum_exp / n
    t_mkt_kelvin = (ACTIVATION_ENERGY_DH / GAS_CONSTANT_R) / (-math.log(avg_exp))
    return round(t_mkt_kelvin - 273.15, 2)


class DTPLogisticsEngine:
    """Zero-I/O Direct-to-Patient and Cold-Chain Dispatch Gating Engine."""

    @classmethod
    def evaluate_dtp_dispatch(
        cls,
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
        """Evaluates compliance rules and cold-chain integrity before authorizing home courier dispatch.

        Args:
            study_id: Clinical protocol identifier.
            site_id: Clinical site identifier.
            subject_id: Enrolled subject identifier.
            kit_number: Investigational product unit identifier.
            lot_number: Manufacturing lot number.
            days_until_expiry: Remaining shelf-life in days.
            is_quarantined: Whether kit is under quality quarantine.
            is_recalled: Whether lot has an active regulatory recall.
            temperature_readings: Temperature logger telemetry.
            min_allowed_temp: Minimum acceptable storage temperature in Celsius (e.g. 2.0 C).
            max_allowed_temp: Maximum acceptable storage temperature in Celsius (e.g. 8.0 C).
            min_shelf_life_days: Minimum shelf life required at time of dispatch.

        Returns:
            DTPDispatchAuthorization with eligibility status, rejection reasons, and tamper seal.
        """
        rejection_reasons: list[str] = []

        if is_quarantined:
            rejection_reasons.append(
                f"Kit '{kit_number}' is under active quality quarantine."
            )

        if is_recalled:
            rejection_reasons.append(
                f"Lot '{lot_number}' has an active regulatory recall notice."
            )

        if days_until_expiry < min_shelf_life_days:
            rejection_reasons.append(
                f"Remaining shelf-life ({days_until_expiry} days) is less than the required "
                f"minimum of {min_shelf_life_days} days for home dispensation."
            )

        calculated_mkt: float | None = None
        if temperature_readings:
            try:
                calculated_mkt = calculate_mean_kinetic_temperature(
                    temperature_readings
                )
                if (
                    calculated_mkt < min_allowed_temp
                    or calculated_mkt > max_allowed_temp
                ):
                    rejection_reasons.append(
                        f"Mean Kinetic Temperature excursion: calculated MKT {calculated_mkt} C "
                        f"is outside the permitted range [{min_allowed_temp} C, {max_allowed_temp} C]."
                    )
            except Exception as e:
                rejection_reasons.append(
                    f"Failed to process temperature telemetry: {str(e)}"
                )

        is_eligible = len(rejection_reasons) == 0

        # Generate tamper-evident packaging seal
        dispatch_id = f"DTP-{study_id}-{site_id}-{kit_number}"
        seal_payload = f"{dispatch_id}:{lot_number}:{is_eligible}:{days_until_expiry}:{calculated_mkt}"
        tamper_seal_id = (
            f"SEAL-{hashlib.sha256(seal_payload.encode('utf-8')).hexdigest()[:12].upper()}"
            if is_eligible
            else None
        )

        return DTPDispatchAuthorization(
            dispatch_id=dispatch_id,
            study_id=study_id,
            site_id=site_id,
            subject_id=subject_id,
            kit_number=kit_number,
            lot_number=lot_number,
            is_eligible_for_dispatch=is_eligible,
            rejection_reasons=rejection_reasons,
            calculated_mkt_celsius=calculated_mkt,
            tamper_seal_id=tamper_seal_id,
        )
