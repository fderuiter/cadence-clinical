"""Finite State Machine (FSM) DAG Lifecycle Engine Module.

Provides state models, Python 3.14 generic StateMachine engine, and DAG graph validator.
"""

from packages.engine_core.fsm.engine import (
    InvalidStateError,
    StateMachine,
    StateMachineError,
    TransitionNotAllowedError,
)
from packages.engine_core.fsm.models import (
    Part11AuditEnvelope,
    StateMachineDefinition,
    StateNode,
    StateType,
    TransitionContext,
    TransitionEdge,
    TransitionResult,
)
from packages.engine_core.fsm.validator import (
    StateMachineValidationError,
    validate_state_machine_graph,
)

__all__ = [
    "InvalidStateError",
    "Part11AuditEnvelope",
    "StateMachine",
    "StateMachineDefinition",
    "StateMachineError",
    "StateMachineValidationError",
    "StateNode",
    "StateType",
    "TransitionContext",
    "TransitionEdge",
    "TransitionNotAllowedError",
    "TransitionResult",
    "validate_state_machine_graph",
]
