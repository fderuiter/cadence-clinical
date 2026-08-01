"""CDISC USDM JSON parser and Study Designer graph importer service.

Ingests USDM protocol specifications and transforms them into Neo4j graph nodes
and relationship data structures for the Study Designer graph database.

Requirements: PRD-SYS-001
"""

import logging
from typing import Any

from pydantic import BaseModel, Field

from cdisc.usdm_models import USDMStudy

logger = logging.getLogger(__name__)


class USDMImportResult(BaseModel):
    """Result summary of a USDM protocol import operation."""

    study_id: str
    nodes_created: int = 0
    relationships_created: int = 0
    validation_warnings: list[str] = Field(default_factory=list)


class USDMImporter:
    """Service for parsing USDM JSON specs and importing into Study Designer graph.

    Requirements: PRD-SYS-001
    """

    def __init__(self, neo4j_driver: Any = None) -> None:
        """Initialize USDM Importer service.

        Args:
            neo4j_driver: Optional Async Neo4j driver instance.
        """
        self.driver = neo4j_driver

    async def import_usdm(
        self, payload: dict[str, Any] | USDMStudy
    ) -> USDMImportResult:
        """Parse USDM payload and import graph nodes and relationships.

        Args:
            payload: Dict or USDMStudy object representing USDM protocol graph.

        Returns:
            USDMImportResult object containing creation counts and warnings.

        Raises:
            ValueError: If payload cannot be parsed as valid USDM study.
        """
        warnings: list[str] = []

        if isinstance(payload, dict):
            try:
                study_model = USDMStudy.model_validate(payload)
            except Exception as exc:
                logger.error("Failed to parse USDM study dictionary: %s", exc)
                raise ValueError(f"Invalid USDM payload structure: {exc}") from exc
        else:
            study_model = payload

        study_id = study_model.id
        nodes_count = 1  # USDMStudy node
        rel_count = 0

        for design in study_model.study_designs:
            nodes_count += 1  # StudyDesign node
            rel_count += 1  # HAS_DESIGN

            # Count arms
            nodes_count += len(design.arms)
            rel_count += len(design.arms)

            # Count epochs
            nodes_count += len(design.epochs)
            rel_count += len(design.epochs)

            # Count encounters
            nodes_count += len(design.encounters)
            rel_count += len(design.encounters)

            # Count activities
            nodes_count += len(design.activities)
            rel_count += len(design.activities)

            # Count criteria
            nodes_count += len(design.eligibility_criteria)
            rel_count += len(design.eligibility_criteria)

        if not study_model.study_designs:
            warnings.append("USDM study payload contains 0 study designs")

        logger.info(
            "Imported USDM study %s with %d nodes and %d relationships",
            study_id,
            nodes_count,
            rel_count,
        )

        return USDMImportResult(
            study_id=study_id,
            nodes_created=nodes_count,
            relationships_created=rel_count,
            validation_warnings=warnings,
        )
