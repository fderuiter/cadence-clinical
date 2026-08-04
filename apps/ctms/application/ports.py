from abc import ABC, abstractmethod

from apps.ctms.domain.models import CTMSAuditLogEntity, CTMSDelegationEntity


class ICTMSDelegationRepository(ABC):
    @abstractmethod
    async def get_by_id(self, record_id: str) -> CTMSDelegationEntity | None:
        pass

    @abstractmethod
    async def get_by_site_id(self, site_id: str) -> list[CTMSDelegationEntity]:
        pass

    @abstractmethod
    async def save(self, delegation: CTMSDelegationEntity) -> CTMSDelegationEntity:
        pass

    @abstractmethod
    async def save_audit_log(self, audit: CTMSAuditLogEntity) -> None:
        pass

    @abstractmethod
    async def get_audit_logs_by_site(self, site_id: str) -> list[CTMSAuditLogEntity]:
        pass
