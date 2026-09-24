"""Central & Local Lab CTCAE v5.0 / DAIDS Toxicity Grading, Hy's Law, and Delta Check Engine.

Requirements:
- PRD-LAB-001 (Laboratory Batch Ingestion & Range Evaluation)
- PRD-PVG-001 (Automated Pharmacovigilance & Safety Escalation)
- PRD-SYS-001 / PRD-SYS-123 (NCI-CTCAE v5.0, DAIDS v2.1, KDIGO, Hy's Law)
"""

from datetime import UTC, datetime
from enum import IntEnum, StrEnum

from pydantic import BaseModel, Field


class CTCAEGrade(IntEnum):
    """NCI-CTCAE v5.0 & DAIDS severity grades."""

    GRADE_0 = 0  # Normal / Within reference range
    GRADE_1 = 1  # Mild; asymptomatic or mild symptoms; clinical or diagnostic observations only
    GRADE_2 = 2  # Moderate; minimal, local or noninvasive intervention indicated
    GRADE_3 = 3  # Severe; medically significant but not immediately life-threatening; hospitalization indicated
    GRADE_4 = 4  # Life-threatening consequences; urgent intervention indicated
    GRADE_5 = 5  # Death related to AE


class ToxicityCategory(StrEnum):
    """Standard clinical laboratory toxicity categories."""

    HEPATIC = "HEPATIC"  # ALT, AST, Total Bilirubin, Alkaline Phosphatase
    RENAL = "RENAL"  # Serum Creatinine, eGFR, Blood Urea Nitrogen
    HEMATOLOGY = "HEMATOLOGY"  # Platelets, Hemoglobin, ANC, WBC, Lymphocytes
    ELECTROLYTES = "ELECTROLYTES"  # Potassium (Hypo/Hyper), Sodium, Calcium, Magnesium
    METABOLIC = "METABOLIC"  # Glucose (Hypo/Hyper), Cholesterol, Triglycerides
    GENERAL = "GENERAL"


class LabToxicityAssessment(BaseModel):
    """Evaluated CTCAE / DAIDS toxicity result for a single laboratory observation."""

    test_code: str
    test_name: str
    observed_value: float
    unit: str
    ref_low: float | None = None
    ref_high: float | None = None
    baseline_value: float | None = None
    ctcae_grade: CTCAEGrade = CTCAEGrade.GRADE_0
    grading_scale: str = "CTCAE_V5"
    toxicity_description: str
    is_critical_ae_trigger: bool = False
    is_hys_law_warning: bool = False
    is_delta_check_alert: bool = False
    delta_percentage: float | None = None
    eval_timestamp: str = Field(default_factory=lambda: datetime.now(UTC).isoformat())


class LabToxicityEngine:
    """Core domain engine evaluating NCI-CTCAE v5.0 and DAIDS v2.1 toxicity grading,
    Hy's Law DILI alerts, and acute delta checks."""

    @classmethod
    def evaluate_hepatic(
        cls,
        test_code: str,
        value: float,
        uln: float,
        baseline: float | None = None,
    ) -> tuple[CTCAEGrade, str]:
        """Evaluates hepatic enzymes (ALT, AST, Bilirubin, ALKP) relative to Upper Limit of Normal (ULN).

        NCI-CTCAE v5.0 Criteria:
        - ALT/AST: Grade 1 (>1.0-3.0x ULN), Grade 2 (>3.0-5.0x ULN), Grade 3 (>5.0-20.0x ULN), Grade 4 (>20.0x ULN)
        - Total Bilirubin: Grade 1 (>1.0-1.5x ULN), Grade 2 (>1.5-3.0x ULN), Grade 3 (>3.0-10.0x ULN), Grade 4 (>10.0x ULN)
        - Alkaline Phosphatase: Grade 1 (>1.0-2.5x ULN), Grade 2 (>2.5-5.0x ULN), Grade 3 (>5.0-20.0x ULN), Grade 4 (>20.0x ULN)
        """
        if uln <= 0.0:
            return CTCAEGrade.GRADE_0, "ULN not defined"

        ratio = value / uln
        if ratio <= 1.0:
            return CTCAEGrade.GRADE_0, "Within normal limits"

        code_u = test_code.strip().upper()

        if code_u in ("BILI", "TBIL", "BILIRUBIN", "TOTAL_BILIRUBIN"):
            if ratio <= 1.5:
                return CTCAEGrade.GRADE_1, f">1.0 - 1.5x ULN ({ratio:.2f}x ULN)"
            if ratio <= 3.0:
                return CTCAEGrade.GRADE_2, f">1.5 - 3.0x ULN ({ratio:.2f}x ULN)"
            if ratio <= 10.0:
                return (
                    CTCAEGrade.GRADE_3,
                    f">3.0 - 10.0x ULN ({ratio:.2f}x ULN) - Severe",
                )
            return (
                CTCAEGrade.GRADE_4,
                f">10.0x ULN ({ratio:.2f}x ULN) - Life Threatening",
            )

        if code_u in ("ALKP", "ALP", "ALKALINE_PHOSPHATASE"):
            if ratio <= 2.5:
                return CTCAEGrade.GRADE_1, f">1.0 - 2.5x ULN ({ratio:.2f}x ULN)"
            if ratio <= 5.0:
                return CTCAEGrade.GRADE_2, f">2.5 - 5.0x ULN ({ratio:.2f}x ULN)"
            if ratio <= 20.0:
                return (
                    CTCAEGrade.GRADE_3,
                    f">5.0 - 20.0x ULN ({ratio:.2f}x ULN) - Severe",
                )
            return (
                CTCAEGrade.GRADE_4,
                f">20.0x ULN ({ratio:.2f}x ULN) - Life Threatening",
            )

        # Standard ALT / AST
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
    def check_hys_law(
        cls,
        alt_val: float | None,
        ast_val: float | None,
        bili_val: float | None,
        alkp_val: float | None,
        alt_uln: float = 35.0,
        ast_uln: float = 35.0,
        bili_uln: float = 1.2,
        alkp_uln: float = 120.0,
    ) -> tuple[bool, str]:
        """Evaluates Hy's Law criteria for Drug-Induced Liver Injury (DILI):
        1. Aminotransferase elevation: ALT > 3x ULN or AST > 3x ULN
        2. Total Bilirubin elevation: Total Bilirubin > 2x ULN
        3. Alkaline Phosphatase: ALKP < 2x ULN (ruling out cholestatic etiology)

        Returns (is_hys_law_met, explanation_message).
        """
        alt_ratio = (alt_val / alt_uln) if (alt_val and alt_uln > 0) else 0.0
        ast_ratio = (ast_val / ast_uln) if (ast_val and ast_uln > 0) else 0.0
        bili_ratio = (bili_val / bili_uln) if (bili_val and bili_uln > 0) else 0.0
        alkp_ratio = (alkp_val / alkp_uln) if (alkp_val and alkp_uln > 0) else 0.0

        transaminase_elevated = (alt_ratio > 3.0) or (ast_ratio > 3.0)
        bilirubin_elevated = bili_ratio > 2.0
        alkp_not_dominant = alkp_ratio < 2.0 or alkp_val is None

        if transaminase_elevated and bilirubin_elevated and alkp_not_dominant:
            return (
                True,
                f"Hy's Law DILI Alert: Transaminase >3x ULN (ALT {alt_ratio:.1f}x, AST {ast_ratio:.1f}x), "
                f"Total Bilirubin >2x ULN ({bili_ratio:.1f}x), and ALKP <2x ULN ({alkp_ratio:.1f}x).",
            )
        return False, "Hy's Law criteria not met"

    @classmethod
    def evaluate_renal_creatinine(
        cls,
        value: float,
        uln: float,
        baseline: float | None = None,
    ) -> tuple[CTCAEGrade, str]:
        """Evaluates Serum Creatinine relative to ULN and baseline (KDIGO/CTCAE)."""
        if uln <= 0.0:
            return CTCAEGrade.GRADE_0, "ULN not defined"

        ratio_uln = value / uln
        ratio_baseline = (value / baseline) if (baseline and baseline > 0) else 1.0

        if ratio_uln > 6.0:
            return (
                CTCAEGrade.GRADE_4,
                f">6.0x ULN ({ratio_uln:.2f}x ULN) - Life Threatening / Dialysis Indicated",
            )
        if ratio_uln > 3.0 or (baseline and ratio_baseline >= 2.0):
            return (
                CTCAEGrade.GRADE_3,
                f">3.0 - 6.0x ULN or >=2.0x baseline ({ratio_uln:.2f}x ULN) - Severe",
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
        """Evaluates Absolute Neutrophil Count (ANC) in 10^9/L (or G/L)."""
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
    def evaluate_platelets(
        cls,
        plt_value_10e9: float,
        lln: float = 150.0,
    ) -> tuple[CTCAEGrade, str]:
        """Evaluates Platelet count in 10^9/L (or 10^3/uL)."""
        if plt_value_10e9 < 25.0:
            return (
                CTCAEGrade.GRADE_4,
                f"<25.0 x 10^9/L ({plt_value_10e9:.1f}) - Life Threatening / Urgent Intervention Indicated",
            )
        if plt_value_10e9 < 50.0:
            return (
                CTCAEGrade.GRADE_3,
                f"<50.0 - 25.0 x 10^9/L ({plt_value_10e9:.1f}) - Severe Thrombocytopenia",
            )
        if plt_value_10e9 < 75.0:
            return (
                CTCAEGrade.GRADE_2,
                f"<75.0 - 50.0 x 10^9/L ({plt_value_10e9:.1f}) - Moderate Thrombocytopenia",
            )
        if plt_value_10e9 < lln:
            return (
                CTCAEGrade.GRADE_1,
                f"<LLN - 75.0 x 10^9/L ({plt_value_10e9:.1f}) - Mild Thrombocytopenia",
            )
        return CTCAEGrade.GRADE_0, "Within normal limits"

    @classmethod
    def evaluate_hemoglobin(
        cls,
        hgb_g_per_dl: float,
        lln: float = 12.0,
    ) -> tuple[CTCAEGrade, str]:
        """Evaluates Hemoglobin in g/dL (Anemia)."""
        if hgb_g_per_dl < 6.5:
            return (
                CTCAEGrade.GRADE_4,
                f"<6.5 g/dL ({hgb_g_per_dl:.1f}) - Life Threatening Anemia / Urgent Intervention Indicated",
            )
        if hgb_g_per_dl < 8.0:
            return (
                CTCAEGrade.GRADE_3,
                f"<8.0 g/dL ({hgb_g_per_dl:.1f}) - Severe Anemia / Transfusion Indicated",
            )
        if hgb_g_per_dl < 10.0:
            return (
                CTCAEGrade.GRADE_2,
                f"<10.0 - 8.0 g/dL ({hgb_g_per_dl:.1f}) - Moderate Anemia",
            )
        if hgb_g_per_dl < lln:
            return (
                CTCAEGrade.GRADE_1,
                f"<LLN - 10.0 g/dL ({hgb_g_per_dl:.1f}) - Mild Anemia",
            )
        return CTCAEGrade.GRADE_0, "Within normal limits"

    @classmethod
    def evaluate_white_blood_cells(
        cls,
        wbc_10e9: float,
        lln: float = 4.0,
    ) -> tuple[CTCAEGrade, str]:
        """Evaluates White Blood Cell count in 10^9/L (Leukopenia)."""
        if wbc_10e9 < 1.0:
            return (
                CTCAEGrade.GRADE_4,
                f"<1.0 x 10^9/L ({wbc_10e9:.2f}) - Life Threatening Leukopenia",
            )
        if wbc_10e9 < 2.0:
            return (
                CTCAEGrade.GRADE_3,
                f"<2.0 - 1.0 x 10^9/L ({wbc_10e9:.2f}) - Severe Leukopenia",
            )
        if wbc_10e9 < 3.0:
            return (
                CTCAEGrade.GRADE_2,
                f"<3.0 - 2.0 x 10^9/L ({wbc_10e9:.2f}) - Moderate Leukopenia",
            )
        if wbc_10e9 < lln:
            return (
                CTCAEGrade.GRADE_1,
                f"<LLN - 3.0 x 10^9/L ({wbc_10e9:.2f}) - Mild Leukopenia",
            )
        return CTCAEGrade.GRADE_0, "Within normal limits"

    @classmethod
    def evaluate_potassium(
        cls,
        val_mmol_l: float,
        lln: float = 3.5,
        uln: float = 5.0,
    ) -> tuple[CTCAEGrade, str]:
        """Evaluates Serum Potassium in mmol/L (Hyperkalemia & Hypokalemia)."""
        # Hyperkalemia
        if val_mmol_l > uln:
            if val_mmol_l > 7.0:
                return (
                    CTCAEGrade.GRADE_4,
                    f">7.0 mmol/L ({val_mmol_l:.1f}) - Life Threatening Hyperkalemia",
                )
            if val_mmol_l > 6.0:
                return (
                    CTCAEGrade.GRADE_3,
                    f">6.0 - 7.0 mmol/L ({val_mmol_l:.1f}) - Severe Hyperkalemia / Hospitalization Indicated",
                )
            if val_mmol_l > 5.5:
                return (
                    CTCAEGrade.GRADE_2,
                    f">5.5 - 6.0 mmol/L ({val_mmol_l:.1f}) - Moderate Hyperkalemia",
                )
            return (
                CTCAEGrade.GRADE_1,
                f">ULN - 5.5 mmol/L ({val_mmol_l:.1f}) - Mild Hyperkalemia",
            )

        # Hypokalemia
        if val_mmol_l < lln:
            if val_mmol_l < 2.0:
                return (
                    CTCAEGrade.GRADE_4,
                    f"<2.0 mmol/L ({val_mmol_l:.1f}) - Life Threatening Hypokalemia",
                )
            if val_mmol_l < 2.5:
                return (
                    CTCAEGrade.GRADE_3,
                    f"<2.5 - 2.0 mmol/L ({val_mmol_l:.1f}) - Severe Hypokalemia / IV Replacement Indicated",
                )
            if val_mmol_l < 3.0:
                return (
                    CTCAEGrade.GRADE_2,
                    f"<3.0 - 2.5 mmol/L ({val_mmol_l:.1f}) - Moderate Hypokalemia",
                )
            return (
                CTCAEGrade.GRADE_1,
                f"<LLN - 3.0 mmol/L ({val_mmol_l:.1f}) - Mild Hypokalemia",
            )

        return CTCAEGrade.GRADE_0, "Within normal limits"

    @classmethod
    def evaluate_sodium(
        cls,
        val_mmol_l: float,
        lln: float = 135.0,
        uln: float = 145.0,
    ) -> tuple[CTCAEGrade, str]:
        """Evaluates Serum Sodium in mmol/L (Hypernatremia & Hyponatremia)."""
        # Hypernatremia
        if val_mmol_l > uln:
            if val_mmol_l > 160.0:
                return (
                    CTCAEGrade.GRADE_4,
                    f">160 mmol/L ({val_mmol_l:.1f}) - Life Threatening Hypernatremia",
                )
            if val_mmol_l > 155.0:
                return (
                    CTCAEGrade.GRADE_3,
                    f">155 - 160 mmol/L ({val_mmol_l:.1f}) - Severe Hypernatremia / Hospitalization Indicated",
                )
            if val_mmol_l > 150.0:
                return (
                    CTCAEGrade.GRADE_2,
                    f">150 - 155 mmol/L ({val_mmol_l:.1f}) - Moderate Hypernatremia",
                )
            return (
                CTCAEGrade.GRADE_1,
                f">ULN - 150 mmol/L ({val_mmol_l:.1f}) - Mild Hypernatremia",
            )

        # Hyponatremia
        if val_mmol_l < lln:
            if val_mmol_l < 120.0:
                return (
                    CTCAEGrade.GRADE_4,
                    f"<120 mmol/L ({val_mmol_l:.1f}) - Life Threatening Hyponatremia",
                )
            if val_mmol_l < 125.0:
                return (
                    CTCAEGrade.GRADE_3,
                    f"<125 - 120 mmol/L ({val_mmol_l:.1f}) - Severe Hyponatremia / Hospitalization Indicated",
                )
            if val_mmol_l < 130.0:
                return (
                    CTCAEGrade.GRADE_2,
                    f"<130 - 125 mmol/L ({val_mmol_l:.1f}) - Moderate Hyponatremia",
                )
            return (
                CTCAEGrade.GRADE_1,
                f"<LLN - 130 mmol/L ({val_mmol_l:.1f}) - Mild Hyponatremia",
            )

        return CTCAEGrade.GRADE_0, "Within normal limits"

    @classmethod
    def evaluate_glucose(
        cls,
        val_mg_dl: float,
        lln: float = 70.0,
        uln: float = 100.0,
    ) -> tuple[CTCAEGrade, str]:
        """Evaluates Fasting Serum Glucose in mg/dL (Hyperglycemia & Hypoglycemia)."""
        # Hyperglycemia
        if val_mg_dl > uln:
            if val_mg_dl > 500.0:
                return (
                    CTCAEGrade.GRADE_4,
                    f">500 mg/dL ({val_mg_dl:.1f}) - Life Threatening Hyperglycemia / Ketoacidosis Risk",
                )
            if val_mg_dl > 250.0:
                return (
                    CTCAEGrade.GRADE_3,
                    f">250 - 500 mg/dL ({val_mg_dl:.1f}) - Severe Hyperglycemia / Insulin Indicated",
                )
            if val_mg_dl > 160.0:
                return (
                    CTCAEGrade.GRADE_2,
                    f">160 - 250 mg/dL ({val_mg_dl:.1f}) - Moderate Hyperglycemia",
                )
            return (
                CTCAEGrade.GRADE_1,
                f">ULN - 160 mg/dL ({val_mg_dl:.1f}) - Mild Hyperglycemia",
            )

        # Hypoglycemia
        if val_mg_dl < lln:
            if val_mg_dl < 30.0:
                return (
                    CTCAEGrade.GRADE_4,
                    f"<30 mg/dL ({val_mg_dl:.1f}) - Life Threatening Hypoglycemia / Seizures Risk",
                )
            if val_mg_dl < 40.0:
                return (
                    CTCAEGrade.GRADE_3,
                    f"<40 - 30 mg/dL ({val_mg_dl:.1f}) - Severe Hypoglycemia / IV Glucose Indicated",
                )
            if val_mg_dl < 55.0:
                return (
                    CTCAEGrade.GRADE_2,
                    f"<55 - 40 mg/dL ({val_mg_dl:.1f}) - Moderate Hypoglycemia",
                )
            return (
                CTCAEGrade.GRADE_1,
                f"<LLN - 55 mg/dL ({val_mg_dl:.1f}) - Mild Hypoglycemia",
            )

        return CTCAEGrade.GRADE_0, "Within normal limits"

    @classmethod
    def evaluate_daids(
        cls,
        test_code: str,
        value: float,
        lln: float | None = None,
        uln: float | None = None,
        baseline: float | None = None,
    ) -> tuple[CTCAEGrade, str]:
        """Evaluates laboratory parameter according to DAIDS v2.1 Severity Grading Table."""
        code_u = test_code.strip().upper()
        ref_h = uln or 0.0
        ref_l = lln or 0.0

        if code_u in ("ALT", "AST", "SGPT", "SGOT"):
            if ref_h <= 0.0:
                return CTCAEGrade.GRADE_0, "DAIDS: ULN not defined"
            ratio = value / ref_h
            if ratio <= 1.0:
                return CTCAEGrade.GRADE_0, "DAIDS: Within normal limits"
            if ratio < 2.5:
                return (
                    CTCAEGrade.GRADE_1,
                    f"DAIDS Grade 1: 1.25 - <2.5x ULN ({ratio:.2f}x ULN)",
                )
            if ratio < 5.0:
                return (
                    CTCAEGrade.GRADE_2,
                    f"DAIDS Grade 2: 2.5 - <5.0x ULN ({ratio:.2f}x ULN)",
                )
            if ratio <= 10.0:
                return (
                    CTCAEGrade.GRADE_3,
                    f"DAIDS Grade 3: 5.0 - 10.0x ULN ({ratio:.2f}x ULN) - Severe",
                )
            return (
                CTCAEGrade.GRADE_4,
                f"DAIDS Grade 4: >10.0x ULN ({ratio:.2f}x ULN) - Potentially Life-Threatening",
            )

        if code_u in ("CREAT", "CREATININE"):
            if ref_h <= 0.0:
                return CTCAEGrade.GRADE_0, "DAIDS: ULN not defined"
            ratio = value / ref_h
            if ratio <= 1.0:
                return CTCAEGrade.GRADE_0, "DAIDS: Within normal limits"
            if ratio <= 1.3:
                return (
                    CTCAEGrade.GRADE_1,
                    f"DAIDS Grade 1: 1.1 - 1.3x ULN ({ratio:.2f}x ULN)",
                )
            if ratio <= 1.8:
                return (
                    CTCAEGrade.GRADE_2,
                    f"DAIDS Grade 2: 1.4 - 1.8x ULN ({ratio:.2f}x ULN)",
                )
            if ratio <= 3.4:
                return (
                    CTCAEGrade.GRADE_3,
                    f"DAIDS Grade 3: 1.9 - 3.4x ULN ({ratio:.2f}x ULN) - Severe",
                )
            return (
                CTCAEGrade.GRADE_4,
                f"DAIDS Grade 4: >=3.5x ULN ({ratio:.2f}x ULN) - Life-Threatening",
            )

        if code_u in ("ANC", "NEUT"):
            if value < 0.4:
                return (
                    CTCAEGrade.GRADE_4,
                    f"DAIDS Grade 4: <0.4 x 10^9/L ({value:.2f}) - Life-Threatening",
                )
            if value < 0.6:
                return (
                    CTCAEGrade.GRADE_3,
                    f"DAIDS Grade 3: 0.4 - <0.6 x 10^9/L ({value:.2f}) - Severe",
                )
            if value < 0.8:
                return (
                    CTCAEGrade.GRADE_2,
                    f"DAIDS Grade 2: 0.6 - <0.8 x 10^9/L ({value:.2f}) - Moderate",
                )
            if value < (ref_l or 1.0):
                return (
                    CTCAEGrade.GRADE_1,
                    f"DAIDS Grade 1: 0.8 - <1.0 x 10^9/L ({value:.2f}) - Mild",
                )
            return CTCAEGrade.GRADE_0, "DAIDS: Within normal limits"

        if code_u in ("PLT", "PLATELETS"):
            if value < 25.0:
                return (
                    CTCAEGrade.GRADE_4,
                    f"DAIDS Grade 4: <25 x 10^9/L ({value:.1f}) - Life-Threatening",
                )
            if value < 50.0:
                return (
                    CTCAEGrade.GRADE_3,
                    f"DAIDS Grade 3: 25 - <50 x 10^9/L ({value:.1f}) - Severe",
                )
            if value < 100.0:
                return (
                    CTCAEGrade.GRADE_2,
                    f"DAIDS Grade 2: 50 - <100 x 10^9/L ({value:.1f}) - Moderate",
                )
            if value < (ref_l or 125.0):
                return (
                    CTCAEGrade.GRADE_1,
                    f"DAIDS Grade 1: 100 - <125 x 10^9/L ({value:.1f}) - Mild",
                )
            return CTCAEGrade.GRADE_0, "DAIDS: Within normal limits"

        # Fallback to standard CTCAE logic
        return cls.evaluate_observation(
            test_code=test_code,
            test_name=test_code,
            value=value,
            unit="",
            ref_low=lln,
            ref_high=uln,
            baseline_value=baseline,
            grading_scale="CTCAE_V5",
        ).ctcae_grade, "DAIDS standard evaluation applied"

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
        grading_scale: str = "CTCAE_V5",
        alt_co_analyte: float | None = None,
        ast_co_analyte: float | None = None,
        bili_co_analyte: float | None = None,
        alkp_co_analyte: float | None = None,
    ) -> LabToxicityAssessment:
        """Assesses toxicity grade (CTCAE v5.0 / DAIDS v2.1), Hy's Law DILI alerts,
        and acute delta checks for an incoming laboratory observation.

        Args:
            test_code: Standardized test identifier (e.g. 'ALT', 'AST', 'CREAT', 'ANC', 'PLT', 'POTASSIUM').
            test_name: Full descriptive test name.
            value: Observed numeric test result.
            unit: Normalized measurement unit string.
            ref_low: Lower boundary of demographic normal range (LLN).
            ref_high: Upper boundary of demographic normal range (ULN).
            baseline_value: Optional subject baseline measurement.
            grading_scale: Protocol-pinned toxicity scale ('CTCAE_V5' or 'DAIDS_V2_1').
            alt_co_analyte: Co-reported ALT for Hy's Law compound assessment.
            ast_co_analyte: Co-reported AST for Hy's Law compound assessment.
            bili_co_analyte: Co-reported Total Bilirubin for Hy's Law compound assessment.
            alkp_co_analyte: Co-reported ALKP for Hy's Law compound assessment.

        Returns:
            LabToxicityAssessment containing computed grade, descriptions, and safety alert flags.
        """
        code_upper = test_code.strip().upper()
        uln = ref_high or 0.0
        lln = ref_low or 0.0
        norm_scale = grading_scale.strip().upper()

        grade = CTCAEGrade.GRADE_0
        desc = "Within normal limits"

        if norm_scale == "DAIDS_V2_1":
            grade, desc = cls.evaluate_daids(
                test_code=code_upper,
                value=value,
                lln=lln,
                uln=uln,
                baseline=baseline_value,
            )
        elif code_upper in (
            "ALT",
            "AST",
            "SGPT",
            "SGOT",
            "BILI",
            "TBIL",
            "ALKP",
            "ALP",
        ):
            grade, desc = cls.evaluate_hepatic(code_upper, value, uln, baseline_value)
        elif code_upper in ("CREAT", "CREATININE", "SERUM_CREATININE"):
            grade, desc = cls.evaluate_renal_creatinine(value, uln, baseline_value)
        elif code_upper in ("NEUT", "ANC", "NEUTROPHILS"):
            grade, desc = cls.evaluate_neutrophils_anc(value, lln=lln or 1.5)
        elif code_upper in ("PLT", "PLATELETS"):
            grade, desc = cls.evaluate_platelets(value, lln=lln or 150.0)
        elif code_upper in ("HGB", "HEMOGLOBIN"):
            grade, desc = cls.evaluate_hemoglobin(value, lln=lln or 12.0)
        elif code_upper in ("WBC", "LEUKOCYTES"):
            grade, desc = cls.evaluate_white_blood_cells(value, lln=lln or 4.0)
        elif code_upper in ("POTASSIUM", "K"):
            grade, desc = cls.evaluate_potassium(value, lln=lln or 3.5, uln=uln or 5.0)
        elif code_upper in ("SODIUM", "NA"):
            grade, desc = cls.evaluate_sodium(value, lln=lln or 135.0, uln=uln or 145.0)
        elif code_upper in ("GLUCOSE", "GLUC"):
            grade, desc = cls.evaluate_glucose(value, lln=lln or 70.0, uln=uln or 100.0)
        else:
            # Generic ratio-based grading fallback
            if uln > 0 and value > uln:
                ratio = value / uln
                if ratio > 20.0:
                    grade, desc = (
                        CTCAEGrade.GRADE_4,
                        f">20.0x ULN ({ratio:.2f}x ULN) - Life Threatening",
                    )
                elif ratio > 5.0:
                    grade, desc = (
                        CTCAEGrade.GRADE_3,
                        f">5.0 - 20.0x ULN ({ratio:.2f}x ULN) - Severe",
                    )
                elif ratio > 3.0:
                    grade, desc = (
                        CTCAEGrade.GRADE_2,
                        f">3.0 - 5.0x ULN ({ratio:.2f}x ULN) - Moderate",
                    )
                else:
                    grade, desc = (
                        CTCAEGrade.GRADE_1,
                        f">1.0 - 3.0x ULN ({ratio:.2f}x ULN) - Mild",
                    )
            elif lln > 0 and value < lln:
                ratio = value / lln
                if ratio < 0.2:
                    grade, desc = (
                        CTCAEGrade.GRADE_4,
                        f"<0.2x LLN ({ratio:.2f}x LLN) - Life Threatening",
                    )
                elif ratio < 0.5:
                    grade, desc = (
                        CTCAEGrade.GRADE_3,
                        f"<0.5 - 0.2x LLN ({ratio:.2f}x LLN) - Severe",
                    )
                elif ratio < 0.8:
                    grade, desc = (
                        CTCAEGrade.GRADE_2,
                        f"<0.8 - 0.5x LLN ({ratio:.2f}x LLN) - Moderate",
                    )
                else:
                    grade, desc = (
                        CTCAEGrade.GRADE_1,
                        f"<1.0 - 0.8x LLN ({ratio:.2f}x LLN) - Mild",
                    )

        # Hy's Law evaluation for hepatic analytes
        is_hys_law = False
        effective_alt = value if code_upper == "ALT" else alt_co_analyte
        effective_ast = value if code_upper == "AST" else ast_co_analyte
        effective_bili = value if code_upper in ("BILI", "TBIL") else bili_co_analyte
        effective_alkp = value if code_upper in ("ALKP", "ALP") else alkp_co_analyte

        if (effective_alt or effective_ast) and effective_bili:
            hys_law_met, hys_law_msg = cls.check_hys_law(
                alt_val=effective_alt,
                ast_val=effective_ast,
                bili_val=effective_bili,
                alkp_val=effective_alkp,
                alt_uln=uln if code_upper == "ALT" else 35.0,
                ast_uln=uln if code_upper == "AST" else 35.0,
                bili_uln=uln if code_upper in ("BILI", "TBIL") else 1.2,
                alkp_uln=uln if code_upper in ("ALKP", "ALP") else 120.0,
            )
            if hys_law_met:
                is_hys_law = True
                desc = f"{desc} | {hys_law_msg}"

        # Delta check: acute change > 50% from baseline
        is_delta_alert = False
        delta_pct = None
        if baseline_value and baseline_value > 0:
            delta_pct = round(((value - baseline_value) / baseline_value) * 100.0, 2)
            if abs(delta_pct) >= 50.0 and grade >= CTCAEGrade.GRADE_2:
                is_delta_alert = True

        # Grade >= 3 triggers automated critical Adverse Event escalation
        is_ae_trigger = grade >= CTCAEGrade.GRADE_3 or is_hys_law

        return LabToxicityAssessment(
            test_code=test_code,
            test_name=test_name,
            observed_value=value,
            unit=unit,
            ref_low=ref_low,
            ref_high=ref_high,
            baseline_value=baseline_value,
            ctcae_grade=grade,
            grading_scale=norm_scale,
            toxicity_description=desc,
            is_critical_ae_trigger=is_ae_trigger,
            is_hys_law_warning=is_hys_law,
            is_delta_check_alert=is_delta_alert,
            delta_percentage=delta_pct,
        )
