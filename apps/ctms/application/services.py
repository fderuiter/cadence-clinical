from datetime import UTC, datetime

from apps.ctms.application.ports import ICTMSDelegationRepository
from apps.ctms.domain.exceptions import CTMSDelegationNotFoundError
from apps.ctms.domain.models import CTMSAuditLogEntity, CTMSDelegationEntity


class CTMSDelegationUseCase:
    def __init__(self, repository: ICTMSDelegationRepository):
        self.repository = repository

    async def delegate_site_tasks(
        self,
        site_id: str,
        staff_user_id: str,
        task_codes: list[str],
        start_date: str,
        created_by: str,
        reason_for_change: str,
        user_roles: str,
    ) -> CTMSDelegationEntity:
        delegation = CTMSDelegationEntity(
            site_id=site_id,
            staff_user_id=staff_user_id,
            task_codes=task_codes,
            start_date=start_date,
            is_active=False,
            signed_off=False,
            created_by=created_by,
            reason_for_change=reason_for_change,
            version_index=1,
        )
        saved = await self.repository.save(delegation)

        # Log audit entry
        audit = CTMSAuditLogEntity(
            user_id=created_by,
            user_role=user_roles,
            action="DOA_LOG_MODIFIED",
            details=f"Delegated tasks {task_codes} to staff {staff_user_id} at site {site_id}. Status: PENDING_PI_APPROVAL. Reason: {reason_for_change}",
            timestamp=datetime.now(UTC).isoformat(),
        )
        await self.repository.save_audit_log(audit)
        return saved

    async def revoke_site_tasks(
        self,
        record_id: str,
        user_id: str,
        user_role: str,
        reason_for_change: str,
    ) -> CTMSDelegationEntity:
        delegation = await self.repository.get_by_id(record_id)
        if not delegation:
            raise CTMSDelegationNotFoundError(
                f"Delegation record {record_id} not found"
            )

        delegation.is_active = False
        delegation.end_date = datetime.now(UTC).date().isoformat()
        delegation.version_index += 1
        delegation.reason_for_change = reason_for_change

        saved = await self.repository.save(delegation)

        # Log audit entry
        audit = CTMSAuditLogEntity(
            user_id=user_id,
            user_role=user_role,
            action="DOA_LOG_MODIFIED",
            details=f"Revoked delegation {record_id} for site {delegation.site_id}. Reason: {reason_for_change}",
            timestamp=datetime.now(UTC).isoformat(),
        )
        await self.repository.save_audit_log(audit)
        return saved

    async def sign_off_delegation(
        self,
        record_id: str,
        user_id: str,
        user_role: str,
        reason_for_change: str,
    ) -> CTMSDelegationEntity:
        delegation = await self.repository.get_by_id(record_id)
        if not delegation:
            raise CTMSDelegationNotFoundError(
                f"Delegation record {record_id} not found"
            )

        delegation.signed_off = True
        delegation.is_active = True
        delegation.version_index += 1
        delegation.reason_for_change = reason_for_change

        saved = await self.repository.save(delegation)

        # Log audit entry
        audit = CTMSAuditLogEntity(
            user_id=user_id,
            user_role=user_role,
            action="DOA_LOG_MODIFIED",
            details=f"Signed off and activated delegation {record_id} for site {delegation.site_id}. Reason: {reason_for_change}",
            timestamp=datetime.now(UTC).isoformat(),
        )
        await self.repository.save_audit_log(audit)
        return saved

    async def get_site_doa_log(
        self, site_id: str
    ) -> tuple[list[CTMSDelegationEntity], list[CTMSAuditLogEntity]]:
        delegations = await self.repository.get_by_site_id(site_id)
        audit_logs = await self.repository.get_audit_logs_by_site(site_id)
        return delegations, audit_logs
