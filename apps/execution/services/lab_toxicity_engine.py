"""Central & Local Lab CTCAE v5.0 / DAIDS Toxicity Grading and Delta Check Engine.

Requirements: PRD-LAB-001, PRD-PVG-001, PRD-SYS-001 (NCI-CTCAE v5.0, KDIGO)
"""

from datetime import UTC, datetime
from enum import IntEnum, StrEnum

from pydantic import BaseModel, Field


class CTCAEGrade(IntEnum):
    """NCI-CTCAE v5.0 severity grades."""

    GRADE_0 = 0  # Normal / Within reference range
    GRADE_1 = 1  # Mild; asymptomatic or mild symptoms
    GRADE_2 = 2  # Moderate; minimal, local or noninvasive intervention
    GRADE_3 = 3  # Severe; medically significant, hospitalization indicated
    GRADE_4 = 4  # Life-threatening; urgent intervention indicated
    GRADE_5 = 5  # Death related to AE


class ToxicityCategory(StrEnum):
    """Standard clinical laboratory toxicity categories."""

    HEPATIC = "HEPATIC"  # ALT, AST, Total Bilirubin, Alkaline Phosphatase
    RENAL = "RENAL"  # Serum Creatinine, eGFR, Blood Urea Nitrogen
    HEMATOLOGY = "HEMATOLOGY"  # Platelets, Hemoglobin, ANC, WBC
    ELECTROLYTES = "ELECTROLYTES"  # Potassium (Hypo/Hyper), Sodium, Calcium
    METABOLIC = "METABOLIC"  # Glucose (Hypo/Hyper)


class LabToxicityAssessment(BaseModel):
    """Evaluated CTCAE toxicity result for a single laboratory observation."""

    test_code: str
    test_name: str
    observed_value: float
    unit: str
    ref_low: float | None = None
    ref_high: float | None = None
    baseline_value: float | None = None
    ctcae_grade: CTCAEGrade = CTCAEGrade.GRADE_0
    toxicity_description: str
    is_critical_ae_trigger: bool = False
    is_delta_check_alert: bool = False
    delta_percentage: float | None = None
    eval_timestamp: str = Field(default_factory=lambda: datetime.now(UTC).isoformat())


class LabToxicityEngine:
    """Core domain engine evaluating NCI-CTCAE v5.0 toxicity grading and acute delta checks."""

    @classmethod
    def evaluate_hepatic(
        cls,
        test_code: str,
        value: float,
        uln: float,
        baseline: float | None = None,
    ) -> tuple[CTCAEGrade, str]:
        """Evaluates hepatic enzymes (ALT, AST, Bilirubin) relative to Upper Limit of Normal (ULN).

        Args:
            test_code: Standardized test identifier (e.g. 'ALT', 'AST', 'BILI').
            value: Observed numeric test value.
            uln: Upper limit of normal from demographic reference range.
            baseline: Optional subject baseline measurement.

        Returns:
            Tuple of (CTCAEGrade, clinical_description).
        """
        if uln <= 0.0:
            return CTCAEGrade.GRADE_0, "ULN not defined"

        ratio = value / uln
        if ratio <= 1.0:
            return CTCAEGrade.GRADE_0, "Within normal limits"
        if ratio <= 3.0:
            return CTCAEGrade.GRADE_1, f">1.0 - 3.0x ULN ({ratio:.2f}x ULN)"
        if ratio <= 5.0:
            return CTCAEGrade.GRADE_2, f">3.0 - 5.0x ULN ({ratio:.2f}x ULN)"
        if ratio <= 20.0:
            return CTCAEGrade.GRADE_3, f">5.0 - 20.0x ULN ({ratio:.2f}x ULN) - Severe"
        return (
            CTCAEGrade.GRADE_4,
            f">20.0x ULN ({ratio:.2f}x ULN) - Life Threatening",
        )

    @classmethod
    def evaluate_renal_creatinine(
        cls,
        value: float,
        uln: float,
        baseline: float | None = None,
    ) -> tuple[CTCAEGrade, str]:
        """Evaluates Serum Creatinine relative to ULN and baseline (KDIGO/CTCAE).

        Args:
            value: Observed serum creatinine value.
            uln: Demographic upper limit of normal.
            baseline: Baseline serum creatinine.

        Returns:
            Tuple of (CTCAEGrade, clinical_description).
        """
        if uln <= 0.0:
            return CTCAEGrade.GRADE_0, "ULN not defined"

        ratio_uln = value / uln
        ratio_baseline = (value / baseline) if (baseline and baseline > 0) else 1.0

        if ratio_uln > 6.0 or (baseline and ratio_baseline >= 3.0):
            return (
                CTCAEGrade.GRADE_4,
                f">6.0x ULN or >=3.0x baseline ({ratio_uln:.2f}x ULN)",
            )
        if ratio_uln > 3.0 or (baseline and ratio_baseline >= 2.0):
            return (
                CTCAEGrade.GRADE_3,
                f">3.0 - 6.0x ULN or >=2.0x baseline ({ratio_uln:.2f}x ULN)",
            )
        if ratio_uln > 1.5 or (baseline and (value - baseline) >= 0.3):
            return CTCAEGrade.GRADE_2, f">1.5 - 3.0x ULN ({ratio_uln:.2f}x ULN)"
        if ratio_uln > 1.0:
            return CTCAEGrade.GRADE_1, f">1.0 - 1.5x ULN ({ratio_uln:.2f}x ULN)"
        return CTCAEGrade.GRADE_0, "Within normal limits"

    @classmethod
    def evaluate_neutrophils_anc(
        cls,
        anc_value_g_per_l: float,
        lln: float = 1.5,
    ) -> tuple[CTCAEGrade, str]:
        """Evaluates Absolute Neutrophil Count (ANC) in 10^9/L (or G/L).

        Args:
            anc_value_g_per_l: Absolute Neutrophil Count.
            lln: Lower limit of normal.

        Returns:
            Tuple of (CTCAEGrade, clinical_description).
        """
        if anc_value_g_per_l < 0.5:
            return (
                CTCAEGrade.GRADE_4,
                f"<0.5 x 10^9/L ({anc_value_g_per_l:.2f}) - Life Threatening",
            )
        if anc_value_g_per_l < 1.0:
            return (
                CTCAEGrade.GRADE_3,
                f"<1.0 - 0.5 x 10^9/L ({anc_value_g_per_l:.2f}) - Severe",
            )
        if anc_value_g_per_l < 1.5:
            return (
                CTCAEGrade.GRADE_2,
                f"<1.5 - 1.0 x 10^9/L ({anc_value_g_per_l:.2f}) - Moderate",
            )
        if anc_value_g_per_l < lln:
            return (
                CTCAEGrade.GRADE_1,
                f"<LLN - 1.5 x 10^9/L ({anc_value_g_per_l:.2f}) - Mild",
            )
        return CTCAEGrade.GRADE_0, "Within normal limits"

    @classmethod
    def evaluate_observation(
        cls,
        test_code: str,
        test_name: str,
        value: float,
        unit: str,
        ref_low: float | None = None,
        ref_high: float | None = None,
        baseline_value: float | None = None,
    ) -> LabToxicityAssessment:
        """Assesses CTCAE grade and delta check anomaly for any incoming laboratory test observation.

        Args:
            test_code: Test identifier (e.g., 'ALT', 'AST', 'CREAT', 'NEUT', 'PLT').
            test_name: Full descriptive test name.
            value: Observed numeric test result.
            unit: Normalized UCUM unit string.
            ref_low: Lower boundary of demographic normal range.
            ref_high: Upper boundary of demographic normal range.
            baseline_value: Optional subject baseline measurement.

        Returns:
            LabToxicityAssessment containing computed grade and alert flags.
        """
        code_upper = test_code.strip().upper()
        uln = ref_high or 0.0
        lln = ref_low or 0.0

        grade = CTCAEGrade.GRADE_0
        desc = "Within normal limits"

        if code_upper in ("ALT", "AST", "SGPT", "SGOT", "BILI", "ALKP"):
            grade, desc = cls.evaluate_hepatic(code_upper, value, uln, baseline_value)
        elif code_upper in ("CREAT", "CREATININE", "SERUM_CREATININE"):
            grade, desc = cls.evaluate_renal_creatinine(value, uln, baseline_value)
        elif code_upper in ("NEUT", "ANC", "NEUTROPHILS"):
            grade, desc = cls.evaluate_neutrophils_anc(value, lln=lln or 1.5)
        else:
            # Generic grading based on range multiplier
            if uln > 0 and value > uln:
                ratio = value / uln
                if ratio > 5.0:
                    grade, desc = CTCAEGrade.GRADE_3, f">5.0x ULN ({ratio:.2f}x ULN)"
                elif ratio > 3.0:
                    grade, desc = (
                        CTCAEGrade.GRADE_2,
                        f">3.0 - 5.0x ULN ({ratio:.2f}x ULN)",
                    )
                else:
                    grade, desc = (
                        CTCAEGrade.GRADE_1,
                        f">1.0 - 3.0x ULN ({ratio:.2f}x ULN)",
                    )
            elif lln > 0 and value < lln:
                ratio = value / lln
                if ratio < 0.2:
                    grade, desc = CTCAEGrade.GRADE_3, f"<0.2x LLN ({ratio:.2f}x LLN)"
                elif ratio < 0.5:
                    grade, desc = (
                        CTCAEGrade.GRADE_2,
                        f"<0.5 - 0.2x LLN ({ratio:.2f}x LLN)",
                    )
                else:
                    grade, desc = (
                        CTCAEGrade.GRADE_1,
                        f"<1.0 - 0.5x LLN ({ratio:.2f}x LLN)",
                    )

        # Delta check: acute change > 50% from baseline
        is_delta_alert = False
        delta_pct = None
        if baseline_value and baseline_value > 0:
            delta_pct = round(((value - baseline_value) / baseline_value) * 100.0, 2)
            if abs(delta_pct) >= 50.0 and grade >= CTCAEGrade.GRADE_2:
                is_delta_alert = True

        # Grade >= 3 triggers automated critical Adverse Event escalation
        is_ae_trigger = grade >= CTCAEGrade.GRADE_3

        return LabToxicityAssessment(
            test_code=test_code,
            test_name=test_name,
            observed_value=value,
            unit=unit,
            ref_low=ref_low,
            ref_high=ref_high,
            baseline_value=baseline_value,
            ctcae_grade=grade,
            toxicity_description=desc,
            is_critical_ae_trigger=is_ae_trigger,
            is_delta_check_alert=is_delta_alert,
            delta_percentage=delta_pct,
        )
