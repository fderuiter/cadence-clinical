"""
Pytest unit tests for the Python AST evaluator, validating exact semantic parity with the client-side JavaScript engine.
"""

from apps.execution.evaluator import evaluate_ast


def test_literal_and_constant():
    # Style A and Style B
    node_a = {"type": "constant", "value": 42}
    node_b = {"node_type": "LITERAL", "value": "hello"}

    assert evaluate_ast(node_a) == 42
    assert evaluate_ast(node_b) == "hello"


def test_field_reference_and_xpath():
    node_a = {"type": "field_ref", "field_ref": {"field_id": "vssbp"}}
    node_b = {"node_type": "XPATH", "value": "../vssbp"}

    context = {"vssbp": 120}
    assert evaluate_ast(node_a, context) == 120
    assert evaluate_ast(node_b, context) == 120

    # Test bare fallback and resolved relative path
    context_nested = {"/clinical_data/subject/vssbp": 135}
    assert evaluate_ast(node_b, context_nested) == 135


def test_comparison_operators():
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
    # If either operand is None, ordered comparison is False
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
    # weight / (height * height)
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
