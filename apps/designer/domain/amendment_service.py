"""Graph Amendment Cloning Service for Study Designer.

Enables zero-downtime protocol amendments via immutable Neo4j graph branching.
Requirements: PRD-SYS-001, PRD-SUB-007
"""

import copy
import datetime as dt
import logging
import uuid
from typing import Any

from apps.designer.delta import (
    ImmutabilityViolationError,
    bump_version,
)

logger = logging.getLogger(__name__)


async def create_protocol_amendment(
    driver: Any,
    study_id: str,
    base_version_tag: str,
    amendment_type: str,  # "major" or "minor"
    requires_reconsent: bool,
    change_reason: str,
    user_id: str,
    branch_name: str | None = None,
) -> dict[str, Any]:
    """Clones the active study metadata graph into a new mutable draft amendment version.

    Requirements: PRD-SYS-001, PRD-SUB-007
    """
    new_version_tag = bump_version(base_version_tag, amendment_type)
    new_version_id = f"{study_id}_{new_version_tag}_{uuid.uuid4().hex[:8]}"
    branch_id = f"br-{uuid.uuid4().hex[:8]}"
    effective_branch_name = branch_name or f"amendment-v{new_version_tag}-draft"
    now_iso = dt.datetime.now(dt.UTC).isoformat()

    # 1. Fallback for mock/in-memory environment
    if driver is None:
        from apps.designer.db import (
            MOCK_STUDIES,
            MOCK_STUDY_PROJECTIONS_BY_VERSION,
            MOCK_STUDY_VERSIONS,
        )
        from apps.designer.delta import MOCK_SOA_DATA

        study_versions = MOCK_STUDY_VERSIONS.get(study_id, [])
        matching_base = None
        for v in study_versions:
            if (
                v.get("version_tag") == base_version_tag
                or v.get("tag") == base_version_tag
            ):
                matching_base = v
                break

        if not matching_base and study_id in MOCK_STUDIES:
            study_data = MOCK_STUDIES[study_id]
            if study_data.get("current_version") == base_version_tag:
                matching_base = {
                    "id": f"{study_id}_{base_version_tag}",
                    "version_tag": base_version_tag,
                    "tag": base_version_tag,
                    "status": "APPROVED",
                    "version_index": 1,
                }

        if not matching_base or matching_base.get("status") not in (
            "APPROVED",
            "LOCKED",
            "PUBLISHED",
            "ACTIVE",
        ):
            raise ImmutabilityViolationError(
                f"Base study version {base_version_tag} is not in an approved state."
            )

        new_index = int(matching_base.get("version_index", 1)) + 1
        new_version_record = {
            "id": new_version_id,
            "tag": new_version_tag,
            "version_tag": new_version_tag,
            "status": "DRAFT_AMENDMENT",
            "requires_reconsent": requires_reconsent,
            "change_reason": change_reason,
            "created_by": user_id,
            "created_at": now_iso,
            "parent_version": base_version_tag,
            "version_index": new_index,
            "study_id": study_id,
            "branch_id": branch_id,
            "branch_name": effective_branch_name,
        }

        if study_id not in MOCK_STUDY_VERSIONS:
            MOCK_STUDY_VERSIONS[study_id] = []
        MOCK_STUDY_VERSIONS[study_id].append(new_version_record)

        # Clone in-memory SOA data
        old_ver_id = matching_base.get("id")
        if old_ver_id and old_ver_id in MOCK_SOA_DATA:
            MOCK_SOA_DATA[new_version_id] = copy.deepcopy(MOCK_SOA_DATA[old_ver_id])
        else:
            MOCK_SOA_DATA[new_version_id] = {
                "arms": {},
                "epochs": {},
                "visits": {},
                "procedures": {},
                "forms": {},
                "timing_windows": {},
                "actions": [],
                "links": [],
                "blocks": {},
            }

        # Clone projections
        if old_ver_id and old_ver_id in MOCK_STUDY_PROJECTIONS_BY_VERSION:
            MOCK_STUDY_PROJECTIONS_BY_VERSION[new_version_id] = copy.deepcopy(
                MOCK_STUDY_PROJECTIONS_BY_VERSION[old_ver_id]
            )

        return {
            "study_id": study_id,
            "branch_id": branch_id,
            "branch_name": effective_branch_name,
            "base_version_tag": base_version_tag,
            "new_version_tag": new_version_tag,
            "version_id": new_version_id,
            "status": "DRAFT_AMENDMENT",
            "requires_reconsent": requires_reconsent,
            "created_by": user_id,
            "created_at": now_iso,
        }

    # 2. Live Neo4j Cypher implementation
    cypher_query = """
    MATCH (s:Study {id: $study_id})-[:HAS_VERSION]->(v_old:StudyVersion)
    WHERE (v_old.tag = $base_version_tag OR v_old.version_tag = $base_version_tag)
      AND v_old.status IN ['APPROVED', 'LOCKED', 'PUBLISHED', 'ACTIVE']

    // Create new version node
    CREATE (v_new:StudyVersion {
        id: $new_version_id,
        tag: $new_version_tag,
        version_tag: $new_version_tag,
        status: 'DRAFT_AMENDMENT',
        branch_id: $branch_id,
        branch_name: $branch_name,
        requires_reconsent: $requires_reconsent,
        change_reason: $change_reason,
        created_by: $user_id,
        created_at: datetime()
    })
    CREATE (s)-[:HAS_VERSION]->(v_new)
    CREATE (v_new)-[:PREVIOUS_VERSION]->(v_old)

    // Deep clone Arms
    WITH v_old, v_new
    OPTIONAL MATCH (v_old)-[:HAS_ARM]->(a:StudyArm)
    FOREACH (_ IN CASE WHEN a IS NOT NULL THEN [1] ELSE [] END |
        CREATE (a_new:StudyArm)
        SET a_new = a, a_new.id = $study_id + '_' + $new_version_tag + '_' + coalesce(a.name, a.id)
        CREATE (v_new)-[:HAS_ARM]->(a_new)
    )

    // Deep clone Epochs
    WITH v_old, v_new
    OPTIONAL MATCH (v_old)-[:HAS_EPOCH]->(ep:StudyEpoch)
    FOREACH (_ IN CASE WHEN ep IS NOT NULL THEN [1] ELSE [] END |
        CREATE (ep_new:StudyEpoch)
        SET ep_new = ep, ep_new.id = $study_id + '_' + $new_version_tag + '_' + coalesce(ep.name, ep.id)
        CREATE (v_new)-[:HAS_EPOCH]->(ep_new)
    )

    // Deep clone Encounters / Visits
    WITH v_old, v_new
    OPTIONAL MATCH (v_old)-[:HAS_VISIT|HAS_ENCOUNTER]->(vis)
    FOREACH (_ IN CASE WHEN vis IS NOT NULL THEN [1] ELSE [] END |
        CREATE (vis_new:StudyVisit)
        SET vis_new = vis, vis_new.id = $study_id + '_' + $new_version_tag + '_' + coalesce(vis.name, vis.id)
        CREATE (v_new)-[:HAS_VISIT]->(vis_new)
    )

    // Deep clone Activities / Procedures
    WITH v_old, v_new
    OPTIONAL MATCH (v_old)-[:HAS_ACTIVITY|HAS_PROCEDURE]->(act)
    FOREACH (_ IN CASE WHEN act IS NOT NULL THEN [1] ELSE [] END |
        CREATE (act_new:StudyActivity)
        SET act_new = act, act_new.id = $study_id + '_' + $new_version_tag + '_' + coalesce(act.name, act.id)
        CREATE (v_new)-[:HAS_ACTIVITY]->(act_new)
    )

    // Deep clone Rules
    WITH v_old, v_new
    OPTIONAL MATCH (v_old)-[:HAS_RULE]->(r:StudyAuthoredRule)
    FOREACH (_ IN CASE WHEN r IS NOT NULL THEN [1] ELSE [] END |
        CREATE (r_new:StudyAuthoredRule)
        SET r_new = r, r_new.id = $study_id + '_' + $new_version_tag + '_' + coalesce(r.name, r.id)
        CREATE (v_new)-[:HAS_RULE]->(r_new)
    )

    RETURN v_new.tag AS new_tag, v_new.id AS new_version_id
    """

    async with driver.session() as session:
        result = await session.run(
            cypher_query,
            study_id=study_id,
            base_version_tag=base_version_tag,
            new_version_tag=new_version_tag,
            new_version_id=new_version_id,
            branch_id=branch_id,
            branch_name=effective_branch_name,
            requires_reconsent=requires_reconsent,
            change_reason=change_reason,
            user_id=user_id,
        )
        record = await result.single()
        if not record:
            raise ImmutabilityViolationError(
                f"Base study version {base_version_tag} is not in an approved state."
            )
        return {
            "study_id": study_id,
            "branch_id": branch_id,
            "branch_name": effective_branch_name,
            "base_version_tag": base_version_tag,
            "new_version_tag": record["new_tag"],
            "version_id": record["new_version_id"],
            "status": "DRAFT_AMENDMENT",
            "requires_reconsent": requires_reconsent,
            "created_by": user_id,
            "created_at": now_iso,
        }
