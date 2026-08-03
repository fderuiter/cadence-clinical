"""Unit tests for the SDV State Machine transition validator."""

import pytest

from apps.execution.database.models import SDVStatus
from apps.execution.sdv_state_machine import (
    SDVStateTransitionError,
    validate_transition,
)


def test_sdv_state_machine_early_return():
    """Verify that validating transition to the same state returns early.

    @req:PRD-QRY-005
    """
    # Should not raise any error, even if has_reason is False and target is DROPPED/RESOLVED/FLAGGED
    validate_transition("PENDING", "PENDING", has_reason=False)
    validate_transition("VERIFIED", "VERIFIED", has_reason=False)
    validate_transition("FLAGGED", "FLAGGED", has_reason=False)
    validate_transition("RESOLVED", "RESOLVED", has_reason=False)
    validate_transition("DROPPED", "DROPPED", has_reason=False)


def test_sdv_state_machine_normalization():
    """Verify that state names are normalized to upper-case correctly.

    @req:PRD-QRY-005
    """
    # Lowercase inputs should work and transition correctly
    validate_transition("pending", "verified")


def test_sdv_state_machine_enum_support():
    """Verify that SDVStatus enum values are supported as inputs.

    @req:PRD-QRY-005
    """
    # Using SDVStatus enum objects directly
    validate_transition(SDVStatus.PENDING, SDVStatus.VERIFIED)


def test_sdv_state_machine_allowed_transitions():
    """Verify all allowed transitions in the transition graph.

    @req:PRD-QRY-005
    """
    # PENDING transitions
    validate_transition("PENDING", "VERIFIED")
    validate_transition("PENDING", "FLAGGED", has_reason=True)
    validate_transition("PENDING", "DROPPED", has_reason=True)

    # VERIFIED transitions
    validate_transition("VERIFIED", "FLAGGED", has_reason=True)
    validate_transition("VERIFIED", "DROPPED", has_reason=True)

    # FLAGGED transitions
    validate_transition("FLAGGED", "RESOLVED", has_reason=True)
    validate_transition("FLAGGED", "DROPPED", has_reason=True)

    # RESOLVED transitions
    validate_transition("RESOLVED", "VERIFIED")
    validate_transition("RESOLVED", "FLAGGED", has_reason=True)
    validate_transition("RESOLVED", "DROPPED", has_reason=True)

    # DROPPED transitions
    validate_transition("DROPPED", "PENDING")
    validate_transition("DROPPED", "VERIFIED")
    validate_transition("DROPPED", "FLAGGED", has_reason=True)


def test_sdv_state_machine_disallowed_transitions():
    """Verify that disallowed transitions raise SDVStateTransitionError.

    @req:PRD-QRY-005
    """
    # PENDING invalid transitions
    with pytest.raises(SDVStateTransitionError) as exc_info:
        validate_transition("PENDING", "RESOLVED", has_reason=True)
    assert "Invalid transition" in str(exc_info.value)

    # VERIFIED invalid transitions
    with pytest.raises(SDVStateTransitionError):
        validate_transition("VERIFIED", "PENDING")
    with pytest.raises(SDVStateTransitionError):
        validate_transition("VERIFIED", "RESOLVED", has_reason=True)

    # FLAGGED invalid transitions
    with pytest.raises(SDVStateTransitionError):
        validate_transition("FLAGGED", "PENDING")
    with pytest.raises(SDVStateTransitionError):
        validate_transition("FLAGGED", "VERIFIED")

    # RESOLVED invalid transitions
    with pytest.raises(SDVStateTransitionError):
        validate_transition("RESOLVED", "PENDING")

    # DROPPED invalid transitions
    with pytest.raises(SDVStateTransitionError):
        validate_transition("DROPPED", "RESOLVED", has_reason=True)


def test_sdv_state_machine_reason_requirements():
    """Verify that transition to FLAGGED, RESOLVED, and DROPPED requires has_reason.

    @req:PRD-QRY-005
    """
    # Transition to FLAGGED without reason
    with pytest.raises(SDVStateTransitionError) as exc_info:
        validate_transition("PENDING", "FLAGGED", has_reason=False)
    assert "requires a meaningful reason" in str(exc_info.value)

    # Transition to DROPPED without reason
    with pytest.raises(SDVStateTransitionError) as exc_info:
        validate_transition("VERIFIED", "DROPPED", has_reason=False)
    assert "requires a meaningful reason" in str(exc_info.value)

    # Transition to RESOLVED without reason
    with pytest.raises(SDVStateTransitionError) as exc_info:
        validate_transition("FLAGGED", "RESOLVED", has_reason=False)
    assert "requires a meaningful reason" in str(exc_info.value)


def test_sdv_state_machine_unsupported_statuses():
    """Verify that unsupported or invalid state inputs raise errors.

    @req:PRD-QRY-005
    """
    with pytest.raises(SDVStateTransitionError) as exc_info:
        validate_transition("INVALID_CURRENT", "VERIFIED")
    assert "Unsupported status" in str(exc_info.value)

    with pytest.raises(SDVStateTransitionError) as exc_info:
        validate_transition("PENDING", "INVALID_TARGET")
    assert "Invalid target status" in str(exc_info.value)
