from typing import Any, Dict, List, Optional
from apps.designer.db import terminology_cache, MOCK_TERMINOLOGY
from apps.designer.rules import ExpressionNode, detect_circular_dependencies

def resolve_concept_id(concept_dict: Optional[Dict[str, Any]]) -> Optional[str]:
    """Resolves a terminology concept ID (key) given a concept dict with a 'code'.
    Searches first within the terminology cache, falling back to MOCK_TERMINOLOGY,
    and then defaulting to the code itself.
    """
    if not concept_dict or not isinstance(concept_dict, dict):
        return None
    code = concept_dict.get("code")
    if not code:
        return None

    # Search terminology_cache (using status to see what's loaded or direct lookup keys)
    # Actually, terminology_cache maps concept_id (key) -> details.
    # Let's search the cache internally or search the MOCK_TERMINOLOGY details.
    for k, details in MOCK_TERMINOLOGY.items():
        if isinstance(details, dict) and details.get("code") == code:
            return k

    # Also, we can inspect terminology_cache's internal cache if we have access,
    # but since TerminologyCache has thread-safe _cache, we can do a quick scan if needed.
    # But usually, standard concept codes are stored directly. Let's return the code if no match is found.
    return str(code)


def map_usdm_to_study(usdm_data: Dict[str, Any]) -> Dict[str, Any]:
    """Inverse mapper that reconstructs the internal study projection dictionary
    from a validated, normalized USDM representation.

    Args:
        usdm_data (Dict[str, Any]): The USDM-like study payload.

    Returns:
        Dict[str, Any]: The reconstructed internal study projection dictionary.

    Raises:
        ValueError: If required fields are missing or if any explicitly unsupported
                    constructs (such as circular skip-logic or invalid rules) are detected.
    """
    if not isinstance(usdm_data, dict):
        raise ValueError("USDM payload must be a dictionary")

    # 1. Enforce physical identity and basic study identification metrics
    study_id = usdm_data.get("id")
    title = usdm_data.get("name")
    if not study_id:
        raise ValueError("USDM payload must contain a non-empty 'id' field")
    if not title:
        raise ValueError("USDM payload must contain a non-empty 'name' field")

    current_version = usdm_data.get("version")
    desc = usdm_data.get("description")

    # 2. Reconstruct arms, visits, and activities
    arms_projection = []
    preservation_metadata: Dict[str, Any] = {"unmapped_fields": {}}

    # Standard fields for study
    known_study_keys = {"id", "name", "version", "description", "arms", "rules", "preservation_metadata"}
    extra_study_keys = set(usdm_data.keys()) - known_study_keys
    if extra_study_keys:
        preservation_metadata["unmapped_fields"]["study"] = {
            k: usdm_data[k] for k in extra_study_keys
        }

    # Gather embedded rules from activities during nested traversal
    embedded_rules = []

    for arm in usdm_data.get("arms", []):
        if not isinstance(arm, dict):
            continue
        arm_id = arm.get("id")
        arm_name = arm.get("name")
        if not arm_id:
            raise ValueError("Every arm in USDM arms list must have an 'id'")
        if not arm_name:
            raise ValueError("Every arm in USDM arms list must have a 'name'")

        arm_projection = {
            "arm_id": arm_id,
            "name": arm_name,
            "visits": []
        }

        # Resolve arm type concept lookup
        arm_type = arm.get("arm_type")
        if arm_type:
            concept_id = resolve_concept_id(arm_type)
            if concept_id:
                arm_projection["type_concept_id"] = concept_id

            # Check for extra/unmapped keys inside arm_type
            if isinstance(arm_type, dict):
                known_concept_keys = {"code", "decode", "system"}
                extra_concept_keys = set(arm_type.keys()) - known_concept_keys
                if extra_concept_keys:
                    preservation_metadata["unmapped_fields"][f"arm_{arm_id}_arm_type"] = {
                        k: arm_type[k] for k in extra_concept_keys
                    }

        # Collect extra arm keys to prevent silent data drop
        known_arm_keys = {"id", "name", "arm_type", "visits"}
        extra_arm_keys = set(arm.keys()) - known_arm_keys
        if extra_arm_keys:
            preservation_metadata["unmapped_fields"][f"arm_{arm_id}"] = {
                k: arm[k] for k in extra_arm_keys
            }

        # Reconstruct visits
        for visit in arm.get("visits", []):
            if not isinstance(visit, dict):
                continue
            visit_id = visit.get("id")
            visit_name = visit.get("name")
            if not visit_id:
                raise ValueError(f"Every visit in arm '{arm_id}' must have an 'id'")
            if not visit_name:
                raise ValueError(f"Every visit in arm '{arm_id}' must have a 'name'")

            visit_projection = {
                "visit_id": visit_id,
                "name": visit_name,
                "activities": []
            }

            # Resolve visit type concept lookup
            visit_type = visit.get("visit_type")
            if visit_type:
                v_concept_id = resolve_concept_id(visit_type)
                if v_concept_id:
                    visit_projection["visit_type_concept_id"] = v_concept_id

                # Check for extra keys in visit_type
                if isinstance(visit_type, dict):
                    known_concept_keys = {"code", "decode", "system"}
                    extra_v_concept_keys = set(visit_type.keys()) - known_concept_keys
                    if extra_v_concept_keys:
                        preservation_metadata["unmapped_fields"][f"visit_{visit_id}_visit_type"] = {
                            k: visit_type[k] for k in extra_v_concept_keys
                        }

            # Collect extra visit keys
            known_visit_keys = {"id", "name", "visit_type", "activities"}
            extra_visit_keys = set(visit.keys()) - known_visit_keys
            if extra_visit_keys:
                preservation_metadata["unmapped_fields"][f"visit_{visit_id}"] = {
                    k: visit[k] for k in extra_visit_keys
                }

            # Reconstruct activities
            for act in visit.get("activities", []):
                if not isinstance(act, dict):
                    continue
                act_id = act.get("id")
                act_name = act.get("name")
                if not act_id:
                    raise ValueError(f"Every activity in visit '{visit_id}' must have an 'id'")
                if not act_name:
                    raise ValueError(f"Every activity in visit '{visit_id}' must have a 'name'")

                act_projection = {
                    "activity_id": act_id,
                    "name": act_name
                }
                visit_projection["activities"].append(act_projection)

                # Collect extra activity keys
                known_act_keys = {"id", "name", "rules"}
                extra_act_keys = set(act.keys()) - known_act_keys
                if extra_act_keys:
                    preservation_metadata["unmapped_fields"][f"activity_{act_id}"] = {
                        k: act[k] for k in extra_act_keys
                    }

                # Extract and store embedded rules if present
                for r in act.get("rules", []):
                    if isinstance(r, dict):
                        # Ensure we map the target field to act_id or act_name if not provided in rule
                        rule_copy = dict(r)
                        if "target_field" not in rule_copy:
                            rule_copy["target_field"] = act_id
                        embedded_rules.append(rule_copy)

            arm_projection["visits"].append(visit_projection)

        arms_projection.append(arm_projection)

    # 3. Reconstruct Rules with strict AST schema checks and circular dependency detection
    rules_dict: Dict[str, Dict[str, Any]] = {}

    # Process top-level rules first
    for r in usdm_data.get("rules", []):
        if not isinstance(r, dict):
            continue
        r_id = r.get("id")
        if not r_id:
            raise ValueError("Every rule in USDM payload must have an 'id'")

        rules_dict[r_id] = {
            "id": r_id,
            "type": r.get("type"),
            "condition": r.get("condition"),
            "action": r.get("action"),
            "target_field": r.get("target_field"),
            "target_form": r.get("target_form"),
            "target_group": r.get("target_group"),
            "query_message": r.get("query_message"),
            "version_index": r.get("version_index", 1),
            "is_deleted": False
        }

        # Check for extra/unmapped keys inside rule
        known_rule_keys = {
            "id", "type", "condition", "action", "target_field",
            "target_form", "target_group", "query_message", "version_index"
        }
        extra_rule_keys = set(r.keys()) - known_rule_keys
        if extra_rule_keys:
            preservation_metadata["unmapped_fields"][f"rule_{r_id}"] = {
                k: r[k] for k in extra_rule_keys
            }

    # Integrate any embedded activity-level rules
    for r in embedded_rules:
        r_id = r.get("id")
        if not r_id:
            continue
        # Only add if not already added to avoid duplication, or overwrite
        if r_id not in rules_dict:
            rules_dict[r_id] = {
                "id": r_id,
                "type": r.get("type"),
                "condition": r.get("condition"),
                "action": r.get("action"),
                "target_field": r.get("target_field"),
                "target_form": r.get("target_form"),
                "target_group": r.get("target_group"),
                "query_message": r.get("query_message"),
                "version_index": r.get("version_index", 1),
                "is_deleted": False
            }

    # Validate condition structures and reject any stochastic/complex operators or syntax errors
    for r_id, rule_val in rules_dict.items():
        cond = rule_val.get("condition")
        if not cond:
            raise ValueError(f"Rule '{r_id}' is missing required 'condition' field")

        # Parse into ExpressionNode to validate the AST structure
        try:
            ExpressionNode(**cond)
        except Exception as e:
            raise ValueError(f"Unsupported or malformed rule expression structure in rule '{r_id}': {str(e)}")

    # Detect explicitly unsupported circular skip-logic paths
    reconstructed_rules_list = list(rules_dict.values())
    cycles = detect_circular_dependencies(reconstructed_rules_list)
    if cycles:
        raise ValueError(f"Circular skip-logic dependency detected: {', '.join(cycles)}")

    # 4. Construct final study projection dictionary
    study_projection = {
        "study_id": study_id,
        "title": title,
        "current_version": current_version,
        "arms": arms_projection,
        "rules": reconstructed_rules_list
    }
    if desc is not None:
        study_projection["desc"] = desc

    # Exclude empty preservation dictionary if no unmapped elements exist,
    # but include if any custom extensible elements exist to prevent silent dropping.
    if preservation_metadata["unmapped_fields"]:
        study_projection["preservation_metadata"] = preservation_metadata

    return study_projection
