"""Static Graph Topology Validator for State Machine Definitions.

Enforces reachability, deadlock prevention, orphan state detection,
and deterministic transition resolution for regulated clinical lifecycles.
"""

from __future__ import annotations

from collections import deque

from packages.engine_core.fsm.models import StateMachineDefinition


class StateMachineValidationError(Exception):
    """Raised when a state machine definition violates graph integrity rules."""


def validate_state_machine_graph(definition: StateMachineDefinition) -> list[str]:
    """Validates structural integrity, reachability, and determinism of a state machine definition.

    Returns:
        List of validation error messages (empty list if the definition is valid).
    """
    errors: list[str] = []
    state_names = {s.name for s in definition.states}

    # 1. Verify initial state
    if definition.initial_state not in state_names:
        errors.append(
            f"Initial state '{definition.initial_state}' is not defined in states list"
        )

    # 2. Verify all transitions reference defined states
    adj: dict[str, list[str]] = {s: [] for s in state_names}
    reverse_adj: dict[str, list[str]] = {s: [] for s in state_names}
    seen_transitions: set[tuple[str, str, str | None]] = set()

    for idx, edge in enumerate(definition.transitions):
        if edge.from_state not in state_names:
            errors.append(
                f"Transition #{idx} references unknown from_state: '{edge.from_state}'"
            )
        if edge.to_state not in state_names:
            errors.append(
                f"Transition #{idx} references unknown to_state: '{edge.to_state}'"
            )

        if edge.from_state in state_names and edge.to_state in state_names:
            adj[edge.from_state].append(edge.to_state)
            reverse_adj[edge.to_state].append(edge.from_state)

            # Check duplicate unguarded transitions (non-determinism)
            guard_key = edge.guard.model_dump_json() if edge.guard else None
            key = (edge.from_state, edge.trigger, guard_key)
            if key in seen_transitions:
                errors.append(
                    f"Non-deterministic duplicate transition from '{edge.from_state}' on trigger '{edge.trigger}'"
                )
            seen_transitions.add(key)

    if errors:
        return errors

    # 3. Check for unreachable / orphan states (BFS from initial state)
    visited_forward: set[str] = set()
    queue = deque([definition.initial_state])
    visited_forward.add(definition.initial_state)

    while queue:
        curr = queue.popleft()
        for neighbor in adj.get(curr, []):
            if neighbor not in visited_forward:
                visited_forward.add(neighbor)
                queue.append(neighbor)

    unreachable = state_names - visited_forward
    if unreachable:
        errors.append(f"Unreachable states detected: {sorted(unreachable)}")

    # 4. Check for dead-end non-terminal states
    terminal_states = {
        s.name for s in definition.states if s.state_type in ("terminal", "error")
    }

    for s in definition.states:
        if s.name in visited_forward:
            if s.state_type not in ("terminal", "error") and not adj.get(s.name):
                errors.append(
                    f"Non-terminal state '{s.name}' has no outgoing transitions (deadlock trap)"
                )

    # 5. Check that at least one terminal state is reachable from reachable states
    if terminal_states:
        can_reach_terminal: set[str] = set()
        term_queue = deque(terminal_states)
        can_reach_terminal.update(terminal_states)

        while term_queue:
            curr = term_queue.popleft()
            for pred in reverse_adj.get(curr, []):
                if pred not in can_reach_terminal:
                    can_reach_terminal.add(pred)
                    term_queue.append(pred)

        deadlocks = visited_forward - can_reach_terminal
        if deadlocks:
            errors.append(
                f"Reachable states cannot reach any terminal state: {sorted(deadlocks)}"
            )

    return errors
