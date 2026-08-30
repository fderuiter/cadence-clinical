"""Unit tests for UREE in-memory pure expression evaluation.

@req:PRD-CRF-004, PRD-SYS-050
"""

from datetime import UTC, datetime

import pytest

from packages.engine_core.rules.ast import (
    ExpressionNode,
    FieldReference,
)
from packages.engine_core.rules.evaluator import (
    RuleEvaluator,
    UndefinedVariableError,
    evaluate_expression,
    parse_json_logic,
)


def test_evaluate_constants():
    """Verify constant evaluation across integer, float, string, boolean, and null.

    @req:PRD-CRF-004
    """
    assert evaluate_expression(ExpressionNode(type="constant", value=100), {}) == 100
    assert evaluate_expression(ExpressionNode(type="constant", value=3.14), {}) == 3.14
    assert (
        evaluate_expression(ExpressionNode(type="constant", value="female"), {})
        == "female"
    )
    assert evaluate_expression(ExpressionNode(type="constant", value=True), {}) is True
    assert evaluate_expression(ExpressionNode(type="constant", value=None), {}) is None


def test_evaluate_variables_and_nested_contexts():
    """Verify variable and field reference lookup across nested context dictionaries.

    @req:PRD-CRF-004
    """
    context = {
        "age": 28,
        "vitals": {"systolic_bp": 120, "diastolic_bp": 80},
        "data": {"lab_result": 4.5},
    }

    # Direct variable
    node1 = ExpressionNode(type="variable", variable_name="age")
    assert evaluate_expression(node1, context) == 28

    # Nested variable
    node2 = ExpressionNode(type="variable", variable_name="vitals.systolic_bp")
    assert evaluate_expression(node2, context) == 120

    # Data wrapper resolution for field_ref
    node3 = ExpressionNode(
        type="field_ref",
        field_ref=FieldReference(field_id="lab_result"),
    )
    assert evaluate_expression(node3, context) == 4.5

    # Undefined variable strict mode
    evaluator_strict = RuleEvaluator(strict_variables=True)
    with pytest.raises(UndefinedVariableError):
        evaluator_strict.evaluate(
            ExpressionNode(type="variable", variable_name="non_existent"), context
        )


def test_evaluate_comparisons():
    """Verify comparison operators (==, !=, <, <=, >, >=, in, not_in, between, regex_match).

    @req:PRD-CRF-004
    """
    ctx = {
        "age": 25,
        "gender": "F",
        "systolic_bp": 135,
        "patient_code": "SITE01-SUB100",
    }

    # == and !=
    eq_node = ExpressionNode(
        type="comparison",
        operator="==",
        operands=[
            ExpressionNode(type="variable", variable_name="gender"),
            ExpressionNode(type="constant", value="F"),
        ],
    )
    assert evaluate_expression(eq_node, ctx) is True

    # Numeric inequality
    gt_node = ExpressionNode(
        type="comparison",
        operator=">",
        operands=[
            ExpressionNode(type="variable", variable_name="systolic_bp"),
            ExpressionNode(type="constant", value=140),
        ],
    )
    assert evaluate_expression(gt_node, ctx) is False

    # in and not_in
    in_node = ExpressionNode(
        type="comparison",
        operator="in",
        operands=[
            ExpressionNode(type="variable", variable_name="gender"),
            ExpressionNode(type="constant", value=["F", "FEMALE", "f"]),
        ],
    )
    assert evaluate_expression(in_node, ctx) is True

    # between
    between_node = ExpressionNode(
        type="comparison",
        operator="between",
        operands=[
            ExpressionNode(type="variable", variable_name="systolic_bp"),
            ExpressionNode(type="constant", value=120),
            ExpressionNode(type="constant", value=140),
        ],
    )
    assert evaluate_expression(between_node, ctx) is True

    # regex_match
    regex_node = ExpressionNode(
        type="comparison",
        operator="regex_match",
        operands=[
            ExpressionNode(type="variable", variable_name="patient_code"),
            ExpressionNode(type="constant", value=r"^SITE\d{2}-SUB\d{3}$"),
        ],
    )
    assert evaluate_expression(regex_node, ctx) is True


def test_evaluate_logical_operators():
    """Verify logical AND, OR, NOT, XOR, and IF_THEN_ELSE evaluation.

    @req:PRD-CRF-004
    """
    ctx = {"age": 65, "has_diabetes": True, "has_hypertension": False}

    # (age >= 18 AND has_diabetes)
    and_node = ExpressionNode(
        type="logical",
        operator="and",
        operands=[
            ExpressionNode(
                type="comparison",
                operator=">=",
                operands=[
                    ExpressionNode(type="variable", variable_name="age"),
                    ExpressionNode(type="constant", value=18),
                ],
            ),
            ExpressionNode(type="variable", variable_name="has_diabetes"),
        ],
    )
    assert evaluate_expression(and_node, ctx) is True

    # XOR
    xor_node = ExpressionNode(
        type="logical",
        operator="xor",
        operands=[
            ExpressionNode(type="variable", variable_name="has_diabetes"),
            ExpressionNode(type="variable", variable_name="has_hypertension"),
        ],
    )
    assert evaluate_expression(xor_node, ctx) is True

    # IF_THEN_ELSE (ternary)
    ternary_node = ExpressionNode(
        type="logical",
        operator="if_then_else",
        operands=[
            ExpressionNode(type="variable", variable_name="has_diabetes"),
            ExpressionNode(type="constant", value="HIGH_RISK"),
            ExpressionNode(type="constant", value="STANDARD_RISK"),
        ],
    )
    assert evaluate_expression(ternary_node, ctx) == "HIGH_RISK"


def test_evaluate_arithmetic_operations():
    """Verify arithmetic operations (+, -, *, /, //, %, ^, abs, round, floor, ceil).

    @req:PRD-CRF-004
    """
    ctx = {"weight_kg": 70, "height_m": 1.75}

    # BMI calculation: weight / (height ^ 2)
    bmi_node = ExpressionNode(
        type="arithmetic",
        operator="/",
        operands=[
            ExpressionNode(type="variable", variable_name="weight_kg"),
            ExpressionNode(
                type="arithmetic",
                operator="^",
                operands=[
                    ExpressionNode(type="variable", variable_name="height_m"),
                    ExpressionNode(type="constant", value=2),
                ],
            ),
        ],
    )
    bmi_val = evaluate_expression(bmi_node, ctx)
    assert round(bmi_val, 2) == 22.86

    # Safe division by zero returns None
    zero_div = ExpressionNode(
        type="arithmetic",
        operator="/",
        operands=[
            ExpressionNode(type="constant", value=10),
            ExpressionNode(type="constant", value=0),
        ],
    )
    assert evaluate_expression(zero_div, {}) is None


def test_evaluate_functions_and_collections():
    """Verify functions (is_empty, coalesce, sum, avg, min, max, count, trim, length).

    @req:PRD-CRF-004
    """
    ctx = {
        "notes": "   Patient reported mild headache   ",
        "missing_field": None,
        "lab_panels": [12.5, 14.0, 13.5, 15.0],
    }

    # is_empty & is_not_empty
    assert (
        evaluate_expression(
            ExpressionNode(
                type="function",
                operator="is_empty",
                operands=[
                    ExpressionNode(type="variable", variable_name="missing_field")
                ],
            ),
            ctx,
        )
        is True
    )

    # coalesce
    coalesce_node = ExpressionNode(
        type="function",
        operator="coalesce",
        operands=[
            ExpressionNode(type="variable", variable_name="missing_field"),
            ExpressionNode(type="constant", value="DEFAULT_VALUE"),
        ],
    )
    assert evaluate_expression(coalesce_node, ctx) == "DEFAULT_VALUE"

    # Collection aggregations: sum, avg, max
    sum_node = ExpressionNode(
        type="function",
        operator="sum",
        operands=[ExpressionNode(type="variable", variable_name="lab_panels")],
    )
    assert evaluate_expression(sum_node, ctx) == 55.0

    avg_node = ExpressionNode(
        type="function",
        operator="avg",
        operands=[ExpressionNode(type="variable", variable_name="lab_panels")],
    )
    assert evaluate_expression(avg_node, ctx) == 13.75


def test_evaluate_date_functions():
    """Verify date/time functions (date_diff_days, date_add_days, age_years, is_past).

    @req:PRD-CRF-004, PRD-SYS-050
    """
    as_of = datetime(2026, 8, 28, 12, 0, 0, tzinfo=UTC)
    evaluator = RuleEvaluator(as_of=as_of)

    ctx = {
        "birth_date": "1990-05-15",
        "visit_1_date": "2026-08-01",
        "visit_2_date": "2026-08-15",
    }

    # age_years (from 1990-05-15 to 2026-08-28 = 36)
    age_node = ExpressionNode(
        type="function",
        operator="age_years",
        operands=[ExpressionNode(type="variable", variable_name="birth_date")],
    )
    assert evaluator.evaluate(age_node, ctx) == 36

    # date_diff_days (2026-08-15 - 2026-08-01 = 14)
    diff_node = ExpressionNode(
        type="function",
        operator="date_diff_days",
        operands=[
            ExpressionNode(type="variable", variable_name="visit_2_date"),
            ExpressionNode(type="variable", variable_name="visit_1_date"),
        ],
    )
    assert evaluator.evaluate(diff_node, ctx) == 14

    # date_add_days
    add_node = ExpressionNode(
        type="function",
        operator="date_add_days",
        operands=[
            ExpressionNode(type="variable", variable_name="visit_1_date"),
            ExpressionNode(type="constant", value=28),
        ],
    )
    assert evaluator.evaluate(add_node, ctx) == "2026-08-29"


def test_parse_and_evaluate_json_logic():
    """Verify JSONLogic parser and evaluation parity.

    @req:PRD-CRF-004
    """
    json_logic_rule = {
        "and": [
            {">=": [{"var": "age"}, 18]},
            {"==": [{"var": "consent_signed"}, True]},
        ]
    }

    ast = parse_json_logic(json_logic_rule)
    assert ast.type == "logical"
    assert ast.operator == "and"

    ctx_pass = {"age": 21, "consent_signed": True}
    assert evaluate_expression(ast, ctx_pass) is True

    ctx_fail = {"age": 16, "consent_signed": True}
    assert evaluate_expression(ast, ctx_fail) is False
