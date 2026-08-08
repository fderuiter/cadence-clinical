"""
Focused unit tests for the framework-agnostic Eligibility Criteria Evaluation Engine.

Verifies parser operations, every comparison/logical operator, Kleene 3-valued
logic propagation, invalid syntax rejections, and aggregate eligibility outcomes.
"""

import pytest

from apps.designer.src.domain.eligibility import (
    ComparisonOperator,
    EligibilityCriterion,
    LogicalOperator,
    evaluate_criteria_group,
    evaluate_eligibility,
    evaluate_node,
    evaluate_structured_expression,
    parse_dsl,
)


# @req:PRD-ELIGIBILITY-001
def test_parse_simple_expressions():
    """Verify parser correctly builds AST for simple comparison and literal expressions."""
    # @req:PRD-MDR-007
    node1 = parse_dsl("eCRF.DM.AGE >= 18")
    assert node1.type == "comparison"
    assert node1.operator == ">="
    assert node1.operands[0].type == "field_ref"
    assert node1.operands[0].field_ref.raw_reference == "eCRF.DM.AGE"
    assert node1.operands[0].field_ref.domain == "DM"
    assert node1.operands[0].field_ref.variable == "AGE"
    assert node1.operands[1].type == "constant"
    assert node1.operands[1].value == 18

    node2 = parse_dsl("eCRF.LB.ALT <= 150.5")
    assert node2.type == "comparison"
    assert node2.operator == "<="
    assert node2.operands[1].value == 150.5

    node3 = parse_dsl('eCRF.DM.SEX == "M"')
    assert node3.operands[1].value == "M"

    node4 = parse_dsl("eCRF.MH.DIABETES == True")
    assert node4.operands[1].value is True

    node5 = parse_dsl("eCRF.MH.DIABETES == null")
    assert node5.operands[1].value is None


# @req:PRD-ELIGIBILITY-002
def test_parse_logical_and_nested_expressions():
    """Verify parsing of binary logical operators, not, and parentheses nesting."""
    # @req:PRD-MDR-007
    node = parse_dsl("eCRF.DM.AGE >= 18 and eCRF.LB.ALT < 150")
    assert node.type == "logical"
    assert node.operator == "and"
    assert len(node.operands) == 2

    node_or = parse_dsl("eCRF.DM.SEX == 'F' or eCRF.DM.OVERRIDE == True")
    assert node_or.type == "logical"
    assert node_or.operator == "or"

    node_not = parse_dsl("not eCRF.EX.PREGNANT == 'Y'")
    assert node_not.type == "logical"
    assert node_not.operator == "not"
    assert node_not.operands[0].type == "comparison"

    node_nested = parse_dsl(
        "(eCRF.DM.AGE >= 18 and eCRF.DM.SEX == 'F') or eCRF.DM.OVERRIDE == True"
    )
    assert node_nested.type == "logical"
    assert node_nested.operator == "or"
    assert node_nested.operands[0].type == "logical"
    assert node_nested.operands[0].operator == "and"


# @req:PRD-ELIGIBILITY-003
def test_parse_invalid_syntax():
    """Verify that malformed expressions, syntax errors, and invalid field references are rejected."""
    # Malformed operators
    with pytest.raises(
        ValueError, match="Syntax error|Unexpected leftover|Unexpected token"
    ):
        parse_dsl("eCRF.DM.AGE => 18")

    # Mismatched parenthesis
    with pytest.raises(
        ValueError, match="Expected token type RPAREN|Unexpected end of input"
    ):
        parse_dsl("(eCRF.DM.AGE >= 18")

    # Empty/whitespace inputs
    with pytest.raises(ValueError, match="cannot be empty or whitespace"):
        parse_dsl("   ")

    # Malformed field references
    with pytest.raises(ValueError, match="Syntax error|is malformed|Unexpected token"):
        parse_dsl("eCRF.DM")

    with pytest.raises(ValueError, match="Syntax error|is malformed|Unexpected token"):
        parse_dsl("eCRF..AGE")

    with pytest.raises(
        ValueError, match="Syntax error|Unexpected leftover|Unexpected token"
    ):
        parse_dsl("eCRF.DM.AGE.SUB")


# @req:PRD-ELIGIBILITY-004
def test_evaluation_all_operators():
    """Verify execution of every comparison operator under various input data states."""
    # @req:PRD-MDR-007
    context = {
        "eCRF.DM.AGE": 25,
        "eCRF.LB.ALT": 120.5,
        "eCRF.DM.SEX": "F",
        "eCRF.MH.COND": "Y",
    }

    # ==
    assert evaluate_node(parse_dsl("eCRF.DM.AGE == 25"), context).value is True
    assert evaluate_node(parse_dsl("eCRF.DM.AGE == 30"), context).value is False

    # !=
    assert evaluate_node(parse_dsl("eCRF.DM.AGE != 30"), context).value is True
    assert evaluate_node(parse_dsl("eCRF.DM.AGE != 25"), context).value is False

    # <
    assert evaluate_node(parse_dsl("eCRF.DM.AGE < 30"), context).value is True
    assert evaluate_node(parse_dsl("eCRF.DM.AGE < 20"), context).value is False

    # <=
    assert evaluate_node(parse_dsl("eCRF.DM.AGE <= 25"), context).value is True
    assert evaluate_node(parse_dsl("eCRF.DM.AGE <= 20"), context).value is False

    # >
    assert evaluate_node(parse_dsl("eCRF.DM.AGE > 20"), context).value is True
    assert evaluate_node(parse_dsl("eCRF.DM.AGE > 25"), context).value is False

    # >=
    assert evaluate_node(parse_dsl("eCRF.DM.AGE >= 25"), context).value is True
    assert evaluate_node(parse_dsl("eCRF.DM.AGE >= 30"), context).value is False


# @req:PRD-ELIGIBILITY-005
def test_evaluation_kleene_indeterminate_propagation():
    """Verify Kleene 3-valued logic evaluations and short-circuit outcomes for missing/null values."""
    # @req:PRD-MDR-007
    # Context with some fields missing or null
    context = {
        "eCRF.DM.AGE": 25,
        "eCRF.LB.ALT": None,  # Null
        # "eCRF.DM.SEX" is missing
    }

    # Direct reference to null/missing values
    alt_eval = evaluate_node(parse_dsl("eCRF.LB.ALT < 150"), context)
    assert alt_eval.is_indeterminate is True
    assert alt_eval.value is None

    sex_eval = evaluate_node(parse_dsl("eCRF.DM.SEX == 'F'"), context)
    assert sex_eval.is_indeterminate is True
    assert sex_eval.value is None

    # logical NOT on indeterminate
    not_eval = evaluate_node(parse_dsl("not eCRF.DM.SEX == 'F'"), context)
    assert not_eval.is_indeterminate is True

    # logical AND:
    # 1. True and Indeterminate -> Indeterminate
    and_ind = evaluate_node(
        parse_dsl("eCRF.DM.AGE >= 18 and eCRF.DM.SEX == 'F'"), context
    )
    assert and_ind.is_indeterminate is True
    assert and_ind.value is None

    # 2. False and Indeterminate -> False (short-circuited!)
    and_false = evaluate_node(
        parse_dsl("eCRF.DM.AGE < 18 and eCRF.DM.SEX == 'F'"), context
    )
    assert and_false.is_indeterminate is False
    assert and_false.value is False

    # logical OR:
    # 1. False or Indeterminate -> Indeterminate
    or_ind = evaluate_node(parse_dsl("eCRF.DM.AGE < 18 or eCRF.DM.SEX == 'F'"), context)
    assert or_ind.is_indeterminate is True
    assert or_ind.value is None

    # 2. True or Indeterminate -> True (short-circuited!)
    or_true = evaluate_node(
        parse_dsl("eCRF.DM.AGE >= 18 or eCRF.DM.SEX == 'F'"), context
    )
    assert or_true.is_indeterminate is False
    assert or_true.value is True


# @req:PRD-ELIGIBILITY-006
def test_evaluation_incompatible_types_graceful_handling():
    """Verify that comparing incompatible types (e.g. string and number) results in Indeterminate instead of crashing."""
    context = {"eCRF.DM.SEX": "F"}
    expr = parse_dsl("eCRF.DM.SEX > 18")

    # In Python, "F" > 18 raises TypeError. Our evaluator should catch it and return indeterminate.
    eval_res = evaluate_node(expr, context)
    assert eval_res.is_indeterminate is True
    assert eval_res.value is None
    assert "Comparison failed due to incompatible operand types" in eval_res.explanation


# @req:PRD-ELIGIBILITY-007
def test_aggregate_eligibility_evaluation():
    """Verify total/aggregate eligibility calculation with list of inclusions/exclusions."""
    # @req:PRD-MDR-007
    audit_args = {
        "created_by": "tester",
        "reason_for_change": "Initial eligibility criteria definitions.",
    }

    criteria = [
        # Inclusions (expected outcome is True)
        EligibilityCriterion(
            criterion_id="INC01",
            criterion_type="inclusion",
            description="Subject must be 18 years of age or older.",
            dsl_source="eCRF.DM.AGE >= 18",
            condition=parse_dsl("eCRF.DM.AGE >= 18"),
            expected_outcome=True,
            **audit_args,
        ),
        EligibilityCriterion(
            criterion_id="INC02",
            criterion_type="inclusion",
            description="ALT must be less than or equal to 150.",
            dsl_source="eCRF.LB.ALT <= 150",
            condition=parse_dsl("eCRF.LB.ALT <= 150"),
            expected_outcome=True,
            **audit_args,
        ),
        # Exclusions (expected outcome is False)
        EligibilityCriterion(
            criterion_id="EXC01",
            criterion_type="exclusion",
            description="Subject must not have prior history of cardiovascular issues.",
            dsl_source="eCRF.MH.CARDIAC == True",
            condition=parse_dsl("eCRF.MH.CARDIAC == True"),
            expected_outcome=False,
            **audit_args,
        ),
    ]

    # Scenario A: All criteria perfectly met
    ctx_pass = {
        "eCRF.DM.AGE": 25,
        "eCRF.LB.ALT": 50,
        "eCRF.MH.CARDIAC": False,
    }
    res_pass = evaluate_eligibility(criteria, ctx_pass)
    assert res_pass.eligible is True
    assert len(res_pass.failed_criteria) == 0
    assert len(res_pass.indeterminate_criteria) == 0
    assert res_pass.criteria_evaluations["INC01"].is_met is True
    assert res_pass.criteria_evaluations["INC02"].is_met is True
    assert res_pass.criteria_evaluations["EXC01"].is_met is True

    # Scenario B: One inclusion criterion fails
    ctx_fail_inc = {
        "eCRF.DM.AGE": 16,  # Underage (Fails INC01)
        "eCRF.LB.ALT": 50,
        "eCRF.MH.CARDIAC": False,
    }
    res_fail_inc = evaluate_eligibility(criteria, ctx_fail_inc)
    assert res_fail_inc.eligible is False
    assert "INC01" in res_fail_inc.failed_criteria
    assert len(res_fail_inc.indeterminate_criteria) == 0
    assert res_fail_inc.criteria_evaluations["INC01"].is_met is False

    # Scenario C: One exclusion criterion fails (subject is excluded because condition evaluates to True)
    ctx_fail_exc = {
        "eCRF.DM.AGE": 30,
        "eCRF.LB.ALT": 50,
        "eCRF.MH.CARDIAC": True,  # Has cardiac history (Fails EXC01)
    }
    res_fail_exc = evaluate_eligibility(criteria, ctx_fail_exc)
    assert res_fail_exc.eligible is False
    assert "EXC01" in res_fail_exc.failed_criteria
    assert len(res_fail_exc.indeterminate_criteria) == 0
    assert res_fail_exc.criteria_evaluations["EXC01"].is_met is False

    # Scenario D: Missing values result in Indeterminate (no hard failures)
    ctx_ind = {
        "eCRF.DM.AGE": 25,
        "eCRF.LB.ALT": None,  # Indeterminate INC02
        "eCRF.MH.CARDIAC": False,
    }
    res_ind = evaluate_eligibility(criteria, ctx_ind)
    assert res_ind.eligible is None  # Indeterminate overall
    assert len(res_fail_exc.failed_criteria) == 1
    assert "INC02" in res_ind.indeterminate_criteria
    assert res_ind.criteria_evaluations["INC02"].is_indeterminate is True

    # Scenario E: Mix of hard failure and indeterminate
    ctx_fail_and_ind = {
        "eCRF.DM.AGE": 15,  # Underage (Hard failure INC01)
        "eCRF.LB.ALT": None,  # Indeterminate INC02
        "eCRF.MH.CARDIAC": False,
    }
    res_fail_and_ind = evaluate_eligibility(criteria, ctx_fail_and_ind)
    # Hard failure always takes precedence (we definitely know the subject is ineligible!)
    assert res_fail_and_ind.eligible is False
    assert "INC01" in res_fail_and_ind.failed_criteria
    assert "INC02" in res_fail_and_ind.indeterminate_criteria


# @req:PRD-ELIGIBILITY-008
def test_eligibility_criterion_compatibility_and_enums():
    """Verify that both old and new field sets function correctly and the enums are respected."""
    # Enums verification
    assert ComparisonOperator.EQ == "=="
    assert LogicalOperator.AND == "and"

    # Instantiate with new fields
    crit_new = EligibilityCriterion(
        id="INC-001",
        criterion_type="inclusion",
        identifier="INC-001",
        human_readable_text="Subject must be adult.",
        dsl_expression_string="eCRF.DM.AGE >= 18",
        structured_expression_tree=parse_dsl("eCRF.DM.AGE >= 18"),
        expected_outcome=True,
        created_by="test_user",
        reason_for_change="Authoring test.",
    )
    assert crit_new.id == "INC-001"
    assert crit_new.criterion_id == "INC-001"
    assert crit_new.description == "Subject must be adult."
    assert crit_new.dsl_source == "eCRF.DM.AGE >= 18"
    assert crit_new.condition is not None

    # Instantiate with old fields
    crit_old = EligibilityCriterion(
        criterion_id="INC-002",
        criterion_type="inclusion",
        description="Subject must be female.",
        dsl_source="eCRF.DM.SEX == 'F'",
        condition=parse_dsl("eCRF.DM.SEX == 'F'"),
        expected_outcome=True,
        created_by="test_user",
        reason_for_change="Authoring test old fields.",
    )
    assert crit_old.id == "INC-002"
    assert crit_old.identifier == "INC-002"
    assert crit_old.human_readable_text == "Subject must be female."
    assert crit_old.dsl_expression_string == "eCRF.DM.SEX == 'F'"
    assert crit_old.structured_expression_tree is not None


# @req:PRD-ELIGIBILITY-009
def test_evaluate_structured_expression_helper():
    """Verify that evaluate_structured_expression works with positive, negative, and missing data."""
    tree = parse_dsl("eCRF.DM.AGE >= 18")

    # Positive context
    res, exp = evaluate_structured_expression(tree, {"eCRF.DM.AGE": 21})
    assert res is True
    assert "evaluated to True" in exp

    # Negative context
    res, exp = evaluate_structured_expression(tree, {"eCRF.DM.AGE": 15})
    assert res is False
    assert "evaluated to False" in exp
    assert "Failed comparisons" in exp

    # Indeterminate context
    res, exp = evaluate_structured_expression(tree, {"eCRF.DM.AGE": None})
    assert res is None
    assert "cannot determine" in exp


# @req:PRD-ELIGIBILITY-010
def test_evaluate_criteria_group_helper():
    """Verify that evaluate_criteria_group returns failing business identifiers correctly."""
    criteria = [
        EligibilityCriterion(
            id="INC-001",
            criterion_type="inclusion",
            identifier="INC-001",
            human_readable_text="Subject must be adult.",
            dsl_expression_string="eCRF.DM.AGE >= 18",
            structured_expression_tree=parse_dsl("eCRF.DM.AGE >= 18"),
            expected_outcome=True,
            created_by="test_user",
            reason_for_change="Init",
        ),
        EligibilityCriterion(
            id="EXC-001",
            criterion_type="exclusion",
            identifier="EXC-001",
            human_readable_text="Subject must not have diabetes.",
            dsl_expression_string="eCRF.MH.DIABETES == True",
            structured_expression_tree=parse_dsl("eCRF.MH.DIABETES == True"),
            expected_outcome=False,
            created_by="test_user",
            reason_for_change="Init",
        ),
    ]

    # Overall Pass
    res, fails = evaluate_criteria_group(
        criteria, {"eCRF.DM.AGE": 25, "eCRF.MH.DIABETES": False}
    )
    assert res is True
    assert len(fails) == 0

    # Inclusion Fail
    res, fails = evaluate_criteria_group(
        criteria, {"eCRF.DM.AGE": 16, "eCRF.MH.DIABETES": False}
    )
    assert res is False
    assert fails == ["INC-001"]

    # Exclusion Fail
    res, fails = evaluate_criteria_group(
        criteria, {"eCRF.DM.AGE": 25, "eCRF.MH.DIABETES": True}
    )
    assert res is False
    assert fails == ["EXC-001"]

    # Both Fail
    res, fails = evaluate_criteria_group(
        criteria, {"eCRF.DM.AGE": 16, "eCRF.MH.DIABETES": True}
    )
    assert res is False
    # Both fail, let's verify both are returned
    assert set(fails) == {"INC-001", "EXC-001"}
