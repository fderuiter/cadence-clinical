"""Unit tests for Lab CTCAE v5.0 Toxicity Grading and Delta Check Engine.

@req:PRD-LAB-001
@req:PRD-SYS-001
"""

from apps.execution.services.lab_toxicity_engine import (
    CTCAEGrade,
    LabToxicityEngine,
)


def test_hepatic_alt_ctcae_grading():
    """Verify ALT grading relative to ULN."""
    uln = 35.0  # U/L

    # Normal: 30 U/L <= 35.0 -> Grade 0
    res_normal = LabToxicityEngine.evaluate_observation(
        "ALT", "Alanine Aminotransferase", 30.0, "U/L", ref_high=uln
    )
    assert res_normal.ctcae_grade == CTCAEGrade.GRADE_0
    assert res_normal.is_critical_ae_trigger is False

    # Grade 1: 70 U/L (2.0x ULN) -> Grade 1
    res_g1 = LabToxicityEngine.evaluate_observation(
        "ALT", "Alanine Aminotransferase", 70.0, "U/L", ref_high=uln
    )
    assert res_g1.ctcae_grade == CTCAEGrade.GRADE_1
    assert res_g1.is_critical_ae_trigger is False

    # Grade 2: 140 U/L (4.0x ULN) -> Grade 2
    res_g2 = LabToxicityEngine.evaluate_observation(
        "ALT", "Alanine Aminotransferase", 140.0, "U/L", ref_high=uln
    )
    assert res_g2.ctcae_grade == CTCAEGrade.GRADE_2

    # Grade 3: 280 U/L (8.0x ULN) -> Grade 3 (Severe, AE Trigger)
    res_g3 = LabToxicityEngine.evaluate_observation(
        "ALT", "Alanine Aminotransferase", 280.0, "U/L", ref_high=uln
    )
    assert res_g3.ctcae_grade == CTCAEGrade.GRADE_3
    assert res_g3.is_critical_ae_trigger is True

    # Grade 4: 800 U/L (22.8x ULN) -> Grade 4 (Life-threatening)
    res_g4 = LabToxicityEngine.evaluate_observation(
        "ALT", "Alanine Aminotransferase", 800.0, "U/L", ref_high=uln
    )
    assert res_g4.ctcae_grade == CTCAEGrade.GRADE_4
    assert res_g4.is_critical_ae_trigger is True


def test_renal_creatinine_grading_and_delta_check():
    """Verify serum creatinine toxicity grading and KDIGO acute delta check."""
    uln = 1.2  # mg/dL
    baseline = 1.0  # mg/dL

    # Acute spike to 2.5 mg/dL (+150% increase, >2.0x baseline -> Grade 3)
    res = LabToxicityEngine.evaluate_observation(
        test_code="CREAT",
        test_name="Serum Creatinine",
        value=2.5,
        unit="mg/dL",
        ref_high=uln,
        baseline_value=baseline,
    )

    assert res.ctcae_grade == CTCAEGrade.GRADE_3
    assert res.is_critical_ae_trigger is True
    assert res.is_delta_check_alert is True
    assert res.delta_percentage == 150.0


def test_neutrophil_anc_grading():
    """Verify Absolute Neutrophil Count (ANC) toxicity grading."""
    # ANC = 0.8 x 10^9/L (<1.0 - 0.5 -> Grade 3)
    res_g3 = LabToxicityEngine.evaluate_observation(
        test_code="ANC",
        test_name="Absolute Neutrophils",
        value=0.8,
        unit="10^9/L",
        ref_low=1.5,
    )
    assert res_g3.ctcae_grade == CTCAEGrade.GRADE_3
    assert res_g3.is_critical_ae_trigger is True

    # ANC = 0.3 x 10^9/L (<0.5 -> Grade 4 Life Threatening)
    res_g4 = LabToxicityEngine.evaluate_observation(
        test_code="ANC",
        test_name="Absolute Neutrophils",
        value=0.3,
        unit="10^9/L",
        ref_low=1.5,
    )
    assert res_g4.ctcae_grade == CTCAEGrade.GRADE_4
    assert res_g4.is_critical_ae_trigger is True
