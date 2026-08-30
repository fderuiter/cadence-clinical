"""Unit tests for the CTCAE & DAIDS Lab Toxicity Grading Engine.

@req:PRD-PVG-001, PRD-SYS-123
"""

from packages.engine_core.toxicity.engine import (
    LabToxicityGradingEngine,
)
from packages.engine_core.toxicity.models import (
    CTCAEGrade,
    GradeCutoff,
    ToxicityCategory,
    ToxicityRuleDefinition,
)


def test_standard_ctcae_hepatic_grading_alt():
    """Verify NCI-CTCAE v5.0 ALT toxicity grading relative to ULN.

    @req:PRD-PVG-001
    """
    engine = LabToxicityGradingEngine()
    alt_uln = 35.0  # U/L

    # 1. Normal: 30 U/L (<= 1.0x ULN) -> Grade 0
    res_0 = engine.evaluate_observation("ALT", 30.0, "U/L", ref_high=alt_uln)
    assert res_0.grade == CTCAEGrade.GRADE_0
    assert res_0.is_critical_ae_trigger is False
    assert res_0.ae_draft is None

    # 2. Grade 1: 70 U/L (2.0x ULN) -> >1.0 to 3.0x ULN
    res_1 = engine.evaluate_observation("ALT", 70.0, "U/L", ref_high=alt_uln)
    assert res_1.grade == CTCAEGrade.GRADE_1
    assert res_1.is_critical_ae_trigger is False

    # 3. Grade 2: 140 U/L (4.0x ULN) -> >3.0 to 5.0x ULN
    res_2 = engine.evaluate_observation("ALT", 140.0, "U/L", ref_high=alt_uln)
    assert res_2.grade == CTCAEGrade.GRADE_2

    # 4. Grade 3: 350 U/L (10.0x ULN) -> >5.0 to 20.0x ULN (Severe)
    res_3 = engine.evaluate_observation("ALT", 350.0, "U/L", ref_high=alt_uln)
    assert res_3.grade == CTCAEGrade.GRADE_3
    assert res_3.is_critical_ae_trigger is True
    assert res_3.ae_draft is not None
    assert res_3.ae_draft.severity == "SEVERE"
    assert res_3.ae_draft.is_serious is True
    assert res_3.ae_draft.meddra_term == "Alanine aminotransferase increased"

    # 5. Grade 4: 800 U/L (22.86x ULN) -> >20.0x ULN (Life-threatening)
    res_4 = engine.evaluate_observation("ALT", 800.0, "U/L", ref_high=alt_uln)
    assert res_4.grade == CTCAEGrade.GRADE_4
    assert res_4.is_critical_ae_trigger is True
    assert res_4.ae_draft is not None
    assert res_4.ae_draft.severity == "LIFE_THREATENING"


def test_standard_ctcae_renal_grading_creatinine():
    """Verify Serum Creatinine toxicity grading.

    @req:PRD-PVG-001
    """
    engine = LabToxicityGradingEngine()
    creat_uln = 1.2  # mg/dL

    # 1.8 mg/dL -> 1.5x ULN -> Grade 1
    res_1 = engine.evaluate_observation("CREATININE", 1.8, "mg/dL", ref_high=creat_uln)
    assert res_1.grade == CTCAEGrade.GRADE_1

    # 2.8 mg/dL -> 2.33x ULN -> Grade 2 (>1.5 - 3.0x ULN)
    res_2 = engine.evaluate_observation("CREATININE", 2.8, "mg/dL", ref_high=creat_uln)
    assert res_2.grade == CTCAEGrade.GRADE_2

    # 5.0 mg/dL -> 4.17x ULN -> Grade 3 (>3.0 - 6.0x ULN)
    res_3 = engine.evaluate_observation("CREATININE", 5.0, "mg/dL", ref_high=creat_uln)
    assert res_3.grade == CTCAEGrade.GRADE_3
    assert res_3.is_critical_ae_trigger is True


def test_hematology_absolute_range_grading_platelets():
    """Verify absolute threshold grading for Platelets (Thrombocytopenia).

    @req:PRD-PVG-001
    """
    engine = LabToxicityGradingEngine()

    # 1. 100,000 /mm3 -> Grade 1 (75,000 - 150,000)
    res_1 = engine.evaluate_observation("PLATELETS", 100000, "cells/uL")
    assert res_1.grade == CTCAEGrade.GRADE_1

    # 2. 60,000 /mm3 -> Grade 2 (50,000 - 75,000)
    res_2 = engine.evaluate_observation("PLATELETS", 60000, "cells/uL")
    assert res_2.grade == CTCAEGrade.GRADE_2

    # 3. 35,000 /mm3 -> Grade 3 (25,000 - 50,000)
    res_3 = engine.evaluate_observation("PLATELETS", 35000, "cells/uL")
    assert res_3.grade == CTCAEGrade.GRADE_3
    assert res_3.is_critical_ae_trigger is True

    # 4. 15,000 /mm3 -> Grade 4 (<25,000 - Life-threatening)
    res_4 = engine.evaluate_observation("PLATELETS", 15000, "cells/uL")
    assert res_4.grade == CTCAEGrade.GRADE_4
    assert res_4.is_critical_ae_trigger is True


def test_hys_law_dili_sentinel_triad():
    """Verify FDA / ICH Hy's Law DILI sentinel evaluation.

    @req:PRD-PVG-001, PRD-SYS-123
    """
    # Reference Upper Limits of Normal (ULN)
    alt_uln = 35.0
    ast_uln = 35.0
    tbil_uln = 1.2
    alkp_uln = 120.0

    # 1. Hy's Law Triad MET: ALT >= 3x ULN, TBIL >= 2x ULN, ALKP < 2x ULN
    hys_positive = LabToxicityGradingEngine.evaluate_hys_law(
        alt=150.0,  # 4.29x ULN (>= 3.0x)
        ast=120.0,  # 3.43x ULN
        tbil=3.0,  # 2.50x ULN (>= 2.0x)
        alkp=150.0,  # 1.25x ULN (< 2.0x)
        alt_uln=alt_uln,
        ast_uln=ast_uln,
        tbil_uln=tbil_uln,
        alkp_uln=alkp_uln,
    )
    assert hys_positive.is_hys_law_met is True
    assert hys_positive.alert_level == "HYS_LAW_CRITICAL"
    assert "Hy's Law DILI Criteria MET" in hys_positive.rationale

    # 2. Cholestatic DILI (ALKP >= 2x ULN -> Potential DILI with Cholestasis)
    hys_cholestatic = LabToxicityGradingEngine.evaluate_hys_law(
        alt=150.0,
        ast=120.0,
        tbil=3.0,
        alkp=300.0,  # 2.50x ULN (>= 2.0x)
        alt_uln=alt_uln,
        ast_uln=ast_uln,
        tbil_uln=tbil_uln,
        alkp_uln=alkp_uln,
    )
    assert hys_cholestatic.is_hys_law_met is False
    assert hys_cholestatic.alert_level == "POTENTIAL_DILI_WARNING"

    # 3. Isolated transaminase elevation without bilirubin (ALT 4x ULN, TBIL normal)
    hys_transaminase = LabToxicityGradingEngine.evaluate_hys_law(
        alt=140.0,
        ast=40.0,
        tbil=1.0,
        alkp=100.0,
        alt_uln=alt_uln,
        ast_uln=ast_uln,
        tbil_uln=tbil_uln,
        alkp_uln=alkp_uln,
    )
    assert hys_transaminase.is_hys_law_met is False
    assert hys_transaminase.alert_level == "BORDERLINE_HEPATIC"


def test_delta_check_alert_and_custom_rule_extension():
    """Verify acute delta check and custom toxicity rule definition overrides.

    @req:PRD-PVG-001
    """
    engine = LabToxicityGradingEngine()

    # Baseline 1.0 -> Current 2.0 (+100% change -> Delta alert)
    res_delta = engine.evaluate_observation(
        "CREATININE", 2.0, "mg/dL", ref_high=1.2, baseline_value=1.0
    )
    assert res_delta.is_delta_check_alert is True
    assert res_delta.delta_percentage == 100.0

    # Custom DAIDS rule extension
    custom_rule = ToxicityRuleDefinition(
        test_code="CUSTOM_BIOMARKER",
        test_name="Custom Biomarker X",
        category=ToxicityCategory.GENERAL,
        scale="CUSTOM",
        grades=[
            GradeCutoff(
                grade=CTCAEGrade.GRADE_1,
                criteria_type="ULN_MULTIPLIER",
                min_multiplier=1.2,
                max_multiplier=2.0,
                description=">1.2 to 2.0x ULN",
            ),
            GradeCutoff(
                grade=CTCAEGrade.GRADE_3,
                criteria_type="ULN_MULTIPLIER",
                min_multiplier=2.0,
                max_multiplier=None,
                description=">2.0x ULN - Critical",
            ),
        ],
    )
    custom_engine = LabToxicityGradingEngine(custom_rules=[custom_rule])
    res_custom = custom_engine.evaluate_observation(
        "CUSTOM_BIOMARKER", 25.0, "ng/mL", ref_high=10.0
    )
    assert res_custom.grade == CTCAEGrade.GRADE_3
    assert res_custom.is_critical_ae_trigger is True
