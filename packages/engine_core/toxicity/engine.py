"""Pure mathematical execution engine for CTCAE & DAIDS laboratory toxicity grading.

Evaluates NCI-CTCAE v5.0 and DAIDS v2.1 criteria, Hy's Law DILI sentinels, acute delta checks,
and generates automated Adverse Event (AE) draft envelopes under 21 CFR Part 11.
"""

from packages.engine_core.toxicity.models import (
    AdverseEventDraftPayload,
    CTCAEGrade,
    GradeCutoff,
    HysLawEvaluation,
    ToxicityAssessmentResult,
    ToxicityCategory,
    ToxicityRuleDefinition,
)


def _build_standard_ctcae_rules() -> dict[str, ToxicityRuleDefinition]:
    """Pre-compiles standard NCI-CTCAE v5.0 clinical laboratory grading specifications."""
    rules: dict[str, ToxicityRuleDefinition] = {}

    # ALT / SGPT
    rules["ALT"] = ToxicityRuleDefinition(
        test_code="ALT",
        test_name="Alanine Aminotransferase",
        category=ToxicityCategory.HEPATIC,
        scale="CTCAE_V5",
        meddra_term="Alanine aminotransferase increased",
        grades=[
            GradeCutoff(
                grade=CTCAEGrade.GRADE_1,
                criteria_type="ULN_MULTIPLIER",
                min_multiplier=1.0,
                max_multiplier=3.0,
                description=">1.0 to 3.0x ULN",
            ),
            GradeCutoff(
                grade=CTCAEGrade.GRADE_2,
                criteria_type="ULN_MULTIPLIER",
                min_multiplier=3.0,
                max_multiplier=5.0,
                description=">3.0 to 5.0x ULN",
            ),
            GradeCutoff(
                grade=CTCAEGrade.GRADE_3,
                criteria_type="ULN_MULTIPLIER",
                min_multiplier=5.0,
                max_multiplier=20.0,
                description=">5.0 to 20.0x ULN - Severe",
            ),
            GradeCutoff(
                grade=CTCAEGrade.GRADE_4,
                criteria_type="ULN_MULTIPLIER",
                min_multiplier=20.0,
                max_multiplier=None,
                description=">20.0x ULN - Life-threatening",
            ),
        ],
    )

    # AST / SGOT
    rules["AST"] = ToxicityRuleDefinition(
        test_code="AST",
        test_name="Aspartate Aminotransferase",
        category=ToxicityCategory.HEPATIC,
        scale="CTCAE_V5",
        meddra_term="Aspartate aminotransferase increased",
        grades=[
            GradeCutoff(
                grade=CTCAEGrade.GRADE_1,
                criteria_type="ULN_MULTIPLIER",
                min_multiplier=1.0,
                max_multiplier=3.0,
                description=">1.0 to 3.0x ULN",
            ),
            GradeCutoff(
                grade=CTCAEGrade.GRADE_2,
                criteria_type="ULN_MULTIPLIER",
                min_multiplier=3.0,
                max_multiplier=5.0,
                description=">3.0 to 5.0x ULN",
            ),
            GradeCutoff(
                grade=CTCAEGrade.GRADE_3,
                criteria_type="ULN_MULTIPLIER",
                min_multiplier=5.0,
                max_multiplier=20.0,
                description=">5.0 to 20.0x ULN - Severe",
            ),
            GradeCutoff(
                grade=CTCAEGrade.GRADE_4,
                criteria_type="ULN_MULTIPLIER",
                min_multiplier=20.0,
                max_multiplier=None,
                description=">20.0x ULN - Life-threatening",
            ),
        ],
    )

    # Total Bilirubin
    rules["BILIRUBIN"] = ToxicityRuleDefinition(
        test_code="BILIRUBIN",
        test_name="Total Bilirubin",
        category=ToxicityCategory.HEPATIC,
        scale="CTCAE_V5",
        meddra_term="Blood bilirubin increased",
        grades=[
            GradeCutoff(
                grade=CTCAEGrade.GRADE_1,
                criteria_type="ULN_MULTIPLIER",
                min_multiplier=1.0,
                max_multiplier=1.5,
                description=">1.0 to 1.5x ULN",
            ),
            GradeCutoff(
                grade=CTCAEGrade.GRADE_2,
                criteria_type="ULN_MULTIPLIER",
                min_multiplier=1.5,
                max_multiplier=3.0,
                description=">1.5 to 3.0x ULN",
            ),
            GradeCutoff(
                grade=CTCAEGrade.GRADE_3,
                criteria_type="ULN_MULTIPLIER",
                min_multiplier=3.0,
                max_multiplier=10.0,
                description=">3.0 to 10.0x ULN - Severe",
            ),
            GradeCutoff(
                grade=CTCAEGrade.GRADE_4,
                criteria_type="ULN_MULTIPLIER",
                min_multiplier=10.0,
                max_multiplier=None,
                description=">10.0x ULN - Life-threatening",
            ),
        ],
    )

    # Serum Creatinine
    rules["CREATININE"] = ToxicityRuleDefinition(
        test_code="CREATININE",
        test_name="Serum Creatinine",
        category=ToxicityCategory.RENAL,
        scale="CTCAE_V5",
        meddra_term="Blood creatinine increased",
        grades=[
            GradeCutoff(
                grade=CTCAEGrade.GRADE_1,
                criteria_type="ULN_MULTIPLIER",
                min_multiplier=1.0,
                max_multiplier=1.5,
                description=">1.0 to 1.5x ULN",
            ),
            GradeCutoff(
                grade=CTCAEGrade.GRADE_2,
                criteria_type="ULN_MULTIPLIER",
                min_multiplier=1.5,
                max_multiplier=3.0,
                description=">1.5 to 3.0x ULN",
            ),
            GradeCutoff(
                grade=CTCAEGrade.GRADE_3,
                criteria_type="ULN_MULTIPLIER",
                min_multiplier=3.0,
                max_multiplier=6.0,
                description=">3.0 to 6.0x ULN - Severe",
            ),
            GradeCutoff(
                grade=CTCAEGrade.GRADE_4,
                criteria_type="ULN_MULTIPLIER",
                min_multiplier=6.0,
                max_multiplier=None,
                description=">6.0x ULN - Life-threatening / Dialysis",
            ),
        ],
    )

    # Platelets (Thrombocytopenia - LLN/Absolute)
    rules["PLATELETS"] = ToxicityRuleDefinition(
        test_code="PLATELETS",
        test_name="Platelet Count",
        category=ToxicityCategory.HEMATOLOGY,
        scale="CTCAE_V5",
        meddra_term="Platelet count decreased",
        grades=[
            GradeCutoff(
                grade=CTCAEGrade.GRADE_1,
                criteria_type="ABSOLUTE_RANGE",
                min_value=75000,
                max_value=150000,
                description="<LLN - 75,000/mm3",
            ),
            GradeCutoff(
                grade=CTCAEGrade.GRADE_2,
                criteria_type="ABSOLUTE_RANGE",
                min_value=50000,
                max_value=75000,
                description="50,000 - 75,000/mm3",
            ),
            GradeCutoff(
                grade=CTCAEGrade.GRADE_3,
                criteria_type="ABSOLUTE_RANGE",
                min_value=25000,
                max_value=50000,
                description="25,000 - 50,000/mm3 - Severe",
            ),
            GradeCutoff(
                grade=CTCAEGrade.GRADE_4,
                criteria_type="ABSOLUTE_RANGE",
                min_value=0,
                max_value=25000,
                description="<25,000/mm3 - Life-threatening",
            ),
        ],
    )

    # Potassium - Hypokalemia (LLN/Absolute)
    rules["POTASSIUM_LOW"] = ToxicityRuleDefinition(
        test_code="POTASSIUM_LOW",
        test_name="Potassium (Hypokalemia)",
        category=ToxicityCategory.ELECTROLYTES,
        scale="CTCAE_V5",
        meddra_term="Hypokalaemia",
        grades=[
            GradeCutoff(
                grade=CTCAEGrade.GRADE_1,
                criteria_type="ABSOLUTE_RANGE",
                min_value=3.0,
                max_value=3.5,
                description="<LLN - 3.0 mmol/L",
            ),
            GradeCutoff(
                grade=CTCAEGrade.GRADE_2,
                criteria_type="ABSOLUTE_RANGE",
                min_value=3.0,
                max_value=3.5,
                description="Symptomatic or <3.0 mmol/L",
            ),
            GradeCutoff(
                grade=CTCAEGrade.GRADE_3,
                criteria_type="ABSOLUTE_RANGE",
                min_value=2.5,
                max_value=3.0,
                description="<3.0 - 2.5 mmol/L - Severe",
            ),
            GradeCutoff(
                grade=CTCAEGrade.GRADE_4,
                criteria_type="ABSOLUTE_RANGE",
                min_value=0,
                max_value=2.5,
                description="<2.5 mmol/L - Life-threatening / Arrhythmia",
            ),
        ],
    )

    return rules


class LabToxicityGradingEngine:
    """Pure in-memory clinical laboratory toxicity evaluator and DILI sentinel runtime."""

    def __init__(
        self, custom_rules: list[ToxicityRuleDefinition] | None = None
    ) -> None:
        self.rules: dict[str, ToxicityRuleDefinition] = _build_standard_ctcae_rules()
        if custom_rules:
            for r in custom_rules:
                self.rules[r.test_code.upper()] = r

    def evaluate_observation(
        self,
        test_code: str,
        value: float,
        unit: str,
        ref_low: float | None = None,
        ref_high: float | None = None,
        baseline_value: float | None = None,
        rule_override: ToxicityRuleDefinition | None = None,
    ) -> ToxicityAssessmentResult:
        """Evaluates a single lab observation against CTCAE / DAIDS toxicity criteria."""
        code_u = test_code.strip().upper()
        rule = rule_override or self.rules.get(code_u)

        if rule is None:
            # Check for aliases
            if code_u in ("SGPT", "ALAT"):
                rule = self.rules.get("ALT")
            elif code_u in ("SGOT", "ASAT"):
                rule = self.rules.get("AST")
            elif code_u in ("TBIL", "TOTAL_BILIRUBIN", "BILI"):
                rule = self.rules.get("BILIRUBIN")
            elif code_u in ("CREAT", "SERUM_CREATININE", "SCR"):
                rule = self.rules.get("CREATININE")
            elif code_u in ("PLT", "THROMBOCYTES"):
                rule = self.rules.get("PLATELETS")

        test_title = rule.test_name if rule else test_code

        # Default normal result
        if rule is None or not rule.grades:
            return ToxicityAssessmentResult(
                test_code=test_code,
                test_name=test_title,
                observed_value=value,
                unit=unit,
                ref_low=ref_low,
                ref_high=ref_high,
                baseline_value=baseline_value,
                grade=CTCAEGrade.GRADE_0,
                description="Within reference limits or unconfigured toxicity rule",
            )

        matched_grade = CTCAEGrade.GRADE_0
        matched_desc = "Within normal limits"

        # Evaluate cutoffs sorted from highest severity (Grade 4) down to Grade 1
        sorted_grades = sorted(rule.grades, key=lambda g: int(g.grade), reverse=True)

        for cutoff in sorted_grades:
            ctype = cutoff.criteria_type

            if ctype == "ULN_MULTIPLIER":
                if ref_high is None or ref_high <= 0:
                    continue
                ratio = value / ref_high
                if ratio > (cutoff.min_multiplier or 1.0):
                    if cutoff.max_multiplier is None or ratio <= cutoff.max_multiplier:
                        matched_grade = cutoff.grade
                        matched_desc = f"{cutoff.description} ({ratio:.2f}x ULN)"
                        break

            elif ctype == "LLN_MULTIPLIER":
                if ref_low is None or ref_low <= 0:
                    continue
                ratio = value / ref_low
                if ratio < (cutoff.max_multiplier or 1.0):
                    if cutoff.min_multiplier is None or ratio >= cutoff.min_multiplier:
                        matched_grade = cutoff.grade
                        matched_desc = f"{cutoff.description} ({ratio:.2f}x LLN)"
                        break

            elif ctype == "ABSOLUTE_RANGE":
                # Check absolute numerical bounds
                min_v = (
                    cutoff.min_value if cutoff.min_value is not None else float("-inf")
                )
                max_v = (
                    cutoff.max_value if cutoff.max_value is not None else float("inf")
                )
                if min_v <= value <= max_v:
                    matched_grade = cutoff.grade
                    matched_desc = f"{cutoff.description} ({value} {unit})"
                    break

        # Check for delta check alert
        is_delta_alert = False
        delta_pct: float | None = None
        if baseline_value is not None and baseline_value > 0:
            delta_pct = round(((value - baseline_value) / baseline_value) * 100.0, 2)
            if abs(delta_pct) >= 50.0:
                is_delta_alert = True

        # Generate AE Draft if Grade >= 3
        is_critical = matched_grade in (
            CTCAEGrade.GRADE_3,
            CTCAEGrade.GRADE_4,
            CTCAEGrade.GRADE_5,
        )
        ae_draft: AdverseEventDraftPayload | None = None

        if is_critical:
            severity = (
                "LIFE_THREATENING" if matched_grade == CTCAEGrade.GRADE_4 else "SEVERE"
            )
            ae_draft = AdverseEventDraftPayload(
                test_code=test_code,
                test_name=test_title,
                observed_value=value,
                unit=unit,
                grade=matched_grade,
                meddra_term=rule.meddra_term or f"{test_title} abnormal",
                severity=severity,
                is_serious=True,
                hys_law_indicated=False,
            )

        return ToxicityAssessmentResult(
            test_code=test_code,
            test_name=test_title,
            observed_value=value,
            unit=unit,
            ref_low=ref_low,
            ref_high=ref_high,
            baseline_value=baseline_value,
            grade=matched_grade,
            scale=rule.scale,
            description=matched_desc,
            is_critical_ae_trigger=is_critical,
            is_delta_check_alert=is_delta_alert,
            delta_percentage=delta_pct,
            ae_draft=ae_draft,
        )

    @staticmethod
    def evaluate_hys_law(
        alt: float,
        ast: float,
        tbil: float,
        alkp: float,
        alt_uln: float,
        ast_uln: float,
        tbil_uln: float,
        alkp_uln: float,
    ) -> HysLawEvaluation:
        """Evaluates multi-analyte hepatic triad against FDA / ICH Hy's Law DILI criteria.

        Hy's Law Criteria:
        1. Aminotransferase elevation: (ALT >= 3x ULN OR AST >= 3x ULN)
        2. Total Bilirubin elevation: (TBIL >= 2x ULN)
        3. No initial cholestasis: (ALKP < 2x ULN)
        """
        if any(uln <= 0 for uln in (alt_uln, ast_uln, tbil_uln, alkp_uln)):
            return HysLawEvaluation(
                is_hys_law_met=False,
                alt_ast_ratio_uln=0.0,
                tbil_ratio_uln=0.0,
                alkp_ratio_uln=0.0,
                alert_level="NORMAL",
                rationale="ULN reference bounds not fully defined for all hepatic analytes",
            )

        alt_ratio = alt / alt_uln
        ast_ratio = ast / ast_uln
        max_transaminase_ratio = max(alt_ratio, ast_ratio)
        tbil_ratio = tbil / tbil_uln
        alkp_ratio = alkp / alkp_uln

        is_transaminase_elevated = max_transaminase_ratio >= 3.0
        is_bili_elevated = tbil_ratio >= 2.0
        is_not_cholestatic = alkp_ratio < 2.0

        if is_transaminase_elevated and is_bili_elevated and is_not_cholestatic:
            return HysLawEvaluation(
                is_hys_law_met=True,
                alt_ast_ratio_uln=round(max_transaminase_ratio, 2),
                tbil_ratio_uln=round(tbil_ratio, 2),
                alkp_ratio_uln=round(alkp_ratio, 2),
                alert_level="HYS_LAW_CRITICAL",
                rationale=(
                    f"Hy's Law DILI Criteria MET: ALT/AST {max_transaminase_ratio:.2f}x ULN (>=3.0x), "
                    f"Total Bilirubin {tbil_ratio:.2f}x ULN (>=2.0x), and ALKP {alkp_ratio:.2f}x ULN (<2.0x)."
                ),
            )

        if is_transaminase_elevated and is_bili_elevated:
            return HysLawEvaluation(
                is_hys_law_met=False,
                alt_ast_ratio_uln=round(max_transaminase_ratio, 2),
                tbil_ratio_uln=round(tbil_ratio, 2),
                alkp_ratio_uln=round(alkp_ratio, 2),
                alert_level="POTENTIAL_DILI_WARNING",
                rationale=(
                    f"Potential DILI with Cholestasis: ALT/AST {max_transaminase_ratio:.2f}x ULN (>=3.0x) "
                    f"and TBIL {tbil_ratio:.2f}x ULN (>=2.0x), but ALKP is elevated ({alkp_ratio:.2f}x ULN >= 2.0x)."
                ),
            )

        if is_transaminase_elevated or is_bili_elevated:
            return HysLawEvaluation(
                is_hys_law_met=False,
                alt_ast_ratio_uln=round(max_transaminase_ratio, 2),
                tbil_ratio_uln=round(tbil_ratio, 2),
                alkp_ratio_uln=round(alkp_ratio, 2),
                alert_level="BORDERLINE_HEPATIC",
                rationale=f"Borderline hepatic elevation: Transaminases {max_transaminase_ratio:.2f}x ULN, TBIL {tbil_ratio:.2f}x ULN.",
            )

        return HysLawEvaluation(
            is_hys_law_met=False,
            alt_ast_ratio_uln=round(max_transaminase_ratio, 2),
            tbil_ratio_uln=round(tbil_ratio, 2),
            alkp_ratio_uln=round(alkp_ratio, 2),
            alert_level="NORMAL",
            rationale="Hepatic biomarkers within acceptable non-DILI boundaries.",
        )
