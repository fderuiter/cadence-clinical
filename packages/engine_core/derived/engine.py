"""Pure mathematical execution engine for clinical derived variables and biostatistical formulas.

Evaluates standardized medical formulas (eGFR, BSA, BMI, QTcF, MAP, Cockcroft-Gault)
with physiological plausibility bounds and unit normalization under 21 CFR Part 11.
"""

import math
from typing import Any

from packages.engine_core.derived.models import (
    DerivedCalculationResult,
    DerivedFormulaDefinition,
)
from packages.engine_core.rules.evaluator import evaluate_expression


def normalize_input_units(inputs: dict[str, Any]) -> dict[str, Any]:
    """Normalizes common clinical units into standard SI metric equivalents."""
    normalized = dict(inputs)

    # Weight conversion (lbs -> kg)
    if (
        "weight_lbs" in normalized
        and "weight" not in normalized
        and "weight_kg" not in normalized
    ):
        try:
            normalized["weight_kg"] = float(normalized["weight_lbs"]) * 0.45359237
            normalized["weight"] = normalized["weight_kg"]
        except ValueError, TypeError:
            pass
    elif "weight" in normalized and "weight_kg" not in normalized:
        normalized["weight_kg"] = float(normalized["weight"])

    # Height conversion (inches -> cm)
    if (
        "height_in" in normalized
        and "height" not in normalized
        and "height_cm" not in normalized
    ):
        try:
            normalized["height_cm"] = float(normalized["height_in"]) * 2.54
            normalized["height"] = normalized["height_cm"]
        except ValueError, TypeError:
            pass
    elif "height" in normalized and "height_cm" not in normalized:
        normalized["height_cm"] = float(normalized["height"])

    # Serum Creatinine (umol/L -> mg/dL)
    if "serum_creatinine_umol" in normalized and "serum_creatinine" not in normalized:
        try:
            normalized["serum_creatinine"] = (
                float(normalized["serum_creatinine_umol"]) / 88.42
            )
            normalized["scr"] = normalized["serum_creatinine"]
        except ValueError, TypeError:
            pass
    elif "serum_creatinine" in normalized and "scr" not in normalized:
        normalized["scr"] = float(normalized["serum_creatinine"])

    return normalized


class DerivedVariableEngine:
    """Pure in-memory clinical derived variable execution runtime."""

    @classmethod
    def calculate(
        cls,
        formula: DerivedFormulaDefinition,
        context: dict[str, Any],
    ) -> DerivedCalculationResult:
        """Evaluates a derived variable formula against provided subject clinical context."""
        ctx = normalize_input_units(context)
        violations: list[str] = []

        # Validate input requirements and plausibility bounds
        for req in formula.inputs:
            val = ctx.get(req.name)
            if val is None:
                val = req.default_value

            if val is None:
                if req.required:
                    return DerivedCalculationResult(
                        formula_id=formula.formula_id,
                        output_variable=formula.output_variable,
                        is_valid=False,
                        error_message=f"Missing required input variable '{req.name}'",
                        input_values_used=ctx,
                    )
                continue

            try:
                num_val = float(val)
            except ValueError, TypeError:
                return DerivedCalculationResult(
                    formula_id=formula.formula_id,
                    output_variable=formula.output_variable,
                    is_valid=False,
                    error_message=f"Input variable '{req.name}' must be numeric (got {val})",
                    input_values_used=ctx,
                )

            # Plausibility bounds checks
            if req.plausibility:
                p = req.plausibility
                if p.min_value is not None and num_val < p.min_value:
                    msg = f"Input '{req.name}' value {num_val} is below plausible minimum {p.min_value} {p.unit or ''}".strip()
                    violations.append(msg)
                    if not p.warning_only:
                        return DerivedCalculationResult(
                            formula_id=formula.formula_id,
                            output_variable=formula.output_variable,
                            is_valid=False,
                            error_message=msg,
                            plausibility_violations=violations,
                            input_values_used=ctx,
                        )
                if p.max_value is not None and num_val > p.max_value:
                    msg = f"Input '{req.name}' value {num_val} exceeds plausible maximum {p.max_value} {p.unit or ''}".strip()
                    violations.append(msg)
                    if not p.warning_only:
                        return DerivedCalculationResult(
                            formula_id=formula.formula_id,
                            output_variable=formula.output_variable,
                            is_valid=False,
                            error_message=msg,
                            plausibility_violations=violations,
                            input_values_used=ctx,
                        )

        # Dispatch calculation based on formula_type
        ft = formula.formula_type
        calc_val: float | None = None

        try:
            if ft == "BMI":
                calc_val = cls._calculate_bmi(ctx)
            elif ft == "BSA_MOSTELLER":
                calc_val = cls._calculate_bsa_mosteller(ctx)
            elif ft == "BSA_DUBOIS":
                calc_val = cls._calculate_bsa_dubois(ctx)
            elif ft == "EGFR_CKD_EPI_2021":
                calc_val = cls._calculate_egfr_ckd_epi_2021(ctx)
            elif ft == "EGFR_MDRD":
                calc_val = cls._calculate_egfr_mdrd(ctx)
            elif ft == "QTC_FRIDERICIA":
                calc_val = cls._calculate_qtc_fridericia(ctx)
            elif ft == "QTC_BAZETT":
                calc_val = cls._calculate_qtc_bazett(ctx)
            elif ft == "MAP":
                calc_val = cls._calculate_map(ctx)
            elif ft == "COCKCROFT_GAULT":
                calc_val = cls._calculate_cockcroft_gault(ctx)
            elif ft == "CUSTOM_AST":
                if not formula.expression:
                    return DerivedCalculationResult(
                        formula_id=formula.formula_id,
                        output_variable=formula.output_variable,
                        is_valid=False,
                        error_message="Formula type CUSTOM_AST requires an expression AST node",
                        input_values_used=ctx,
                    )
                raw_eval = evaluate_expression(formula.expression, ctx)
                calc_val = float(raw_eval) if raw_eval is not None else None
            else:
                return DerivedCalculationResult(
                    formula_id=formula.formula_id,
                    output_variable=formula.output_variable,
                    is_valid=False,
                    error_message=f"Unsupported formula type: {ft}",
                    input_values_used=ctx,
                )
        except Exception as err:
            return DerivedCalculationResult(
                formula_id=formula.formula_id,
                output_variable=formula.output_variable,
                is_valid=False,
                error_message=f"Calculation error: {err}",
                plausibility_violations=violations,
                input_values_used=ctx,
            )

        return DerivedCalculationResult(
            formula_id=formula.formula_id,
            output_variable=formula.output_variable,
            calculated_value=round(calc_val, 4) if calc_val is not None else None,
            output_unit=formula.output_unit,
            is_valid=True,
            plausibility_violations=violations,
            input_values_used=ctx,
        )

    # Standard Clinical Formula Implementations

    @staticmethod
    def _calculate_bmi(ctx: dict[str, Any]) -> float:
        """BMI = weight_kg / (height_m ^ 2)."""
        weight_kg = float(ctx.get("weight_kg") or ctx["weight"])
        height_cm = float(ctx.get("height_cm") or ctx["height"])
        height_m = height_cm / 100.0
        if height_m <= 0:
            raise ValueError("Height must be greater than zero")
        return weight_kg / (height_m**2)

    @staticmethod
    def _calculate_bsa_mosteller(ctx: dict[str, Any]) -> float:
        """BSA (Mosteller) = sqrt((height_cm * weight_kg) / 3600)."""
        weight_kg = float(ctx.get("weight_kg") or ctx["weight"])
        height_cm = float(ctx.get("height_cm") or ctx["height"])
        return math.sqrt((height_cm * weight_kg) / 3600.0)

    @staticmethod
    def _calculate_bsa_dubois(ctx: dict[str, Any]) -> float:
        """BSA (DuBois) = 0.007184 * (height_cm ^ 0.725) * (weight_kg ^ 0.425)."""
        weight_kg = float(ctx.get("weight_kg") or ctx["weight"])
        height_cm = float(ctx.get("height_cm") or ctx["height"])
        return 0.007184 * (height_cm**0.725) * (weight_kg**0.425)

    @staticmethod
    def _calculate_egfr_ckd_epi_2021(ctx: dict[str, Any]) -> float:
        """eGFR CKD-EPI (2021 race-free equation for serum creatinine)."""
        scr = float(ctx.get("scr") or ctx.get("serum_creatinine") or ctx["creatinine"])
        age = float(ctx["age"])
        sex_raw = str(ctx.get("sex") or ctx.get("gender") or "F").strip().upper()
        is_female = sex_raw in ("F", "FEMALE")

        if is_female:
            kappa = 0.7
            alpha = -0.241
            sex_factor = 1.012
        else:
            kappa = 0.9
            alpha = -0.302
            sex_factor = 1.0

        scr_ratio = scr / kappa
        min_term = min(scr_ratio, 1.0) ** alpha
        max_term = max(scr_ratio, 1.0) ** -1.200
        age_factor = 0.9938**age

        return 142.0 * min_term * max_term * age_factor * sex_factor

    @staticmethod
    def _calculate_egfr_mdrd(ctx: dict[str, Any]) -> float:
        """eGFR MDRD equation."""
        scr = float(ctx.get("scr") or ctx.get("serum_creatinine") or ctx["creatinine"])
        age = float(ctx["age"])
        sex_raw = str(ctx.get("sex") or ctx.get("gender") or "F").strip().upper()
        is_female = sex_raw in ("F", "FEMALE")

        female_factor = 0.742 if is_female else 1.0
        return 175.0 * (scr**-1.154) * (age**-0.203) * female_factor

    @staticmethod
    def _calculate_qtc_fridericia(ctx: dict[str, Any]) -> float:
        """QTcF = QT_ms / (RR_s ^ (1/3))."""
        qt_ms = float(ctx.get("qt_ms") or ctx["qt"])
        if "rr_s" in ctx or "rr" in ctx:
            rr_s = float(ctx.get("rr_s") or ctx["rr"])
        elif "heart_rate" in ctx or "hr" in ctx:
            hr = float(ctx.get("heart_rate") or ctx["hr"])
            if hr <= 0:
                raise ValueError("Heart rate must be greater than zero")
            rr_s = 60.0 / hr
        else:
            raise ValueError(
                "Either 'rr_s' or 'heart_rate' is required for QTcF calculation"
            )

        if rr_s <= 0:
            raise ValueError("RR interval must be greater than zero")
        return qt_ms / (rr_s ** (1.0 / 3.0))

    @staticmethod
    def _calculate_qtc_bazett(ctx: dict[str, Any]) -> float:
        """QTcB = QT_ms / sqrt(RR_s)."""
        qt_ms = float(ctx.get("qt_ms") or ctx["qt"])
        if "rr_s" in ctx or "rr" in ctx:
            rr_s = float(ctx.get("rr_s") or ctx["rr"])
        elif "heart_rate" in ctx or "hr" in ctx:
            hr = float(ctx.get("heart_rate") or ctx["hr"])
            if hr <= 0:
                raise ValueError("Heart rate must be greater than zero")
            rr_s = 60.0 / hr
        else:
            raise ValueError(
                "Either 'rr_s' or 'heart_rate' is required for QTcB calculation"
            )

        if rr_s <= 0:
            raise ValueError("RR interval must be greater than zero")
        return qt_ms / math.sqrt(rr_s)

    @staticmethod
    def _calculate_map(ctx: dict[str, Any]) -> float:
        """MAP = DBP + (1/3) * (SBP - DBP)."""
        sbp = float(ctx.get("systolic_bp") or ctx.get("sbp") or ctx["systolic"])
        dbp = float(ctx.get("diastolic_bp") or ctx.get("dbp") or ctx["diastolic"])
        return dbp + (1.0 / 3.0) * (sbp - dbp)

    @staticmethod
    def _calculate_cockcroft_gault(ctx: dict[str, Any]) -> float:
        """Cockcroft-Gault CrCl = [((140 - age) * weight_kg) / (72 * Scr)] * (0.85 if female)."""
        age = float(ctx["age"])
        weight_kg = float(ctx.get("weight_kg") or ctx["weight"])
        scr = float(ctx.get("scr") or ctx.get("serum_creatinine") or ctx["creatinine"])
        sex_raw = str(ctx.get("sex") or ctx.get("gender") or "F").strip().upper()
        is_female = sex_raw in ("F", "FEMALE")

        if scr <= 0:
            raise ValueError("Serum creatinine must be greater than zero")

        crcl = ((140.0 - age) * weight_kg) / (72.0 * scr)
        if is_female:
            crcl *= 0.85
        return crcl
