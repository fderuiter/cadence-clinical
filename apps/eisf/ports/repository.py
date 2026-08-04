from abc import ABC, abstractmethod
from collections.abc import Sequence

from ..models import ISFAuditLog, ISFDocument


class EISFRepositoryPort(ABC):
    @abstractmethod
    async def get_documents_by_site(self, site_id: str) -> Sequence[ISFDocument]:
        pass

    @abstractmethod
    async def get_all_documents(self) -> Sequence[ISFDocument]:
        pass

    @abstractmethod
    async def get_document_by_id(self, doc_id: str) -> ISFDocument | None:
        pass

    @abstractmethod
    async def save_document(self, doc: ISFDocument) -> ISFDocument:
        pass

    @abstractmethod
    async def delete_document(self, doc: ISFDocument) -> None:
        pass

    @abstractmethod
    async def save_audit_log(self, log: ISFAuditLog) -> ISFAuditLog:
        pass

    @abstractmethod
    async def save_security_alert_out_of_band(self, alert: ISFAuditLog) -> None:
        pass

    @abstractmethod
    async def get_documents_by_study(self, study_id: str) -> Sequence[ISFDocument]:
        pass

    @abstractmethod
    async def get_latest_document(
        self, study_id: str, site_id: str, section_code: str
    ) -> ISFDocument | None:
        pass

    @abstractmethod
    async def get_documents_by_correlation_or_logical_fields(
        self,
        correlation_key: str | None,
        study_id: str,
        site_id: str,
        binder_classification: str,
    ) -> Sequence[ISFDocument]:
        pass

    @abstractmethod
    async def list_documents_filtered(
        self,
        site_ids: str | list[str] | None,
        study_id: str | None,
        binder_section: str | None,
        binder_classification: str | None,
    ) -> Sequence[ISFDocument]:
        pass
