"""Clinical Study Designer Rules Engine.

Re-exports canonical AST models and rule compilation engines from packages.ddf.rules
while maintaining Designer-specific graph projection validation helpers.
"""

from __future__ import annotations

from typing import Any

from packages.ddf.rules import (
    ConstraintRule,
    CreateRuleRequest,
    CrossFormCheckRule,
    ExpressionNode,
    FieldReference,
    SkipLogicRule,
    compile_to_xpath,
    detect_circular_dependencies,
    extract_field_references,
)

__all__ = [
    "ConstraintRule",
    "CreateRuleRequest",
    "CrossFormCheckRule",
    "ExpressionNode",
    "FieldReference",
    "SkipLogicRule",
    "compile_to_xpath",
    "detect_circular_dependencies",
    "detect_unknown_fields",
    "extract_field_references",
]


def detect_unknown_fields(
    node: ExpressionNode, study_projection: dict[str, Any]
) -> list[str]:
    """Checks if referenced fields, forms, or visits do not exist in the study projection.

    Returns a list of validation failure messages.
    """
    failures: list[str] = []
    refs = extract_field_references(node)

    # Gather all valid visits, forms/activities/items from study projection
    valid_visits: set[str] = set()
    valid_fields: set[str] = set()

    for arm in study_projection.get("arms", []):
        for visit in arm.get("visits", []):
            if "visit_id" in visit:
                valid_visits.add(visit["visit_id"])
            for act in visit.get("activities", []):
                if "activity_id" in act:
                    valid_fields.add(act["activity_id"])
                if "name" in act:
                    valid_fields.add(act["name"])

    for ref in refs:
        if ref.visit_id and ref.visit_id not in valid_visits:
            failures.append(f"Unknown visit reference: '{ref.visit_id}'")
        if ref.field_id not in valid_fields:
            failures.append(f"Unknown field reference: '{ref.field_id}'")

    return failures
