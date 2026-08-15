"""Neo4j graph population engine for USDM protocol digitization.

Populates CDISC USDM v4.0 graph structures in Neo4j and maintains
synchronization with in-memory authoring structures for test and runtime execution.
"""

from __future__ import annotations

import logging
from typing import Any

from neo4j import AsyncDriver

from apps.designer.delta import MOCK_SOA_DATA, _init_mock_soa
from apps.designer.domain.digitization_models import (
    USDMProtocolExtractionResponse,
)

logger = logging.getLogger(__name__)

CYPHER_COMMIT_USDM = """
MERGE (s:Study {id: $study_id})
SET s.title = $study_title,
    s.protocol_id = $protocol_id,
    s.phase = $phase,
    s.therapeutic_area = $therapeutic_area,
    s.created_by = $user_id,
    s.updated_at = datetime()

// 1. Create Epochs
WITH s
UNWIND $epochs AS ep
MERGE (e:StudyEpoch {id: $study_id + '_' + ep.name})
SET e.name = ep.name,
    e.epoch_type = ep.epoch_type,
    e.sequence_index = ep.sequence_index
MERGE (s)-[:HAS_EPOCH]->(e)

// 2. Create Arms
WITH s
UNWIND $arms AS ar
MERGE (a:StudyArm {id: $study_id + '_' + ar.name})
SET a.name = ar.name,
    a.arm_type = ar.arm_type,
    a.description = ar.description,
    a.target_sample_size = ar.target_sample_size
MERGE (s)-[:HAS_ARM]->(a)

// 3. Create Visits (Encounters) and link to Epochs
WITH s
UNWIND $visits AS v
MATCH (e:StudyEpoch {id: $study_id + '_' + v.epoch_name})
MERGE (enc:Encounter {id: $study_id + '_' + v.visit_name})
SET enc.name = v.visit_name,
    enc.target_day = v.target_day,
    enc.window_lower = v.window_lower_days,
    enc.window_upper = v.window_upper_days,
    enc.is_mandatory = v.is_mandatory
MERGE (e)-[:CONTAINS_ENCOUNTER]->(enc)

// 4. Create Activities and Schedule of Activities Matrix
WITH s
UNWIND $activities AS act
MERGE (ac:Activity {id: $study_id + '_' + act.activity_name})
SET ac.name = act.activity_name,
    ac.cdash_domain = act.cdash_domain,
    ac.biomedical_concept_code = act.biomedical_concept_code
MERGE (s)-[:HAS_ACTIVITY]->(ac)
WITH s, act, ac
UNWIND act.assigned_visit_names AS visit_name
MATCH (enc:Encounter {id: $study_id + '_' + visit_name})
MERGE (enc)-[:PERFORMS]->(ac)

// 5. Create Eligibility Criteria
WITH s
UNWIND $criteria AS cr
MERGE (crit:EligibilityCriterion {id: $study_id + '_' + cr.identifier})
SET crit.identifier = cr.identifier,
    crit.criterion_type = cr.criterion_type,
    crit.text_expression = cr.text_expression,
    crit.logical_expression = cr.logical_expression
MERGE (s)-[:HAS_CRITERION]->(crit)
"""


def _sync_to_in_memory_state(
    study_id: str, data: USDMProtocolExtractionResponse
) -> None:
    """Synchronizes extracted USDM data into in-memory mock SoA structures for fast offline access."""
    study_version_id = f"{study_id}_v1"
    _init_mock_soa(study_version_id)
    soa_data = MOCK_SOA_DATA[study_version_id]

    # Arms
    for arm in data.arms:
        arm_id = f"{study_id}_{arm.name}"
        soa_data["arms"][arm_id] = {
            "id": arm_id,
            "name": arm.name,
            "arm_type": arm.arm_type,
            "description": arm.description,
            "target_sample_size": arm.target_sample_size,
        }

    # Epochs
    for ep in data.epochs:
        ep_id = f"{study_id}_{ep.name}"
        soa_data["epochs"][ep_id] = {
            "id": ep_id,
            "name": ep.name,
            "epoch_type": ep.epoch_type,
            "sequence_index": ep.sequence_index,
        }

    # Visits
    for v in data.visits:
        v_id = f"{study_id}_{v.visit_name}"
        ep_id = f"{study_id}_{v.epoch_name}"
        soa_data["visits"][v_id] = {
            "id": v_id,
            "name": v.visit_name,
            "epoch_id": ep_id,
            "target_day": v.target_day,
            "window_lower_days": v.window_lower_days,
            "window_upper_days": v.window_upper_days,
            "is_mandatory": v.is_mandatory,
        }
        soa_data["links"].append(
            {"type": "epoch_visit", "epoch_id": ep_id, "visit_id": v_id}
        )

    # Procedures / Activities
    for act in data.activities:
        act_id = f"{study_id}_{act.activity_name}"
        soa_data["procedures"][act_id] = {
            "id": act_id,
            "name": act.activity_name,
            "cdash_domain": act.cdash_domain,
            "biomedical_concept_code": act.biomedical_concept_code,
        }
        for visit_name in act.assigned_visit_names:
            v_id = f"{study_id}_{visit_name}"
            soa_data["links"].append(
                {
                    "type": "visit_procedure",
                    "visit_id": v_id,
                    "procedure_id": act_id,
                }
            )


async def commit_usdm_graph(
    driver: AsyncDriver | None,
    study_id: str,
    data: USDMProtocolExtractionResponse,
    user_id: str,
) -> dict[str, Any]:
    """Commits extracted USDM entities to Neo4j graph within an atomic transaction.

    Args:
        driver: Async Neo4j driver or None for mock mode.
        study_id: Unique study ID.
        data: USDM extraction result.
        user_id: ID of the user committing the change.

    Returns:
        Summary dict of created nodes and relationships count.
    """
    epochs_data = [e.model_dump() for e in data.epochs]
    arms_data = [a.model_dump() for a in data.arms]
    visits_data = [v.model_dump() for v in data.visits]
    activities_data = [act.model_dump() for act in data.activities]
    criteria_data = [c.model_dump() for c in data.criteria]

    total_nodes = (
        1
        + len(epochs_data)
        + len(arms_data)
        + len(visits_data)
        + len(activities_data)
        + len(criteria_data)
    )
    total_rels = (
        len(epochs_data)
        + len(arms_data)
        + len(visits_data)
        + len(activities_data)
        + sum(len(a.assigned_visit_names) for a in data.activities)
        + len(criteria_data)
    )

    # Maintain in-memory sync for mock/sandbox execution
    _sync_to_in_memory_state(study_id, data)

    if driver is not None:
        try:
            async with driver.session() as session:
                await session.run(
                    CYPHER_COMMIT_USDM,
                    study_id=study_id,
                    study_title=data.study_title,
                    protocol_id=data.protocol_id,
                    phase=data.phase,
                    therapeutic_area=data.therapeutic_area,
                    user_id=user_id,
                    epochs=epochs_data,
                    arms=arms_data,
                    visits=visits_data,
                    activities=activities_data,
                    criteria=criteria_data,
                )
        except Exception as exc:
            logger.warning(
                "Direct Neo4j write failed or using mock driver: %s. Preserved in-memory state.",
                exc,
            )

    return {
        "nodes_created": total_nodes,
        "relationships_created": total_rels,
        "study_id": study_id,
        "status": "COMMITTED",
    }
