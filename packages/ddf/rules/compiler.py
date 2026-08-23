"""XPath Rule Compiler for Digital Data Flow.

Recursively compiles structured rule conditions into OpenRosa-compliant XPath expressions
for eCRF visibility skip logic and field validation constraints.
"""

from __future__ import annotations

import re
import uuid
from typing import Any


def sanitize_identifier(raw_id: Any) -> str:
    """Sanitize identifier values to be valid XML tag names deterministically.

    Replacing spaces and non-alphanumeric characters, and adjusting leading digits.
    Existing valid identifier formats (alphanumeric strings starting with letters)
    remain unchanged during translation.
    Falls back to standard unique ID generation if the original identifier is entirely missing.
    """
    if not raw_id or not isinstance(raw_id, str) or not raw_id.strip():
        return f"item_{uuid.uuid4().hex[:8]}"

    stripped_id = raw_id.strip()

    if re.match(r"^[a-zA-Z][a-zA-Z0-9_]*$", stripped_id):
        return stripped_id

    starts_with_digit = re.match(r"^\d", stripped_id) is not None

    sanitized_chars = []
    for c in stripped_id:
        if c.isalnum() or c == "_":
            sanitized_chars.append(c)
        elif c == " ":
            sanitized_chars.append("_")
        else:
            sanitized_chars.append(f"_{ord(c):02x}")

    sanitized_str = "".join(sanitized_chars)

    if starts_with_digit:
        sanitized_str = f"item_{sanitized_str}"

    if not re.match(r"^[a-zA-Z_]", sanitized_str):
        sanitized_str = f"item_{sanitized_str}"

    return sanitized_str


def extract_appearance(item: dict[str, Any]) -> str | None:
    """Extract grid layout metadata properties into standard Enketo appearance classes.

    Parses USDM item layout properties (`cols`, `column_span`, `span`) directly or from
    a nested layout/grid object, converting width factors into OpenRosa/Enketo classes (`w1`-`w4`).
    """
    keys = ["cols", "column_span", "span"]
    for k in keys:
        if k in item:
            val = item[k]
            if str(val) in ["1", "2", "3", "4"]:
                return f"w{val}"

    for sub in ["layout", "grid"]:
        if sub in item and isinstance(item[sub], dict):
            for k in keys:
                if k in item[sub]:
                    val = item[sub][k]
                    if str(val) in ["1", "2", "3", "4"]:
                        return f"w{val}"
    return None


def compile_condition_to_xpath(node: Any) -> str:
    """Recursively compiles a structured rule condition (dict or ExpressionNode) into an XPath expression string.

    References to fields are sanitized using `sanitize_identifier`.
    """
    if not node:
        return ""

    # Standardize dictionary vs ExpressionNode
    node_type = getattr(node, "type", None) or (
        node.get("type") if isinstance(node, dict) else None
    )
    if not node_type:
        return ""

    if node_type == "constant":
        val = (
            getattr(node, "value", None)
            if hasattr(node, "value")
            else node.get("value")
        )
        if val is True:
            return "true()"
        if val is False:
            return "false()"
        if val is None:
            return ""
        if isinstance(val, str):
            return f"'{val}'"
        return str(val)

    if node_type == "field_ref":
        ref = (
            getattr(node, "field_ref", None)
            if hasattr(node, "field_ref")
            else node.get("field_ref")
        )
        if not ref:
            return ""
        field_id = (
            getattr(ref, "field_id", None)
            if hasattr(ref, "field_id")
            else (ref.get("field_id") if isinstance(ref, dict) else None)
        )
        if not field_id:
            return ""
        sanitized = sanitize_identifier(field_id)
        return f"/{sanitized}"

    if node_type == "logical":
        operator = (
            getattr(node, "operator", None)
            if hasattr(node, "operator")
            else node.get("operator")
        )
        operands = (
            getattr(node, "operands", None)
            if hasattr(node, "operands")
            else node.get("operands")
        )
        if not operator or not operands:
            return ""
        if operator == "not":
            return f"not({compile_condition_to_xpath(operands[0])})"
        op_lower = f" {operator.lower()} "
        compiled_ops = [compile_condition_to_xpath(op) for op in operands]
        return f"({op_lower.join(compiled_ops)})"

    if node_type == "comparison":
        operator = (
            getattr(node, "operator", None)
            if hasattr(node, "operator")
            else node.get("operator")
        )
        operands = (
            getattr(node, "operands", None)
            if hasattr(node, "operands")
            else node.get("operands")
        )
        if not operator or not operands or len(operands) < 2:
            return ""
        op_symbol = "=" if operator == "==" else operator
        left = compile_condition_to_xpath(operands[0])
        right = compile_condition_to_xpath(operands[1])
        return f"({left} {op_symbol} {right})"

    if node_type == "function":
        operator = (
            getattr(node, "operator", None)
            if hasattr(node, "operator")
            else node.get("operator")
        )
        operands = (
            getattr(node, "operands", None)
            if hasattr(node, "operands")
            else node.get("operands")
        )
        if not operator or not operands:
            return ""
        if operator in ("is_empty", "empty"):
            if len(operands) != 1:
                raise ValueError(f"Function '{operator}' requires exactly 1 operand")
            return f"empty({compile_condition_to_xpath(operands[0])})"
        if operator == "is_not_empty":
            if len(operands) != 1:
                raise ValueError(f"Function '{operator}' requires exactly 1 operand")
            return f"not(empty({compile_condition_to_xpath(operands[0])}))"
        if operator == "indexed-repeat":
            if len(operands) != 3:
                raise ValueError(f"Function '{operator}' requires exactly 3 operands")
            compiled_ops = [compile_condition_to_xpath(op) for op in operands]
            return f"indexed-repeat({', '.join(compiled_ops)})"

        compiled_ops = [compile_condition_to_xpath(op) for op in operands]
        return f"{operator}({', '.join(compiled_ops)})"

    return ""


# Alias for Designer compatibility
compile_to_xpath = compile_condition_to_xpath
