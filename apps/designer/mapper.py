import uuid
from typing import Any, Dict

from apps.designer.db import terminology_cache


def to_uuid(val: Any, namespace_suffix: str = "") -> str:
    if not val:
        return str(uuid.uuid4())
    val_str = str(val)
    try:
        uuid.UUID(val_str)
        return val_str
    except ValueError:
        composite = f"{val_str}_{namespace_suffix}" if namespace_suffix else val_str
        return str(uuid.uuid5(uuid.NAMESPACE_DNS, composite))


def map_study_to_usdm(study_data: Dict[str, Any]) -> Dict[str, Any]:
    """Maps the internal study projection dictionary into a USDM-like structure.

    Args:
        study_data (Dict[str, Any]): The internal study projection dictionary.

    Returns:
        Dict[str, Any]: The mapped study data.
    """
    if not isinstance(study_data, dict):
        raise ValueError("study_data must be a dictionary")

    # Capture and propagate any mapping errors for missing mandatory fields
    if "study_id" not in study_data or not study_data["study_id"]:
        raise ValueError("Missing required internal field: 'study_id'")
    if "title" not in study_data or not study_data["title"]:
        raise ValueError("Missing required internal field: 'title'")
    if "current_version" not in study_data or not study_data["current_version"]:
        raise ValueError("Missing required internal field: 'current_version'")

    active_rules = [
        r for r in study_data.get("rules", []) if not r.get("is_deleted", False)
    ]

    # --- 1. Map to Legacy Flat Structure ---
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

    eligibility_criteria = []
    for crit in study_data.get("eligibility_criteria", []):
        eligibility_criteria.append({
            "id": crit.get("id") or crit.get("criterion_id"),
            "criterion_id": crit.get("id") or crit.get("criterion_id"),
            "type": crit.get("criterion_type"),
            "text": crit.get("description"),
            "expression": crit.get("dsl_source"),
        })

    # --- 2. Build Canonical USDM v3 Schema ---
    study_id = study_data["study_id"]
    study_title = study_data["title"]

    def make_code_obj(concept_id: str, default_code: str, default_decode: str, default_system: str = "NCI") -> Dict[str, Any]:
        concept_data = None
        if concept_id:
            concept_data = terminology_cache.get(concept_id)

        if concept_data:
            code_val = concept_data.get("code") or default_code
            decode_val = concept_data.get("decode") or default_decode
            system_val = concept_data.get("system") or default_system
        else:
            code_val = default_code
            decode_val = default_decode
            system_val = default_system

        original_concept_id = concept_id or f"concept_{code_val}"

        return {
            "id": to_uuid(code_val, "code"),
            "_original_id": original_concept_id,
            "code": code_val,
            "codeSystem": system_val,
            "codeSystemVersion": "1.0",
            "decode": decode_val,
            "instanceType": "Code"
        }

    canonical_arms = []
    for arm_data in study_data.get("arms", []):
        arm_id = arm_data["arm_id"]
        arm_name = arm_data["name"]

        type_concept_id = arm_data.get("type_concept_id")
        type_code = make_code_obj(type_concept_id, "C123", "Treatment Arm")

        # Map visits and activities nested to support round-trip loss-less mapping
        arm_visits = []
        for visit_data in arm_data.get("visits", []):
            visit_id = visit_data["visit_id"]
            visit_name = visit_data["name"]
            visit_type_concept_id = visit_data.get("visit_type_concept_id")
            visit_type_code = make_code_obj(visit_type_concept_id, "C789", "Screening Visit")

            arm_activities = []
            for act_data in visit_data.get("activities", []):
                act_id = act_data["activity_id"]
                act_name = act_data["name"]

                # Check rules targeting this activity
                act_rules = []
                for rule in active_rules:
                    if rule.get("type") in ("skip_logic", "constraint") and rule.get(
                        "target_field"
                    ) in (act_id, act_name):
                        act_rules.append({
                            "id": to_uuid(rule["id"], "rule"),
                            "_original_id": rule["id"],
                            "type": rule["type"],
                            "condition": rule["condition"],
                            "action": rule.get("action"),
                            "query_message": rule.get("query_message"),
                            "version_index": rule.get("version_index", 1),
                        })

                act_dict = {
                    "id": to_uuid(act_id, "activity"),
                    "_original_id": act_id,
                    "name": act_name,
                    "instanceType": "Activity"
                }
                if act_rules:
                    act_dict["rules"] = act_rules
                arm_activities.append(act_dict)

            arm_visits.append({
                "id": to_uuid(visit_id, "visit"),
                "_original_id": visit_id,
                "name": visit_name,
                "visit_type": visit_type_code,
                "activities": arm_activities,
            })

        canonical_arms.append({
            "id": to_uuid(arm_id, "arm"),
            "_original_id": arm_id,
            "name": arm_name,
            "type": type_code,
            "dataOriginDescription": "Assigned",
            "dataOriginType": make_code_obj(None, "data_origin_default", "Data Origin Default"),
            "instanceType": "StudyArm",
            "visits": arm_visits  # Nested for 100% loss-less mapping
        })

    unique_visits = {}
    unique_activities = {}
    for arm_data in study_data.get("arms", []):
        for visit_data in arm_data.get("visits", []):
            v_id = visit_data["visit_id"]
            if v_id not in unique_visits:
                unique_visits[v_id] = visit_data
            for act_data in visit_data.get("activities", []):
                act_id = act_data["activity_id"]
                if act_id not in unique_activities:
                    unique_activities[act_id] = act_data

    canonical_encounters = []
    for v_id, visit_data in unique_visits.items():
        visit_name = visit_data["name"]
        visit_type_concept_id = visit_data.get("visit_type_concept_id")
        visit_type_code = make_code_obj(visit_type_concept_id, "C789", "Screening Visit")
        canonical_encounters.append({
            "id": to_uuid(v_id, "visit"),
            "_original_id": v_id,
            "name": visit_name,
            "type": visit_type_code,
            "instanceType": "Encounter"
        })

    canonical_activities = []
    for act_id, act_data in unique_activities.items():
        act_name = act_data["name"]
        canonical_activities.append({
            "id": to_uuid(act_id, "activity"),
            "_original_id": act_id,
            "name": act_name,
            "instanceType": "Activity"
        })

    canonical_criteria = []
    for crit in study_data.get("eligibility_criteria", []):
        crit_id = crit.get("id") or crit.get("criterion_id") or "criterion_default"
        crit_type = crit.get("criterion_type") or "inclusion"
        crit_desc = crit.get("description") or "Eligibility Criterion"

        canonical_criteria.append({
            "id": to_uuid(crit_id, "criterion"),
            "_original_id": crit_id,
            "name": crit_id,
            "description": crit_desc,
            "category": make_code_obj(None, crit_type, crit_type, "CDISC-CT"),
            "identifier": crit_id,
            "criterionItemId": to_uuid(crit_id, "criterion_item"),
            "instanceType": "EligibilityCriterion",
            "_dsl_source": crit.get("dsl_source")
        })

    default_epoch = {
        "id": to_uuid(f"{study_id}_epoch_default", "epoch"),
        "_original_id": f"{study_id}_epoch_default",
        "name": "Default Epoch",
        "type": make_code_obj(None, "epoch_type_default", "Epoch Type Default"),
        "instanceType": "StudyEpoch"
    }

    default_population = {
        "id": to_uuid(f"{study_id}_population", "population"),
        "_original_id": f"{study_id}_population",
        "name": "Study Population",
        "includesHealthySubjects": False,
        "instanceType": "StudyDesignPopulation"
    }

    canonical_design = {
        "id": to_uuid(f"{study_id}_design", "design"),
        "_original_id": f"{study_id}_design",
        "name": f"{study_title} Design",
        "studyCells": [],
        "rationale": "Primary study design",
        "epochs": [default_epoch],
        "population": default_population,
        "arms": canonical_arms,
        "model": make_code_obj(None, "interventional_model", "Interventional Model"),
        "encounters": canonical_encounters,
        "activities": canonical_activities,
        "eligibilityCriteria": canonical_criteria,
        "instanceType": "InterventionalStudyDesign"
    }

    audit_metadata = {
        "reason_for_change": study_data.get("change_reason") or "Initial setup",
        "changeReason": study_data.get("change_reason") or "Initial setup"
    }

    canonical_versions = [
        {
            "id": to_uuid(f"{study_id}_version_{study_data['current_version']}", "version"),
            "_original_id": f"{study_id}_version_{study_data['current_version']}",
            "versionIdentifier": study_data["current_version"],
            "rationale": "Initial Version",
            "studyIdentifiers": [],
            "titles": [],
            "instanceType": "StudyVersion",
            "studyDesigns": [canonical_design]
        }
    ]

    # --- 3. Construct and Return Combined Payload ---
    return {
        # Legacy Top-Level Structure
        "id": study_id,
        "name": study_title,
        "version": study_data["current_version"],
        "description": study_data.get("desc"),
        "arms": arms,
        "rules": mapped_rules,
        "eligibility_criteria": eligibility_criteria,

        # Standard Canonical USDM v3 Structure
        "instanceType": "Study",
        "audit_metadata": audit_metadata,
        "reason_for_change": study_data.get("change_reason") or "Initial setup",
        "versions": canonical_versions
    }
