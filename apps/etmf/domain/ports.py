from abc import abstractmethod
from collections.abc import Sequence
from typing import Any

from packages.hexagonal import RepositoryPort


class ETMFRepositoryPort(RepositoryPort[Any]):
    """Driven repository port for eTMF microservice."""

    @property
    @abstractmethod
    def session(self) -> Any:
        pass

    @abstractmethod
    async def get_by_id(self, entity_id: str) -> Any | None:
        pass

    @abstractmethod
    async def save(self, entity: Any) -> Any:
        pass

    @abstractmethod
    async def get_document_by_id(self, doc_id: str) -> Any | None:
        pass

    @abstractmethod
    async def get_documents_by_study(self, study_id: str) -> Sequence[Any]:
        pass

    @abstractmethod
    async def save_document(self, doc: Any) -> Any:
        pass

    @abstractmethod
    async def delete_document(self, doc: Any) -> None:
        pass

    @abstractmethod
    async def get_expected_document_by_id(self, edl_id: str) -> Any | None:
        pass

    @abstractmethod
    async def get_expected_documents_by_study(self, study_id: str) -> Sequence[Any]:
        pass

    @abstractmethod
    async def get_expected_documents_by_study_and_site(
        self, study_id: str, site_id: str | None
    ) -> Sequence[Any]:
        pass

    @abstractmethod
    async def save_expected_document(self, edl: Any) -> Any:
        pass

    @abstractmethod
    async def get_audit_logs(self, skip: int, limit: int) -> Sequence[Any]:
        pass

    @abstractmethod
    async def get_audit_logs_count(self) -> int:
        pass

    @abstractmethod
    async def save_audit_log(self, log: Any) -> Any:
        pass

    @abstractmethod
    async def get_qc_transitions_by_document_id(self, doc_id: str) -> Sequence[Any]:
        pass

    @abstractmethod
    async def get_qc_transitions_by_document_id_asc(self, doc_id: str) -> Sequence[Any]:
        pass

    @abstractmethod
    async def save_qc_transition(self, transition: Any) -> Any:
        pass

    @abstractmethod
    async def get_documents_filtered(
        self,
        study_id: str | None,
        zone: int | None,
        search: str | None,
        status: str | None,
        principal: Any,
    ) -> Sequence[Any]:
        pass

    @abstractmethod
    async def get_max_version_index(
        self, study_id: str, site_id: str | None, artifact_code: str
    ) -> int:
        pass

    @abstractmethod
    async def get_redacted_successor(self, doc_id: str) -> Any | None:
        pass

    @abstractmethod
    async def get_document_lineage(
        self, study_id: str, artifact_code: str
    ) -> Sequence[Any]:
        pass

    @abstractmethod
    async def get_expected_document_by_study_milestone_and_artifact(
        self, study_id: str, milestone: str, artifact_type: str
    ) -> Any | None:
        pass

    @abstractmethod
    async def get_documents_by_study_and_status(
        self, study_id: str, status: str
    ) -> Sequence[Any]:
        pass

    @abstractmethod
    async def get_unsealed_audit_logs(self) -> Sequence[Any]:
        pass

    @abstractmethod
    async def get_all_audit_logs(self) -> Sequence[Any]:
        pass

    @abstractmethod
    async def get_audit_log_by_id(self, log_id: str) -> Any | None:
        pass

    @abstractmethod
    async def get_audit_logs_paginated(
        self,
        user_id: str | None,
        action: str | None,
        document_id: str | None,
        start_time: Any,
        end_time: Any,
        offset: int,
        limit: int,
    ) -> tuple[int, Sequence[Any]]:
        pass

    @abstractmethod
    async def get_expected_documents_filtered(
        self,
        study_id: str,
        site_id: str | None,
        milestone: str | None,
    ) -> Sequence[Any]:
        pass

    @abstractmethod
    async def get_document_history(
        self,
        study_id: str,
        artifact_type: str,
        canonical_name: str,
        principal: Any = None,
    ) -> Sequence[Any]:
        pass

    @abstractmethod
    async def get_all_audit_ledger_seals(self) -> Sequence[Any]:
        pass

    @abstractmethod
    async def create_document(
        self,
        study_id: str,
        artifact_type: str,
        filename: str,
        content: str,
        mime_type: str,
        created_by: str,
        version_index: int,
        status: str,
        taxonomy_version: str,
        artifact_code: str,
        zone: int,
        section: str,
        site_id: str | None = None,
        reason_for_change: str | None = None,
        metadata_json: dict[str, Any] | None = None,
        document_owner_id: str | None = None,
    ) -> Any:
        pass

    @abstractmethod
    async def create_audit_log(
        self,
        user_id: str,
        user_role: str,
        action: str,
        document_id: str | None = None,
        details: str | None = None,
        reason_for_change: str | None = None,
    ) -> Any:
        pass

    @abstractmethod
    async def create_qc_transition(
        self,
        document_id: str,
        from_status: str,
        to_status: str,
        actor_id: str,
        actor_role: str,
        reason_for_change: str,
    ) -> Any:
        pass

    @abstractmethod
    async def create_redacted_document(
        self,
        source_doc: Any,
        redacted_filename: str,
        redacted_content: str,
        new_version_index: int,
        actor_id: str,
        reason_for_change: str,
        manifest_data: dict[str, Any],
    ) -> Any:
        pass
