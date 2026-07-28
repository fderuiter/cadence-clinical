"""
Deterministic, semantically matching Python AST evaluator for execution-side checks.
Supports comparison/null semantics, arithmetic, relative paths, and indexed-repeat consistently.
"""

from typing import Any, Dict, Optional


def resolve_relative_path(path: str) -> str:
    """
    Translates relative path references using standard rules.
    """
    if not isinstance(path, str):
        return path
    if path.startswith("../"):
        return "/clinical_data/subject/" + path[3:]
    return path


def get_node_attribute(node: Any, attr: str) -> Any:
    """
    Safely retrieves an attribute or dictionary key from an AST node.
    Supports both Pydantic models (or class objects) and raw dictionaries.
    """
    if node is None:
        return None
    if isinstance(node, dict):
        return node.get(attr, None)
    return getattr(node, attr, None)


def evaluate_ast(
    node: Any,
    context: Optional[Dict[str, Any]] = None,
    current_indices: Optional[Dict[str, int]] = None,
) -> Any:
    """
    Deterministically evaluates an ExpressionNode AST against the given data context.
    Matches the client-side JavaScript evaluator semantics exactly.
    """
    if node is None:
        return None
    if context is None:
        context = {}
    if current_indices is None:
        current_indices = {}

    # Extract attributes supporting both dict and object structures
    node_type = (
        get_node_attribute(node, "node_type") or get_node_attribute(node, "type") or ""
    )
    value = get_node_attribute(node, "value")
    operands = (
        get_node_attribute(node, "children")
        or get_node_attribute(node, "operands")
        or []
    )
    field_ref = get_node_attribute(node, "field_ref")

    # 1. LITERAL / CONSTANT
    if node_type in ("LITERAL", "constant"):
        return value

    # 2. XPATH / FIELD_REF
    if node_type in ("XPATH", "field_ref"):
        path = ""
        if field_ref is not None:
            path = get_node_attribute(field_ref, "field_id") or ""
        elif isinstance(value, str):
            path = value

        if not path:
            return None

        # Direct context lookup
        if path in context:
            return context[path]

        # Resolved path lookup
        resolved_path = resolve_relative_path(path)
        if resolved_path in context:
            return context[resolved_path]

        # Bare field name lookup fallback (e.g. /clinical_data/vssbp -> vssbp)
        bare_field = path.split("/")[-1]
        if bare_field in context:
            return context[bare_field]

        return None

    # 3. OPERATOR / LOGICAL / COMPARISON
    if node_type in ("OPERATOR", "logical", "comparison"):
        operator_val = get_node_attribute(node, "operator") or value
        if not isinstance(operator_val, str):
            return None
        operator = operator_val.lower()

        # Logical operators (Kleene 3-valued logic)
        if operator == "not":
            if not operands:
                return None
            child_val = evaluate_ast(operands[0], context, current_indices)
            if child_val is None:
                return None
            return not child_val

        if operator == "and":
            # Kleene logic: if any operand is False, return False.
            # Else if any is None, return None. Else return True.
            has_none = False
            for child in operands:
                child_val = evaluate_ast(child, context, current_indices)
                if child_val is False:
                    return False
                if child_val is None:
                    has_none = True
            return None if has_none else True

        if operator == "or":
            # Kleene logic: if any operand is True, return True.
            # Else if any is None, return None. Else return False.
            has_none = False
            for child in operands:
                child_val = evaluate_ast(child, context, current_indices)
                if child_val is True:
                    return True
                if child_val is None:
                    has_none = True
            return None if has_none else False

        # Arithmetic and Comparison requires at least 2 operands
        if len(operands) < 2:
            return None

        left_val = evaluate_ast(operands[0], context, current_indices)
        right_val = evaluate_ast(operands[1], context, current_indices)

        # Arithmetic Operators (with null-safety)
        if operator in ("+", "-", "*", "/"):
            if left_val is None or right_val is None:
                return None
            try:
                l_num = float(left_val)
                r_num = float(right_val)
            except (ValueError, TypeError):
                return None

            if operator == "+":
                return l_num + r_num
            elif operator == "-":
                return l_num - r_num
            elif operator == "*":
                return l_num * r_num
            elif operator == "/":
                if r_num == 0.0:
                    return None  # Safe division by zero
                return l_num / r_num

        # Comparison Operators (with null-safety)
        if operator in ("==", "!=", "<", "<=", ">", ">="):
            if operator == "==":
                return left_val == right_val
            if operator == "!=":
                return left_val != right_val

            # Ordered comparison with None always returns False
            if left_val is None or right_val is None:
                return False

            try:
                l_num = float(left_val)
                r_num = float(right_val)
                use_numeric = True
            except (ValueError, TypeError):
                use_numeric = False

            l_cmp = l_num if use_numeric else str(left_val)
            r_cmp = r_num if use_numeric else str(right_val)

            if operator == "<":
                return l_cmp < r_cmp
            elif operator == "<=":
                return l_cmp <= r_cmp
            elif operator == ">":
                return l_cmp > r_cmp
            elif operator == ">=":
                return l_cmp >= r_cmp

        return None

    # 4. FUNCTION
    if node_type in ("FUNCTION", "function"):
        func_val = get_node_attribute(node, "operator") or value
        if not isinstance(func_val, str):
            return None
        func_name = func_val.lower()

        if func_name in ("is_empty", "empty"):
            if len(operands) != 1:
                return None
            child_val = evaluate_ast(operands[0], context, current_indices)
            if child_val is None:
                return True
            if isinstance(child_val, str) and child_val.strip() == "":
                return True
            return False

        if func_name == "is_not_empty":
            if len(operands) != 1:
                return None
            child_val = evaluate_ast(operands[0], context, current_indices)
            if child_val is None:
                return False
            if isinstance(child_val, str) and child_val.strip() == "":
                return False
            return True

        if func_name == "indexed-repeat":
            if len(operands) != 3:
                return None
            target_field_node = operands[0]
            repeat_group_node = operands[1]
            index_node = operands[2]

            target_path = (
                get_node_attribute(target_field_node, "value")
                or (
                    get_node_attribute(target_field_node, "field_ref")
                    and get_node_attribute(
                        get_node_attribute(target_field_node, "field_ref"), "field_id"
                    )
                )
                or ""
            )
            repeat_group = (
                get_node_attribute(repeat_group_node, "value")
                or (
                    get_node_attribute(repeat_group_node, "field_ref")
                    and get_node_attribute(
                        get_node_attribute(repeat_group_node, "field_ref"), "field_id"
                    )
                )
                or ""
            )
            index_val = evaluate_ast(index_node, context, current_indices)

            try:
                target_index = int(index_val)
            except (ValueError, TypeError):
                return None

            field_name = target_path.split("/")[-1]
            indexed_path = f"{repeat_group}[{target_index}]/{field_name}"

            return context.get(indexed_path, None)

        return None

    return None
