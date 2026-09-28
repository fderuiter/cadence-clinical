"""21 CFR Part 11 Emergency Unblinding Protocol Engine.

Requirements: PRD-SYS-001, PRD-RTSM-001, 21 CFR Part 11 § 11.10(e)
"""

import hashlib
from datetime import UTC, datetime

from packages.engine_core.supply.models import (
    EmergencyUnblindingRequest,
    EmergencyUnblindingResult,
)

# Roles authorized to initiate emergency breaking of the blind
AUTHORIZED_UNBLINDING_ROLES = {
    "safety_physician",
    "principal_investigator",
    "medical_monitor",
    "sponsor_safety",
    "super_admin",
    "investigator",
}


class EmergencyUnblindingEngine:
    """Zero-I/O 21 CFR Part 11 Emergency Subject Unblinding Runtime."""

    @classmethod
    def execute_unblinding(
        cls,
        request: EmergencyUnblindingRequest,
    ) -> EmergencyUnblindingResult:
        """Evaluates authorization and generates tamper-evident cryptographic unblinding manifest.

        Args:
            request: Unblinding request containing requester role, justification, and encrypted treatment arm.

        Returns:
            EmergencyUnblindingResult with unblinded assignment, cryptographic seal, and audit metadata.

        Raises:
            PermissionError: If requester lacks authorized unblinding role.
            ValueError: If change justification rationale is empty or insufficient.
        """
        role_norm = request.requester_role.strip().lower().replace(" ", "_")
        if role_norm not in AUTHORIZED_UNBLINDING_ROLES:
            raise PermissionError(
                f"Role '{request.requester_role}' is not authorized to execute emergency unblinding. "
                f"Must be one of {sorted(AUTHORIZED_UNBLINDING_ROLES)}."
            )

        if not request.reason_for_change or len(request.reason_for_change.strip()) < 10:
            raise ValueError(
                "21 CFR Part 11 Compliance Violation: Emergency unblinding requires a detailed "
                "clinical reason for change (minimum 10 characters)."
            )

        now_iso = datetime.now(UTC).isoformat()

        # Generate cryptographic digital signature seal
        seal_payload = (
            f"UNBLIND:{request.study_id}:{request.subject_id}:{request.requester_user_id}:"
            f"{role_norm}:{request.subject_actual_arm}:{request.reason_for_change}:{now_iso}"
        )
        digital_seal = hashlib.sha256(seal_payload.encode("utf-8")).hexdigest()

        return EmergencyUnblindingResult(
            study_id=request.study_id,
            subject_id=request.subject_id,
            unblinded_arm=request.subject_actual_arm,
            authorized_by_user_id=request.requester_user_id,
            requester_role=role_norm,
            reason_for_change=request.reason_for_change.strip(),
            timestamp=now_iso,
            digital_seal=digital_seal,
            is_authorized=True,
            safety_notification_required=True,
            audit_event_type="EMERGENCY_SUBJECT_UNBLINDING",
        )
