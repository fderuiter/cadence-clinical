from typing import Any, Dict

from apps.designer.db import terminology_cache


def map_study_to_usdm(study_data: Dict[str, Any]) -> Dict[str, Any]:
    """Maps the internal study projection dictionary into a USDM-like structure.

    Args:
        study_data (Dict[str, Any]): The internal study projection dictionary.

    Returns:
        Dict[str, Any]: The mapped study data.
    """
    arms = []
    for arm_data in study_data.get("arms", []):
        visits = []
        for visit_data in arm_data.get("visits", []):
            activities = []
            for act_data in visit_data.get("activities", []):
                activities.append(
                    {"id": act_data["activity_id"], "name": act_data["name"]}
                )

            visit_type_concept = None
            if "visit_type_concept_id" in visit_data:
                concept_data = terminology_cache.get(
                    visit_data["visit_type_concept_id"]
                )
                if concept_data:
                    visit_type_concept = {
                        "code": concept_data["code"],
                        "decode": concept_data["decode"],
                        "system": concept_data["system"],
                    }

            visits.append(
                {
                    "id": visit_data["visit_id"],
                    "name": visit_data["name"],
                    "visit_type": visit_type_concept,
                    "activities": activities,
                }
            )

        arm_type_concept = None
        if "type_concept_id" in arm_data:
            concept_data = terminology_cache.get(arm_data["type_concept_id"])
            if concept_data:
                arm_type_concept = {
                    "code": concept_data["code"],
                    "decode": concept_data["decode"],
                    "system": concept_data["system"],
                }

        arms.append(
            {
                "id": arm_data["arm_id"],
                "name": arm_data["name"],
                "arm_type": arm_type_concept,
                "visits": visits,
            }
        )

    return {
        "id": study_data["study_id"],
        "name": study_data["title"],
        "version": study_data["current_version"],
        "description": study_data.get("desc"),
        "arms": arms,
    }
