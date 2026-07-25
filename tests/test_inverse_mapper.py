import pytest

from apps.designer.inverse_mapper import map_usdm_to_study, resolve_concept_id
from apps.designer.mapper import map_study_to_usdm


def test_resolve_concept_id():
    # C123 is present in MOCK_TERMINOLOGY
    assert (
        resolve_concept_id({"code": "C123", "decode": "Treatment Arm", "system": "NCI"})
        == "C123"
    )
    # C999 is not present in MOCK_TERMINOLOGY, should return the code itself
    assert (
        resolve_concept_id(
            {"code": "C999", "decode": "Custom Concept", "system": "NCI"}
        )
        == "C999"
    )
    # None cases
    assert resolve_concept_id(None) is None
    assert resolve_concept_id({}) is None


def test_inverse_mapping_valid_round_trip():
    # Define a clean internal study projection
    study_data = {
        "study_id": "study_1",
        "title": "Oncology Phase II",
        "current_version": "2.1",
        "desc": "A study for solid tumors.",
        "arms": [
            {
                "arm_id": "arm_1",
                "name": "Arm A",
                "type_concept_id": "C123",
                "visits": [
                    {
                        "visit_id": "visit_1",
                        "name": "Visit 1",
                        "visit_type_concept_id": "C789",
                        "activities": [
                            {"activity_id": "act_1", "name": "Blood Draw"},
                            {"activity_id": "act_2", "name": "Vitals"},
                        ],
                    }
                ],
            }
        ],
        "rules": [
            {
                "id": "rule_1",
                "type": "skip_logic",
                "condition": {
                    "type": "comparison",
                    "operator": "==",
                    "operands": [
                        {"type": "field_ref", "field_ref": {"field_id": "act_1"}},
                        {"type": "constant", "value": True},
                    ],
                },
                "action": "hide",
                "target_field": "act_2",
                "version_index": 1,
                "is_deleted": False,
            }
        ],
    }

    # 1. Map to USDM
    usdm_payload = map_study_to_usdm(study_data)

    # 2. Reconstruct internal projection using inverse mapper
    reconstructed = map_usdm_to_study(usdm_payload)

    # 3. Assert full fidelity
    assert reconstructed["study_id"] == study_data["study_id"]
    assert reconstructed["title"] == study_data["title"]
    assert reconstructed["current_version"] == study_data["current_version"]
    assert reconstructed["desc"] == study_data["desc"]

    # Compare arms
    assert len(reconstructed["arms"]) == len(study_data["arms"])
    arm_rec = reconstructed["arms"][0]
    arm_orig = study_data["arms"][0]
    assert arm_rec["arm_id"] == arm_orig["arm_id"]
    assert arm_rec["name"] == arm_orig["name"]
    assert arm_rec["type_concept_id"] == arm_orig["type_concept_id"]

    # Compare visits
    assert len(arm_rec["visits"]) == len(arm_orig["visits"])
    v_rec = arm_rec["visits"][0]
    v_orig = arm_orig["visits"][0]
    assert v_rec["visit_id"] == v_orig["visit_id"]
    assert v_rec["name"] == v_orig["name"]
    assert v_rec["visit_type_concept_id"] == v_orig["visit_type_concept_id"]

    # Compare activities
    assert len(v_rec["activities"]) == len(v_orig["activities"])
    act_rec = v_rec["activities"][0]
    act_orig = v_orig["activities"][0]
    assert act_rec["activity_id"] == act_orig["activity_id"]
    assert act_rec["name"] == act_orig["name"]

    # Compare rules
    assert len(reconstructed["rules"]) == len(study_data["rules"])
    rule_rec = reconstructed["rules"][0]
    rule_orig = study_data["rules"][0]
    assert rule_rec["id"] == rule_orig["id"]
    assert rule_rec["type"] == rule_orig["type"]
    assert rule_rec["condition"] == rule_orig["condition"]
    assert rule_rec["action"] == rule_orig["action"]
    assert rule_rec["target_field"] == rule_orig["target_field"]


def test_unmapped_fields_preservation():
    # Payload contains extra custom metadata
    usdm_payload = {
        "id": "study_1",
        "name": "Oncology Phase II",
        "version": "2.1",
        "custom_sponsor_id": "SPONSOR-1234",  # extra
        "arms": [
            {
                "id": "arm_1",
                "name": "Arm A",
                "custom_arm_index": 5,  # extra
                "arm_type": {
                    "code": "C123",
                    "decode": "Treatment Arm",
                    "system": "NCI",
                    "custom_schema": "V1",  # extra
                },
                "visits": [
                    {
                        "id": "visit_1",
                        "name": "Visit 1",
                        "custom_visit_note": "fasting required",  # extra
                        "visit_type": {
                            "code": "C789",
                            "decode": "Screening Visit",
                            "system": "NCI",
                            "unmapped_val": "hello",  # extra
                        },
                        "activities": [
                            {
                                "id": "act_1",
                                "name": "Blood Draw",
                                "custom_field": "some_extra_val",  # extra
                            }
                        ],
                    }
                ],
            }
        ],
        "rules": [
            {
                "id": "rule_1",
                "type": "skip_logic",
                "condition": {"type": "constant", "value": True},
                "action": "hide",
                "target_field": "act_2",
                "custom_rule_level": "high",  # extra
            }
        ],
    }

    reconstructed = map_usdm_to_study(usdm_payload)

    # Extra fields must be preserved in preservation_metadata
    assert "preservation_metadata" in reconstructed
    unmapped = reconstructed["preservation_metadata"]["unmapped_fields"]

    assert unmapped["study"] == {"custom_sponsor_id": "SPONSOR-1234"}
    assert unmapped["arm_arm_1"] == {"custom_arm_index": 5}
    assert unmapped["arm_arm_1_arm_type"] == {"custom_schema": "V1"}
    assert unmapped["visit_visit_1"] == {"custom_visit_note": "fasting required"}
    assert unmapped["visit_visit_1_visit_type"] == {"unmapped_val": "hello"}
    assert unmapped["activity_act_1"] == {"custom_field": "some_extra_val"}
    assert unmapped["rule_rule_1"] == {"custom_rule_level": "high"}


def test_missing_required_fields_raises_value_error():
    # Missing study ID
    with pytest.raises(ValueError, match="must contain a non-empty 'id' field"):
        map_usdm_to_study({"name": "No ID Study"})

    # Missing study name
    with pytest.raises(ValueError, match="must contain a non-empty 'name' field"):
        map_usdm_to_study({"id": "study_1"})

    # Arm missing ID
    with pytest.raises(
        ValueError, match="Every arm in USDM arms list must have an 'id'"
    ):
        map_usdm_to_study(
            {"id": "study_1", "name": "Study A", "arms": [{"name": "Arm without ID"}]}
        )

    # Arm missing Name
    with pytest.raises(
        ValueError, match="Every arm in USDM arms list must have a 'name'"
    ):
        map_usdm_to_study(
            {"id": "study_1", "name": "Study A", "arms": [{"id": "arm_1"}]}
        )

    # Visit missing ID
    with pytest.raises(
        ValueError, match="Every visit in arm 'arm_1' must have an 'id'"
    ):
        map_usdm_to_study(
            {
                "id": "study_1",
                "name": "Study A",
                "arms": [
                    {
                        "id": "arm_1",
                        "name": "Arm A",
                        "visits": [{"name": "Visit without ID"}],
                    }
                ],
            }
        )

    # Visit missing Name
    with pytest.raises(
        ValueError, match="Every visit in arm 'arm_1' must have a 'name'"
    ):
        map_usdm_to_study(
            {
                "id": "study_1",
                "name": "Study A",
                "arms": [
                    {"id": "arm_1", "name": "Arm A", "visits": [{"id": "visit_1"}]}
                ],
            }
        )


def test_unsupported_rule_expression_raises_value_error():
    # Rule with invalid expression syntax (e.g. constant node without 'value')
    payload = {
        "id": "study_1",
        "name": "Study A",
        "rules": [
            {
                "id": "rule_1",
                "type": "skip_logic",
                "condition": {
                    "type": "constant"  # missing value
                },
                "target_field": "act_1",
                "action": "hide",
            }
        ],
    }

    with pytest.raises(
        ValueError, match="Unsupported or malformed rule expression structure"
    ):
        map_usdm_to_study(payload)


def test_circular_skip_logic_rules_raises_value_error():
    # Circular skip logic dependency: act_1 depends on act_2, and act_2 depends on act_1
    payload = {
        "id": "study_1",
        "name": "Study A",
        "rules": [
            {
                "id": "rule_1",
                "type": "skip_logic",
                "condition": {
                    "type": "comparison",
                    "operator": "==",
                    "operands": [
                        {"type": "field_ref", "field_ref": {"field_id": "act_2"}},
                        {"type": "constant", "value": 1},
                    ],
                },
                "target_field": "act_1",
                "action": "hide",
            },
            {
                "id": "rule_2",
                "type": "skip_logic",
                "condition": {
                    "type": "comparison",
                    "operator": "==",
                    "operands": [
                        {"type": "field_ref", "field_ref": {"field_id": "act_1"}},
                        {"type": "constant", "value": 2},
                    ],
                },
                "target_field": "act_2",
                "action": "hide",
            },
        ],
    }

    with pytest.raises(ValueError, match="Circular skip-logic dependency detected"):
        map_usdm_to_study(payload)
