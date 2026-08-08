from abc import abstractmethod
from collections.abc import Sequence
from typing import Any

from packages.hexagonal import RepositoryPort


class EISFRepositoryPort(RepositoryPort[Any]):
    """Driven repository port for eISF microservice."""

    @abstractmethod
    async def get_by_id(self, entity_id: str) -> Any | None:
        pass

    @abstractmethod
    async def save(self, entity: Any) -> Any:
        pass

    @abstractmethod
    async def get_documents_by_site(self, site_id: str) -> Sequence[Any]:
        pass

    @abstractmethod
    async def get_all_documents(self) -> Sequence[Any]:
        pass

    @abstractmethod
    async def get_document_by_id(self, doc_id: str) -> Any | None:
        pass

    @abstractmethod
    async def save_document(self, doc: Any) -> Any:
        pass

    @abstractmethod
    async def delete_document(self, doc: Any) -> None:
        pass

    @abstractmethod
    async def save_audit_log(self, log: Any) -> Any:
        pass

    @abstractmethod
    async def save_security_alert_out_of_band(self, alert: Any) -> None:
        pass

    @abstractmethod
    async def get_documents_by_study(self, study_id: str) -> Sequence[Any]:
        pass

    @abstractmethod
    async def get_latest_document(
        self, study_id: str, site_id: str, section_code: str
    ) -> Any | None:
        pass

    @abstractmethod
    async def get_documents_by_correlation_or_logical_fields(
        self,
        correlation_key: str | None,
        study_id: str,
        site_id: str,
        binder_classification: str,
    ) -> Sequence[Any]:
        pass

    @abstractmethod
    async def list_documents_filtered(
        self,
        site_ids: str | list[str] | None,
        study_id: str | None,
        binder_section: str | None,
        binder_classification: str | None,
    ) -> Sequence[Any]:
        pass
