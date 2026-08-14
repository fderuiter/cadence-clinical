"""CDISC USDM JSON parser and Study Designer graph importer service.

Ingests USDM protocol specifications and transforms them into Neo4j graph nodes
and relationship data structures for the Study Designer graph database.

Requirements: PRD-SYS-001, PRD-DDF-001, PRD-MDR-007
"""

import asyncio
import copy
import logging
from datetime import UTC, datetime
from typing import Any

from pydantic import BaseModel, ConfigDict, Field

from .usdm_models import (
    Activity,
    BiomedicalConcept,
    EligibilityCriterion,
    Encounter,
    StudyArm,
    StudyDesign,
    StudyEpoch,
    StudyVersion,
    USDMStudy,
)

logger = logging.getLogger(__name__)


class USDMImportResult(BaseModel):
    """Result summary of a USDM protocol import operation."""

    model_config = ConfigDict(
        populate_by_name=True, extra="ignore", validate_assignment=True
    )

    study_id: str
    protocol_title: str = Field(default="", alias="protocolTitle")
    phase: str | None = None
    therapeutic_area: str | None = Field(default=None, alias="therapeuticArea")
    nodes_created: int = 0
    relationships_created: int = 0
    entity_counts: dict[str, int] = Field(default_factory=dict)
    validation_warnings: list[str] = Field(default_factory=list)
    status: str = "COMMITTED"


def _normalize_usdm_payload(payload: dict[str, Any]) -> dict[str, Any]:
    """Normalizes raw USDM JSON dictionary keys across USDM v2.0, v3.0, and v4.0."""
    normalized = copy.deepcopy(payload)

    # Top-level aliases
    if "protocolTitle" not in normalized and "title" in normalized:
        normalized["protocolTitle"] = normalized["title"]
    if "id" not in normalized and "studyId" in normalized:
        normalized["id"] = normalized["studyId"]
    if "usdmVersion" not in normalized and "version" in normalized:
        normalized["usdmVersion"] = str(normalized["version"])

    # Study Designs normalization
    if "studyDesigns" not in normalized and "studyDesign" in normalized:
        sd = normalized.pop("studyDesign")
        normalized["studyDesigns"] = sd if isinstance(sd, list) else [sd]

    # Study Versions normalization
    if "studyVersions" not in normalized and "versions" in normalized:
        ver = normalized.pop("versions")
        normalized["studyVersions"] = ver if isinstance(ver, list) else [ver]

    # Biomedical Concepts normalization
    if "biomedicalConcepts" not in normalized and "concepts" in normalized:
        bc = normalized.pop("concepts")
        normalized["biomedicalConcepts"] = bc if isinstance(bc, list) else [bc]

    # Normalize nested structures within studyDesigns
    if "studyDesigns" in normalized and isinstance(normalized["studyDesigns"], list):
        for design in normalized["studyDesigns"]:
            if not isinstance(design, dict):
                continue
            if "arms" not in design and "studyArms" in design:
                design["arms"] = design.pop("studyArms")
            if "epochs" not in design and "studyEpochs" in design:
                design["epochs"] = design.pop("studyEpochs")
            if "encounters" not in design and "visits" in design:
                design["encounters"] = design.pop("visits")
            if "eligibilityCriteria" not in design and "criteria" in design:
                design["eligibilityCriteria"] = design.pop("criteria")
            if "biomedicalConcepts" not in design and "concepts" in design:
                design["biomedicalConcepts"] = design.pop("concepts")

    return normalized


def _sync_in_memory_mock_state(
    study_model: USDMStudy,
    user_id: str = "system",
    change_reason: str = "Zero-Click USDM Study Ingestion",
) -> None:
    """Synchronizes ingested USDM study entities into in-memory mock structures."""
    study_id = study_model.id
    version_id = (
        study_model.study_versions[0].id
        if study_model.study_versions
        else f"{study_id}_v1"
    )

    # 1. Sync MOCK_SOA_DATA in apps.designer.delta
    try:
        from apps.designer.delta import MOCK_SOA_DATA, _init_mock_soa

        _init_mock_soa(version_id)
        soa_data = MOCK_SOA_DATA[version_id]

        for d in study_model.study_designs:
            for arm in d.arms:
                soa_data["arms"][arm.id] = {
                    "id": arm.id,
                    "name": arm.name,
                    "arm_type": arm.arm_type,
                    "description": arm.description,
                    "target_sample_size": arm.target_sample_size,
                }
            for ep in d.epochs:
                soa_data["epochs"][ep.id] = {
                    "id": ep.id,
                    "name": ep.name,
                    "epoch_type": ep.epoch_type,
                    "sequence_index": ep.sequence_index,
                    "sequence_number": ep.sequence_number,
                }
            for enc in d.encounters:
                soa_data["visits"][enc.id] = {
                    "id": enc.id,
                    "name": enc.name,
                    "encounter_type": enc.encounter_type,
                    "epoch_id": enc.epoch_id,
                    "target_day": enc.target_day,
                    "window_lower_days": enc.window_lower
                    if enc.window_lower is not None
                    else enc.window_lower_days,
                    "window_upper_days": enc.window_upper
                    if enc.window_upper is not None
                    else enc.window_upper_days,
                    "is_mandatory": enc.is_mandatory,
                }
                if enc.epoch_id:
                    soa_data["links"].append(
                        {
                            "type": "epoch_visit",
                            "epoch_id": enc.epoch_id,
                            "visit_id": enc.id,
                        }
                    )
            for act in d.activities:
                soa_data["procedures"][act.id] = {
                    "id": act.id,
                    "name": act.name,
                    "cdash_domain": act.cdash_domain,
                    "biomedical_concept_code": act.biomedical_concept_code,
                }
                for visit_name in act.assigned_visit_names:
                    # Match encounter by name or ID
                    matching_enc = next(
                        (
                            e
                            for e in d.encounters
                            if e.name == visit_name or e.id == visit_name
                        ),
                        None,
                    )
                    v_id = matching_enc.id if matching_enc else visit_name
                    soa_data["links"].append(
                        {
                            "type": "visit_procedure",
                            "visit_id": v_id,
                            "procedure_id": act.id,
                        }
                    )
                for enc_id in act.assigned_encounter_ids:
                    soa_data["links"].append(
                        {
                            "type": "visit_procedure",
                            "visit_id": enc_id,
                            "procedure_id": act.id,
                        }
                    )
    except Exception as exc:
        logger.debug("In-memory MOCK_SOA_DATA sync skipped: %s", exc)

    # 2. Sync MOCK_STUDIES & MOCK_STUDY_VERSIONS in apps.designer.db
    try:
        from apps.designer.db import MOCK_STUDIES, MOCK_STUDY_VERSIONS

        all_arms = [a.model_dump() for d in study_model.study_designs for a in d.arms]
        all_epochs = [
            e.model_dump() for d in study_model.study_designs for e in d.epochs
        ]
        all_encounters = [
            enc.model_dump() for d in study_model.study_designs for enc in d.encounters
        ]
        all_activities = [
            act.model_dump() for d in study_model.study_designs for act in d.activities
        ]
        all_concepts = [
            bc.model_dump()
            for d in study_model.study_designs
            for bc in d.biomedical_concepts
        ] + [bc.model_dump() for bc in study_model.biomedical_concepts]
        all_criteria = [
            c.model_dump()
            for d in study_model.study_designs
            for c in d.eligibility_criteria
        ]

        MOCK_STUDIES[study_id] = {
            "study_id": study_id,
            "title": study_model.protocol_title or study_model.name,
            "protocol_title": study_model.protocol_title or study_model.name,
            "phase": study_model.phase,
            "therapeutic_area": study_model.therapeutic_area,
            "usdm_version": study_model.usdm_version,
            "arms": all_arms,
            "epochs": all_epochs,
            "visits": all_encounters,
            "encounters": all_encounters,
            "activities": all_activities,
            "procedures": all_activities,
            "biomedical_concepts": all_concepts,
            "eligibility_criteria": all_criteria,
            "rules": [],
        }

        if study_id not in MOCK_STUDY_VERSIONS:
            MOCK_STUDY_VERSIONS[study_id] = []

        ver_tag = (
            study_model.study_versions[0].version_tag
            if study_model.study_versions
            else "1.0"
        )
        ver_idx = (
            study_model.study_versions[0].version_index
            if study_model.study_versions
            else 1
        )

        if not any(v.get("id") == version_id for v in MOCK_STUDY_VERSIONS[study_id]):
            MOCK_STUDY_VERSIONS[study_id].append(
                {
                    "id": version_id,
                    "version_tag": ver_tag,
                    "status": "DRAFT",
                    "version_index": ver_idx,
                    "study_id": study_id,
                    "created_by": user_id,
                    "created_at": datetime.now(UTC).isoformat(),
                    "change_reason": change_reason,
                }
            )
    except Exception as exc:
        logger.debug("In-memory MOCK_STUDIES sync skipped: %s", exc)


class USDMGraphImporter:
    """Service for parsing USDM JSON specs and importing into Study Designer graph.

    Requirements: PRD-SYS-001, PRD-DDF-001, PRD-MDR-007
    """

    def __init__(self, neo4j_driver: Any = None) -> None:
        """Initialize USDM Graph Importer service.

        Args:
            neo4j_driver: Optional Async Neo4j driver or MockGraphDriver instance.
        """
        self.driver = neo4j_driver

    async def import_usdm(
        self,
        payload: dict[str, Any] | USDMStudy,
        user_id: str = "system",
        change_reason: str = "Zero-Click USDM Study Ingestion",
    ) -> USDMImportResult:
        """Parse USDM payload and import graph nodes and relationships.

        Args:
            payload: Dict or USDMStudy object representing USDM protocol graph.
            user_id: User identifier executing the import.
            change_reason: GxP 21 CFR Part 11 audit reason.

        Returns:
            USDMImportResult object containing creation counts and warnings.

        Raises:
            ValueError: If payload cannot be parsed as valid USDM study.
        """
        warnings: list[str] = []

        if isinstance(payload, dict):
            try:
                normalized = _normalize_usdm_payload(payload)
                study_model = USDMStudy.model_validate(normalized)
            except Exception as exc:
                logger.error("Failed to parse USDM study dictionary: %s", exc)
                raise ValueError(f"Invalid USDM payload structure: {exc}") from exc
        else:
            study_model = payload

        study_id = study_model.id
        study_name = study_model.name or study_id
        protocol_title = study_model.protocol_title or study_name
        phase = study_model.phase
        therapeutic_area = study_model.therapeutic_area
        usdm_version = study_model.usdm_version

        # Evaluate explicit study versions vs implicit single-version
        has_explicit_versions = bool(study_model.study_versions)
        study_versions = list(study_model.study_versions)
        if not study_versions and study_model.study_designs:
            default_version = StudyVersion(
                id=f"{study_id}_v1",
                version_tag="1.0",
                status="DRAFT",
                version_index=1,
                study_designs=study_model.study_designs,
            )
            study_versions = [default_version]

        # Gather Study Designs
        study_designs: list[StudyDesign] = list(study_model.study_designs)
        for ver in study_model.study_versions:
            for sd in ver.study_designs:
                if sd.id not in [d.id for d in study_designs]:
                    study_designs.append(sd)

        # Gather Child Entities across designs and root
        all_arms: list[tuple[str, StudyArm]] = []
        all_epochs: list[tuple[str, StudyEpoch]] = []
        all_encounters: list[tuple[str, Encounter]] = []
        all_activities: list[tuple[str, Activity]] = []
        all_criteria: list[tuple[str, EligibilityCriterion]] = []
        all_concepts: list[tuple[str, BiomedicalConcept]] = []

        # Root concepts
        for bc in study_model.biomedical_concepts:
            design_id = study_designs[0].id if study_designs else f"{study_id}_sd1"
            all_concepts.append((design_id, bc))

        for design in study_designs:
            for arm in design.arms:
                all_arms.append((design.id, arm))
            for epoch in design.epochs:
                all_epochs.append((design.id, epoch))
            for encounter in design.encounters:
                all_encounters.append((design.id, encounter))
            for activity in design.activities:
                all_activities.append((design.id, activity))
                for bc in activity.biomedical_concepts:
                    if bc.id not in [x[1].id for x in all_concepts]:
                        all_concepts.append((design.id, bc))
            for concept in design.biomedical_concepts:
                if concept.id not in [x[1].id for x in all_concepts]:
                    all_concepts.append((design.id, concept))
            for criterion in design.eligibility_criteria:
                all_criteria.append((design.id, criterion))

        # Resolve PERFORMS and MEASURES_CONCEPT links
        performs_links: list[dict[str, str]] = []
        measures_links: list[dict[str, str]] = []

        # Map encounters by ID and Name for fast lookup
        enc_by_id = {enc.id: enc for _, enc in all_encounters}
        enc_by_name = {enc.name: enc for _, enc in all_encounters}
        concept_ids = {bc.id for _, bc in all_concepts}
        concept_by_code = {
            bc.concept_code: bc for _, bc in all_concepts if bc.concept_code
        }

        for _, activity in all_activities:
            # 1. PERFORMS links from assigned visit names or encounter IDs
            for v_name in activity.assigned_visit_names:
                enc = enc_by_name.get(v_name) or enc_by_id.get(v_name)
                if enc:
                    performs_links.append(
                        {"encounter_id": enc.id, "activity_id": activity.id}
                    )
            for enc_id in activity.assigned_encounter_ids:
                if enc_id in enc_by_id:
                    performs_links.append(
                        {"encounter_id": enc_id, "activity_id": activity.id}
                    )

            # 2. MEASURES_CONCEPT links
            for bc_id in activity.biomedical_concept_ids:
                if bc_id in concept_ids:
                    measures_links.append(
                        {"activity_id": activity.id, "concept_id": bc_id}
                    )
                else:
                    warnings.append(
                        f"Activity '{activity.id}' references unknown biomedical concept '{bc_id}'"
                    )

            for bc in activity.biomedical_concepts:
                measures_links.append({"activity_id": activity.id, "concept_id": bc.id})

            if (
                activity.biomedical_concept_code
                and activity.biomedical_concept_code in concept_by_code
            ):
                matched_bc = concept_by_code[activity.biomedical_concept_code]
                measures_link = {
                    "activity_id": activity.id,
                    "concept_id": matched_bc.id,
                }
                if measures_link not in measures_links:
                    measures_links.append(measures_link)

        # Count Nodes & Relationships
        nodes_count = 1  # Study node
        if has_explicit_versions:
            nodes_count += len(study_versions)
        nodes_count += len(study_designs)
        nodes_count += len(all_arms)
        nodes_count += len(all_epochs)
        nodes_count += len(all_encounters)
        nodes_count += len(all_activities)
        nodes_count += len(all_concepts)
        nodes_count += len(all_criteria)

        rel_count = 0
        if has_explicit_versions:
            rel_count += len(study_versions)  # HAS_VERSION
        rel_count += len(study_designs)  # HAS_DESIGN
        rel_count += len(all_arms)  # HAS_ARM
        rel_count += len(all_epochs)  # HAS_EPOCH
        rel_count += len(all_encounters)  # CONTAINS_ENCOUNTER / HAS_ENCOUNTER
        rel_count += len(all_activities)  # HAS_ACTIVITY
        rel_count += len(all_concepts)  # HAS_CONCEPT
        rel_count += len(all_criteria)  # HAS_CRITERION
        rel_count += len(performs_links)  # PERFORMS
        rel_count += len(measures_links)  # MEASURES_CONCEPT

        entity_counts = {
            "study_versions": len(study_versions) if has_explicit_versions else 0,
            "study_designs": len(study_designs),
            "arms": len(all_arms),
            "epochs": len(all_epochs),
            "encounters": len(all_encounters),
            "activities": len(all_activities),
            "biomedical_concepts": len(all_concepts),
            "eligibility_criteria": len(all_criteria),
        }

        if not study_designs:
            warnings.append("USDM study payload contains 0 study designs")

        # Maintain In-Memory State Synchronization
        _sync_in_memory_mock_state(
            study_model, user_id=user_id, change_reason=change_reason
        )

        # Execute Transactional Neo4j Cypher Operations
        if self.driver is not None:
            await self._execute_graph_transaction(
                study_id=study_id,
                study_name=study_name,
                protocol_title=protocol_title,
                protocol_id=study_model.protocol_id or study_id,
                phase=phase,
                therapeutic_area=therapeutic_area,
                usdm_version=usdm_version,
                user_id=user_id,
                study_versions=study_versions,
                study_designs=study_designs,
                all_arms=all_arms,
                all_epochs=all_epochs,
                all_encounters=all_encounters,
                all_activities=all_activities,
                all_concepts=all_concepts,
                all_criteria=all_criteria,
                performs_links=performs_links,
                measures_links=measures_links,
            )

        logger.info(
            "Imported USDM study %s with %d nodes and %d relationships",
            study_id,
            nodes_count,
            rel_count,
        )

        return USDMImportResult(
            study_id=study_id,
            protocol_title=protocol_title,
            phase=phase,
            therapeutic_area=therapeutic_area,
            nodes_created=nodes_count,
            relationships_created=rel_count,
            entity_counts=entity_counts,
            validation_warnings=warnings,
            status="COMMITTED",
        )

    def import_usdm_sync(
        self,
        payload: dict[str, Any] | USDMStudy,
        user_id: str = "system",
        change_reason: str = "Zero-Click USDM Study Ingestion",
    ) -> USDMImportResult:
        """Synchronous wrapper for import_usdm."""
        try:
            asyncio.get_running_loop()
        except RuntimeError:
            return asyncio.run(
                self.import_usdm(payload, user_id=user_id, change_reason=change_reason)
            )
        else:
            import concurrent.futures

            with concurrent.futures.ThreadPoolExecutor(max_workers=1) as executor:
                return executor.submit(
                    asyncio.run,
                    self.import_usdm(
                        payload, user_id=user_id, change_reason=change_reason
                    ),
                ).result()

    async def _execute_graph_transaction(
        self,
        study_id: str,
        study_name: str,
        protocol_title: str,
        protocol_id: str,
        phase: str | None,
        therapeutic_area: str | None,
        usdm_version: str,
        user_id: str,
        study_versions: list[StudyVersion],
        study_designs: list[StudyDesign],
        all_arms: list[tuple[str, StudyArm]],
        all_epochs: list[tuple[str, StudyEpoch]],
        all_encounters: list[tuple[str, Encounter]],
        all_activities: list[tuple[str, Activity]],
        all_concepts: list[tuple[str, BiomedicalConcept]],
        all_criteria: list[tuple[str, EligibilityCriterion]],
        performs_links: list[dict[str, str]],
        measures_links: list[dict[str, str]],
    ) -> None:
        """Executes transactional Cypher queries to build the complete study protocol graph."""
        versions_data = [
            {
                "id": v.id,
                "version_tag": v.version_tag,
                "status": v.status,
                "version_index": v.version_index,
            }
            for v in study_versions
        ]
        designs_data = [
            {
                "id": d.id,
                "name": d.name,
                "design_type": d.design_type,
                "version_id": study_versions[0].id if study_versions else None,
            }
            for d in study_designs
        ]
        epochs_data = [
            {
                "id": ep.id,
                "design_id": design_id,
                "name": ep.name,
                "epoch_type": ep.epoch_type,
                "sequence_number": ep.sequence_number,
                "sequence_index": ep.sequence_index,
            }
            for design_id, ep in all_epochs
        ]
        arms_data = [
            {
                "id": ar.id,
                "design_id": design_id,
                "name": ar.name,
                "arm_type": ar.arm_type,
                "description": ar.description,
                "target_sample_size": ar.target_sample_size,
            }
            for design_id, ar in all_arms
        ]
        encounters_data = [
            {
                "id": enc.id,
                "design_id": design_id,
                "epoch_id": enc.epoch_id,
                "name": enc.name,
                "encounter_type": enc.encounter_type,
                "target_day": enc.target_day,
                "window_lower": enc.window_lower
                if enc.window_lower is not None
                else enc.window_lower_days,
                "window_upper": enc.window_upper
                if enc.window_upper is not None
                else enc.window_upper_days,
                "is_mandatory": enc.is_mandatory,
            }
            for design_id, enc in all_encounters
        ]
        concepts_data = [
            {
                "id": bc.id,
                "design_id": design_id,
                "name": bc.name,
                "label": bc.label,
                "concept_code": bc.concept_code,
                "display_name": bc.display_name,
                "definition": bc.definition,
                "cdash_domain": bc.cdash_domain,
                "cdash_variable": bc.cdash_variable,
                "data_type": bc.data_type,
                "allowable_units": bc.allowable_units,
                "codelist": bc.codelist,
            }
            for design_id, bc in all_concepts
        ]
        activities_data = [
            {
                "id": act.id,
                "design_id": design_id,
                "name": act.name,
                "description": act.description,
                "cdash_domain": act.cdash_domain,
                "biomedical_concept_code": act.biomedical_concept_code,
            }
            for design_id, act in all_activities
        ]
        criteria_data = [
            {
                "id": cr.id,
                "design_id": design_id,
                "name": cr.name,
                "identifier": cr.identifier or cr.name,
                "criterion_type": cr.criterion_type,
                "category": cr.category,
                "text": cr.text,
                "text_expression": cr.text_expression,
                "logical_expression": cr.logical_expression,
            }
            for design_id, cr in all_criteria
        ]

        async with self.driver.session() as session:
            async with session.begin_transaction() as tx:
                try:
                    # 1. Merge Study node
                    await tx.run(
                        "MERGE (s:Study {id: $study_id}) "
                        "SET s.name = $study_name, "
                        "    s.protocol_title = $protocol_title, "
                        "    s.protocol_id = $protocol_id, "
                        "    s.phase = $phase, "
                        "    s.therapeutic_area = $therapeutic_area, "
                        "    s.usdm_version = $usdm_version, "
                        "    s.created_by = $user_id, "
                        "    s.updated_at = datetime()",
                        {
                            "study_id": study_id,
                            "study_name": study_name,
                            "protocol_title": protocol_title,
                            "protocol_id": protocol_id,
                            "phase": phase,
                            "therapeutic_area": therapeutic_area,
                            "usdm_version": usdm_version,
                            "user_id": user_id,
                        },
                    )

                    # 2. Merge StudyVersions and HAS_VERSION
                    if versions_data:
                        await tx.run(
                            "UNWIND $versions AS v "
                            "MERGE (sv:StudyVersion {id: v.id}) "
                            "SET sv.version_tag = v.version_tag, "
                            "    sv.status = v.status, "
                            "    sv.version_index = v.version_index, "
                            "    sv.created_by = $user_id, "
                            "    sv.study_id = $study_id "
                            "WITH sv "
                            "MATCH (s:Study {id: $study_id}) "
                            "MERGE (s)-[:HAS_VERSION]->(sv)",
                            {
                                "versions": versions_data,
                                "study_id": study_id,
                                "user_id": user_id,
                            },
                        )

                    # 3. Merge StudyDesigns and HAS_DESIGN
                    if designs_data:
                        await tx.run(
                            "UNWIND $designs AS d "
                            "MERGE (sd:StudyDesign {id: d.id}) "
                            "SET sd.name = d.name, "
                            "    sd.design_type = d.design_type, "
                            "    sd.study_id = $study_id "
                            "WITH sd, d "
                            "MATCH (s:Study {id: $study_id}) "
                            "MERGE (s)-[:HAS_DESIGN]->(sd) "
                            "WITH sd, d "
                            "WHERE d.version_id IS NOT NULL "
                            "MATCH (sv:StudyVersion {id: d.version_id}) "
                            "MERGE (sv)-[:HAS_DESIGN]->(sd)",
                            {"designs": designs_data, "study_id": study_id},
                        )

                    # 4. Merge Epochs and HAS_EPOCH
                    if epochs_data:
                        await tx.run(
                            "UNWIND $epochs AS ep "
                            "MERGE (e:StudyEpoch {id: ep.id}) "
                            "SET e.name = ep.name, "
                            "    e.epoch_type = ep.epoch_type, "
                            "    e.sequence_number = ep.sequence_number, "
                            "    e.sequence_index = ep.sequence_index, "
                            "    e.study_id = $study_id "
                            "WITH e, ep "
                            "MATCH (sd:StudyDesign {id: ep.design_id}) "
                            "MERGE (sd)-[:HAS_EPOCH]->(e)",
                            {"epochs": epochs_data, "study_id": study_id},
                        )

                    # 5. Merge Arms and HAS_ARM
                    if arms_data:
                        await tx.run(
                            "UNWIND $arms AS ar "
                            "MERGE (a:StudyArm {id: ar.id}) "
                            "SET a.name = ar.name, "
                            "    a.arm_type = ar.arm_type, "
                            "    a.description = ar.description, "
                            "    a.target_sample_size = ar.target_sample_size, "
                            "    a.study_id = $study_id "
                            "WITH a, ar "
                            "MATCH (sd:StudyDesign {id: ar.design_id}) "
                            "MERGE (sd)-[:HAS_ARM]->(a)",
                            {"arms": arms_data, "study_id": study_id},
                        )

                    # 6. Merge Encounters and CONTAINS_ENCOUNTER
                    if encounters_data:
                        await tx.run(
                            "UNWIND $encounters AS enc "
                            "MERGE (en:Encounter {id: enc.id}) "
                            "SET en.name = enc.name, "
                            "    en.encounter_type = enc.encounter_type, "
                            "    en.target_day = enc.target_day, "
                            "    en.window_lower = enc.window_lower, "
                            "    en.window_upper = enc.window_upper, "
                            "    en.is_mandatory = enc.is_mandatory, "
                            "    en.study_id = $study_id "
                            "WITH en, enc "
                            "WHERE enc.epoch_id IS NOT NULL "
                            "MATCH (e:StudyEpoch {id: enc.epoch_id}) "
                            "MERGE (e)-[:CONTAINS_ENCOUNTER]->(en)",
                            {
                                "encounters": encounters_data,
                                "study_id": study_id,
                            },
                        )

                    # 7. Merge Biomedical Concepts and HAS_CONCEPT
                    if concepts_data:
                        await tx.run(
                            "UNWIND $concepts AS bc "
                            "MERGE (b:BiomedicalConcept {id: bc.id}) "
                            "SET b.name = bc.name, "
                            "    b.label = bc.label, "
                            "    b.concept_code = bc.concept_code, "
                            "    b.display_name = bc.display_name, "
                            "    b.definition = bc.definition, "
                            "    b.cdash_domain = bc.cdash_domain, "
                            "    b.cdash_variable = bc.cdash_variable, "
                            "    b.data_type = bc.data_type, "
                            "    b.allowable_units = bc.allowable_units, "
                            "    b.codelist = bc.codelist, "
                            "    b.study_id = $study_id "
                            "WITH b, bc "
                            "MATCH (sd:StudyDesign {id: bc.design_id}) "
                            "MERGE (sd)-[:HAS_CONCEPT]->(b)",
                            {"concepts": concepts_data, "study_id": study_id},
                        )

                    # 8. Merge Activities and HAS_ACTIVITY
                    if activities_data:
                        await tx.run(
                            "UNWIND $activities AS act "
                            "MERGE (ac:Activity {id: act.id}) "
                            "SET ac.name = act.name, "
                            "    ac.description = act.description, "
                            "    ac.cdash_domain = act.cdash_domain, "
                            "    ac.biomedical_concept_code = act.biomedical_concept_code, "
                            "    ac.study_id = $study_id "
                            "WITH ac, act "
                            "MATCH (sd:StudyDesign {id: act.design_id}) "
                            "MERGE (sd)-[:HAS_ACTIVITY]->(ac)",
                            {
                                "activities": activities_data,
                                "study_id": study_id,
                            },
                        )

                    # 9. Merge PERFORMS (Encounter -> Activity)
                    if performs_links:
                        await tx.run(
                            "UNWIND $performs AS p "
                            "MATCH (en:Encounter {id: p.encounter_id}), (ac:Activity {id: p.activity_id}) "
                            "MERGE (en)-[:PERFORMS]->(ac)",
                            {"performs": performs_links},
                        )

                    # 10. Merge MEASURES_CONCEPT (Activity -> BiomedicalConcept)
                    if measures_links:
                        await tx.run(
                            "UNWIND $measures AS m "
                            "MATCH (ac:Activity {id: m.activity_id}), (bc:BiomedicalConcept {id: m.concept_id}) "
                            "MERGE (ac)-[:MEASURES_CONCEPT]->(bc)",
                            {"measures": measures_links},
                        )

                    # 11. Merge EligibilityCriteria and HAS_CRITERION
                    if criteria_data:
                        await tx.run(
                            "UNWIND $criteria AS cr "
                            "MERGE (crit:EligibilityCriterion {id: cr.id}) "
                            "SET crit.name = cr.name, "
                            "    crit.identifier = cr.identifier, "
                            "    crit.criterion_type = cr.criterion_type, "
                            "    crit.category = cr.category, "
                            "    crit.text = cr.text, "
                            "    crit.text_expression = cr.text_expression, "
                            "    crit.logical_expression = cr.logical_expression, "
                            "    crit.study_id = $study_id "
                            "WITH crit, cr "
                            "MATCH (sd:StudyDesign {id: cr.design_id}) "
                            "MERGE (sd)-[:HAS_CRITERION]->(crit) "
                            "WITH crit "
                            "MATCH (s:Study {id: $study_id}) "
                            "MERGE (s)-[:HAS_CRITERION]->(crit)",
                            {"criteria": criteria_data, "study_id": study_id},
                        )

                    # Commit transaction atomically
                    if hasattr(tx, "commit"):
                        commit_res = tx.commit()
                        if asyncio.iscoroutine(commit_res):
                            await commit_res
                except Exception as exc:
                    logger.error("Transactional Neo4j import failed: %s", exc)
                    if hasattr(tx, "rollback"):
                        try:
                            rb_res = tx.rollback()
                            if asyncio.iscoroutine(rb_res):
                                await rb_res
                        except Exception as rb_exc:
                            logger.warning("Rollback failed: %s", rb_exc)
                    raise exc


# Backward compatibility alias
USDMImporter = USDMGraphImporter
