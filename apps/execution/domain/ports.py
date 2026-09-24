from abc import abstractmethod

from apps.execution.domain.models import (
    AuditLogDomain,
    ClinicalSubjectDomain,
    ConsentFormRecordDomain,
    ConsentSignatureDomain,
    ExecutionAuditLogEntity,
    ExecutionDelegationEntity,
    ExecutionStaffEntity,
    SubjectConsentDomain,
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

    @abstractmethod
    async def get_by_subject_code(
        self, subject_id: str, study_id: str | None = None
    ) -> ClinicalSubjectDomain | None:
        """Retrieve subject by domain subject identifier string."""
        pass

    @abstractmethod
    async def get_max_enrollment_index(self, study_id: str) -> int:
        """Get the highest current enrollment index in the study."""
        pass

    @abstractmethod
    async def list_by_study(
        self, study_id: str, site_id: str | None = None
    ) -> list[ClinicalSubjectDomain]:
        """List subjects for a study and optional site."""
        pass


class IConsentRepository(RepositoryPort[ConsentSignatureDomain]):
    """Repository port for ConsentSignatureDomain and SubjectConsentDomain."""

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

    @abstractmethod
    async def get_subject_consent(
        self, subject_id: str, study_id: str, version_index: int
    ) -> SubjectConsentDomain | None:
        """Retrieve subject consent for a specific study version index."""
        pass

    @abstractmethod
    async def save_subject_consent(
        self, consent: SubjectConsentDomain
    ) -> SubjectConsentDomain:
        """Persist or update subject consent record."""
        pass

    @abstractmethod
    async def list_subject_consents(
        self, subject_id: str, study_id: str
    ) -> list[SubjectConsentDomain]:
        """List all consent records for a subject in a study."""
        pass


class IAuditRepository(RepositoryPort[AuditLogDomain]):
    """Repository port for AuditLogDomain."""

    @abstractmethod
    async def save_audit_log(self, audit: AuditLogDomain) -> AuditLogDomain:
        """Persist a 21 CFR Part 11 immutable audit log entry."""
        pass

    @abstractmethod
    async def get_all_audit_logs(self) -> list[AuditLogDomain]:
        """Retrieve all recorded audit log entries."""
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
