import enum
from datetime import datetime
from typing import Any


class SubjectState(enum.StrEnum):
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
ALLOWED_SUBJECT_TRANSITIONS: dict[SubjectState, set[SubjectState]] = {
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


def randomize_subject_model(
    subject: Any, randomization_id: str, kit_reference: str, strat_factors: dict
) -> None:
    """Helper to transition subject state to RANDOMIZED with details."""
    from apps.execution.eligibility_service import (
        verify_subject_eligible_for_randomization,
    )

    verify_subject_eligible_for_randomization(subject)

    subject.strat_factors = strat_factors
    subject.status = "RANDOMIZED"
    subject.randomization_id = randomization_id
    subject.kit_reference = kit_reference


def unblind_subject_model(subject: Any, unblinded_by: str, reason: str) -> None:
    """Helper to transition subject state to UNBLINDED with details."""
    subject.status = "UNBLINDED"
    subject.is_unblinded = True
    subject.unblinded_at = datetime.now()
    subject.unblinded_by = unblinded_by
    subject.unblinded_reason = reason


def withdraw_subject_model(subject: Any, reason: str) -> None:
    """Helper to transition subject state to WITHDRAWN with details."""
    subject.status = "WITHDRAWN"
    subject.withdrawn_at = datetime.now()
    subject.withdrawal_reason = reason


class ReConsentRequiredError(Exception):
    """Raised when data entry is attempted on a subject requiring amendment re-consent."""

    pass


# Backward compatibility alias
ReConsentRequiredException = ReConsentRequiredError


async def validate_subject_version_gating(
    session: Any,
    subject_id: str,
    target_visit_id: str,
    active_protocol_version: str,
    requires_reconsent: bool,
) -> str:
    """Verifies subject re-consent compliance before permitting form data entry.

    Requirements: PRD-SUB-007, PRD-SYS-001
    """
    from sqlalchemy import or_, select

    from apps.execution.database.models import ClinicalSubject, SubjectConsent

    # 1. Fetch Subject
    stmt = select(ClinicalSubject).where(
        or_(
            ClinicalSubject.id == subject_id,
            ClinicalSubject.subject_id == subject_id,
        )
    )
    result = await session.execute(stmt)
    subject = result.scalars().first()
    if not subject:
        raise ValueError(f"Subject {subject_id} not found.")

    subject_canonical_id = getattr(subject, "subject_id", None) or subject.id
    current_active_ver = getattr(subject, "active_protocol_version", None)

    # 2. Check if subject has signed consent for the active protocol version
    if requires_reconsent and current_active_ver != active_protocol_version:
        stmt_icf = select(SubjectConsent).where(
            or_(
                SubjectConsent.subject_id == subject_canonical_id,
                SubjectConsent.subject_id == subject.id,
            ),
            or_(
                SubjectConsent.version_tag == active_protocol_version,
                SubjectConsent.protocol_version == active_protocol_version,
            ),
            or_(
                SubjectConsent.icf_signed.is_(True),
                SubjectConsent.status == "SIGNED",
            ),
            SubjectConsent.is_deleted.is_(False),
        )
        icf_res = await session.execute(stmt_icf)
        signed_icf = icf_res.scalars().first()

        if not signed_icf:
            raise ReConsentRequiredException(
                f"Protocol Amendment {active_protocol_version} is active. "
                f"Subject {subject_id} must execute re-consent before data entry on visit {target_visit_id}."
            )

        # Advance subject version
        subject.active_protocol_version = active_protocol_version
        await session.flush()

    return subject.active_protocol_version or active_protocol_version
