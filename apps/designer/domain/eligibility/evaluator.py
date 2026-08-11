"""
Deterministic AST evaluator and aggregate eligibility calculation engine.

This module provides deterministic AST evaluation using Kleene 3-valued logic,
entirely avoiding eval/exec. It tracks fine-grained node-level traceability
explanations and aggregates individual criteria into a final eligibility report.
"""

from typing import Any

from .models import (
    AggregateEligibilityResult,
    CriterionEvaluation,
    EligibilityCriterion,
    ExpressionNode,
    NodeEvaluation,
)


def evaluate_node(node: ExpressionNode, context: dict[str, Any]) -> NodeEvaluation:
    """
    Deterministically evaluates an ExpressionNode AST against the given data context.
    Propagates missing or null values as indeterminate (Kleene 3-valued logic).
    """
    if node.type == "constant":
        explanation = f"Constant value is {node.value!r}."
        return NodeEvaluation(
            node_type="constant",
            value=node.value,
            is_indeterminate=False,
            explanation=explanation,
        )

    if node.type == "field_ref":
        ref = node.field_ref
        if ref is None:
            return NodeEvaluation(
                node_type="field_ref",
                value=None,
                is_indeterminate=True,
                explanation="Field reference details missing from AST node.",
            )

        raw_ref = ref.raw_reference
        if raw_ref not in context or context[raw_ref] is None:
            explanation = (
                f"Field {raw_ref!r} is missing or null in the current capture context."
            )
            return NodeEvaluation(
                node_type="field_ref",
                value=None,
                is_indeterminate=True,
                explanation=explanation,
            )

        val = context[raw_ref]
        explanation = f"Field {raw_ref!r} value is {val!r}."
        return NodeEvaluation(
            node_type="field_ref",
            value=val,
            is_indeterminate=False,
            explanation=explanation,
        )

    if node.type == "logical":
        if not node.operands:
            return NodeEvaluation(
                node_type="logical",
                operator=node.operator,
                value=None,
                is_indeterminate=True,
                explanation="Logical node has no child operands.",
            )

        op_evals = [evaluate_node(op, context) for op in node.operands]

        if node.operator == "not":
            child = op_evals[0]
            if child.is_indeterminate:
                return NodeEvaluation(
                    node_type="logical",
                    operator="not",
                    value=None,
                    is_indeterminate=True,
                    explanation="not of an indeterminate operand is indeterminate.",
                    children=op_evals,
                )
            negated_val = not child.value
            return NodeEvaluation(
                node_type="logical",
                operator="not",
                value=negated_val,
                is_indeterminate=False,
                explanation=f"not {child.value!r} is {negated_val!r}.",
                children=op_evals,
            )

        if node.operator == "and":
            # Kleene logic: if any operand is False, overall result is False (even if others are indeterminate)
            has_indeterminate = False
            for child in op_evals:
                if not child.is_indeterminate and child.value is False:
                    return NodeEvaluation(
                        node_type="logical",
                        operator="and",
                        value=False,
                        is_indeterminate=False,
                        explanation="At least one operand is False, short-circuiting logical 'and' to False.",
                        children=op_evals,
                    )
                if child.is_indeterminate:
                    has_indeterminate = True

            if has_indeterminate:
                return NodeEvaluation(
                    node_type="logical",
                    operator="and",
                    value=None,
                    is_indeterminate=True,
                    explanation="One or more operands are indeterminate and none are False, so logical 'and' is indeterminate.",
                    children=op_evals,
                )

            # All operands must be True
            return NodeEvaluation(
                node_type="logical",
                operator="and",
                value=True,
                is_indeterminate=False,
                explanation="All operands evaluated to True, so logical 'and' is True.",
                children=op_evals,
            )

        if node.operator == "or":
            # Kleene logic: if any operand is True, overall result is True (even if others are indeterminate)
            has_indeterminate = False
            for child in op_evals:
                if not child.is_indeterminate and child.value is True:
                    return NodeEvaluation(
                        node_type="logical",
                        operator="or",
                        value=True,
                        is_indeterminate=False,
                        explanation="At least one operand is True, short-circuiting logical 'or' to True.",
                        children=op_evals,
                    )
                if child.is_indeterminate:
                    has_indeterminate = True

            if has_indeterminate:
                return NodeEvaluation(
                    node_type="logical",
                    operator="or",
                    value=None,
                    is_indeterminate=True,
                    explanation="One or more operands are indeterminate and none are True, so logical 'or' is indeterminate.",
                    children=op_evals,
                )

            # All operands must be False
            return NodeEvaluation(
                node_type="logical",
                operator="or",
                value=False,
                is_indeterminate=False,
                explanation="All operands evaluated to False, so logical 'or' is False.",
                children=op_evals,
            )

    elif node.type == "comparison":
        if not node.operands or len(node.operands) != 2:
            return NodeEvaluation(
                node_type="comparison",
                operator=node.operator,
                value=None,
                is_indeterminate=True,
                explanation="Comparison node requires exactly 2 operands.",
            )

        left_eval = evaluate_node(node.operands[0], context)
        right_eval = evaluate_node(node.operands[1], context)
        children = [left_eval, right_eval]

        if left_eval.is_indeterminate or right_eval.is_indeterminate:
            return NodeEvaluation(
                node_type="comparison",
                operator=node.operator,
                value=None,
                is_indeterminate=True,
                explanation="One or more comparison operands are missing or null.",
                children=children,
            )

        l_val = left_eval.value
        r_val = right_eval.value

        l_coerced = l_val
        r_coerced = r_val

        if isinstance(l_val, str):
            try:
                l_coerced = float(l_val)
            except (ValueError, TypeError):
                pass
        if isinstance(r_val, str):
            try:
                r_coerced = float(r_val)
            except (ValueError, TypeError):
                pass

        try:
            if node.operator == "==":
                res = l_coerced == r_coerced
            elif node.operator == "!=":
                res = l_coerced != r_coerced
            elif node.operator == "<":
                res = l_coerced < r_coerced
            elif node.operator == "<=":
                res = l_coerced <= r_coerced
            elif node.operator == ">":
                res = l_coerced > r_coerced
            elif node.operator == ">=":
                res = l_coerced >= r_coerced
            else:
                return NodeEvaluation(
                    node_type="comparison",
                    operator=node.operator,
                    value=None,
                    is_indeterminate=True,
                    explanation=f"Unsupported comparison operator: {node.operator!r}.",
                    children=children,
                )
        except TypeError as err:
            explanation = (
                f"Comparison failed due to incompatible operand types: "
                f"{type(l_val).__name__} and {type(r_val).__name__}. Details: {err}"
            )
            return NodeEvaluation(
                node_type="comparison",
                operator=node.operator,
                value=None,
                is_indeterminate=True,
                explanation=explanation,
                children=children,
            )

        explanation = (
            f"Comparison {l_val!r} {node.operator} {r_val!r} evaluated to {res!r}."
        )
        return NodeEvaluation(
            node_type="comparison",
            operator=node.operator,
            value=res,
            is_indeterminate=False,
            explanation=explanation,
            children=children,
        )

    # Fallback/Unknown node type
    return NodeEvaluation(
        node_type="unknown",
        value=None,
        is_indeterminate=True,
        explanation=f"Unknown AST node type: {node.type!r}.",
    )


def evaluate_eligibility(
    criteria: list[EligibilityCriterion], context: dict[str, Any]
) -> AggregateEligibilityResult:
    """
    Aggregates the eligibility assessment over a list of inclusion/exclusion criteria.
    Inclusion criteria must evaluate to True, and exclusion criteria must evaluate to False.
    """
    failed_criteria = []
    indeterminate_criteria = []
    evaluations = {}

    for crit in criteria:
        node_eval = evaluate_node(crit.condition, context)

        if node_eval.is_indeterminate:
            is_met = False
            is_indeterminate = True
            indeterminate_criteria.append(crit.criterion_id)
        else:
            is_indeterminate = False
            # Check if condition outcome matches expected outcome
            # Inclusion expects True, Exclusion expects False
            is_met = node_eval.value == crit.expected_outcome
            if not is_met:
                failed_criteria.append(crit.criterion_id)

        evaluations[crit.criterion_id] = CriterionEvaluation(
            criterion_id=crit.criterion_id,
            criterion_type=crit.criterion_type,
            description=crit.description,
            dsl_source=crit.dsl_source,
            expected_outcome=crit.expected_outcome,
            evaluation_detail=node_eval,
            is_indeterminate=is_indeterminate,
            is_met=is_met,
        )

    # Determine aggregated eligibility
    if len(failed_criteria) > 0:
        eligible = False
    elif len(indeterminate_criteria) > 0:
        eligible = None
    else:
        eligible = True

    return AggregateEligibilityResult(
        eligible=eligible,
        failed_criteria=failed_criteria,
        indeterminate_criteria=indeterminate_criteria,
        criteria_evaluations=evaluations,
    )


def evaluate_structured_expression(
    tree: ExpressionNode, context: dict[str, Any]
) -> tuple[bool | None, str]:
    """
    Deterministically evaluates a structured expression tree against a context dict of eCRF field values.
    Returns:
        (result, explanation) where:
        - result is a boolean value (True/False) or None (for indeterminate).
        - explanation details why the expression evaluated to its current state.
          If any node is missing/None, it explicitly yields "cannot determine / not yet eligible".
    """
    node_eval = evaluate_node(tree, context)
    if node_eval.is_indeterminate:
        return None, f"cannot determine / not yet eligible: {node_eval.explanation}"

    explanation = node_eval.explanation
    if node_eval.value is False:
        # Walk the tree to gather detailed explanations of failing leaf comparison nodes
        failed_parts = []

        def walk(e: NodeEvaluation):
            if e.node_type == "comparison" and e.value is False:
                failed_parts.append(e.explanation)
            for c in e.children:
                walk(c)

        walk(node_eval)
        if failed_parts:
            explanation += f" Failed comparisons: {'; '.join(failed_parts)}"

    return node_eval.value, explanation


def evaluate_criteria_group(
    criteria: list[EligibilityCriterion], context: dict[str, Any]
) -> tuple[bool | None, list[str]]:
    """
    Aggregates evaluations across a list of inclusion/exclusion criteria.
    Derives an overall outcome:
    - Any inclusion criterion that is False, or any exclusion criterion that is True -> fail.
    Returns:
        (overall_eligible, failing_criterion_identifiers)
        where overall_eligible is True, False, or None (if indeterminate and no hard failures exist),
        and failing_criterion_identifiers are the identifier fields of the failing criteria.
    """
    result = evaluate_eligibility(criteria, context)

    failing_identifiers = []
    for crit_id in result.failed_criteria:
        # Find the matching criterion to get its business 'identifier'
        crit_obj = next((c for c in criteria if c.criterion_id == crit_id), None)
        if crit_obj and getattr(crit_obj, "identifier", None):
            failing_identifiers.append(crit_obj.identifier)
        else:
            failing_identifiers.append(crit_id)

    return result.eligible, failing_identifiers
