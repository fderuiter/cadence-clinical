"""Static Analysis Sentinels for Clinical Rule Definitions.

Provides cycle detection, unresolved reference scans, and complexity metrics
for clinical rule definitions before publication to active study arms.
"""

from __future__ import annotations

from typing import Any

from packages.engine_core.rules.ast import (
    ExpressionNode,
    FieldReference,
    RuleDefinition,
)


def extract_field_references(node: ExpressionNode) -> list[FieldReference]:
    """Traverses an ExpressionNode AST to collect all FieldReference instances."""
    refs: list[FieldReference] = []
    if node.type == "field_ref" and node.field_ref:
        refs.append(node.field_ref)
    if node.operands:
        for operand in node.operands:
            refs.extend(extract_field_references(operand))
    return refs


def extract_variable_names(node: ExpressionNode) -> list[str]:
    """Traverses an ExpressionNode AST to collect all variable names."""
    vars_found: list[str] = []
    if node.type == "variable" and node.variable_name:
        vars_found.append(node.variable_name)
    if node.operands:
        for operand in node.operands:
            vars_found.extend(extract_variable_names(operand))
    return vars_found


def calculate_expression_complexity(node: ExpressionNode) -> dict[str, int]:
    """Calculates structural complexity metrics for an expression tree."""
    node_count = 1
    max_depth = 1
    ref_count = 1 if node.type == "field_ref" else 0
    var_count = 1 if node.type == "variable" else 0

    if node.operands:
        child_depths: list[int] = []
        for operand in node.operands:
            metrics = calculate_expression_complexity(operand)
            node_count += metrics["node_count"]
            ref_count += metrics["ref_count"]
            var_count += metrics["var_count"]
            child_depths.append(metrics["depth"])
        if child_depths:
            max_depth += max(child_depths)

    return {
        "depth": max_depth,
        "node_count": node_count,
        "ref_count": ref_count,
        "var_count": var_count,
    }


def detect_unresolved_references(
    node: ExpressionNode,
    valid_fields: set[str],
    valid_visits: set[str] | None = None,
) -> list[str]:
    """Validates that all field and visit references in an expression exist in the schema."""
    failures: list[str] = []
    refs = extract_field_references(node)

    for ref in refs:
        if ref.field_id not in valid_fields:
            failures.append(f"Unresolved field reference: '{ref.field_id}'")
        if (
            valid_visits is not None
            and ref.visit_id
            and ref.visit_id not in valid_visits
        ):
            failures.append(f"Unresolved visit reference: '{ref.visit_id}'")

    return failures


def detect_circular_dependencies(
    rules: list[RuleDefinition | dict[str, Any]],
) -> list[str]:
    """Analyzes a set of rules to detect circular visibility or derivation dependencies.

    Returns:
        List of formatted dependency cycle trace strings (e.g., 'field_a -> field_b -> field_a').
    """
    adj: dict[str, set[str]] = {}

    for item in rules:
        if isinstance(item, RuleDefinition):
            if item.is_deleted or not item.is_active:
                continue
            if item.rule_type not in ("skip_logic", "derived_variable"):
                continue
            target = item.target_field
            condition_node = item.condition
        elif isinstance(item, dict):
            if item.get("is_deleted", False) or item.get("is_active") is False:
                continue
            rule_type = item.get("rule_type") or item.get("type")
            if rule_type not in ("skip_logic", "derived_variable"):
                continue
            target = item.get("target_field")
            cond_raw = item.get("condition")
            if not cond_raw:
                continue
            if isinstance(cond_raw, dict):
                try:
                    condition_node = ExpressionNode(**cond_raw)
                except Exception:
                    continue
            elif isinstance(cond_raw, ExpressionNode):
                condition_node = cond_raw
            else:
                continue
        else:
            continue

        if not target:
            continue

        refs = extract_field_references(condition_node)
        ref_ids = {r.field_id for r in refs if r.field_id != target}
        if target not in adj:
            adj[target] = set()
        adj[target].update(ref_ids)

    state: dict[str, int] = {}
    parent: dict[str, str | None] = {}
    cycles: list[str] = []

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
                cycle_str = " -> ".join(path)
                if cycle_str not in cycles:
                    cycles.append(cycle_str)
                return True
        state[node] = 2
        return False

    for node_name in list(adj.keys()):
        if state.get(node_name, 0) == 0:
            parent[node_name] = None
            dfs(node_name)

    return cycles
