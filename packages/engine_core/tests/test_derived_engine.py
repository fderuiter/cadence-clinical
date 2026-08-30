"""Unit tests for the Derived Variable Math Engine.

@req:PRD-CRF-004
"""

from packages.engine_core.derived.engine import (
    DerivedVariableEngine,
    normalize_input_units,
)
from packages.engine_core.derived.models import (
    DerivedFormulaDefinition,
    FormulaInputRequirement,
    PlausibilityRange,
)
from packages.engine_core.rules.ast import (
    ExpressionNode,
    FieldReference,
)


def test_unit_normalization_helper():
    """Verify standard clinical unit conversion (lbs->kg, in->cm, umol/L->mg/dL).

    @req:PRD-CRF-004
    """
    raw_ctx = {
        "weight_lbs": 154.3236,
        "height_in": 70.0,
        "serum_creatinine_umol": 88.42,
    }
    normalized = normalize_input_units(raw_ctx)
    assert round(normalized["weight_kg"], 1) == 70.0
    assert round(normalized["height_cm"], 1) == 177.8
    assert round(normalized["serum_creatinine"], 2) == 1.0


def test_calculate_bmi():
    """Verify BMI calculation and unit resolution.

    @req:PRD-CRF-004
    """
    formula = DerivedFormulaDefinition(
        formula_id="F-BMI",
        name="Body Mass Index",
        formula_type="BMI",
        output_variable="bmi",
        output_unit="kg/m^2",
        inputs=[
            FormulaInputRequirement(name="weight", unit="kg"),
            FormulaInputRequirement(name="height", unit="cm"),
        ],
    )
    # 70 kg, 175 cm -> 70 / (1.75 ^ 2) = 22.8571
    ctx = {"weight": 70, "height": 175}
    res = DerivedVariableEngine.calculate(formula, ctx)
    assert res.is_valid is True
    assert res.calculated_value == 22.8571


def test_calculate_bsa_mosteller_and_dubois():
    """Verify Mosteller and DuBois Body Surface Area equations.

    @req:PRD-CRF-004
    """
    ctx = {"weight": 80, "height": 180}

    # Mosteller: sqrt(180 * 80 / 3600) = sqrt(4) = 2.0
    form_mosteller = DerivedFormulaDefinition(
        formula_id="F-BSA-M",
        name="BSA Mosteller",
        formula_type="BSA_MOSTELLER",
        output_variable="bsa_m",
        output_unit="m^2",
    )
    res_m = DerivedVariableEngine.calculate(form_mosteller, ctx)
    assert res_m.is_valid is True
    assert res_m.calculated_value == 2.0

    # DuBois
    form_dubois = DerivedFormulaDefinition(
        formula_id="F-BSA-D",
        name="BSA DuBois",
        formula_type="BSA_DUBOIS",
        output_variable="bsa_d",
        output_unit="m^2",
    )
    res_d = DerivedVariableEngine.calculate(form_dubois, ctx)
    assert res_d.is_valid is True
    assert round(res_d.calculated_value, 2) == 2.00


def test_calculate_egfr_ckd_epi_2021():
    """Verify eGFR CKD-EPI 2021 race-free equation for males and females.

    @req:PRD-CRF-004
    """
    formula = DerivedFormulaDefinition(
        formula_id="F-EGFR-2021",
        name="eGFR CKD-EPI 2021",
        formula_type="EGFR_CKD_EPI_2021",
        output_variable="egfr",
        output_unit="mL/min/1.73m2",
    )

    # 1. 50-year-old female with Scr = 0.8 mg/dL
    ctx_female = {"scr": 0.8, "age": 50, "sex": "F"}
    res_f = DerivedVariableEngine.calculate(formula, ctx_female)
    assert res_f.is_valid is True
    assert 80.0 <= res_f.calculated_value <= 95.0

    # 2. 60-year-old male with Scr = 1.2 mg/dL
    ctx_male = {"scr": 1.2, "age": 60, "sex": "M"}
    res_m = DerivedVariableEngine.calculate(formula, ctx_male)
    assert res_m.is_valid is True
    assert 60.0 <= res_m.calculated_value <= 75.0


def test_calculate_qtc_fridericia_and_bazett():
    """Verify QTcF (Fridericia) and QTcB (Bazett) calculation from HR and QT interval.

    @req:PRD-CRF-004
    """
    ctx = {"qt_ms": 400.0, "heart_rate": 60.0}  # 60 bpm -> RR = 1.0s

    form_fridericia = DerivedFormulaDefinition(
        formula_id="F-QTCF",
        name="QTcF Fridericia",
        formula_type="QTC_FRIDERICIA",
        output_variable="qtcf",
        output_unit="ms",
    )
    res_f = DerivedVariableEngine.calculate(form_fridericia, ctx)
    assert res_f.is_valid is True
    assert res_f.calculated_value == 400.0  # 400 / 1^(1/3) = 400

    form_bazett = DerivedFormulaDefinition(
        formula_id="F-QTCB",
        name="QTcB Bazett",
        formula_type="QTC_BAZETT",
        output_variable="qtcb",
        output_unit="ms",
    )
    res_b = DerivedVariableEngine.calculate(form_bazett, ctx)
    assert res_b.is_valid is True
    assert res_b.calculated_value == 400.0  # 400 / sqrt(1) = 400


def test_calculate_map_and_cockcroft_gault():
    """Verify Mean Arterial Pressure (MAP) and Cockcroft-Gault CrCl calculations.

    @req:PRD-CRF-004
    """
    # MAP: SBP 120, DBP 80 -> 80 + (1/3)*(40) = 93.3333
    form_map = DerivedFormulaDefinition(
        formula_id="F-MAP",
        name="Mean Arterial Pressure",
        formula_type="MAP",
        output_variable="map",
        output_unit="mmHg",
    )
    res_map = DerivedVariableEngine.calculate(
        form_map, {"systolic_bp": 120, "diastolic_bp": 80}
    )
    assert res_map.is_valid is True
    assert res_map.calculated_value == 93.3333

    # Cockcroft-Gault: 50yo male, 70kg, Scr 1.0 -> (90 * 70) / (72 * 1.0) = 87.5
    form_cg = DerivedFormulaDefinition(
        formula_id="F-CG",
        name="Cockcroft-Gault CrCl",
        formula_type="COCKCROFT_GAULT",
        output_variable="crcl",
        output_unit="mL/min",
    )
    res_cg = DerivedVariableEngine.calculate(
        form_cg, {"age": 50, "weight": 70, "scr": 1.0, "sex": "M"}
    )
    assert res_cg.is_valid is True
    assert res_cg.calculated_value == 87.5


def test_plausibility_bounds_gating():
    """Verify physiological plausibility checks block impossible clinical values.

    @req:PRD-CRF-004
    """
    formula = DerivedFormulaDefinition(
        formula_id="F-BMI-BOUNDS",
        name="BMI with Bounds",
        formula_type="BMI",
        output_variable="bmi",
        inputs=[
            FormulaInputRequirement(
                name="weight",
                unit="kg",
                plausibility=PlausibilityRange(
                    min_value=20.0, max_value=300.0, unit="kg"
                ),
            ),
            FormulaInputRequirement(
                name="height",
                unit="cm",
                plausibility=PlausibilityRange(
                    min_value=50.0, max_value=250.0, unit="cm"
                ),
            ),
        ],
    )

    # Negative or impossible weight (5.0 kg for adult trial)
    ctx_invalid = {"weight": 5.0, "height": 175.0}
    res_invalid = DerivedVariableEngine.calculate(formula, ctx_invalid)
    assert res_invalid.is_valid is False
    assert "below plausible minimum 20.0 kg" in (res_invalid.error_message or "")


def test_custom_ast_expression_formula():
    """Verify custom UREE AST formula evaluation in DerivedVariableEngine.

    @req:PRD-CRF-004
    """
    # Custom formula: (sys_bp - dia_bp) [Pulse Pressure]
    ast = ExpressionNode(
        type="arithmetic",
        operator="-",
        operands=[
            ExpressionNode(
                type="field_ref", field_ref=FieldReference(field_id="sys_bp")
            ),
            ExpressionNode(
                type="field_ref", field_ref=FieldReference(field_id="dia_bp")
            ),
        ],
    )
    formula = DerivedFormulaDefinition(
        formula_id="F-PULSE-PRESSURE",
        name="Pulse Pressure",
        formula_type="CUSTOM_AST",
        expression=ast,
        output_variable="pulse_pressure",
        output_unit="mmHg",
    )
    res = DerivedVariableEngine.calculate(formula, {"sys_bp": 130, "dia_bp": 85})
    assert res.is_valid is True
    assert res.calculated_value == 45.0
