"""Backward-compatibility re-export shim for Interop Designer client."""

from apps.interop.adapters.designer_client import (
    EligibilityCriterion,
    ExpressionNode,
    InteropDesignerClientError,
    fetch_eligibility_criteria,
    map_db_to_criterion,
    parse_dsl,
    register_eligibility_criteria_resolver,
)

__all__ = [
    "EligibilityCriterion",
    "ExpressionNode",
    "InteropDesignerClientError",
    "fetch_eligibility_criteria",
    "map_db_to_criterion",
    "parse_dsl",
    "register_eligibility_criteria_resolver",
]
