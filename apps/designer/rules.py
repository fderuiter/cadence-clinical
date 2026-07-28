from __future__ import annotations

from typing import Any, Dict, List, Literal, Optional

from pydantic import BaseModel, model_validator


class FieldReference(BaseModel):
    """
    Represents a structured field reference within an expression tree.
    """

    field_id: str
    form_id: Optional[str] = None
    visit_id: Optional[str] = None
    visit_relative: Optional[str] = None  # e.g., "previous", "next"


class ExpressionNode(BaseModel):
    """
    A recursive node in a structured clinical expression tree.
    """

    type: Literal["logical", "comparison", "function", "field_ref", "constant"]
    operator: Optional[str] = None
    operands: Optional[List[ExpressionNode]] = None
    value: Optional[Any] = None
    field_ref: Optional[FieldReference] = None

    @model_validator(mode="after")
    def validate_node(self) -> ExpressionNode:
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
    """
    A rule that defines visibility skip logic for fields, groups, or forms.
    """

    id: str
    study_id: str
    type: Literal["skip_logic"] = "skip_logic"
    condition: ExpressionNode
    action: Literal["show", "hide"]
    target_field: str
    target_form: Optional[str] = None
    target_group: Optional[str] = None
    version_index: int = 1
    is_deleted: bool = False


class ConstraintRule(BaseModel):
    """
    A rule that defines constraints and validations on a single field.
    """

    id: str
    study_id: str
    type: Literal["constraint"] = "constraint"
    condition: ExpressionNode
    target_field: str
    target_form: Optional[str] = None
    query_message: str
    version_index: int = 1
    is_deleted: bool = False

    @model_validator(mode="after")
    def validate_message(self) -> ConstraintRule:
        if not self.query_message:
            raise ValueError("Constraint rule requires a non-empty query_message")
        return self


class CrossFormCheckRule(BaseModel):
    """
    A rule that defines edit checks spanning multiple forms or visits (longitudinal checks).
    """

    id: str
    study_id: str
    type: Literal["cross_form_check"] = "cross_form_check"
    condition: ExpressionNode
    query_message: str
    version_index: int = 1
    is_deleted: bool = False

    @model_validator(mode="after")
    def validate_message(self) -> CrossFormCheckRule:
        if not self.query_message:
            raise ValueError("Cross-form check rule requires a non-empty query_message")
        return self


class CreateRuleRequest(BaseModel):
    """
    Request schema to create a rule.
    """

    type: Literal["skip_logic", "constraint", "cross_form_check"]
    condition: ExpressionNode
    action: Optional[Literal["show", "hide"]] = None
    target_field: Optional[str] = None
    target_form: Optional[str] = None
    target_group: Optional[str] = None
    query_message: Optional[str] = None

    @model_validator(mode="after")
    def validate_payload(self) -> CreateRuleRequest:
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


def compile_to_xpath(node: ExpressionNode) -> str:
    """
    Recursively compiles an ExpressionNode tree into an XPath expression string.
    """
    if node.type == "constant":
        if node.value is True:
            return "true()"
        if node.value is False:
            return "false()"
        if isinstance(node.value, str):
            return f"'{node.value}'"
        return str(node.value)

    elif node.type == "field_ref":
        ref = node.field_ref
        parts = []
        if ref.visit_relative:
            parts.append(ref.visit_relative)
        elif ref.visit_id:
            parts.append(ref.visit_id)

        if ref.form_id:
            parts.append(ref.form_id)

        parts.append(ref.field_id)
        return f"/clinical_data/{'/'.join(parts)}"

    elif node.type == "logical":
        if node.operator == "not":
            return f"not({compile_to_xpath(node.operands[0])})"
        op_upper = f" {node.operator.upper()} "
        compiled_ops = [compile_to_xpath(op) for op in node.operands]
        return f"({op_upper.join(compiled_ops)})"

    elif node.type == "comparison":
        op_symbol = "=" if node.operator == "==" else node.operator
        left = compile_to_xpath(node.operands[0])
        right = compile_to_xpath(node.operands[1])
        return f"({left} {op_symbol} {right})"

    elif node.type == "function":
        if node.operator in ("is_empty", "empty"):
            if len(node.operands) != 1:
                raise ValueError(
                    f"Function '{node.operator}' requires exactly 1 operand"
                )
            return f"empty({compile_to_xpath(node.operands[0])})"
        elif node.operator == "is_not_empty":
            if len(node.operands) != 1:
                raise ValueError(
                    f"Function '{node.operator}' requires exactly 1 operand"
                )
            return f"not(empty({compile_to_xpath(node.operands[0])}))"
        elif node.operator == "indexed-repeat":
            if len(node.operands) != 3:
                raise ValueError(
                    f"Function '{node.operator}' requires exactly 3 operands"
                )
            compiled_ops = [compile_to_xpath(op) for op in node.operands]
            return f"indexed-repeat({', '.join(compiled_ops)})"

        if not node.operands:
            raise ValueError(f"Function '{node.operator}' requires operands")
        compiled_ops = [compile_to_xpath(op) for op in node.operands]
        return f"{node.operator}({', '.join(compiled_ops)})"

    return ""


def extract_field_references(node: ExpressionNode) -> List[FieldReference]:
    """
    Traverses the ExpressionNode tree to collect all FieldReferences.
    """
    refs = []
    if node.type == "field_ref" and node.field_ref:
        refs.append(node.field_ref)
    if node.operands:
        for op in node.operands:
            refs.extend(extract_field_references(op))
    return refs


def detect_unknown_fields(
    node: ExpressionNode, study_projection: Dict[str, Any]
) -> List[str]:
    """
    Checks if referenced fields, forms, or visits do not exist in the study projection.
    Returns a list of validation failure messages.
    """
    failures = []
    refs = extract_field_references(node)

    # Gather all valid visits, forms/activities/items from study projection
    valid_visits = set()
    valid_fields = set()

    for arm in study_projection.get("arms", []):
        for visit in arm.get("visits", []):
            valid_visits.add(visit["visit_id"])
            for act in visit.get("activities", []):
                valid_fields.add(act["activity_id"])
                if "name" in act:
                    valid_fields.add(act["name"])

    for ref in refs:
        if ref.visit_id and ref.visit_id not in valid_visits:
            failures.append(f"Unknown visit reference: '{ref.visit_id}'")
        if ref.field_id not in valid_fields:
            # Also allow standard variables if we want, but check against known fields first
            failures.append(f"Unknown field reference: '{ref.field_id}'")

    return failures


def detect_circular_dependencies(rules: List[Dict[str, Any]]) -> List[str]:
    """
    Analyzes skip-logic rules to detect circular visibility dependencies.
    Returns a list of circular dependency path description strings.
    """
    # Filter for active skip logic rules
    skip_rules = [
        r
        for r in rules
        if r.get("type") == "skip_logic" and not r.get("is_deleted", False)
    ]

    # Build adjacency list: target_field -> referenced_fields
    adj = {}
    for rule in skip_rules:
        target = rule.get("target_field")
        if not target:
            continue

        cond_node = rule.get("condition")
        if not cond_node:
            continue

        # If condition is already a dictionary, deserialize to ExpressionNode first
        if isinstance(cond_node, dict):
            try:
                cond_node = ExpressionNode(**cond_node)
            except Exception:
                continue

        refs = extract_field_references(cond_node)
        ref_fields = {ref.field_id for ref in refs}
        adj[target] = list(ref_fields)

    # Three-color DFS cycle detection to guarantee robustness and prevent index crashes
    state = {}  # node -> 0 (unvisited/absent), 1 (visiting), 2 (visited)
    cycles = []
    parent = {}

    def dfs(node: str) -> bool:
        state[node] = 1
        for neighbor in adj.get(node, []):
            if neighbor not in state or state[neighbor] == 0:
                parent[neighbor] = node
                if dfs(neighbor):
                    return True
            elif state[neighbor] == 1:
                # Cycle detected! Construct cycle path using parent pointers
                path = [neighbor]
                curr = node
                while curr != neighbor:
                    path.append(curr)
                    curr = parent.get(curr)
                    if curr is None:
                        break
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
