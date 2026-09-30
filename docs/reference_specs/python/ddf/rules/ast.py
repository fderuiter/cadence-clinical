"""Canonical Clinical Rule AST Engine.

Defines structured condition expressions, field references, and circular dependency
detection for clinical skip-logic and validation constraints.
"""

from __future__ import annotations

from typing import Any, Literal

from pydantic import BaseModel, model_validator


class FieldReference(BaseModel):
    """Represents a structured field reference within an expression tree."""

    field_id: str
    form_id: str | None = None
    visit_id: str | None = None
    visit_relative: str | None = None  # e.g., "previous", "next"


class ExpressionNode(BaseModel):
    """A recursive node in a structured clinical expression tree."""

    type: Literal["logical", "comparison", "function", "field_ref", "constant"]
    operator: str | None = None
    operands: list[ExpressionNode] | None = None
    value: Any | None = None
    field_ref: FieldReference | None = None

    @model_validator(mode="after")
    def validate_node(self) -> ExpressionNode:
        """Validates node structure based on node type."""
        if self.type == "constant":
            if self.value is None:
                raise ValueError("Constant node must provide a 'value'")
        elif self.type == "field_ref":
            if self.field_ref is None:
                raise ValueError("Field reference node must provide 'field_ref'")
        elif self.type == "logical":
            if self.operator not in ("and", "or", "not"):
                raise ValueError(f"Invalid logical operator: '{self.operator}'")
            if not self.operands:
                raise ValueError(f"Logical node '{self.operator}' requires operands")
            if self.operator == "not" and len(self.operands) != 1:
                raise ValueError("Logical 'not' operator requires exactly 1 operand")
        elif self.type == "comparison":
            if self.operator not in ("==", "!=", "<", "<=", ">", ">="):
                raise ValueError(f"Invalid comparison operator: '{self.operator}'")
            if not self.operands or len(self.operands) != 2:
                raise ValueError(
                    f"Comparison operator '{self.operator}' requires exactly 2 operands"
                )
        elif self.type == "function":
            valid_funcs = {
                "is_empty": 1,
                "empty": 1,
                "is_not_empty": 1,
                "indexed-repeat": 3,
                "sum": -1,
                "avg": -1,
                "min": -1,
                "max": -1,
                "count": -1,
            }
            if self.operator not in valid_funcs:
                raise ValueError(f"Invalid function operator: '{self.operator}'")
            if not self.operands:
                raise ValueError(f"Function node '{self.operator}' requires operands")
            expected_arity = valid_funcs[self.operator]
            if expected_arity != -1 and len(self.operands) != expected_arity:
                suffix = "operand" if expected_arity == 1 else "operands"
                raise ValueError(
                    f"Function '{self.operator}' requires exactly {expected_arity} {suffix}"
                )
        return self


# Rebuild recursive models in Pydantic v2
ExpressionNode.model_rebuild()


class SkipLogicRule(BaseModel):
    """A rule that defines visibility skip logic for fields, groups, or forms."""

    id: str
    study_id: str
    type: Literal["skip_logic"] = "skip_logic"
    condition: ExpressionNode
    action: Literal["show", "hide"]
    target_field: str
    target_form: str | None = None
    target_group: str | None = None
    version_index: int = 1
    is_deleted: bool = False


class ConstraintRule(BaseModel):
    """A rule that defines constraints and validations on a single field."""

    id: str
    study_id: str
    type: Literal["constraint"] = "constraint"
    condition: ExpressionNode
    target_field: str
    target_form: str | None = None
    query_message: str
    version_index: int = 1
    is_deleted: bool = False

    @model_validator(mode="after")
    def validate_message(self) -> ConstraintRule:
        """Ensure constraint rule specifies a query message."""
        if not self.query_message:
            raise ValueError("Constraint rule requires a non-empty query_message")
        return self


class CrossFormCheckRule(BaseModel):
    """A rule that defines edit checks spanning multiple forms or visits."""

    id: str
    study_id: str
    type: Literal["cross_form_check"] = "cross_form_check"
    condition: ExpressionNode
    query_message: str
    version_index: int = 1
    is_deleted: bool = False

    @model_validator(mode="after")
    def validate_message(self) -> CrossFormCheckRule:
        """Ensure cross form check rule specifies a query message."""
        if not self.query_message:
            raise ValueError("Cross-form check rule requires a non-empty query_message")
        return self


class CreateRuleRequest(BaseModel):
    """Request schema to create a rule."""

    type: Literal["skip_logic", "constraint", "cross_form_check"]
    condition: ExpressionNode
    action: Literal["show", "hide"] | None = None
    target_field: str | None = None
    target_form: str | None = None
    target_group: str | None = None
    query_message: str | None = None

    @model_validator(mode="after")
    def validate_payload(self) -> CreateRuleRequest:
        """Validate request payload according to rule type."""
        if self.type == "skip_logic":
            if not self.target_field:
                raise ValueError("Skip logic rule requires 'target_field'")
            if self.action not in ("show", "hide"):
                raise ValueError("Skip logic rule requires 'action' ('show' or 'hide')")
        elif self.type == "constraint":
            if not self.target_field:
                raise ValueError("Constraint rule requires 'target_field'")
            if not self.query_message:
                raise ValueError("Constraint rule requires 'query_message'")
        elif self.type == "cross_form_check":
            if not self.query_message:
                raise ValueError("Cross-form check rule requires 'query_message'")
        return self


def extract_field_references(node: ExpressionNode) -> list[FieldReference]:
    """Traverses the ExpressionNode tree to collect all FieldReferences."""
    refs: list[FieldReference] = []
    if node.type == "field_ref" and node.field_ref:
        refs.append(node.field_ref)
    if node.operands:
        for op in node.operands:
            refs.extend(extract_field_references(op))
    return refs


def detect_circular_dependencies(rules: list[dict[str, Any]]) -> list[str]:
    """Analyzes skip-logic rules to detect circular visibility dependencies.

    Returns a list of circular dependency path description strings.
    """
    skip_rules = [
        r
        for r in rules
        if r.get("type") == "skip_logic" and not r.get("is_deleted", False)
    ]

    adj: dict[str, list[str]] = {}
    for rule in skip_rules:
        target = rule.get("target_field")
        if not target:
            continue

        cond_node = rule.get("condition")
        if not cond_node:
            continue

        if isinstance(cond_node, dict):
            try:
                cond_node = ExpressionNode(**cond_node)
            except Exception:
                continue

        refs = extract_field_references(cond_node)
        ref_fields = {ref.field_id for ref in refs}
        adj[target] = list(ref_fields)

    state: dict[str, int] = {}  # 0: unvisited, 1: visiting, 2: visited
    cycles: list[str] = []
    parent: dict[str, str | None] = {}

    def dfs(node: str) -> bool:
        state[node] = 1
        for neighbor in adj.get(node, []):
            if neighbor not in state or state[neighbor] == 0:
                parent[neighbor] = node
                if dfs(neighbor):
                    return True
            elif state[neighbor] == 1:
                path = [neighbor]
                curr: str | None = node
                while curr is not None and curr != neighbor:
                    path.append(curr)
                    curr = parent.get(curr)
                path.append(neighbor)
                path.reverse()
                cycles.append(" -> ".join(path))
                return True
        state[node] = 2
        return False

    for node in adj:
        if node not in state or state[node] == 0:
            dfs(node)

    return cycles
