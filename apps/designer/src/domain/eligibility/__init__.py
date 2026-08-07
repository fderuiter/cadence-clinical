"""
Clinical Eligibility Criteria and AST Evaluation package.

Exposes stable Pydantic v2 models, DSL parsing capabilities, and deterministic
Kleene 3-valued logic evaluation functions. Usable across designer, execution,
and interop without external database or framework dependencies.
"""

from eligibility.evaluator import (
    evaluate_criteria_group,
    evaluate_eligibility,
    evaluate_node,
    evaluate_structured_expression,
)
from eligibility.models import (
    AggregateEligibilityResult,
    ComparisonOperator,
    CriterionEvaluation,
    EligibilityCriterion,
    ExpressionNode,
    FieldReference,
    LogicalOperator,
    NodeEvaluation,
)
from eligibility.parser import parse_dsl

__all__ = [
    "FieldReference",
    "ExpressionNode",
    "EligibilityCriterion",
    "NodeEvaluation",
    "CriterionEvaluation",
    "AggregateEligibilityResult",
    "parse_dsl",
    "evaluate_node",
    "evaluate_eligibility",
    "evaluate_structured_expression",
    "evaluate_criteria_group",
    "ComparisonOperator",
    "LogicalOperator",
]
