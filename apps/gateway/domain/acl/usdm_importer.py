"""Gateway ACL USDM Importer service for parsing USDM protocol specifications.

Requirements: PRD-SYS-001
"""

import logging
from typing import Any

from pydantic import BaseModel, Field

logger = logging.getLogger(__name__)


class USDMImportResult(BaseModel):
    study_id: str
    nodes_created: int = 0
    relationships_created: int = 0
    validation_warnings: list[str] = Field(default_factory=list)


class USDMImporter:
    """Service for parsing USDM JSON specs in Gateway.

    Requirements: PRD-SYS-001
    """

    def __init__(self, neo4j_driver: Any = None) -> None:
        self.driver = neo4j_driver

    async def import_usdm(self, payload: dict[str, Any]) -> USDMImportResult:
        warnings: list[str] = []
        study_id = payload.get("id") or "study_unknown"
        study_designs = payload.get("studyDesigns") or []

        nodes_count = 1
        rel_count = 0

        for design in study_designs:
            nodes_count += 1
            rel_count += 1
            arms = design.get("arms", [])
            nodes_count += len(arms)
            rel_count += len(arms)

        if not study_designs:
            warnings.append("USDM study payload contains 0 study designs")

        return USDMImportResult(
            study_id=study_id,
            nodes_created=nodes_count,
            relationships_created=rel_count,
            validation_warnings=warnings,
        )
