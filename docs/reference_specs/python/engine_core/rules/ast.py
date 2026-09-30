"""Canonical Clinical Rule AST Engine.

Defines strongly-typed Pydantic v2 condition expressions, field references,
and version-controlled rule definitions for clinical skip-logic, edit checks,
derived calculations, and eligibility constraints.
"""

from __future__ import annotations

import hashlib
import json
from datetime import UTC, datetime
from typing import Any, Literal

from pydantic import BaseModel, ConfigDict, Field, model_validator


class FieldReference(BaseModel):
    """Represents a structured field reference within an expression tree."""

    model_config = ConfigDict(frozen=True)

    field_id: str
    form_id: str | None = None
    visit_id: str | None = None
    visit_relative: str | None = None  # e.g., "previous", "next", "first", "last"


NodeType = Literal[
    "constant",
    "field_ref",
    "variable",
    "comparison",
    "logical",
    "arithmetic",
    "function",
    "json_logic",
]


class ExpressionNode(BaseModel):
    """A recursive node in a structured clinical expression tree."""

    type: NodeType
    operator: str | None = None
    operands: list[ExpressionNode] | None = None
    value: Any | None = None
    field_ref: FieldReference | None = None
    variable_name: str | None = None

    @model_validator(mode="after")
    def validate_node(self) -> ExpressionNode:
        """Validates node structure and operator constraints based on node type."""
        if self.type == "constant":
            if self.value is None and not hasattr(self, "_allow_null_constant"):
                pass  # None is a valid constant value in clinical logic
        elif self.type == "field_ref":
            if self.field_ref is None:
                raise ValueError("Field reference node must provide 'field_ref'")
        elif self.type == "variable":
            if not self.variable_name:
                raise ValueError("Variable node must provide 'variable_name'")
        elif self.type == "logical":
            valid_logical = ("and", "or", "not", "xor", "if_then_else")
            if self.operator not in valid_logical:
                raise ValueError(
                    f"Invalid logical operator '{self.operator}'. Must be one of {valid_logical}"
                )
            if not self.operands:
                raise ValueError(f"Logical node '{self.operator}' requires operands")
            if self.operator == "not" and len(self.operands) != 1:
                raise ValueError("Logical 'not' operator requires exactly 1 operand")
            if self.operator == "if_then_else" and len(self.operands) not in (2, 3):
                raise ValueError(
                    "Logical 'if_then_else' operator requires 2 or 3 operands (condition, true_branch, [false_branch])"
                )
        elif self.type == "comparison":
            valid_comp = (
                "==",
                "!=",
                "<",
                "<=",
                ">",
                ">=",
                "in",
                "not_in",
                "between",
                "regex_match",
            )
            if self.operator not in valid_comp:
                raise ValueError(
                    f"Invalid comparison operator '{self.operator}'. Must be one of {valid_comp}"
                )
            if not self.operands or len(self.operands) < 2:
                raise ValueError(
                    f"Comparison operator '{self.operator}' requires at least 2 operands"
                )
            if self.operator == "between" and len(self.operands) != 3:
                raise ValueError(
                    "Comparison 'between' operator requires exactly 3 operands (value, lower, upper)"
                )
        elif self.type == "arithmetic":
            valid_arith = (
                "+",
                "-",
                "*",
                "/",
                "//",
                "%",
                "^",
                "**",
                "abs",
                "round",
                "floor",
                "ceil",
            )
            if self.operator not in valid_arith:
                raise ValueError(
                    f"Invalid arithmetic operator '{self.operator}'. Must be one of {valid_arith}"
                )
            if not self.operands:
                raise ValueError(f"Arithmetic node '{self.operator}' requires operands")
            if self.operator in ("abs", "floor", "ceil") and len(self.operands) != 1:
                raise ValueError(
                    f"Arithmetic operator '{self.operator}' requires exactly 1 operand"
                )
        elif self.type == "function":
            valid_funcs = {
                "is_empty": 1,
                "empty": 1,
                "is_not_empty": 1,
                "coalesce": -1,
                "if_null": 2,
                "contains": 2,
                "starts_with": 2,
                "ends_with": 2,
                "lower": 1,
                "upper": 1,
                "trim": 1,
                "length": 1,
                "sum": -1,
                "avg": -1,
                "min": -1,
                "max": -1,
                "count": -1,
                "date_diff_days": 2,
                "date_add_days": 2,
                "is_future": 1,
                "is_past": 1,
                "age_years": -1,  # 1 or 2 operands (birth_date, [as_of])
                "indexed_repeat": 3,
            }
            if self.operator not in valid_funcs:
                raise ValueError(
                    f"Invalid function operator '{self.operator}'. Must be one of {list(valid_funcs.keys())}"
                )
            if not self.operands:
                raise ValueError(f"Function node '{self.operator}' requires operands")
            expected_arity = valid_funcs[self.operator]
            if expected_arity != -1 and len(self.operands) != expected_arity:
                suffix = "operand" if expected_arity == 1 else "operands"
                raise ValueError(
                    f"Function '{self.operator}' requires exactly {expected_arity} {suffix}"
                )
        return self


# Rebuild recursive model for Pydantic v2
ExpressionNode.model_rebuild()


RuleType = Literal[
    "skip_logic",
    "constraint",
    "cross_form_check",
    "derived_variable",
    "eligibility_criterion",
    "sla_trigger",
]

RuleAction = Literal[
    "show",
    "hide",
    "require",
    "disable",
    "set_value",
    "raise_query",
    "flag_deviation",
    "execute_action",
]


class RuleDefinition(BaseModel):
    """A version-controlled, 21 CFR Part 11 auditable clinical rule DSL specification."""

    rule_id: str
    study_id: str
    name: str
    description: str | None = None
    rule_type: RuleType
    condition: ExpressionNode
    action: RuleAction | None = None
    target_field: str | None = None
    target_form: str | None = None
    target_group: str | None = None
    query_message: str | None = None
    version_index: int = Field(default=1, ge=1)
    sha256_checksum: str | None = None
    is_active: bool = True
    is_deleted: bool = False
    created_at: datetime = Field(default_factory=lambda: datetime.now(UTC))
    created_by: str = "system"
    reason_for_change: str = "Initial rule authoring"

    @model_validator(mode="after")
    def compute_checksum_and_validate(self) -> RuleDefinition:
        """Enforces Part 11 validation constraints and computes SHA-256 rule digest."""
        if self.rule_type == "skip_logic":
            if not self.target_field:
                raise ValueError("Skip logic rule requires 'target_field'")
            if self.action not in ("show", "hide", "require", "disable"):
                raise ValueError(
                    "Skip logic rule action must be 'show', 'hide', 'require', or 'disable'"
                )
        elif self.rule_type in ("constraint", "cross_form_check"):
            if not self.query_message:
                raise ValueError(
                    f"{self.rule_type} rule requires a non-empty 'query_message'"
                )
        elif self.rule_type == "derived_variable":
            if not self.target_field:
                raise ValueError("Derived variable rule requires 'target_field'")

        if not self.reason_for_change or len(self.reason_for_change.strip()) < 5:
            raise ValueError(
                "GxP compliance requires a valid 'reason_for_change' (min 5 chars)"
            )

        if not self.sha256_checksum:
            payload = {
                "rule_id": self.rule_id,
                "study_id": self.study_id,
                "rule_type": self.rule_type,
                "condition": self.condition.model_dump(),
                "action": self.action,
                "target_field": self.target_field,
                "target_form": self.target_form,
                "query_message": self.query_message,
                "version_index": self.version_index,
            }
            canonical_json = json.dumps(payload, sort_keys=True)
            self.sha256_checksum = hashlib.sha256(
                canonical_json.encode("utf-8")
            ).hexdigest()

        return self
