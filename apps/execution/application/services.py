import hashlib
from datetime import UTC, datetime

from apps.execution.application.ports import IExecutionDOARepository
from apps.execution.domain.exceptions import (
    ExecutionDelegationNotFoundError,
    ExecutionStaffNotFoundError,
    ExecutionValidationError,
)
from apps.execution.domain.models import (
    ExecutionAuditLogEntity,
    ExecutionDelegationEntity,
    ExecutionStaffEntity,
)


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
