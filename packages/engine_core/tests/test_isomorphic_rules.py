"""Unit tests for Isomorphic AST Rules Evaluation."""

from packages.ddf.tripartite import AstEditCheck
from packages.engine_core.rules.evaluator import RuleEvaluator, parse_json_logic


def test_isomorphic_ast_arithmetic_and_comparisons():
    """Verify isomorphic AST evaluation for cross-field blood pressure rule.

    @req:PRD-CRF-009
    @req:PRD-EDC-003
    """
    evaluator = RuleEvaluator()

    # Rule: Systolic must be greater than Diastolic
    rule = AstEditCheck(
        id="CHK-BP-01",
        name="Systolic > Diastolic",
        ast_expression={">": [{"var": "VS_SYSBP"}, {"var": "VS_DIABP"}]},
        severity="HARD",
        query_message="Systolic blood pressure must exceed diastolic",
        target_variable_ids=["VS_SYSBP", "VS_DIABP"],
    )

    node = parse_json_logic(rule.ast_expression)

    # Test Valid Scenario
    valid_payload = {"VS_SYSBP": 120, "VS_DIABP": 80}
    res_valid = evaluator.evaluate(node, valid_payload)
    is_valid = bool(res_valid.value) if hasattr(res_valid, "value") else bool(res_valid)
    assert is_valid is True

    # Test Invalid Scenario
    invalid_payload = {"VS_SYSBP": 80, "VS_DIABP": 120}
    res_invalid = evaluator.evaluate(node, invalid_payload)
    is_invalid = (
        bool(res_invalid.value) if hasattr(res_invalid, "value") else bool(res_invalid)
    )
    assert is_invalid is False


def test_isomorphic_ast_derived_bmi_calculation():
    """Verify isomorphic calculation for BMI formula.

    @req:PRD-CRF-010
    @req:PRD-SYS-001
    """
    evaluator = RuleEvaluator()

    # Formula: Weight / ((Height / 100) ^ 2)
    ast_bmi = {
        "/": [
            {"var": "VS_WEIGHT"},
            {
                "*": [
                    {"/": [{"var": "VS_HEIGHT"}, 100]},
                    {"/": [{"var": "VS_HEIGHT"}, 100]},
                ]
            },
        ]
    }

    node = parse_json_logic(ast_bmi)
    payload = {"VS_WEIGHT": 70.0, "VS_HEIGHT": 175.0}  # 70 / (1.75^2) = 22.857
    result = evaluator.evaluate(node, payload)
    val = float(result.value) if hasattr(result, "value") else float(result)

    assert round(val, 2) == 22.86
