"""Unit tests for Canonical Clinical Rule AST Engine."""

from packages.ddf.rules import (
    ExpressionNode,
    FieldReference,
    compile_condition_to_xpath,
    detect_circular_dependencies,
    extract_appearance,
    resolve_concept_id,
    sanitize_identifier,
)


def test_sanitize_identifier():
    assert sanitize_identifier("sys_bp") == "sys_bp"
    assert sanitize_identifier("123_invalid") == "item_123_invalid"
    assert sanitize_identifier("Systolic Blood Pressure") == "Systolic_Blood_Pressure"
    assert sanitize_identifier("item-with-hyphen") == "item_2dwith_2dhyphen"
    assert sanitize_identifier("") != ""


def test_extract_appearance():
    assert extract_appearance({"cols": 2}) == "w2"
    assert extract_appearance({"layout": {"column_span": 3}}) == "w3"
    assert extract_appearance({"grid": {"span": 4}}) == "w4"
    assert extract_appearance({"type": "string"}) is None


def test_compile_condition_to_xpath_constants():
    node = ExpressionNode(type="constant", value=True)
    assert compile_condition_to_xpath(node) == "true()"

    node = ExpressionNode(type="constant", value="female")
    assert compile_condition_to_xpath(node) == "'female'"

    node = ExpressionNode(type="constant", value=120)
    assert compile_condition_to_xpath(node) == "120"


def test_compile_condition_to_xpath_field_ref():
    node = ExpressionNode(
        type="field_ref",
        field_ref=FieldReference(field_id="systolic_bp"),
    )
    assert compile_condition_to_xpath(node) == "/systolic_bp"


def test_compile_condition_to_xpath_logical_and_comparison():
    node = ExpressionNode(
        type="logical",
        operator="and",
        operands=[
            ExpressionNode(
                type="comparison",
                operator=">=",
                operands=[
                    ExpressionNode(
                        type="field_ref",
                        field_ref=FieldReference(field_id="age"),
                    ),
                    ExpressionNode(type="constant", value=18),
                ],
            ),
            ExpressionNode(
                type="comparison",
                operator="==",
                operands=[
                    ExpressionNode(
                        type="field_ref",
                        field_ref=FieldReference(field_id="gender"),
                    ),
                    ExpressionNode(type="constant", value="female"),
                ],
            ),
        ],
    )
    assert compile_condition_to_xpath(node) == "((/age >= 18) and (/gender = 'female'))"


def test_compile_condition_to_xpath_functions():
    node = ExpressionNode(
        type="function",
        operator="is_empty",
        operands=[
            ExpressionNode(
                type="field_ref",
                field_ref=FieldReference(field_id="notes"),
            ),
        ],
    )
    assert compile_condition_to_xpath(node) == "empty(/notes)"

    not_empty_node = ExpressionNode(
        type="function",
        operator="is_not_empty",
        operands=[
            ExpressionNode(
                type="field_ref",
                field_ref=FieldReference(field_id="notes"),
            ),
        ],
    )
    assert compile_condition_to_xpath(not_empty_node) == "not(empty(/notes))"


def test_detect_circular_dependencies():
    rules = [
        {
            "type": "skip_logic",
            "target_field": "field_b",
            "condition": {
                "type": "field_ref",
                "field_ref": {"field_id": "field_a"},
            },
        },
        {
            "type": "skip_logic",
            "target_field": "field_a",
            "condition": {
                "type": "field_ref",
                "field_ref": {"field_id": "field_b"},
            },
        },
    ]
    cycles = detect_circular_dependencies(rules)
    assert len(cycles) > 0


def test_resolve_concept_id():
    assert resolve_concept_id({"code": "SYSBP"}) == "sys_bp"
    assert resolve_concept_id({"code": "CUSTOM_CODE"}) == "CUSTOM_CODE"
    assert resolve_concept_id(None) is None
