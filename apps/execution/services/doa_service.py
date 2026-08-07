"""Delegation of Authority (DOA) log administration and eSignature sign-off service.

Requirements: PRD-SYS-001
"""

import uuid
from datetime import UTC, datetime

import packages  # noqa: F401
from apps.execution.src.domain.doa_models import (
    DOAAssignmentRecord,
    DOATaskDelegationEnum,
    DOATaskRoleEnum,
)
from packages.security.signature_builder import CryptographicSignatureBuilder


class DOAService:
    """Service administering site personnel DOA task delegation and PI eSignature endorsements.

    Requirements: PRD-SYS-001
    """

    def __init__(self) -> None:
        """Initialize in-memory DOA log store and signature builder."""
        self._store: dict[str, DOAAssignmentRecord] = {}
        self._sig_builder = CryptographicSignatureBuilder()

    def add_assignment(
        self,
        study_id: str,
        site_id: str,
        personnel_name: str,
        personnel_email: str,
        role: DOATaskRoleEnum,
        delegated_tasks: list[DOATaskDelegationEnum],
        start_date: str,
    ) -> DOAAssignmentRecord:
        """Add new site personnel task delegation entry to DOA log.

        Args:
            study_id: Protocol study ID.
            site_id: Site ID.
            personnel_name: Full legal name.
            personnel_email: Email address.
            role: Site role.
            delegated_tasks: List of delegated tasks.
            start_date: Effective start date.

        Returns:
            Registered DOAAssignmentRecord instance.
        """
        rec_id = f"doa_{uuid.uuid4().hex[:8]}"
        record = DOAAssignmentRecord(
            record_id=rec_id,
            study_id=study_id,
            site_id=site_id,
            personnel_name=personnel_name,
            personnel_email=personnel_email,
            role=role,
            delegated_tasks=delegated_tasks,
            start_date=start_date,
            is_active=True,
            signed_off=False,
        )
        self._store[rec_id] = record
        return record

    def sign_off_assignment(
        self,
        record_id: str,
        pi_user_id: str,
        reason_for_change: str,
    ) -> DOAAssignmentRecord:
        """Endorse DOA task delegation record with Principal Investigator eSignature.

        Args:
            record_id: Target DOA assignment record ID.
            pi_user_id: PI user ID.
            reason_for_change: GxP 21 CFR Part 11 justification.

        Returns:
            Updated DOAAssignmentRecord marked as signed_off=True.

        Raises:
            KeyError: If record_id is not found.
        """
        if record_id not in self._store:
            raise KeyError(f"DOA Record '{record_id}' not found.")

        record = self._store[record_id]

        now_iso = datetime.now(UTC).isoformat()
        digest = self._sig_builder.compute_content_digest(record.model_dump())
        self._sig_builder.build_signature_payload(
            user_id=pi_user_id,
            purpose=f"DOA Task Delegation Approval: {reason_for_change}",
            content_digest=digest,
            timestamp_utc=now_iso,
        )

        # Mark as signed off
        record.signed_off = True
        return record

    def get_site_doa_log(
        self, study_id: str, site_id: str
    ) -> list[DOAAssignmentRecord]:
        """Retrieve active Delegation of Authority log entries for site.

        Args:
            study_id: Target study ID.
            site_id: Target site ID.

        Returns:
            List of DOAAssignmentRecord instances.
        """
        return [
            rec
            for rec in self._store.values()
            if rec.study_id == study_id and rec.site_id == site_id
        ]
