"""Neo4j graph population engine for USDM protocol digitization.

Populates CDISC USDM v4.0 graph structures in Neo4j and maintains
synchronization with in-memory authoring structures for test and runtime execution.
"""

from __future__ import annotations

import asyncio
import logging
from typing import Any

from neo4j import AsyncDriver

from apps.designer.delta import MOCK_SOA_DATA, _init_mock_soa
from apps.designer.domain.digitization_models import (
    USDMProtocolExtractionResponse,
)

logger = logging.getLogger(__name__)

CYPHER_COMMIT_STUDY = """
MERGE (s:Study {id: $study_id})
SET s.title = $study_title,
    s.protocol_id = $protocol_id,
    s.phase = $phase,
    s.therapeutic_area = $therapeutic_area,
    s.created_by = $user_id,
    s.updated_at = datetime()
"""

CYPHER_COMMIT_EPOCHS = """
UNWIND $epochs AS ep
MATCH (s:Study {id: $study_id})
MERGE (e:StudyEpoch {id: $study_id + '_' + ep.name})
SET e.name = ep.name,
    e.epoch_type = ep.epoch_type,
    e.sequence_index = ep.sequence_index
MERGE (s)-[:HAS_EPOCH]->(e)
"""

CYPHER_COMMIT_ARMS = """
UNWIND $arms AS ar
MATCH (s:Study {id: $study_id})
MERGE (a:StudyArm {id: $study_id + '_' + ar.name})
SET a.name = ar.name,
    a.arm_type = ar.arm_type,
    a.description = ar.description,
    a.target_sample_size = ar.target_sample_size
MERGE (s)-[:HAS_ARM]->(a)
"""

CYPHER_COMMIT_VISITS = """
UNWIND $visits AS v
MERGE (enc:Encounter {id: $study_id + '_' + v.visit_name})
SET enc.name = v.visit_name,
    enc.target_day = v.target_day,
    enc.window_lower = v.window_lower_days,
    enc.window_upper = v.window_upper_days,
    enc.is_mandatory = v.is_mandatory
WITH enc, v
OPTIONAL MATCH (e:StudyEpoch {id: $study_id + '_' + v.epoch_name})
FOREACH (_ IN CASE WHEN e IS NOT NULL THEN [1] ELSE [] END |
    MERGE (e)-[:CONTAINS_ENCOUNTER]->(enc)
)
"""

CYPHER_COMMIT_ACTIVITIES = """
UNWIND $activities AS act
MATCH (s:Study {id: $study_id})
MERGE (ac:Activity {id: $study_id + '_' + act.activity_name})
SET ac.name = act.activity_name,
    ac.cdash_domain = act.cdash_domain,
    ac.biomedical_concept_code = act.biomedical_concept_code
MERGE (s)-[:HAS_ACTIVITY]->(ac)
"""

CYPHER_COMMIT_SOA_LINKS = """
UNWIND $activities AS act
UNWIND act.assigned_visit_names AS visit_name
MATCH (ac:Activity {id: $study_id + '_' + act.activity_name})
MATCH (enc:Encounter {id: $study_id + '_' + visit_name})
MERGE (enc)-[:PERFORMS]->(ac)
"""

CYPHER_COMMIT_CRITERIA = """
UNWIND $criteria AS cr
MATCH (s:Study {id: $study_id})
MERGE (crit:EligibilityCriterion {id: $study_id + '_' + cr.identifier})
SET crit.identifier = cr.identifier,
    crit.criterion_type = cr.criterion_type,
    crit.text_expression = cr.text_expression,
    crit.logical_expression = cr.logical_expression
MERGE (s)-[:HAS_CRITERION]->(crit)
"""

# Backward compatibility alias
CYPHER_COMMIT_USDM = CYPHER_COMMIT_STUDY


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

    if driver is not None:
        max_retries = 3
        initial_delay = 0.05
        backoff_factor = 2.0

        async with driver.session() as session:
            for attempt in range(max_retries):
                try:
                    async with session.begin_transaction() as tx:
                        # 1. Study node
                        await tx.run(
                            CYPHER_COMMIT_STUDY,
                            study_id=study_id,
                            study_title=data.study_title,
                            protocol_id=data.protocol_id,
                            phase=data.phase,
                            therapeutic_area=data.therapeutic_area,
                            user_id=user_id,
                        )
                        # 2. Epochs (conditionally)
                        if epochs_data:
                            await tx.run(
                                CYPHER_COMMIT_EPOCHS,
                                study_id=study_id,
                                epochs=epochs_data,
                            )
                        # 3. Arms (conditionally)
                        if arms_data:
                            await tx.run(
                                CYPHER_COMMIT_ARMS,
                                study_id=study_id,
                                arms=arms_data,
                            )
                        # 4. Visits / Encounters (conditionally)
                        if visits_data:
                            await tx.run(
                                CYPHER_COMMIT_VISITS,
                                study_id=study_id,
                                visits=visits_data,
                            )
                        # 5. Activities (conditionally)
                        if activities_data:
                            await tx.run(
                                CYPHER_COMMIT_ACTIVITIES,
                                study_id=study_id,
                                activities=activities_data,
                            )
                            if any(
                                a.get("assigned_visit_names") for a in activities_data
                            ):
                                await tx.run(
                                    CYPHER_COMMIT_SOA_LINKS,
                                    study_id=study_id,
                                    activities=activities_data,
                                )
                        # 6. Criteria (conditionally)
                        if criteria_data:
                            await tx.run(
                                CYPHER_COMMIT_CRITERIA,
                                study_id=study_id,
                                criteria=criteria_data,
                            )

                        if hasattr(tx, "commit"):
                            res = tx.commit()
                            if asyncio.iscoroutine(res):
                                await res
                    break  # Transaction succeeded
                except Exception as exc:
                    err_name = exc.__class__.__name__
                    err_msg = str(exc).lower()
                    is_transient = (
                        err_name in ("TransientError", "LockError", "OperationalError")
                        or "lock" in err_msg
                        or "transient" in err_msg
                        or "deadlock" in err_msg
                    )
                    if is_transient and attempt < max_retries - 1:
                        if hasattr(tx, "rollback"):
                            try:
                                rb_res = tx.rollback()
                                if asyncio.iscoroutine(rb_res):
                                    await rb_res
                            except Exception:
                                pass
                        await asyncio.sleep(initial_delay * (backoff_factor**attempt))
                        continue
                    logger.error("Transactional Neo4j USDM commit failed: %s", exc)
                    if hasattr(tx, "rollback"):
                        try:
                            rb_res = tx.rollback()
                            if asyncio.iscoroutine(rb_res):
                                await rb_res
                        except Exception:
                            pass
                    raise exc

    # Maintain in-memory sync ONLY AFTER database write transaction succeeds
    _sync_to_in_memory_state(study_id, data)

    return {
        "nodes_created": total_nodes,
        "relationships_created": total_rels,
        "study_id": study_id,
        "status": "COMMITTED",
    }
