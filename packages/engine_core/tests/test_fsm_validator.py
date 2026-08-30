"""Unit tests for FSM DAG topology validation and deadlock detection.

@req:PRD-SUB-001
"""

from packages.engine_core.fsm.models import (
    StateMachineDefinition,
    StateNode,
    TransitionEdge,
)
from packages.engine_core.fsm.validator import validate_state_machine_graph


def test_validator_valid_graph_passes():
    """Verify that a well-formed state machine graph returns zero validation errors.

    @req:PRD-SUB-001
    """
    states = [
        StateNode(name="DRAFT", state_type="initial"),
        StateNode(name="REVIEW", state_type="normal"),
        StateNode(name="APPROVED", state_type="terminal"),
    ]
    transitions = [
        TransitionEdge(trigger="SUBMIT", from_state="DRAFT", to_state="REVIEW"),
        TransitionEdge(trigger="APPROVE", from_state="REVIEW", to_state="APPROVED"),
        TransitionEdge(trigger="REJECT", from_state="REVIEW", to_state="DRAFT"),
    ]
    fsm_def = StateMachineDefinition(
        machine_id="FSM-VALID-1",
        name="Valid Workflow",
        states=states,
        transitions=transitions,
        initial_state="DRAFT",
        reason_for_change="Authoring test workflow definition",
    )

    errors = validate_state_machine_graph(fsm_def)
    assert len(errors) == 0


def test_validator_detects_unreachable_orphan_states():
    """Verify detection of states that cannot be reached from the initial state.

    @req:PRD-SUB-001
    """
    states = [
        StateNode(name="START", state_type="initial"),
        StateNode(name="DONE", state_type="terminal"),
        StateNode(name="ORPHAN", state_type="normal"),
    ]
    transitions = [
        TransitionEdge(trigger="FINISH", from_state="START", to_state="DONE"),
        TransitionEdge(trigger="PROCESS", from_state="ORPHAN", to_state="DONE"),
    ]
    fsm_def = StateMachineDefinition(
        machine_id="FSM-ORPHAN",
        name="Orphan State Machine",
        states=states,
        transitions=transitions,
        initial_state="START",
        reason_for_change="Testing orphan state detection",
    )

    errors = validate_state_machine_graph(fsm_def)
    assert len(errors) == 1
    assert "Unreachable states detected: ['ORPHAN']" in errors[0]


def test_validator_detects_dead_end_non_terminal_states():
    """Verify detection of reachable non-terminal states with no outgoing transitions.

    @req:PRD-SUB-001
    """
    states = [
        StateNode(name="START", state_type="initial"),
        StateNode(
            name="TRAPPED", state_type="normal"
        ),  # Normal state but no transitions out
        StateNode(name="END", state_type="terminal"),
    ]
    transitions = [
        TransitionEdge(trigger="GO_TRAP", from_state="START", to_state="TRAPPED"),
        TransitionEdge(trigger="GO_END", from_state="START", to_state="END"),
    ]
    fsm_def = StateMachineDefinition(
        machine_id="FSM-DEADLOCK",
        name="Deadlock Trap Machine",
        states=states,
        transitions=transitions,
        initial_state="START",
        reason_for_change="Testing deadlock state trap detection",
    )

    errors = validate_state_machine_graph(fsm_def)
    assert any("deadlock trap" in err for err in errors)


def test_validator_detects_duplicate_nondeterministic_transitions():
    """Verify detection of identical unguarded transitions from the same state on the same trigger.

    @req:PRD-SUB-001
    """
    states = [
        StateNode(name="START", state_type="initial"),
        StateNode(name="BRANCH_A", state_type="terminal"),
        StateNode(name="BRANCH_B", state_type="terminal"),
    ]
    transitions = [
        TransitionEdge(trigger="EXECUTE", from_state="START", to_state="BRANCH_A"),
        TransitionEdge(trigger="EXECUTE", from_state="START", to_state="BRANCH_B"),
    ]
    fsm_def = StateMachineDefinition(
        machine_id="FSM-NONDETERMINISTIC",
        name="Nondeterministic Machine",
        states=states,
        transitions=transitions,
        initial_state="START",
        reason_for_change="Testing nondeterministic transition detection",
    )

    errors = validate_state_machine_graph(fsm_def)
    assert any("Non-deterministic duplicate transition" in err for err in errors)
