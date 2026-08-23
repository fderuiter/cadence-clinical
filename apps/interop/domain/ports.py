"""Driven ports for Interop microservice.

Requirements: PRD-CRF-007, PRD-SYS-051
"""

from abc import ABC, abstractmethod
from typing import Any

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
    """Port for Lakehouse persistence, snapshots, pooling, and 21 CFR Part 11 audit records."""

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
    async def get_metrics(self, study_id: str) -> Any:
        """Aggregate real-time Lakehouse repository metrics for a study."""
        pass
