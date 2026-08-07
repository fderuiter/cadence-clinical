import uuid
from datetime import datetime

from pydantic import BaseModel

from apps.execution.subject_lifecycle import (
    LockedFactorMutationError,
    guard_subject_transition,
    randomize_subject_model,
    unblind_subject_model,
    withdraw_subject_model,
)


class ClinicalSubjectDomain:
    """Pure Python clinical subject domain model."""

    def __init__(
        self,
        id: str | None = None,
        subject_id: str | None = None,
        study_id: str | None = None,
        site_id: str | None = None,
        encrypted_demographics: str | None = None,
        status: str = "SCREENING",
        strat_factors: dict | None = None,
        is_unblinded: bool = False,
        unblinded_at: datetime | None = None,
        unblinded_by: str | None = None,
        unblinded_reason: str | None = None,
        unblinded_signature: str | None = None,
        withdrawn_at: datetime | None = None,
        withdrawal_reason: str | None = None,
        randomization_id: str | None = None,
        kit_reference: str | None = None,
        enrollment_index: int | None = None,
        treatment_group: str | None = None,
        randomization_seed: int | None = None,
        investigational_product_id: str | None = None,
        version: int = 1,
        is_deleted: bool = False,
        created_at: datetime | None = None,
        created_by: str | None = None,
        updated_at: datetime | None = None,
        updated_by: str | None = None,
    ):
        self.id = id or str(uuid.uuid4())
        self.subject_id = subject_id
        self.study_id = study_id
        self.site_id = site_id
        self.encrypted_demographics = encrypted_demographics
        self._status = status
        self._strat_factors = strat_factors
        self.is_unblinded = is_unblinded
        self.unblinded_at = unblinded_at
        self.unblinded_by = unblinded_by
        self.unblinded_reason = unblinded_reason
        self.unblinded_signature = unblinded_signature
        self.withdrawn_at = withdrawn_at
        self.withdrawal_reason = withdrawal_reason
        self.randomization_id = randomization_id
        self.kit_reference = kit_reference
        self.enrollment_index = enrollment_index
        self.treatment_group = treatment_group
        self.randomization_seed = randomization_seed
        self.investigational_product_id = investigational_product_id
        self.version = version
        self.is_deleted = is_deleted
        self.created_at = created_at or datetime.now()
        self.created_by = created_by
        self.updated_at = updated_at
        self.updated_by = updated_by

    @property
    def status(self) -> str:
        return self._status

    @status.setter
    def status(self, value: str) -> None:
        if hasattr(self, "_status") and self._status != value:
            guard_subject_transition(self._status, value)
        self._status = value

    @property
    def strat_factors(self) -> dict | None:
        return self._strat_factors

    @strat_factors.setter
    def strat_factors(self, value: dict | None) -> None:
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
            and hasattr(self, "_strat_factors")
            and self._strat_factors is not None
            and self._strat_factors != value
        ):
            raise LockedFactorMutationError()
        self._strat_factors = value

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


class ConsentSignatureDomain:
    """Pure Python consent signature domain model with GxP immutability."""

    def __init__(
        self,
        id: str | None = None,
        subject_id: str | None = None,
        site_id: str | None = None,
        icf_version_id: str | None = None,
        printed_name: str | None = None,
        signature_svg_data: str | None = None,
        signature_svg: str | None = None,
        otp_auth_code: str | None = None,
        meaning: str = "I agree to participate in this research study",
        cryptographic_token: str | None = None,
        verification_hash: str | None = None,
        signed_at: datetime | None = None,
        timestamp: datetime | None = None,
        status: str = "SIGNED",
        version: int = 1,
        is_deleted: bool = False,
        created_at: datetime | None = None,
        created_by: str | None = None,
        updated_at: datetime | None = None,
        updated_by: str | None = None,
        reason_for_change: str | None = None,
    ):
        object.__setattr__(self, "_initialized", False)
        self.id = id or str(uuid.uuid4())
        self.subject_id = subject_id
        self.site_id = site_id
        self.icf_version_id = icf_version_id
        self.printed_name = printed_name
        self.signature_svg_data = signature_svg_data
        self.signature_svg = signature_svg
        self.otp_auth_code = otp_auth_code
        self.meaning = meaning
        self.cryptographic_token = cryptographic_token
        self.verification_hash = verification_hash
        self.signed_at = signed_at
        self.timestamp = timestamp or datetime.now()
        self.status = status
        self.version = version
        self.is_deleted = is_deleted
        self.created_at = created_at or datetime.now()
        self.created_by = created_by
        self.updated_at = updated_at
        self.updated_by = updated_by
        self.reason_for_change = reason_for_change
        object.__setattr__(self, "_initialized", True)

    def __setattr__(self, key, value):
        if getattr(self, "_initialized", False):
            raise ValueError("Cannot modify signed consent records")
        object.__setattr__(self, key, value)

    def __delattr__(self, item):
        raise ValueError("Cannot delete consent records")


class ConsentFormRecordDomain:
    """Pure Python consent form record domain model with GxP immutability."""

    def __init__(
        self,
        id: str | None = None,
        subject_id: str | None = None,
        site_id: str | None = None,
        icf_version_id: str | None = None,
        printed_name: str | None = None,
        relationship_to_subject: str | None = None,
        signature_svg: str | None = None,
        otp_auth_code: str | None = None,
        status: str = "PENDING",
        signed_at: datetime | None = None,
        is_verified: bool = False,
        version: int = 1,
        is_deleted: bool = False,
        created_at: datetime | None = None,
        created_by: str | None = None,
        updated_at: datetime | None = None,
        updated_by: str | None = None,
    ):
        object.__setattr__(self, "_initialized", False)
        self.id = id or str(uuid.uuid4())
        self.subject_id = subject_id
        self.site_id = site_id
        self.icf_version_id = icf_version_id
        self.printed_name = printed_name
        self.relationship_to_subject = relationship_to_subject
        self.signature_svg = signature_svg
        self.otp_auth_code = otp_auth_code
        self.status = status
        self.signed_at = signed_at
        self.is_verified = is_verified
        self.version = version
        self.is_deleted = is_deleted
        self.created_at = created_at or datetime.now()
        self.created_by = created_by
        self.updated_at = updated_at
        self.updated_by = updated_by
        object.__setattr__(self, "_initialized", True)

    def __setattr__(self, key, value):
        if getattr(self, "_initialized", False):
            current_status = getattr(self, "status", None)
            if current_status == "SIGNED":
                if key == "status" and value == "RECONSENT_REQUIRED":
                    # Transition to RECONSENT_REQUIRED is allowed
                    pass
                else:
                    raise ValueError("Cannot modify signed consent records")
            elif current_status == "RECONSENT_REQUIRED":
                if (
                    key in ("subject_id", "icf_version_id")
                    and getattr(self, key, None) != value
                ):
                    raise ValueError("Cannot modify signed consent records")
        object.__setattr__(self, key, value)

    def __delattr__(self, item):
        raise ValueError("Cannot delete consent records")


class AuditLogDomain:
    """Pure Python safety audit log domain model with GxP immutability."""

    def __init__(
        self,
        id: str | None = None,
        table_name: str | None = None,
        record_id: str | None = None,
        action: str | None = None,
        user_id: str | None = None,
        ip_address: str | None = None,
        timestamp: datetime | None = None,
        old_values: dict | None = None,
        new_values: dict | None = None,
        version_index: int = 1,
        change_reason: str | None = None,
        cryptographic_seal: str | None = None,
    ):
        object.__setattr__(self, "_initialized", False)
        self.id = id or str(uuid.uuid4())
        self.table_name = table_name
        self.record_id = record_id
        self.action = action
        self.user_id = user_id
        self.ip_address = ip_address
        self.timestamp = timestamp or datetime.now()
        self.old_values = old_values
        self.new_values = new_values
        self.version_index = version_index
        self.change_reason = change_reason
        self.cryptographic_seal = cryptographic_seal
        object.__setattr__(self, "_initialized", True)

    def __setattr__(self, key, value):
        if getattr(self, "_initialized", False):
            raise ValueError("Audit logs are append-only and cannot be modified")
        object.__setattr__(self, key, value)

    def __delattr__(self, item):
        raise ValueError(
            "Deletion of AuditLog is strictly forbidden to comply with 21 CFR Part 11."
        )


class ExecutionStaffEntity(BaseModel):
    id: str | None = None
    site_id: str
    staff_user_id: str
    name: str
    email: str
    has_gcp_training: bool


class ExecutionDelegationEntity(BaseModel):
    id: str | None = None
    site_id: str
    staff_user_id: str
    task_code: str
    pi_user_id: str | None = None
    status: str
    pi_signature_hash: str | None = None
    pi_approved_at: datetime | None = None
    end_date: datetime | None = None
    reason_for_change: str | None = None
    is_active: bool


class ExecutionAuditLogEntity(BaseModel):
    id: str | None = None
    user_id: str | None = None
    action: str
    details: str
    timestamp: datetime
