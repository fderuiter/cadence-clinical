import enum
from typing import Any, Dict, Set


class SubjectState(str, enum.Enum):
    """Enumeration of clinical trial subject lifecycle states.

    States follow a strict, regulated flow to prevent protocol deviations.
    """

    SCREENING = "SCREENING"
    SCREEN_FAILED = "SCREEN_FAILED"
    ENROLLED = "ENROLLED"
    RANDOMIZED = "RANDOMIZED"
    ACTIVE = "ACTIVE"
    COMPLETED = "COMPLETED"
    UNBLINDED = "UNBLINDED"
    WITHDRAWN = "WITHDRAWN"
    RECONSENT_REQUIRED = "RECONSENT_REQUIRED"


class InvalidStateTransitionError(ValueError):
    """Domain error raised when an invalid subject state transition is attempted.

    Attributes:
        current_state (str): The state before the transition attempt.
        target_state (str): The forbidden target state attempted.
        error_code (str): Structured error code representing this domain error.
    """

    def __init__(self, current_state: str, target_state: str):
        self.current_state = current_state
        self.target_state = target_state
        self.error_code = "INVALID_STATE_TRANSITION"
        super().__init__(
            f"Transition from {current_state} to {target_state} is forbidden."
        )


class LockedFactorMutationError(ValueError):
    """Domain error raised when attempting to modify locked stratification factors on a randomized subject.

    Attributes:
        error_code (str): Structured error code representing this domain error.
    """

    def __init__(
        self,
        message: str = "Cannot modify stratification factors for randomized subjects. Re-randomization is strictly blocked.",
    ):
        self.error_code = "LOCKED_FACTOR_MUTATION"
        super().__init__(message)


# Strict map of allowed state transitions
ALLOWED_SUBJECT_TRANSITIONS: Dict[SubjectState, Set[SubjectState]] = {
    SubjectState.SCREENING: {
        SubjectState.SCREEN_FAILED,
        SubjectState.ENROLLED,
        SubjectState.WITHDRAWN,
        SubjectState.RECONSENT_REQUIRED,
    },
    SubjectState.ENROLLED: {
        SubjectState.RANDOMIZED,
        SubjectState.WITHDRAWN,
        SubjectState.RECONSENT_REQUIRED,
    },
    SubjectState.RANDOMIZED: {
        SubjectState.ACTIVE,
        SubjectState.WITHDRAWN,
        SubjectState.UNBLINDED,
        SubjectState.RECONSENT_REQUIRED,
    },
    SubjectState.ACTIVE: {
        SubjectState.COMPLETED,
        SubjectState.WITHDRAWN,
        SubjectState.UNBLINDED,
        SubjectState.RECONSENT_REQUIRED,
    },
    SubjectState.UNBLINDED: {
        SubjectState.WITHDRAWN,
        SubjectState.COMPLETED,
        SubjectState.RECONSENT_REQUIRED,
    },
    SubjectState.SCREEN_FAILED: set(),
    SubjectState.COMPLETED: set(),
    SubjectState.WITHDRAWN: set(),
    SubjectState.RECONSENT_REQUIRED: {
        SubjectState.SCREENING,
        SubjectState.ENROLLED,
        SubjectState.RANDOMIZED,
        SubjectState.ACTIVE,
        SubjectState.COMPLETED,
        SubjectState.WITHDRAWN,
        SubjectState.UNBLINDED,
    },
}


def normalize_state(state: Any) -> str | None:
    """Normalizes any state input into its standard uppercase underscore representation.

    Args:
        state (Any): The state input, which could be an Enum, a string, or None.

    Returns:
        str | None: The normalized string, or None.
    """
    if state is None:
        return None
    if isinstance(state, enum.Enum):
        state = state.value
    return str(state).strip().upper().replace(" ", "_")


def guard_subject_transition(current_state: Any, target_state: Any) -> None:
    """Guards transitions between subject states according to the protocol state machine.

    This validator acts as the centralized pure-Python guardian of subject state
    pathways, enforcing GxP compliant pathways to prevent protocol deviations.
    Specifically, it verifies that the subject does not undergo unauthorized or
    uncontrolled transitions (e.g., reverting a finalized withdrawal, bypassing
    screening, or re-randomizing) that would compromise trial statistical integrity or
    violate 21 CFR Part 11 traceability rules.

    Args:
        current_state (Any): The current state of the subject, or None.
        target_state (Any): The requested new state.

    Raises:
        InvalidStateTransitionError: If the transition is illegal according to the
            defined ALLOWED_SUBJECT_TRANSITIONS pathways.
    """
    curr = normalize_state(current_state)
    tgt = normalize_state(target_state)

    if curr == tgt:
        return

    if curr is None:
        if tgt != "SCREENING":
            raise InvalidStateTransitionError("None", str(target_state))
        return

    # Validate that both normalized states correspond to known SubjectState values
    valid_states = {s.value for s in SubjectState}
    if curr not in valid_states or tgt not in valid_states:
        raise InvalidStateTransitionError(str(current_state), str(target_state))

    curr_enum = SubjectState(curr)
    tgt_enum = SubjectState(tgt)

    allowed = ALLOWED_SUBJECT_TRANSITIONS.get(curr_enum, set())
    if tgt_enum not in allowed:
        raise InvalidStateTransitionError(str(current_state), str(target_state))
