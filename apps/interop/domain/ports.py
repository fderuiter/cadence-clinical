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
