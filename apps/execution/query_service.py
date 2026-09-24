class StateTransitionError(ValueError):
    """Exception raised when an invalid state transition is attempted."""

    pass


class QueryService:
    """Service to handle clinical query state machines and transition validation in eClinical environments."""

    @staticmethod
    def validate_transition(
        current_status: str,
        new_status: str,
        has_reason: bool = False,
        is_system: bool = False,
    ) -> None:
        """Validate a transition between query statuses against defined regulatory boundaries.

        Args:
            current_status: The current status of the clinical query.
            new_status: The proposed status of the clinical query.
            has_reason: True if a non-empty comment/reason was supplied.
            is_system: True if the transition is triggered automatically by the system.

        Raises:
            StateTransitionError: If the transition is invalid or a required reason is missing.
        """
        current_status = current_status.upper()
        new_status = new_status.upper()
        if current_status == new_status:
            return

        if new_status == "CANCELLED":
            if current_status in ("CLOSED", "CANCELLED"):
                raise StateTransitionError(
                    f"Cannot cancel a query in {current_status} status."
                )
            if not has_reason:
                raise StateTransitionError("Cancellation requires a non-empty reason.")
            return

        if current_status == "NONE":
            if new_status not in ("CANDIDATE", "OPEN"):
                raise StateTransitionError(
                    f"Invalid initial transition from NONE to {new_status}."
                )
            return

        if current_status == "CANDIDATE":
            if new_status not in ("OPEN", "CANCELLED"):
                raise StateTransitionError(
                    f"Invalid transition from CANDIDATE to {new_status}."
                )
            return

        if current_status == "OPEN":
            allowed = ["ANSWERED", "CANCELLED"]
            if is_system:
                allowed.append("CLOSED")
            if new_status not in allowed:
                raise StateTransitionError(
                    f"Invalid transition from OPEN to {new_status}."
                )
            return

        if current_status == "REOPENED":
            if new_status not in ("ANSWERED", "CANCELLED"):
                raise StateTransitionError(
                    f"Invalid transition from REOPENED to {new_status}."
                )
            return

        if current_status == "ANSWERED":
            if new_status not in ("CLOSED", "REOPENED", "CANCELLED"):
                raise StateTransitionError(
                    f"Invalid transition from ANSWERED to {new_status}."
                )
            if new_status == "REOPENED" and not has_reason:
                raise StateTransitionError("Rejection requires a non-empty reason.")
            return

        if current_status == "CLOSED":
            if new_status not in ("REOPENED",):
                raise StateTransitionError(
                    f"Invalid transition from CLOSED to {new_status}."
                )
            return

        raise StateTransitionError(
            f"Unsupported transition from {current_status} to {new_status}."
        )
