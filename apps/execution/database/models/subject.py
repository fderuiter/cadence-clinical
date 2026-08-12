import uuid
from datetime import datetime

from sqlalchemy import JSON, Boolean, DateTime, Integer, String, func
from sqlalchemy.orm import Mapped, mapped_column, validates

from apps.execution.subject_lifecycle import (
    LockedFactorMutationError,
    guard_subject_transition,
    randomize_subject_model,
    unblind_subject_model,
    withdraw_subject_model,
)

from .audit import AuditedModel, Base


class ClinicalSubject(AuditedModel):
    """Represents a pseudonymized clinical subject.

    This class stores subject identification details strictly without storing direct, unencrypted
    personally identifiable information (PII) to comply with HIPAA, GDPR, and GxP standards.
    """

    __tablename__ = "clinical_subjects"

    subject_id: Mapped[str] = mapped_column(String(255), nullable=False)
    study_id: Mapped[str] = mapped_column(String(255), nullable=False)
    site_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    encrypted_demographics: Mapped[str] = mapped_column(String, nullable=True)

    status: Mapped[str] = mapped_column(String(50), default="SCREENING", nullable=False)
    strat_factors: Mapped[dict] = mapped_column(JSON, nullable=True)
    is_unblinded: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    unblinded_at: Mapped[datetime] = mapped_column(DateTime, nullable=True)
    unblinded_by: Mapped[str] = mapped_column(String(255), nullable=True)
    unblinded_reason: Mapped[str] = mapped_column(String(1000), nullable=True)
    unblinded_signature: Mapped[str | None] = mapped_column(String, nullable=True)
    withdrawn_at: Mapped[datetime] = mapped_column(DateTime, nullable=True)
    withdrawal_reason: Mapped[str] = mapped_column(String(1000), nullable=True)
    randomization_id: Mapped[str] = mapped_column(String(36), nullable=True)
    kit_reference: Mapped[str] = mapped_column(String(255), nullable=True)
    enrollment_index: Mapped[int] = mapped_column(Integer, nullable=True)

    # RTSM / Randomization fields
    treatment_group: Mapped[str | None] = mapped_column(String, nullable=True)
    randomization_seed: Mapped[int | None] = mapped_column(Integer, nullable=True)
    investigational_product_id: Mapped[str | None] = mapped_column(
        String(255), nullable=True
    )

    @validates("status")
    def validate_status(self, key, value):
        """Validates that transitions of status obey the allowed-transition guard."""
        curr = getattr(self, "status", None)
        if curr != value:
            guard_subject_transition(curr, value)
        return value

    @validates("strat_factors")
    def validate_strat_factors(self, key, value):
        """Validates that stratification factors are locked and cannot be modified once randomized."""
        curr_status = getattr(self, "status", None)
        if (
            curr_status
            in (
                "RANDOMIZED",
                "ACTIVE",
                "COMPLETED",
                "UNBLINDED",
                "WITHDRAWN",
            )
            and self.strat_factors is not None
            and self.strat_factors != value
        ):
            raise LockedFactorMutationError()
        return value

    def randomize(
        self, randomization_id: str, kit_reference: str, strat_factors: dict
    ) -> None:
        """Assigns randomization details and transitions the subject to the RANDOMIZED state."""
        randomize_subject_model(self, randomization_id, kit_reference, strat_factors)

    def unblind(self, unblinded_by: str, reason: str) -> None:
        """Transitions the subject to the UNBLINDED state and records safety/audit details."""
        unblind_subject_model(self, unblinded_by, reason)

    def withdraw(self, reason: str) -> None:
        """Transitions the subject to the WITHDRAWN state and locks further progression."""
        withdraw_subject_model(self, reason)


class SubjectConsent(AuditedModel):
    """Represents a subject's consent status for a specific protocol version/index."""

    __tablename__ = "subject_consents"

    subject_id: Mapped[str] = mapped_column(String(255), nullable=False)
    study_id: Mapped[str] = mapped_column(String(255), nullable=False)
    site_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    version_tag: Mapped[str] = mapped_column(String(50), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, nullable=False)
    icf_signed: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    icf_signed_date: Mapped[datetime] = mapped_column(DateTime, nullable=True)
    requires_reconsent: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )


class SiteStaffMember(AuditedModel):
    """Represents a clinical trial site staff member for delegation tracking.

    Requirements: PRD-SYS-001
    """

    __tablename__ = "site_staff_members"

    site_id: Mapped[str] = mapped_column(String(255), nullable=False)
    staff_user_id: Mapped[str] = mapped_column(String(255), nullable=False, unique=True)
    name: Mapped[str] = mapped_column(String(255), nullable=False)
    email: Mapped[str] = mapped_column(String(255), nullable=False)
    has_gcp_training: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )


class DOADelegationRecord(AuditedModel):
    """Represents a Delegation of Authority (DOA) task delegation record.

    Requirements: PRD-SYS-001
    """

    __tablename__ = "doa_delegation_records"

    site_id: Mapped[str] = mapped_column(String(255), nullable=False)
    staff_user_id: Mapped[str] = mapped_column(String(255), nullable=False)
    task_code: Mapped[str] = mapped_column(String(50), nullable=False)
    status: Mapped[str] = mapped_column(
        String(50), default="PENDING_PI_APPROVAL", nullable=False
    )
    pi_user_id: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    pi_approved_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    pi_signature_hash: Mapped[str | None] = mapped_column(String(512), nullable=True)
    end_date: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    is_active: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)


class DOAAuditLog(Base):
    """Represents an append-only audit trail for DOA delegation operations.

    Requirements: PRD-SYS-001
    """

    __tablename__ = "doa_audit_logs"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    user_id: Mapped[str] = mapped_column(String(255), nullable=False)
    action: Mapped[str] = mapped_column(String(255), nullable=False)
    details: Mapped[str] = mapped_column(String(1000), nullable=False)
    timestamp: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
