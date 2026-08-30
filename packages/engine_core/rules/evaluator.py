"""Pure Sandboxed Universal Rules & Expression Engine (UREE) Evaluator.

Executes declarative AST expression trees and JSONLogic specifications in-memory
with zero I/O side effects, strict type validation, and clinical arithmetic operators.
"""

from __future__ import annotations

import math
import re
from datetime import UTC, date, datetime, timedelta
from typing import Any

from packages.engine_core.rules.ast import ExpressionNode, FieldReference


class EvaluationError(Exception):
    """Base exception for rule expression evaluation errors."""


class UndefinedVariableError(EvaluationError):
    """Raised when a referenced variable or field does not exist and strict mode is enabled."""


class TypeMismatchError(EvaluationError):
    """Raised when operands are incompatible with the requested operation."""


def _parse_date(val: Any) -> datetime | None:
    """Safely parse various date/datetime inputs into timezone-aware UTC datetime."""
    if val is None:
        return None
    if isinstance(val, datetime):
        return val.replace(tzinfo=UTC) if val.tzinfo is None else val.astimezone(UTC)
    if isinstance(val, date):
        return datetime(val.year, val.month, val.day, tzinfo=UTC)
    if isinstance(val, str):
        clean = val.strip()
        if not clean:
            return None
        if clean.endswith("Z"):
            clean = clean[:-1] + "+00:00"
        try:
            dt = datetime.fromisoformat(clean)
            return dt.replace(tzinfo=UTC) if dt.tzinfo is None else dt.astimezone(UTC)
        except ValueError:
            # Try YYYY-MM-DD
            if len(clean) == 10:
                try:
                    return datetime.strptime(clean, "%Y-%m-%d").replace(tzinfo=UTC)
                except ValueError:
                    pass
    return None


def _to_number(val: Any) -> float | int | None:
    """Safely converts a value to int or float, returning None if not numeric."""
    if val is None or isinstance(val, bool):
        return None
    if isinstance(val, (int, float)):
        return val
    if isinstance(val, str):
        cleaned = val.strip()
        try:
            if "." in cleaned or "e" in cleaned.lower():
                return float(cleaned)
            return int(cleaned)
        except ValueError:
            return None
    return None


def _get_nested_value(data: dict[str, Any], path: str) -> Any:
    """Resolves dot-separated paths in nested dictionaries."""
    if not isinstance(data, dict):
        return None
    if path in data:
        return data[path]
    parts = path.split(".")
    curr: Any = data
    for part in parts:
        if isinstance(curr, dict) and part in curr:
            curr = curr[part]
        else:
            return None
    return curr


def resolve_context_value(
    context: dict[str, Any], key: str, default: Any = None
) -> Any:
    """Resolves a key or field identifier across standard context namespaces."""
    # 1. Direct key lookup or nested dot notation
    val = _get_nested_value(context, key)
    if val is not None:
        return val

    # 2. Check standard clinical data wrappers
    for namespace in ("data", "observations", "variables", "fields", "subject", "form"):
        wrapper = context.get(namespace)
        if isinstance(wrapper, dict):
            val = _get_nested_value(wrapper, key)
            if val is not None:
                return val

    return default


class RuleEvaluator:
    """Pure, sandboxed in-memory expression evaluator."""

    def __init__(
        self, strict_variables: bool = False, as_of: datetime | None = None
    ) -> None:
        """
        Args:
            strict_variables: If True, undefined variables raise UndefinedVariableError.
            as_of: Reference timestamp for relative temporal and date functions.
        """
        self.strict_variables = strict_variables
        self.as_of = (
            as_of.replace(tzinfo=UTC)
            if as_of and as_of.tzinfo is None
            else (as_of or datetime.now(UTC))
        )

    def evaluate(self, node: ExpressionNode, context: dict[str, Any]) -> Any:
        """Recursively evaluates an ExpressionNode AST against runtime context."""
        node_type = node.type

        if node_type == "constant":
            return node.value

        if node_type == "variable":
            var_name = node.variable_name or ""
            val = resolve_context_value(context, var_name, default=None)
            if val is None and self.strict_variables and var_name not in context:
                raise UndefinedVariableError(f"Undefined variable: '{var_name}'")
            return val

        if node_type == "field_ref":
            field_ref = node.field_ref
            if not field_ref:
                return None
            field_id = field_ref.field_id
            val = resolve_context_value(context, field_id, default=None)
            if val is None and self.strict_variables:
                raise UndefinedVariableError(f"Undefined field reference: '{field_id}'")
            return val

        operands = node.operands or []

        if node_type == "logical":
            return self._evaluate_logical(node.operator or "", operands, context)

        if node_type == "comparison":
            return self._evaluate_comparison(node.operator or "", operands, context)

        if node_type == "arithmetic":
            return self._evaluate_arithmetic(node.operator or "", operands, context)

        if node_type == "function":
            return self._evaluate_function(node.operator or "", operands, context)

        raise EvaluationError(f"Unknown node type: '{node_type}'")

    def _evaluate_logical(
        self, op: str, operands: list[ExpressionNode], context: dict[str, Any]
    ) -> Any:
        if op == "and":
            return all(bool(self.evaluate(operand, context)) for operand in operands)

        if op == "or":
            return any(bool(self.evaluate(operand, context)) for operand in operands)

        if op == "not":
            val = self.evaluate(operands[0], context)
            return not bool(val)

        if op == "xor":
            val1 = bool(self.evaluate(operands[0], context))
            val2 = bool(self.evaluate(operands[1], context))
            return val1 ^ val2

        if op == "if_then_else":
            cond = bool(self.evaluate(operands[0], context))
            if cond:
                return self.evaluate(operands[1], context)
            if len(operands) > 2:
                return self.evaluate(operands[2], context)
            return None

        raise EvaluationError(f"Unsupported logical operator: '{op}'")

    def _evaluate_comparison(
        self, op: str, operands: list[ExpressionNode], context: dict[str, Any]
    ) -> bool:
        left = self.evaluate(operands[0], context)
        right = self.evaluate(operands[1], context)

        if op == "==":
            # Handle float/int numeric equivalence e.g. 1.0 == 1
            if isinstance(left, (int, float)) and isinstance(right, (int, float)):
                return left == right
            # Handle string case-insensitive equality if comparing boolean strings
            if isinstance(left, str) and isinstance(right, bool):
                return (left.lower() in ("true", "1", "yes")) == right
            if isinstance(right, str) and isinstance(left, bool):
                return (right.lower() in ("true", "1", "yes")) == left
            return left == right

        if op == "!=":
            return not self._evaluate_comparison("==", operands, context)

        if op in ("<", "<=", ">", ">="):
            # Attempt date comparison first
            d_left = _parse_date(left)
            d_right = _parse_date(right)
            if d_left is not None and d_right is not None:
                if op == "<":
                    return d_left < d_right
                if op == "<=":
                    return d_left <= d_right
                if op == ">":
                    return d_left > d_right
                if op == ">=":
                    return d_left >= d_right

            # Attempt numeric comparison
            n_left = _to_number(left)
            n_right = _to_number(right)
            if n_left is not None and n_right is not None:
                if op == "<":
                    return n_left < n_right
                if op == "<=":
                    return n_left <= n_right
                if op == ">":
                    return n_left > n_right
                if op == ">=":
                    return n_left >= n_right

            # Fallback to string comparison if both are strings
            if isinstance(left, str) and isinstance(right, str):
                if op == "<":
                    return left < right
                if op == "<=":
                    return left <= right
                if op == ">":
                    return left > right
                if op == ">=":
                    return left >= right

            return False

        if op == "in":
            if right is None:
                return False
            if isinstance(right, (list, tuple, set)):
                return left in right
            if isinstance(right, str) and isinstance(left, str):
                return left in right
            return False

        if op == "not_in":
            return not self._evaluate_comparison("in", operands, context)

        if op == "between":
            lower = self.evaluate(operands[1], context)
            upper = self.evaluate(operands[2], context)
            val_num = _to_number(left)
            low_num = _to_number(lower)
            high_num = _to_number(upper)
            if val_num is not None and low_num is not None and high_num is not None:
                return low_num <= val_num <= high_num
            # Date between
            d_val = _parse_date(left)
            d_low = _parse_date(lower)
            d_high = _parse_date(upper)
            if d_val is not None and d_low is not None and d_high is not None:
                return d_low <= d_val <= d_high
            return False

        if op == "regex_match":
            if left is None or right is None:
                return False
            pattern = str(right)
            string_val = str(left)
            try:
                return bool(re.search(pattern, string_val))
            except re.error:
                return False

        raise EvaluationError(f"Unsupported comparison operator: '{op}'")

    def _evaluate_arithmetic(
        self, op: str, operands: list[ExpressionNode], context: dict[str, Any]
    ) -> Any:
        vals = [self.evaluate(operand, context) for operand in operands]

        if op == "+":
            # String concatenation if any string
            if any(isinstance(v, str) for v in vals):
                return "".join(str(v) if v is not None else "" for v in vals)
            nums = [_to_number(v) for v in vals]
            if any(n is None for n in nums):
                return None
            return sum(nums)  # type: ignore[arg-type]

        if op == "-":
            if len(vals) == 1:
                n = _to_number(vals[0])
                return -n if n is not None else None
            n1 = _to_number(vals[0])
            n2 = _to_number(vals[1])
            if n1 is None or n2 is None:
                return None
            return n1 - n2

        if op == "*":
            res: float = 1.0
            for v in vals:
                n = _to_number(v)
                if n is None:
                    return None
                res *= n
            return int(res) if res.is_integer() else res

        if op in ("/", "//"):
            n1 = _to_number(vals[0])
            n2 = _to_number(vals[1])
            if n1 is None or n2 is None or n2 == 0:
                return None
            if op == "//":
                return int(n1 // n2)
            res_div = n1 / n2
            return int(res_div) if res_div.is_integer() else res_div

        if op == "%":
            n1 = _to_number(vals[0])
            n2 = _to_number(vals[1])
            if n1 is None or n2 is None or n2 == 0:
                return None
            return n1 % n2

        if op in ("^", "**"):
            n1 = _to_number(vals[0])
            n2 = _to_number(vals[1])
            if n1 is None or n2 is None:
                return None
            pow_res = n1**n2
            return (
                int(pow_res)
                if isinstance(pow_res, float) and pow_res.is_integer()
                else pow_res
            )

        if op == "abs":
            n = _to_number(vals[0])
            return abs(n) if n is not None else None

        if op == "round":
            n = _to_number(vals[0])
            digits = int(_to_number(vals[1]) or 0) if len(vals) > 1 else 0
            return round(n, digits) if n is not None else None

        if op == "floor":
            n = _to_number(vals[0])
            return math.floor(n) if n is not None else None

        if op == "ceil":
            n = _to_number(vals[0])
            return math.ceil(n) if n is not None else None

        raise EvaluationError(f"Unsupported arithmetic operator: '{op}'")

    def _evaluate_function(
        self, op: str, operands: list[ExpressionNode], context: dict[str, Any]
    ) -> Any:
        if op in ("is_empty", "empty"):
            val = self.evaluate(operands[0], context)
            if val is None or val == "":
                return True
            return isinstance(val, (list, dict, set, tuple)) and len(val) == 0

        if op == "is_not_empty":
            return not self._evaluate_function("is_empty", operands, context)

        if op in ("coalesce", "if_null"):
            for operand in operands:
                val = self.evaluate(operand, context)
                if val is not None and val != "":
                    return val
            return None

        if op == "contains":
            target = self.evaluate(operands[0], context)
            search = self.evaluate(operands[1], context)
            if target is None or search is None:
                return False
            if isinstance(target, (list, tuple, set)):
                return search in target
            return str(search).lower() in str(target).lower()

        if op == "starts_with":
            target = self.evaluate(operands[0], context)
            prefix = self.evaluate(operands[1], context)
            if target is None or prefix is None:
                return False
            return str(target).startswith(str(prefix))

        if op == "ends_with":
            target = self.evaluate(operands[0], context)
            suffix = self.evaluate(operands[1], context)
            if target is None or suffix is None:
                return False
            return str(target).endswith(str(suffix))

        if op == "lower":
            val = self.evaluate(operands[0], context)
            return str(val).lower() if val is not None else ""

        if op == "upper":
            val = self.evaluate(operands[0], context)
            return str(val).upper() if val is not None else ""

        if op == "trim":
            val = self.evaluate(operands[0], context)
            return str(val).strip() if val is not None else ""

        if op == "length":
            val = self.evaluate(operands[0], context)
            if val is None:
                return 0
            if isinstance(val, (str, list, dict, set, tuple)):
                return len(val)
            return len(str(val))

        if op in ("sum", "avg", "min", "max", "count"):
            # If a single operand evaluates to an iterable collection
            raw_vals = [self.evaluate(operand, context) for operand in operands]
            extracted: list[float | int] = []
            for item in raw_vals:
                if isinstance(item, (list, tuple, set)):
                    for sub in item:
                        num = _to_number(sub)
                        if num is not None:
                            extracted.append(num)
                else:
                    num = _to_number(item)
                    if num is not None:
                        extracted.append(num)

            if op == "count":
                return len(extracted)
            if not extracted:
                return None
            if op == "sum":
                total = sum(extracted)
                return (
                    int(total)
                    if isinstance(total, float) and total.is_integer()
                    else total
                )
            if op == "avg":
                return round(sum(extracted) / len(extracted), 4)
            if op == "min":
                return min(extracted)
            if op == "max":
                return max(extracted)

        if op == "date_diff_days":
            d1 = _parse_date(self.evaluate(operands[0], context))
            d2 = _parse_date(self.evaluate(operands[1], context))
            if d1 is None or d2 is None:
                return None
            return (d1 - d2).days

        if op == "date_add_days":
            d = _parse_date(self.evaluate(operands[0], context))
            days = _to_number(self.evaluate(operands[1], context))
            if d is None or days is None:
                return None
            return (d + timedelta(days=float(days))).strftime("%Y-%m-%d")

        if op == "is_future":
            d = _parse_date(self.evaluate(operands[0], context))
            if d is None:
                return False
            return d > self.as_of

        if op == "is_past":
            d = _parse_date(self.evaluate(operands[0], context))
            if d is None:
                return False
            return d < self.as_of

        if op == "age_years":
            birth_dt = _parse_date(self.evaluate(operands[0], context))
            if birth_dt is None:
                return None
            ref_dt = self.as_of
            if len(operands) > 1:
                custom_ref = _parse_date(self.evaluate(operands[1], context))
                if custom_ref is not None:
                    ref_dt = custom_ref
            age = (
                ref_dt.year
                - birth_dt.year
                - ((ref_dt.month, ref_dt.day) < (birth_dt.month, birth_dt.day))
            )
            return max(0, age)

        if op == "indexed_repeat":
            field_name = str(self.evaluate(operands[0], context))
            group_name = str(self.evaluate(operands[1], context))
            idx = int(_to_number(self.evaluate(operands[2], context)) or 1) - 1
            group_data = context.get(group_name, [])
            if isinstance(group_data, list) and 0 <= idx < len(group_data):
                entry = group_data[idx]
                if isinstance(entry, dict):
                    return entry.get(field_name)
            return None

        raise EvaluationError(f"Unsupported function: '{op}'")


def evaluate_expression(
    node: ExpressionNode,
    context: dict[str, Any],
    strict_variables: bool = False,
    as_of: datetime | None = None,
) -> Any:
    """Convenience helper to evaluate an ExpressionNode with zero side effects."""
    evaluator = RuleEvaluator(strict_variables=strict_variables, as_of=as_of)
    return evaluator.evaluate(node, context)


def parse_json_logic(logic: dict[str, Any] | Any) -> ExpressionNode:
    """Converts a standard JSONLogic dictionary or primitive into an ExpressionNode AST."""
    if not isinstance(logic, dict):
        return ExpressionNode(type="constant", value=logic)

    if len(logic) != 1:
        # Dictionary literal or invalid single-key operator
        return ExpressionNode(type="constant", value=logic)

    op, raw_args = next(iter(logic.items()))

    # Map JSONLogic var
    if op == "var":
        if isinstance(raw_args, str):
            # Check if it has field prefix or simple var
            return ExpressionNode(
                type="field_ref", field_ref=FieldReference(field_id=raw_args)
            )
        if isinstance(raw_args, list) and raw_args:
            return ExpressionNode(
                type="field_ref",
                field_ref=FieldReference(field_id=str(raw_args[0])),
            )
        return ExpressionNode(type="constant", value=None)

    args = raw_args if isinstance(raw_args, list) else [raw_args]
    parsed_operands = [parse_json_logic(arg) for arg in args]

    # Map comparisons
    comp_map = {
        "==": "==",
        "===": "==",
        "!=": "!=",
        "!==": "!=",
        "<": "<",
        "<=": "<=",
        ">": ">",
        ">=": ">=",
        "in": "in",
    }
    if op in comp_map:
        return ExpressionNode(
            type="comparison",
            operator=comp_map[op],
            operands=parsed_operands,
        )

    # Map logical
    if op in ("and", "or"):
        return ExpressionNode(
            type="logical",
            operator=op,
            operands=parsed_operands,
        )
    if op in ("!", "not"):
        return ExpressionNode(
            type="logical",
            operator="not",
            operands=parsed_operands,
        )
    if op in ("?:", "if"):
        return ExpressionNode(
            type="logical",
            operator="if_then_else",
            operands=parsed_operands,
        )

    # Map arithmetic
    arith_map = {
        "+": "+",
        "-": "-",
        "*": "*",
        "/": "/",
        "%": "%",
        "min": "min",
        "max": "max",
    }
    if op in arith_map:
        return ExpressionNode(
            type="arithmetic",
            operator=arith_map[op],
            operands=parsed_operands,
        )

    # Fallback to function
    return ExpressionNode(
        type="function",
        operator=op,
        operands=parsed_operands,
    )


RulesEvaluator = RuleEvaluator
