from typing import Any, Dict

from apps.designer.db import terminology_cache


def map_study_to_usdm(study_data: Dict[str, Any]) -> Dict[str, Any]:
    """Maps the internal study projection dictionary into a USDM-like structure.

    Args:
        study_data (Dict[str, Any]): The internal study projection dictionary.

    Returns:
        Dict[str, Any]: The mapped study data.
    """
    active_rules = [
        r for r in study_data.get("rules", []) if not r.get("is_deleted", False)
    ]

    arms = []
    for arm_data in study_data.get("arms", []):
        visits = []
        for visit_data in arm_data.get("visits", []):
            activities = []
            for act_data in visit_data.get("activities", []):
                act_id = act_data["activity_id"]
                act_name = act_data["name"]

                # Find per-item rules targeting this activity/field
                item_rules = []
                for rule in active_rules:
                    if rule.get("type") in ("skip_logic", "constraint") and rule.get(
                        "target_field"
                    ) in (act_id, act_name):
                        item_rules.append(
                            {
                                "id": rule["id"],
                                "type": rule["type"],
                                "condition": rule["condition"],
                                "action": rule.get("action"),
                                "query_message": rule.get("query_message"),
                                "version_index": rule.get("version_index", 1),
                            }
                        )

                act_mapped = {"id": act_id, "name": act_name}
                if item_rules:
                    act_mapped["rules"] = item_rules
                activities.append(act_mapped)

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

    # Map rules to top-level study rules list
    mapped_rules = []
    for rule in active_rules:
        mapped_rules.append(
            {
                "id": rule["id"],
                "type": rule["type"],
                "condition": rule["condition"],
                "action": rule.get("action"),
                "target_field": rule.get("target_field"),
                "target_form": rule.get("target_form"),
                "target_group": rule.get("target_group"),
                "query_message": rule.get("query_message"),
                "version_index": rule.get("version_index", 1),
            }
        )

    return {
        "id": study_data["study_id"],
        "name": study_data["title"],
        "version": study_data["current_version"],
        "description": study_data.get("desc"),
        "arms": arms,
        "rules": mapped_rules,
    }
