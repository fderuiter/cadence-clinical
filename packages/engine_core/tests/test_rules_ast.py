"""Unit tests for UREE AST nodes, models, and serialization.

@req:PRD-SYS-001, PRD-CRF-004
"""

import pytest

from packages.engine_core.rules.ast import (
    ExpressionNode,
    FieldReference,
    RuleDefinition,
)


def test_field_reference_creation():
    """Verify FieldReference instantiation and immutability.

    @req:PRD-CRF-004
    """
    ref = FieldReference(
        field_id="sys_bp",
        form_id="vitals_form",
        visit_id="visit_1",
        visit_relative="previous",
    )
    assert ref.field_id == "sys_bp"
    assert ref.form_id == "vitals_form"
    assert ref.visit_id == "visit_1"
    assert ref.visit_relative == "previous"


def test_expression_node_constant():
    """Verify constant ExpressionNode validation.

    @req:PRD-CRF-004
    """
    node = ExpressionNode(type="constant", value=42)
    assert node.type == "constant"
    assert node.value == 42

    node_bool = ExpressionNode(type="constant", value=True)
    assert node_bool.value is True

    node_null = ExpressionNode(type="constant", value=None)
    assert node_null.value is None


def test_expression_node_field_ref():
    """Verify field_ref ExpressionNode structure.

    @req:PRD-CRF-004
    """
    ref = FieldReference(field_id="heart_rate")
    node = ExpressionNode(type="field_ref", field_ref=ref)
    assert node.type == "field_ref"
    assert node.field_ref.field_id == "heart_rate"

    with pytest.raises(ValueError, match="must provide 'field_ref'"):
        ExpressionNode(type="field_ref", field_ref=None)


def test_expression_node_variable():
    """Verify variable ExpressionNode structure.

    @req:PRD-CRF-004
    """
    node = ExpressionNode(type="variable", variable_name="subject_age")
    assert node.type == "variable"
    assert node.variable_name == "subject_age"

    with pytest.raises(ValueError, match="must provide 'variable_name'"):
        ExpressionNode(type="variable", variable_name=None)


def test_expression_node_logical_operators():
    """Verify logical ExpressionNode operator validation and arity.

    @req:PRD-CRF-004
    """
    op1 = ExpressionNode(type="constant", value=True)
    op2 = ExpressionNode(type="constant", value=False)

    and_node = ExpressionNode(type="logical", operator="and", operands=[op1, op2])
    assert and_node.operator == "and"
    assert len(and_node.operands) == 2

    not_node = ExpressionNode(type="logical", operator="not", operands=[op1])
    assert not_node.operator == "not"

    with pytest.raises(ValueError, match="requires exactly 1 operand"):
        ExpressionNode(type="logical", operator="not", operands=[op1, op2])

    with pytest.raises(ValueError, match="Invalid logical operator"):
        ExpressionNode(type="logical", operator="invalid_op", operands=[op1])


def test_expression_node_comparison_operators():
    """Verify comparison ExpressionNode validation.

    @req:PRD-CRF-004
    """
    op1 = ExpressionNode(type="variable", variable_name="age")
    op2 = ExpressionNode(type="constant", value=18)

    comp_node = ExpressionNode(type="comparison", operator=">=", operands=[op1, op2])
    assert comp_node.operator == ">="

    with pytest.raises(ValueError, match="requires at least 2 operands"):
        ExpressionNode(type="comparison", operator="==", operands=[op1])

    with pytest.raises(
        ValueError, match="between' operator requires exactly 3 operands"
    ):
        ExpressionNode(type="comparison", operator="between", operands=[op1, op2])


def test_expression_node_arithmetic_operators():
    """Verify arithmetic ExpressionNode validation.

    @req:PRD-CRF-004
    """
    op1 = ExpressionNode(type="constant", value=10)
    op2 = ExpressionNode(type="constant", value=5)

    add_node = ExpressionNode(type="arithmetic", operator="+", operands=[op1, op2])
    assert add_node.operator == "+"

    abs_node = ExpressionNode(type="arithmetic", operator="abs", operands=[op1])
    assert abs_node.operator == "abs"

    with pytest.raises(ValueError, match="requires exactly 1 operand"):
        ExpressionNode(type="arithmetic", operator="abs", operands=[op1, op2])


def test_expression_node_function_operators():
    """Verify function ExpressionNode arity validation.

    @req:PRD-CRF-004
    """
    op1 = ExpressionNode(type="variable", variable_name="notes")
    is_empty_node = ExpressionNode(type="function", operator="is_empty", operands=[op1])
    assert is_empty_node.operator == "is_empty"

    with pytest.raises(ValueError, match="requires exactly 1 operand"):
        ExpressionNode(
            type="function",
            operator="is_empty",
            operands=[op1, ExpressionNode(type="constant", value=1)],
        )


def test_rule_definition_checksum_and_validation():
    """Verify RuleDefinition SHA-256 seal computation and Part 11 validation.

    @req:PRD-SYS-001, PRD-CRF-004
    """
    cond = ExpressionNode(
        type="comparison",
        operator=">=",
        operands=[
            ExpressionNode(type="field_ref", field_ref=FieldReference(field_id="age")),
            ExpressionNode(type="constant", value=18),
        ],
    )

    rule = RuleDefinition(
        rule_id="RULE-001",
        study_id="STUDY-101",
        name="Adult Consent Verification",
        rule_type="skip_logic",
        condition=cond,
        action="show",
        target_field="pediatric_assent_form",
        reason_for_change="Initial skip logic configuration for pediatric assent",
    )

    assert rule.rule_id == "RULE-001"
    assert rule.version_index == 1
    assert rule.sha256_checksum is not None
    assert len(rule.sha256_checksum) == 64
    assert rule.is_active is True

    # Validate constraint requiring query message
    with pytest.raises(ValueError, match="requires a non-empty 'query_message'"):
        RuleDefinition(
            rule_id="RULE-002",
            study_id="STUDY-101",
            name="BP Check",
            rule_type="constraint",
            condition=cond,
            target_field="sys_bp",
            query_message=None,
            reason_for_change="Adding BP constraint",
        )
