from abc import abstractmethod

from apps.ctms.domain.models import CTMSAuditLogEntity, CTMSDelegationEntity
from packages.hexagonal import RepositoryPort


class ICTMSDelegationRepository(RepositoryPort[CTMSDelegationEntity]):
    """Repository port for CTMS Delegation of Authority (DOA)."""

    @abstractmethod
    async def get_by_site_id(self, site_id: str) -> list[CTMSDelegationEntity]:
        pass

    @abstractmethod
    async def save_audit_log(self, audit: CTMSAuditLogEntity) -> None:
        pass

    @abstractmethod
    async def get_audit_logs_by_site(self, site_id: str) -> list[CTMSAuditLogEntity]:
        pass
