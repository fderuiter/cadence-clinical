from abc import abstractmethod

from apps.execution.domain.models import (
    AuditLogDomain,
    ClinicalSubjectDomain,
    ConsentFormRecordDomain,
    ConsentSignatureDomain,
    ExecutionAuditLogEntity,
    ExecutionDelegationEntity,
    ExecutionStaffEntity,
)
from apps.execution.econsent_client import (
    IConsentClient,
    IConsentVerificationClient,
)
from packages.hexagonal import RepositoryPort

__all__ = [
    "ISubjectRepository",
    "IConsentRepository",
    "IAuditRepository",
    "IExecutionDOARepository",
    "IConsentClient",
    "IConsentVerificationClient",
    "SubjectRepositoryPort",
    "ConsentRepositoryPort",
    "AuditRepositoryPort",
]


class ISubjectRepository(RepositoryPort[ClinicalSubjectDomain]):
    """Repository port for ClinicalSubjectDomain."""

    pass


class IConsentRepository(RepositoryPort[ConsentSignatureDomain]):
    """Repository port for ConsentSignatureDomain."""

    @abstractmethod
    async def get_signature_by_id(
        self, record_id: str
    ) -> ConsentSignatureDomain | None:
        pass

    @abstractmethod
    async def save_signature(
        self, signature: ConsentSignatureDomain
    ) -> ConsentSignatureDomain:
        pass

    @abstractmethod
    async def get_form_record_by_id(
        self, record_id: str
    ) -> ConsentFormRecordDomain | None:
        pass

    @abstractmethod
    async def save_form_record(
        self, record: ConsentFormRecordDomain
    ) -> ConsentFormRecordDomain:
        pass


class IAuditRepository(RepositoryPort[AuditLogDomain]):
    """Repository port for AuditLogDomain."""

    pass


class IExecutionDOARepository(RepositoryPort[ExecutionDelegationEntity]):
    """Repository port for Execution Delegation of Authority."""

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


SubjectRepositoryPort = ISubjectRepository
ConsentRepositoryPort = IConsentRepository
AuditRepositoryPort = IAuditRepository
