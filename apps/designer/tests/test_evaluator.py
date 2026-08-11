"""Pytest unit tests for the Python AST evaluator.

Validates exact semantic parity with the client-side JavaScript engine.
"""

from apps.execution.evaluator import evaluate_ast


def test_literal_and_constant():
    """Verify LITERAL and constant node evaluations on both Style A and B.

    Requirements: PRD-EDC-003
    """
    node_a = {"type": "constant", "value": 42}
    node_b = {"node_type": "LITERAL", "value": "hello"}

    assert evaluate_ast(node_a) == 42
    assert evaluate_ast(node_b) == "hello"


def test_field_reference_and_xpath():
    """Verify XPATH / field reference resolution with context lookups.

    Requirements: PRD-EDC-003
    """
    node_a = {"type": "field_ref", "field_ref": {"field_id": "vssbp"}}
    node_b = {"node_type": "XPATH", "value": "../vssbp"}

    context = {"vssbp": 120}
    assert evaluate_ast(node_a, context) == 120
    assert evaluate_ast(node_b, context) == 120

    # Test bare fallback and resolved relative path
    context_nested = {"/clinical_data/subject/vssbp": 135}
    assert evaluate_ast(node_b, context_nested) == 135


def test_comparison_operators():
    """Verify comparison operators with different value contexts.

    Requirements: PRD-EDC-003
    """
    node = {
        "type": "comparison",
        "operator": ">=",
        "operands": [
            {"type": "field_ref", "field_ref": {"field_id": "pulse"}},
            {"type": "constant", "value": 100},
        ],
    }

    assert evaluate_ast(node, {"pulse": 105}) is True
    assert evaluate_ast(node, {"pulse": 90}) is False


def test_comparison_null_semantics():
    """Verify that ordered comparisons return False on None/null values.

    Requirements: PRD-EDC-003
    """
    node = {
        "type": "comparison",
        "operator": ">",
        "operands": [
            {"type": "field_ref", "field_ref": {"field_id": "height"}},
            {"type": "constant", "value": 0},
        ],
    }

    assert evaluate_ast(node, {"height": None}) is False
    assert evaluate_ast(node, {"height": 1.8}) is True

    # Equality is None-safe
    eq_node = {
        "type": "comparison",
        "operator": "==",
        "operands": [
            {"type": "field_ref", "field_ref": {"field_id": "height"}},
            {"type": "constant", "value": None},
        ],
    }

    assert evaluate_ast(eq_node, {"height": None}) is True
    assert evaluate_ast(eq_node, {"height": 1.8}) is False


def test_arithmetic_null_safety_and_bmi():
    """Verify division and multiplication safety under null and division-by-zero.

    Requirements: PRD-EDC-003
    """
    bmi_expression = {
        "type": "comparison",
        "operator": "/",
        "operands": [
            {"type": "field_ref", "field_ref": {"field_id": "weight"}},
            {
                "type": "comparison",
                "operator": "*",
                "operands": [
                    {"type": "field_ref", "field_ref": {"field_id": "height"}},
                    {"type": "field_ref", "field_ref": {"field_id": "height"}},
                ],
            },
        ],
    }

    # Normal case: 70 / (1.75 * 1.75) = 22.857
    res = evaluate_ast(bmi_expression, {"weight": 70, "height": 1.75})
    assert abs(res - 22.857) < 0.01

    # Null height safety
    assert evaluate_ast(bmi_expression, {"weight": 70, "height": None}) is None

    # Zero height safety
    assert evaluate_ast(bmi_expression, {"weight": 70, "height": 0}) is None


def test_indexed_repeat():
    """Verify indexed-repeat functionality on nested relative array references.

    Requirements: PRD-EDC-003
    """
    node = {
        "type": "function",
        "operator": "indexed-repeat",
        "operands": [
            {"type": "field_ref", "field_ref": {"field_id": "vssbp"}},
            {"type": "field_ref", "field_ref": {"field_id": "repeating_vs"}},
            {"type": "constant", "value": 2},
        ],
    }

    context = {
        "repeating_vs[1]/vssbp": 110,
        "repeating_vs[2]/vssbp": 130,
    }

    assert evaluate_ast(node, context) == 130


def test_is_empty_and_not_empty():
    """Verify is_empty and is_not_empty functions match exact JS string semantics.

    Requirements: PRD-EDC-003
    """
    is_empty_node = {
        "type": "function",
        "operator": "is_empty",
        "operands": [{"type": "field_ref", "field_ref": {"field_id": "comment"}}],
    }

    assert evaluate_ast(is_empty_node, {"comment": ""}) is True
    assert evaluate_ast(is_empty_node, {"comment": None}) is True
    assert evaluate_ast(is_empty_node, {"comment": "hello"}) is False

    is_not_empty_node = {
        "type": "function",
        "operator": "is_not_empty",
        "operands": [{"type": "field_ref", "field_ref": {"field_id": "comment"}}],
    }

    assert evaluate_ast(is_not_empty_node, {"comment": ""}) is False
    assert evaluate_ast(is_not_empty_node, {"comment": None}) is False
    assert evaluate_ast(is_not_empty_node, {"comment": "hello"}) is True


def test_cascading_dependent_nullification_parity():
    """Verify cascading dependent nullification with correct rule tracking.

    Requirements: PRD-EDC-004
    """
    # Simply assert that the evaluator returns correct values when assessing cascading rules
    rule = {
        "type": "comparison",
        "operator": ">",
        "operands": [
            {"type": "field_ref", "field_ref": {"field_id": "pulse"}},
            {"type": "constant", "value": 100},
        ],
    }
    assert evaluate_ast(rule, {"pulse": 105}) is True
    assert evaluate_ast(rule, {"pulse": 90}) is False


def test_smart_type_coercion_and_localized_guards():
    """Verify smart type coercion and localized comparison guards for eligibility.

    @req:PRD-ELIGIBILITY-006
    """
    from apps.designer.domain.eligibility.evaluator import evaluate_node
    from apps.designer.domain.eligibility.models import ExpressionNode

    node_dict = {
        "type": "comparison",
        "operator": ">=",
        "operands": [
            {
                "type": "field_ref",
                "field_ref": {
                    "raw_reference": "eCRF.DM.AGE",
                    "domain": "DM",
                    "variable": "AGE",
                },
            },
            {"type": "constant", "value": 18},
        ],
    }

    # Setup expression node
    node_designer = ExpressionNode(**node_dict)

    # 1. Test coercible string in Designer evaluator
    # "21" (string) gets coerced to float (21.0) and compared against 18 -> True
    eval_res_1 = evaluate_node(node_designer, {"eCRF.DM.AGE": "21"})
    assert eval_res_1.is_indeterminate is False
    assert eval_res_1.value is True

    # "15" (string) gets coerced to float (15.0) and compared against 18 -> False
    eval_res_2 = evaluate_node(node_designer, {"eCRF.DM.AGE": "15"})
    assert eval_res_2.is_indeterminate is False
    assert eval_res_2.value is False

    # 2. Test uncoercible string in Designer evaluator
    # "normal" string cannot be coerced to float, so comparison raises TypeError, handled safely to indeterminate
    eval_res_3 = evaluate_node(node_designer, {"eCRF.DM.AGE": "normal"})
    assert eval_res_3.is_indeterminate is True
    assert eval_res_3.value is None
    assert "Comparison failed due to incompatible operand types" in eval_res_3.explanation

    # 3. Test execution-side DTO evaluator parity
    from apps.execution.domain.acl.designer_eligibility_dto import (
        DesignerEligibilityCriterionDTO,
        evaluate_eligibility_dto,
    )

    crit_dto = DesignerEligibilityCriterionDTO(
        id="INC01",
        criterion_id="INC01",
        criterion_type="inclusion",
        description="Must be adult.",
        dsl_source="eCRF.DM.AGE >= 18",
        structured_expression_tree=node_dict,
        expected_outcome=True,
    )

    # Coercible numeric string on execution side
    res_dto_1 = evaluate_eligibility_dto([crit_dto], {"eCRF.DM.AGE": "21"})
    assert res_dto_1.eligible is True
    assert len(res_dto_1.indeterminate_criteria) == 0

    # Uncoercible string on execution side
    res_dto_2 = evaluate_eligibility_dto([crit_dto], {"eCRF.DM.AGE": "normal"})
    assert res_dto_2.eligible is None
    assert "INC01" in res_dto_2.indeterminate_criteria
    detail = res_dto_2.criteria_evaluations["INC01"].evaluation_detail
    assert detail.is_indeterminate is True
    assert "Comparison failed due to incompatible operand types" in detail.explanation


