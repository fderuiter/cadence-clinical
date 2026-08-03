"""SDV State Machine transition validator module.

This module centralizes and validates Source Data Verification (SDV) status
transitions in a pure, DB-independent manner, complying with GxP audit guidelines.
"""


class SDVStateTransitionError(ValueError):
    """Exception raised when an invalid SDV state transition is attempted."""

    pass


# Map of allowed target states for each current state
ALLOWED_TRANSITIONS = {
    "PENDING": {"VERIFIED", "FLAGGED", "DROPPED"},
    "VERIFIED": {"FLAGGED", "DROPPED"},
    "FLAGGED": {"RESOLVED", "DROPPED"},
    "RESOLVED": {"VERIFIED", "FLAGGED", "DROPPED"},
    "DROPPED": {"PENDING", "VERIFIED", "FLAGGED"},
}

VALID_STATUSES = {"PENDING", "VERIFIED", "FLAGGED", "RESOLVED", "DROPPED"}


def validate_transition(
    current_status: str,
    new_status: str,
    has_reason: bool = False,
    is_system: bool = False,
) -> None:
    """Validate a transition between SDV statuses against defined regulatory boundaries.

    Args:
        current_status: The current status of the SDV record.
        new_status: The proposed status of the SDV record.
        has_reason: True if a non-empty reason for change was supplied.
        is_system: True if the transition is triggered automatically by the system.

    Raises:
        SDVStateTransitionError: If the transition is invalid or a required reason is missing.
    """
    # Extract string values if enums are passed
    if hasattr(current_status, "value"):
        current_status = current_status.value
    if hasattr(new_status, "value"):
        new_status = new_status.value

    current_status = str(current_status).upper()
    new_status = str(new_status).upper()

    # Return early when current_status == new_status
    if current_status == new_status:
        return

    # Validate that current status is recognized
    if current_status not in ALLOWED_TRANSITIONS:
        raise SDVStateTransitionError(f"Unsupported status: {current_status}")

    # Validate that new status is recognized
    if new_status not in VALID_STATUSES:
        raise SDVStateTransitionError(f"Invalid target status: {new_status}")

    # Encode the transition graph from Assumptions
    allowed_targets = ALLOWED_TRANSITIONS[current_status]
    if new_status not in allowed_targets:
        raise SDVStateTransitionError(
            f"Invalid transition from {current_status} to {new_status}."
        )

    # Require a meaningful reason_for_change for FLAGGED, RESOLVED, and DROPPED transitions
    if new_status in {"FLAGGED", "RESOLVED", "DROPPED"} and not has_reason:
        raise SDVStateTransitionError(
            f"Transition to {new_status} status requires a meaningful reason for change."
        )
