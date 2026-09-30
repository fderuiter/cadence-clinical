"""Universal Rules & Expression Engine (UREE) Core Module.

Provides AST models, pure sandboxed evaluator, and static analysis sentinels.
"""

from packages.engine_core.rules.ast import (
    ExpressionNode,
    FieldReference,
    NodeType,
    RuleAction,
    RuleDefinition,
    RuleType,
)
from packages.engine_core.rules.evaluator import (
    EvaluationError,
    RuleEvaluator,
    TypeMismatchError,
    UndefinedVariableError,
    evaluate_expression,
    parse_json_logic,
    resolve_context_value,
)
from packages.engine_core.rules.sentinels import (
    calculate_expression_complexity,
    detect_circular_dependencies,
    detect_unresolved_references,
    extract_field_references,
    extract_variable_names,
)

__all__ = [
    "EvaluationError",
    "ExpressionNode",
    "FieldReference",
    "NodeType",
    "RuleAction",
    "RuleDefinition",
    "RuleEvaluator",
    "RuleType",
    "TypeMismatchError",
    "UndefinedVariableError",
    "calculate_expression_complexity",
    "detect_circular_dependencies",
    "detect_unresolved_references",
    "evaluate_expression",
    "extract_field_references",
    "extract_variable_names",
    "parse_json_logic",
    "resolve_context_value",
]
