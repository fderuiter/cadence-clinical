from abc import ABC, abstractmethod

from apps.execution.domain.models import (
    ExecutionAuditLogEntity,
    ExecutionDelegationEntity,
    ExecutionStaffEntity,
)


class IExecutionDOARepository(ABC):
    @abstractmethod
    async def get_staff(
        self, site_id: str, staff_user_id: str
    ) -> ExecutionStaffEntity | None:
        pass

    @abstractmethod
    async def get_staff_by_user_id(
        self, staff_user_id: str
    ) -> ExecutionStaffEntity | None:
        pass

    @abstractmethod
    async def save_staff(self, staff: ExecutionStaffEntity) -> ExecutionStaffEntity:
        pass

    @abstractmethod
    async def get_delegation_by_id(
        self, delegation_id: str
    ) -> ExecutionDelegationEntity | None:
        pass

    @abstractmethod
    async def save_delegation(
        self, delegation: ExecutionDelegationEntity
    ) -> ExecutionDelegationEntity:
        pass

    @abstractmethod
    async def save_audit_log(self, audit: ExecutionAuditLogEntity) -> None:
        pass

    @abstractmethod
    async def get_all_audit_logs(self) -> list[ExecutionAuditLogEntity]:
        pass

    @abstractmethod
    async def get_all_delegations(self) -> list[ExecutionDelegationEntity]:
        pass
