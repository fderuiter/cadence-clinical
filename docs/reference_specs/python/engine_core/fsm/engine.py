"""Finite State Machine (FSM) DAG Lifecycle Engine.

Executes declarative state transitions with dynamic UREE expression guards,
role-based authorization gates, Part 11 reason-for-change justification,
and cryptographic audit ledger sealing using Python 3.14 native generics.
"""

from __future__ import annotations

from typing import Any

from packages.engine_core.fsm.models import (
    Part11AuditEnvelope,
    StateMachineDefinition,
    TransitionContext,
    TransitionEdge,
    TransitionResult,
)
from packages.engine_core.rules.evaluator import evaluate_expression


class StateMachineError(Exception):
    """Base exception for state machine transition and execution errors."""


class InvalidStateError(StateMachineError):
    """Raised when an unrecognized state is requested."""


class TransitionNotAllowedError(StateMachineError):
    """Raised when a requested transition is invalid or blocked by guards/authorization."""


class StateMachine[TState: (str, Any), TTrigger: (str, Any)]:
    """Generic, declarative Finite State Machine and DAG Lifecycle Engine."""

    def __init__(self, definition: StateMachineDefinition) -> None:
        self.definition = definition
        self._states = {s.name: s for s in definition.states}
        self._transitions_by_from_state: dict[str, list[TransitionEdge]] = {}

        for edge in definition.transitions:
            if edge.from_state not in self._transitions_by_from_state:
                self._transitions_by_from_state[edge.from_state] = []
            self._transitions_by_from_state[edge.from_state].append(edge)

    @property
    def machine_id(self) -> str:
        return self.definition.machine_id

    @property
    def version_index(self) -> int:
        return self.definition.version_index

    @property
    def initial_state(self) -> str:
        return self.definition.initial_state

    def is_valid_state(self, state: TState) -> bool:
        """Returns True if the given state exists in this machine."""
        return str(state) in self._states

    def get_state_type(self, state: TState) -> str | None:
        """Returns the StateType ('initial', 'normal', 'terminal', 'error') of the state."""
        node = self._states.get(str(state))
        return node.state_type if node else None

    def is_terminal_state(self, state: TState) -> bool:
        """Returns True if the given state is a terminal or error state."""
        st = self.get_state_type(state)
        return st in ("terminal", "error")

    def get_available_transitions(
        self,
        current_state: TState,
        context: dict[str, Any] | None = None,
        roles: list[str] | None = None,
    ) -> list[TransitionEdge]:
        """Returns all valid outgoing transitions from current_state whose guards and roles pass."""
        state_str = str(current_state)
        candidate_edges = self._transitions_by_from_state.get(state_str, [])
        ctx = context or {}
        user_roles_lower = {r.strip().lower() for r in (roles or [])}

        valid_edges: list[TransitionEdge] = []
        for edge in candidate_edges:
            # Check role authorization
            if edge.allowed_roles:
                edge_roles_lower = {r.strip().lower() for r in edge.allowed_roles}
                if not user_roles_lower.intersection(edge_roles_lower):
                    continue

            # Check guard expression
            if edge.guard is not None:
                try:
                    passed = bool(evaluate_expression(edge.guard, ctx))
                    if not passed:
                        continue
                except Exception:
                    continue

            valid_edges.append(edge)

        return valid_edges

    def can_transition(
        self,
        current_state: TState,
        trigger: TTrigger,
        context: dict[str, Any] | None = None,
        roles: list[str] | None = None,
    ) -> tuple[bool, str | None, TransitionEdge | None]:
        """Evaluates whether a transition from current_state on trigger is permissible.

        Returns:
            Tuple of (is_allowed, failure_reason_if_blocked, matching_edge).
        """
        state_str = str(current_state)
        trigger_str = str(trigger)

        if not self.is_valid_state(current_state):
            return False, f"Invalid current state: '{state_str}'", None

        candidate_edges = [
            e
            for e in self._transitions_by_from_state.get(state_str, [])
            if e.trigger == trigger_str
        ]

        if not candidate_edges:
            return (
                False,
                f"No transition defined from state '{state_str}' on trigger '{trigger_str}'",
                None,
            )

        ctx = context or {}
        user_roles_lower = {r.strip().lower() for r in (roles or [])}

        for edge in candidate_edges:
            # 1. Role verification
            if edge.allowed_roles:
                edge_roles_lower = {r.strip().lower() for r in edge.allowed_roles}
                if not user_roles_lower.intersection(edge_roles_lower):
                    return (
                        False,
                        f"User roles {roles} not authorized for transition '{edge.trigger}' (requires {edge.allowed_roles})",
                        edge,
                    )

            # 2. Guard expression verification
            if edge.guard is not None:
                try:
                    passed = bool(evaluate_expression(edge.guard, ctx))
                    if not passed:
                        return (
                            False,
                            f"Guard condition failed for transition to '{edge.to_state}'",
                            edge,
                        )
                except Exception as exc:
                    return (
                        False,
                        f"Guard evaluation error for transition to '{edge.to_state}': {exc}",
                        edge,
                    )

            return True, None, edge

        return False, "Transition conditions not satisfied", None

    def transition(self, context: TransitionContext) -> TransitionResult:
        """Executes a state transition against the runtime context with Part 11 audit envelope."""
        curr_state = context.current_state
        trigger = context.trigger

        is_allowed, reason, edge = self.can_transition(
            current_state=curr_state,  # type: ignore[arg-type]
            trigger=trigger,  # type: ignore[arg-type]
            context=context.payload,
            roles=context.user_roles,
        )

        if not is_allowed or edge is None:
            return TransitionResult(
                success=False,
                from_state=curr_state,
                to_state=curr_state,
                trigger=trigger,
                error_message=reason or "Transition rejected",
                audit_envelope=None,
                executed_action_hooks=[],
            )

        # Enforce 21 CFR Part 11 reason-for-change validation
        if edge.requires_reason_for_change:
            if (
                not context.reason_for_change
                or len(context.reason_for_change.strip()) < edge.min_reason_length
            ):
                return TransitionResult(
                    success=False,
                    from_state=curr_state,
                    to_state=curr_state,
                    trigger=trigger,
                    error_message=(
                        f"GxP compliance requires a valid reason_for_change "
                        f"(minimum {edge.min_reason_length} characters, got '{context.reason_for_change}')"
                    ),
                    audit_envelope=None,
                    executed_action_hooks=[],
                )

        # Enforce electronic signature requirement
        if edge.requires_signature:
            if not context.signature_token or not context.signature_token.strip():
                return TransitionResult(
                    success=False,
                    from_state=curr_state,
                    to_state=curr_state,
                    trigger=trigger,
                    error_message="Transition requires a valid 21 CFR Part 11 electronic signature token",
                    audit_envelope=None,
                    executed_action_hooks=[],
                )

        # Build sealed Part 11 audit envelope
        audit_env = Part11AuditEnvelope(
            machine_id=self.machine_id,
            machine_version=self.version_index,
            from_state=curr_state,
            to_state=edge.to_state,
            trigger=trigger,
            user_id=context.user_id,
            user_roles=context.user_roles,
            reason_for_change=context.reason_for_change or "State transition execution",
            signature_verified=bool(context.signature_token),
        )

        return TransitionResult(
            success=True,
            from_state=curr_state,
            to_state=edge.to_state,
            trigger=trigger,
            error_message=None,
            audit_envelope=audit_env,
            executed_action_hooks=edge.action_hooks,
        )
