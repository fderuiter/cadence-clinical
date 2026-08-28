import hashlib
from datetime import UTC, datetime

from apps.execution.application.ports import IExecutionDOARepository
from apps.execution.demographics import encrypt_demographics
from apps.execution.domain.exceptions import (
    ExecutionDelegationNotFoundError,
    ExecutionStaffNotFoundError,
    ExecutionValidationError,
    SubjectNotFoundError,
)
from apps.execution.domain.models import (
    AuditLogDomain,
    ClinicalSubjectDomain,
    ExecutionAuditLogEntity,
    ExecutionDelegationEntity,
    ExecutionStaffEntity,
    SubjectConsentDomain,
)
from apps.execution.domain.ports import (
    IAuditRepository,
    IConsentRepository,
    ISubjectRepository,
)


class SubjectLifecycleService:
    """Deep domain service encapsulating Clinical Subject & Informed Consent lifecycle logic.

    Requirements:
    - @req:PRD-SYS-001 (21 CFR Part 11 Audit Trail & Electronic Records)
    - @req:PRD-SYS-042 (Subject State Machine Advance on Enrollment)
    - @req:PRD-SYS-121 (Sovereign Site Mutation Authorization)
    - @req:PRD-SUB-001 (Clinical Subject Lifecycle Management)
    """

    def __init__(
        self,
        subject_repo: ISubjectRepository,
        consent_repo: IConsentRepository,
        audit_repo: IAuditRepository,
    ) -> None:
        self.subject_repo = subject_repo
        self.consent_repo = consent_repo
        self.audit_repo = audit_repo

    async def enroll_subject(
        self,
        study_id: str,
        site_id: str,
        subject_id: str,
        arm_id: str | None = None,
        demographics: dict | None = None,
        user_id: str = "system",
        reason: str = "Initial Subject Enrollment",
    ) -> ClinicalSubjectDomain:
        encrypted_demo = None
        if demographics is not None:
            if isinstance(demographics, dict):
                encrypted_demo = encrypt_demographics(demographics)
            elif isinstance(demographics, str):
                encrypted_demo = demographics

        max_idx = await self.subject_repo.get_max_enrollment_index(study_id)
        next_idx = 0 if max_idx < 0 else max_idx + 1

        subject = ClinicalSubjectDomain(
            subject_id=subject_id,
            study_id=study_id,
            site_id=site_id,
            treatment_group=arm_id,
            encrypted_demographics=encrypted_demo,
            enrollment_index=next_idx,
            status="SCREENING",
            created_by=user_id,
        )
        saved = await self.subject_repo.save(subject)

        audit = AuditLogDomain(
            table_name="clinical_subjects",
            record_id=saved.id,
            action="ENROLL_SUBJECT",
            user_id=user_id,
            change_reason=reason,
            timestamp=datetime.now(UTC),
            new_values={
                "subject_id": subject_id,
                "study_id": study_id,
                "site_id": site_id,
                "treatment_group": arm_id,
                "enrollment_index": next_idx,
                "status": "SCREENING",
            },
        )
        await self.audit_repo.save_audit_log(audit)
        return saved

    async def record_consent(
        self,
        subject_id: str,
        study_id: str,
        site_id: str | None,
        version_tag: str,
        version_index: int,
        icf_signed: bool = True,
        icf_signed_date: datetime | None = None,
        requires_reconsent: bool = False,
        is_paper_override: bool = False,
        protocol_version: str | None = None,
        user_id: str = "system",
        reason: str = "Record Subject Consent",
    ) -> SubjectConsentDomain:
        existing = await self.consent_repo.get_subject_consent(
            subject_id=subject_id,
            study_id=study_id,
            version_index=version_index,
        )

        signed_date = icf_signed_date or datetime.now(UTC)

        if existing:
            existing.version_tag = version_tag
            existing.icf_signed = icf_signed
            if icf_signed_date:
                existing.icf_signed_date = icf_signed_date
            elif not existing.icf_signed_date:
                existing.icf_signed_date = signed_date
            existing.requires_reconsent = requires_reconsent
            existing.is_paper_override = is_paper_override
            if protocol_version:
                existing.protocol_version = protocol_version
            consent_to_save = existing
        else:
            consent_to_save = SubjectConsentDomain(
                subject_id=subject_id,
                study_id=study_id,
                site_id=site_id,
                version_tag=version_tag,
                version_index=version_index,
                protocol_version=protocol_version,
                icf_signed=icf_signed,
                icf_signed_date=signed_date,
                requires_reconsent=requires_reconsent,
                is_paper_override=is_paper_override,
                created_by=user_id,
            )

        if (icf_signed or is_paper_override) and not requires_reconsent:
            others = await self.consent_repo.list_subject_consents(subject_id, study_id)
            for other in others:
                if other.id != consent_to_save.id and other.requires_reconsent:
                    other.requires_reconsent = False
                    await self.consent_repo.save_subject_consent(other)

        saved = await self.consent_repo.save_subject_consent(consent_to_save)

        audit = AuditLogDomain(
            table_name="subject_consents",
            record_id=saved.id,
            action="RECORD_CONSENT",
            user_id=user_id,
            change_reason=reason,
            timestamp=datetime.now(UTC),
            new_values={
                "subject_id": subject_id,
                "study_id": study_id,
                "version_tag": version_tag,
                "version_index": version_index,
                "icf_signed": icf_signed,
                "requires_reconsent": requires_reconsent,
                "is_paper_override": is_paper_override,
            },
        )
        await self.audit_repo.save_audit_log(audit)
        return saved

    async def advance_subject_state(
        self,
        subject_id: str,
        target_state: str,
        user_id: str = "system",
        reason: str = "Subject State Transition",
    ) -> ClinicalSubjectDomain:
        subject = await self.subject_repo.get_by_id(subject_id)
        if not subject:
            subject = await self.subject_repo.get_by_subject_code(subject_id)
        if not subject:
            raise SubjectNotFoundError(f"Subject {subject_id} not found.")

        old_state = subject.status
        subject.status = target_state
        subject.updated_by = user_id
        subject.updated_at = datetime.now(UTC)

        saved = await self.subject_repo.save(subject)

        audit = AuditLogDomain(
            table_name="clinical_subjects",
            record_id=saved.id,
            action=f"TRANSITION_{target_state}",
            user_id=user_id,
            change_reason=reason,
            timestamp=datetime.now(UTC),
            old_values={"status": old_state},
            new_values={"status": target_state},
        )
        await self.audit_repo.save_audit_log(audit)
        return saved

    async def update_demographics(
        self,
        subject_id: str,
        demographics: dict | str,
        user_id: str = "system",
        reason: str = "Demographics Update",
    ) -> ClinicalSubjectDomain:
        subject = await self.subject_repo.get_by_id(subject_id)
        if not subject:
            subject = await self.subject_repo.get_by_subject_code(subject_id)
        if not subject:
            raise SubjectNotFoundError(f"Subject {subject_id} not found.")

        if isinstance(demographics, dict):
            encrypted = encrypt_demographics(demographics)
        else:
            encrypted = demographics

        subject.encrypted_demographics = encrypted
        subject.updated_by = user_id
        subject.updated_at = datetime.now(UTC)

        saved = await self.subject_repo.save(subject)

        audit = AuditLogDomain(
            table_name="clinical_subjects",
            record_id=saved.id,
            action="UPDATE_DEMOGRAPHICS",
            user_id=user_id,
            change_reason=reason,
            timestamp=datetime.now(UTC),
        )
        await self.audit_repo.save_audit_log(audit)
        return saved


class ExecutionDOAUseCase:
    def __init__(self, repository: IExecutionDOARepository):
        self.repository = repository

    async def delegate_task(
        self,
        site_id: str,
        staff_user_id: str,
        task_code: str,
        pi_user_id: str,
        reason_for_change: str,
    ) -> ExecutionDelegationEntity:
        staff = await self.repository.get_staff(site_id, staff_user_id)
        if not staff:
            raise ExecutionStaffNotFoundError(
                f"Site staff member {staff_user_id} not found at site {site_id}."
            )

        if not staff.has_gcp_training:
            raise ExecutionValidationError(
                f"Staff member {staff_user_id} has not completed required GCP training."
            )

        delegation = ExecutionDelegationEntity(
            site_id=site_id,
            staff_user_id=staff_user_id,
            task_code=task_code,
            pi_user_id=pi_user_id,
            status="PENDING_PI_APPROVAL",
            reason_for_change=reason_for_change,
            is_active=True,
        )
        saved = await self.repository.save_delegation(delegation)

        # Log audit
        audit = ExecutionAuditLogEntity(
            user_id=pi_user_id,
            action="DELEGATE_TASK",
            details=f"Delegated task {task_code} to staff {staff_user_id} at site {site_id}. Reason: {reason_for_change}",
            timestamp=datetime.now(UTC),
        )
        await self.repository.save_audit_log(audit)
        return saved

    async def approve_delegation(
        self,
        delegation_id: str,
        pi_user_id: str,
    ) -> ExecutionDelegationEntity:
        record = await self.repository.get_delegation_by_id(delegation_id)
        if not record:
            raise ExecutionDelegationNotFoundError(
                f"Delegation record {delegation_id} not found."
            )

        now = datetime.now(UTC)
        verification_payload = f"{delegation_id}:{pi_user_id}:{now.isoformat()}"
        verification_hash = hashlib.sha256(
            verification_payload.encode("utf-8")
        ).hexdigest()

        record.status = "ACTIVE"
        record.pi_approved_at = now
        record.pi_signature_hash = verification_hash
        record.reason_for_change = "PI Delegation Approval"

        saved = await self.repository.save_delegation(record)

        audit = ExecutionAuditLogEntity(
            user_id=pi_user_id,
            action="APPROVE_DELEGATION",
            details=f"Approved delegation {delegation_id} for staff {record.staff_user_id}. Approved by PI {pi_user_id}.",
            timestamp=now,
        )
        await self.repository.save_audit_log(audit)
        return saved

    async def approve_task_via_hash(
        self,
        delegation_id: str,
        pi_user_id: str,
        signature_hash: str,
        reason_for_change: str,
    ) -> ExecutionDelegationEntity:
        record = await self.repository.get_delegation_by_id(delegation_id)
        if not record:
            raise ExecutionDelegationNotFoundError(
                f"Delegation record {delegation_id} not found."
            )

        now = datetime.now(UTC)
        record.status = "ACTIVE"
        record.pi_approved_at = now
        record.pi_signature_hash = signature_hash
        record.reason_for_change = reason_for_change

        saved = await self.repository.save_delegation(record)

        audit = ExecutionAuditLogEntity(
            user_id=pi_user_id,
            action="APPROVE_DELEGATION",
            details=f"Approved delegation {delegation_id} via hash. Reason: {reason_for_change}",
            timestamp=now,
        )
        await self.repository.save_audit_log(audit)
        return saved

    async def revoke_delegation(
        self,
        delegation_id: str,
        end_date: datetime,
        reason_for_change: str,
    ) -> ExecutionDelegationEntity:
        record = await self.repository.get_delegation_by_id(delegation_id)
        if not record:
            raise ExecutionDelegationNotFoundError(
                f"Delegation record {delegation_id} not found."
            )

        record.status = "REVOKED"
        record.end_date = end_date
        record.is_active = False
        record.reason_for_change = reason_for_change

        saved = await self.repository.save_delegation(record)

        audit = ExecutionAuditLogEntity(
            user_id=record.pi_user_id,
            action="REVOKE_DELEGATION",
            details=f"Revoked delegation {delegation_id} with end date {end_date.isoformat()}. Reason: {reason_for_change}",
            timestamp=datetime.now(UTC),
        )
        await self.repository.save_audit_log(audit)
        return saved

    async def create_or_update_staff(
        self,
        site_id: str,
        staff_user_id: str,
        name: str,
        email: str,
        has_gcp_training: bool,
    ) -> ExecutionStaffEntity:
        existing = await self.repository.get_staff_by_user_id(staff_user_id)
        if existing:
            existing.site_id = site_id
            existing.name = name
            existing.email = email
            existing.has_gcp_training = has_gcp_training
            return await self.repository.save_staff(existing)

        staff = ExecutionStaffEntity(
            site_id=site_id,
            staff_user_id=staff_user_id,
            name=name,
            email=email,
            has_gcp_training=has_gcp_training,
        )
        return await self.repository.save_staff(staff)

    async def get_audit_logs(self) -> list[ExecutionAuditLogEntity]:
        return await self.repository.get_all_audit_logs()

    async def get_delegations(self) -> list[ExecutionDelegationEntity]:
        return await self.repository.get_all_delegations()
