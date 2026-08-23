"""Driven ports for Interop microservice.

Requirements: PRD-CRF-007, PRD-SYS-051, PRD-CDR-001, PRD-CDR-004, PRD-CDR-005, PRD-CDR-006, ADR-2199
"""

from abc import ABC, abstractmethod
from typing import Any

from apps.interop.domain.sce_models import (
    DatabaseLockAuthToken,
    ExecutionRunManifest,
    StatisticalProgram,
)
from apps.interop.domain.semantic_mapping_models import (
    ConceptMapElement,
    SemanticMappedItem,
)
from packages.hexagonal import RepositoryPort


class IInteropRepository(RepositoryPort[Any]):
    """Driven repository port for Interop microservice."""

    @abstractmethod
    async def get_by_id(self, entity_id: str) -> Any | None:
        pass

    @abstractmethod
    async def save(self, entity: Any) -> Any:
        pass


class EmbeddingMatcherPort(ABC):
    """Port for vector embedding semantic similarity matching against CDISC concepts."""

    @abstractmethod
    async def match_concept(
        self,
        query_text: str,
        candidates: list[ConceptMapElement] | None = None,
        min_confidence: float = 0.82,
    ) -> tuple[ConceptMapElement | None, float]:
        """Find the closest CDISC concept match by vector cosine similarity.

        Args:
            query_text: Clinical verbatim, display name, or phrase.
            candidates: Candidate ConceptMap elements to search against.
            min_confidence: Minimum cosine similarity threshold.

        Returns:
            Tuple of (Matched ConceptMapElement or None, confidence_score).
        """
        pass


class LLMSemanticReasonerPort(ABC):
    """Port for LLM-based semantic reasoning and extraction of unstructured EHR text."""

    @abstractmethod
    async def extract_concepts_from_narrative(
        self,
        narrative_text: str,
        study_id: str = "DEFAULT_STUDY",
        custom_terms: list[str] | None = None,
    ) -> list[SemanticMappedItem]:
        """Extract structured CDISC clinical concepts from unstructured EHR narrative text.

        Args:
            narrative_text: Clinical note, report div, or narrative excerpt.
            study_id: Clinical study scope identifier.
            custom_terms: Patient names / terms to air-gap de-identify.

        Returns:
            List of extracted SemanticMappedItem objects with reasoning provenance.
        """
        pass


class LakehouseRepositoryPort(RepositoryPort[Any]):
    """Port for Lakehouse persistence, snapshots, pooling, SCE workspaces, and 21 CFR Part 11 audit records."""

    @abstractmethod
    async def get_by_id(self, entity_id: str) -> Any | None:
        pass

    @abstractmethod
    async def save(self, entity: Any) -> Any:
        pass

    @abstractmethod
    async def save_canonical_records(self, records: list[Any]) -> list[Any]:
        """Persist normalized canonical clinical records."""
        pass

    @abstractmethod
    async def get_records(
        self, study_id: str, filter_criteria: Any | None = None
    ) -> list[Any]:
        """Query canonical records for a study matching optional filters."""
        pass

    @abstractmethod
    async def save_snapshot(self, snapshot: Any, record_ids: list[str]) -> Any:
        """Persist a sealed snapshot and associated record link associations."""
        pass

    @abstractmethod
    async def get_snapshot_by_version(
        self, study_id: str, version_tag: str
    ) -> Any | None:
        """Retrieve snapshot by study and version tag."""
        pass

    @abstractmethod
    async def get_snapshot_by_id(self, snapshot_id: str) -> Any | None:
        """Retrieve snapshot by snapshot ID."""
        pass

    @abstractmethod
    async def list_snapshots(
        self, study_id: str, milestone: str | None = None
    ) -> list[Any]:
        """List sealed snapshots for a study."""
        pass

    @abstractmethod
    async def get_snapshot_records(
        self, snapshot_id: str, filter_criteria: Any | None = None
    ) -> tuple[list[Any], Any]:
        """Retrieve pooled records and snapshot metadata for a sealed snapshot."""
        pass

    @abstractmethod
    async def save_audit_log(
        self,
        audit_id: str,
        timestamp: Any,
        user_id: str,
        user_role: str,
        action: str,
        study_id: str,
        snapshot_id: str | None,
        details: dict[str, Any],
        reason_for_change: str | None,
        sha256_seal: str,
    ) -> None:
        """Write an immutable 21 CFR Part 11 audit entry."""
        pass

    @abstractmethod
    async def get_audit_trail(self, snapshot_id: str) -> list[dict[str, Any]]:
        """Retrieve chronological audit trail entries for a snapshot."""
        pass

    @abstractmethod
    async def get_audit_logs(
        self, study_id: str | None = None, snapshot_id: str | None = None
    ) -> list[dict[str, Any]]:
        """Retrieve audit logs filtered by study_id and/or snapshot_id."""
        pass

    @abstractmethod
    async def get_metrics(self, study_id: str) -> Any:
        """Aggregate real-time Lakehouse repository metrics for a study."""
        pass

    # Statistical Computing Environment (SCE) Operations
    @abstractmethod
    async def save_program(self, program: StatisticalProgram) -> StatisticalProgram:
        """Persist or update a statistical program definition."""
        pass

    @abstractmethod
    async def get_program_by_id(self, program_id: str) -> StatisticalProgram | None:
        """Retrieve statistical program by ID."""
        pass

    @abstractmethod
    async def get_program_by_name(
        self, study_id: str, name: str
    ) -> StatisticalProgram | None:
        """Retrieve statistical program by study and filename."""
        pass

    @abstractmethod
    async def list_programs(self, study_id: str) -> list[StatisticalProgram]:
        """List all statistical programs for a study."""
        pass

    @abstractmethod
    async def save_execution_run(
        self, manifest: ExecutionRunManifest
    ) -> ExecutionRunManifest:
        """Persist execution run results and sealed manifest."""
        pass

    @abstractmethod
    async def get_execution_run_by_id(self, run_id: str) -> ExecutionRunManifest | None:
        """Retrieve execution run manifest by run ID."""
        pass

    @abstractmethod
    async def list_execution_runs(
        self,
        study_id: str,
        program_id: str | None = None,
        snapshot_id: str | None = None,
    ) -> list[ExecutionRunManifest]:
        """List execution runs for a study filtered by program or snapshot."""
        pass

    @abstractmethod
    async def save_lock_token(
        self, token: DatabaseLockAuthToken
    ) -> DatabaseLockAuthToken:
        """Persist a cryptographic database lock authorization token."""
        pass

    @abstractmethod
    async def get_lock_token_by_id(self, token_id: str) -> DatabaseLockAuthToken | None:
        """Retrieve database lock authorization token by ID."""
        pass

    @abstractmethod
    async def revoke_lock_token(self, token_id: str) -> bool:
        """Revoke an active database lock authorization token."""
        pass

    @abstractmethod
    async def list_lock_tokens(self, study_id: str) -> list[DatabaseLockAuthToken]:
        """List active authorization tokens for a study."""
        pass


class ISensorRepositoryPort(RepositoryPort[Any]):
    """Port for IoT sensor repository persistence."""

    @abstractmethod
    async def get_by_id(self, entity_id: str) -> Any | None:
        pass

    @abstractmethod
    async def save(self, entity: Any) -> Any:
        pass

    @abstractmethod
    async def save_device(self, device: Any) -> Any:
        pass

    @abstractmethod
    async def get_device_by_token(self, device_token: str) -> Any | None:
        pass

    @abstractmethod
    async def get_device_by_udi(
        self, udi_di: str, serial: str | None = None
    ) -> Any | None:
        pass

    @abstractmethod
    async def list_devices(
        self,
        study_id: str,
        site_id: str | None = None,
        subject_id: str | None = None,
        device_type: str | None = None,
        is_active: bool | None = True,
    ) -> list[Any]:
        pass

    @abstractmethod
    async def save_telemetry_stream(self, stream_record: Any) -> Any:
        pass

    @abstractmethod
    async def list_telemetry_streams(
        self,
        study_id: str,
        subject_id: str | None = None,
        device_token: str | None = None,
        device_type: str | None = None,
        limit: int = 100,
    ) -> list[Any]:
        pass

    @abstractmethod
    async def save_daily_summary(self, summary: Any) -> Any:
        pass

    @abstractmethod
    async def list_daily_summaries(
        self,
        study_id: str,
        subject_id: str | None = None,
        device_type: str | None = None,
        start_date: Any | None = None,
        end_date: Any | None = None,
    ) -> list[Any]:
        pass

    @abstractmethod
    async def save_safety_alerts(self, alerts: list[Any]) -> list[Any]:
        pass

    @abstractmethod
    async def get_safety_alert_by_id(self, alert_id: str) -> Any | None:
        pass

    @abstractmethod
    async def list_safety_alerts(
        self,
        study_id: str,
        subject_id: str | None = None,
        severity: str | None = None,
        alert_type: str | None = None,
        is_acknowledged: bool | None = None,
        limit: int = 100,
    ) -> list[Any]:
        pass

    @abstractmethod
    async def acknowledge_safety_alert(
        self,
        alert_id: str,
        acknowledged_by: str,
        action_taken: str | None = None,
        investigator_notes: str | None = None,
    ) -> Any | None:
        pass

    @abstractmethod
    async def get_sensor_dashboard_metrics(self, study_id: str) -> dict[str, Any]:
        pass


class IDCTRepositoryPort(ABC):
    """Port for Decentralized Clinical Trial (DCT) repository persistence."""

    @abstractmethod
    async def create_telehealth_session(self, record: Any) -> Any:
        pass

    @abstractmethod
    async def get_telehealth_session_by_id(self, session_id: str) -> Any | None:
        pass

    @abstractmethod
    async def get_telehealth_session_by_room(self, room_id: str) -> Any | None:
        pass

    @abstractmethod
    async def list_telehealth_sessions(
        self, study_id: str | None = None, subject_id: str | None = None
    ) -> list[Any]:
        pass

    @abstractmethod
    async def update_telehealth_session(self, record: Any) -> Any:
        pass

    @abstractmethod
    async def create_pediatric_assent(self, record: Any) -> Any:
        pass

    @abstractmethod
    async def get_pediatric_assent_by_id(self, assent_id: str) -> Any | None:
        pass

    @abstractmethod
    async def list_pediatric_assents(
        self, study_id: str | None = None, subject_id: str | None = None
    ) -> list[Any]:
        pass

    @abstractmethod
    async def create_dtp_shipment(self, record: Any) -> Any:
        pass

    @abstractmethod
    async def get_dtp_shipment_by_tracking(self, tracking_number: str) -> Any | None:
        pass

    @abstractmethod
    async def get_dtp_shipment_by_id(self, shipment_id: str) -> Any | None:
        pass

    @abstractmethod
    async def list_dtp_shipments(
        self,
        study_id: str | None = None,
        site_id: str | None = None,
        subject_pseudonym_id: str | None = None,
    ) -> list[Any]:
        pass

    @abstractmethod
    async def update_dtp_shipment(self, record: Any) -> Any:
        pass

    @abstractmethod
    async def create_in_home_visit(self, record: Any) -> Any:
        pass

    @abstractmethod
    async def get_in_home_visit_by_id(self, visit_id: str) -> Any | None:
        pass

    @abstractmethod
    async def list_in_home_visits(
        self,
        study_id: str | None = None,
        site_id: str | None = None,
        nurse_id: str | None = None,
        subject_id: str | None = None,
    ) -> list[Any]:
        pass

    @abstractmethod
    async def update_in_home_visit(self, record: Any) -> Any:
        pass

    @abstractmethod
    async def get_dct_metrics(self, study_id: str) -> Any:
        pass


class IEsourceReconciliationRepositoryPort(RepositoryPort[Any]):
    """Driven repository port for eSource Candidate Ingestion and CRC Reconciliation."""

    @abstractmethod
    async def get_by_id(self, entity_id: str) -> Any | None:
        pass

    @abstractmethod
    async def save(self, entity: Any) -> Any:
        pass

    @abstractmethod
    async def save_candidates_batch(
        self,
        candidates: list[Any],
        user_id: str,
        user_role: str,
        change_reason: str,
    ) -> list[Any]:
        pass

    @abstractmethod
    async def list_candidates(
        self,
        study_id: str | None = None,
        subject_id: str | None = None,
        site_id: str | None = None,
        domain: str | None = None,
        status: str | None = None,
        skip: int = 0,
        limit: int = 100,
    ) -> tuple[list[Any], int]:
        pass

    @abstractmethod
    async def accept_candidate(
        self,
        candidate_id: str,
        user_id: str,
        user_role: str,
        signature_token: str | None = None,
        commit_reason: str | None = None,
        change_reason: str = "CRC Accept & Commit",
    ) -> Any:
        pass

    @abstractmethod
    async def reject_candidate(
        self,
        candidate_id: str,
        user_id: str,
        user_role: str,
        rejection_reason: str,
        change_reason: str = "CRC Reject Candidate",
    ) -> Any:
        pass

    @abstractmethod
    async def raise_clinical_query(
        self,
        candidate_id: str,
        user_id: str,
        user_role: str,
        query_explanation: str,
        discrepancy_type: str = "VALUE_MISMATCH",
        change_reason: str = "CRC Raise Discrepancy Query",
    ) -> tuple[Any, Any]:
        pass

    @abstractmethod
    async def get_provenance_audit(self, candidate_id: str) -> dict[str, Any]:
        pass
