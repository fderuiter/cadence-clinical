"""Unit tests for UREE static analysis sentinels and cycle detection.

@req:PRD-CRF-005
"""

from packages.engine_core.rules.ast import (
    ExpressionNode,
    FieldReference,
    RuleDefinition,
)
from packages.engine_core.rules.sentinels import (
    calculate_expression_complexity,
    detect_circular_dependencies,
    detect_unresolved_references,
    extract_field_references,
    extract_variable_names,
)


def test_extract_field_references_and_variables():
    """Verify recursive extraction of field and variable symbols.

    @req:PRD-CRF-005
    """
    node = ExpressionNode(
        type="logical",
        operator="and",
        operands=[
            ExpressionNode(
                type="comparison",
                operator=">=",
                operands=[
                    ExpressionNode(
                        type="field_ref", field_ref=FieldReference(field_id="field_a")
                    ),
                    ExpressionNode(type="variable", variable_name="var_x"),
                ],
            ),
            ExpressionNode(
                type="comparison",
                operator="<",
                operands=[
                    ExpressionNode(
                        type="field_ref", field_ref=FieldReference(field_id="field_b")
                    ),
                    ExpressionNode(type="constant", value=100),
                ],
            ),
        ],
    )

    refs = extract_field_references(node)
    assert len(refs) == 2
    assert {r.field_id for r in refs} == {"field_a", "field_b"}

    vars_extracted = extract_variable_names(node)
    assert vars_extracted == ["var_x"]


def test_calculate_expression_complexity():
    """Verify expression depth and node count calculation.

    @req:PRD-CRF-005
    """
    node = ExpressionNode(
        type="logical",
        operator="and",
        operands=[
            ExpressionNode(
                type="comparison",
                operator="==",
                operands=[
                    ExpressionNode(type="variable", variable_name="a"),
                    ExpressionNode(type="constant", value=1),
                ],
            ),
            ExpressionNode(
                type="comparison",
                operator="==",
                operands=[
                    ExpressionNode(type="variable", variable_name="b"),
                    ExpressionNode(type="constant", value=2),
                ],
            ),
        ],
    )

    metrics = calculate_expression_complexity(node)
    assert metrics["node_count"] == 7
    assert metrics["depth"] == 3
    assert metrics["var_count"] == 2


def test_detect_unresolved_references():
    """Verify detection of field and visit references missing in schema.

    @req:PRD-CRF-005
    """
    node = ExpressionNode(
        type="field_ref",
        field_ref=FieldReference(field_id="unknown_field", visit_id="unknown_visit"),
    )

    valid_fields = {"sys_bp", "heart_rate"}
    valid_visits = {"VISIT_01", "VISIT_02"}

    errors = detect_unresolved_references(
        node, valid_fields=valid_fields, valid_visits=valid_visits
    )
    assert len(errors) == 2
    assert "Unresolved field reference: 'unknown_field'" in errors[0]
    assert "Unresolved visit reference: 'unknown_visit'" in errors[1]


def test_detect_circular_dependencies_direct_cycle():
    """Verify detection of mutual circular dependency between two rules.

    @req:PRD-CRF-005
    """
    rule1 = RuleDefinition(
        rule_id="RULE-A",
        study_id="STUDY-1",
        name="Rule A",
        rule_type="skip_logic",
        action="show",
        target_field="field_a",
        condition=ExpressionNode(
            type="field_ref", field_ref=FieldReference(field_id="field_b")
        ),
        reason_for_change="Test circular skip logic setup",
    )

    rule2 = RuleDefinition(
        rule_id="RULE-B",
        study_id="STUDY-1",
        name="Rule B",
        rule_type="skip_logic",
        action="show",
        target_field="field_b",
        condition=ExpressionNode(
            type="field_ref", field_ref=FieldReference(field_id="field_a")
        ),
        reason_for_change="Test circular skip logic setup",
    )

    cycles = detect_circular_dependencies([rule1, rule2])
    assert len(cycles) > 0
    assert any(
        "field_a -> field_b -> field_a" in c or "field_b -> field_a -> field_b" in c
        for c in cycles
    )


def test_detect_circular_dependencies_no_cycle():
    """Verify acyclic rule graph passes with zero cycle errors.

    @req:PRD-CRF-005
    """
    rule1 = RuleDefinition(
        rule_id="RULE-A",
        study_id="STUDY-1",
        name="Rule A",
        rule_type="skip_logic",
        action="show",
        target_field="field_a",
        condition=ExpressionNode(
            type="field_ref", field_ref=FieldReference(field_id="field_b")
        ),
        reason_for_change="Test acyclic skip logic setup",
    )

    rule2 = RuleDefinition(
        rule_id="RULE-C",
        study_id="STUDY-1",
        name="Rule C",
        rule_type="skip_logic",
        action="show",
        target_field="field_c",
        condition=ExpressionNode(
            type="field_ref", field_ref=FieldReference(field_id="field_a")
        ),
        reason_for_change="Test acyclic skip logic setup",
    )

    cycles = detect_circular_dependencies([rule1, rule2])
    assert len(cycles) == 0
