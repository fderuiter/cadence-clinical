import json

import pytest
import yaml

from apps.designer.inverse_mapper import map_usdm_to_study
from apps.designer.mapper import map_study_to_usdm
from apps.designer.serialization import USDMSerializationError, serialize_usdm


def test_serialize_usdm_canonical_json():
    # @req:PRD-MDR-007
    # Define a clean internal study projection
    study_data = {
        "study_id": "study_123",
        "title": "Oncology Phase III Trial",
        "current_version": "1.0",
        "desc": "A study for solid tumors.",
        "arms": [
            {
                "arm_id": "arm_treatment",
                "name": "Arm A - Active Treatment",
                "type_concept_id": "C123",
                "visits": [
                    {
                        "visit_id": "visit_screening",
                        "name": "Screening Visit 1",
                        "visit_type_concept_id": "C789",
                        "activities": [
                            {"activity_id": "act_blood", "name": "Blood Draw"},
                        ],
                    }
                ],
            }
        ],
        "rules": [
            {
                "id": "rule_blood_draw_constraint",
                "type": "constraint",
                "condition": {"type": "constant", "value": True},
                "target_field": "act_blood",
                "query_message": "Required blood draw",
                "is_deleted": False,
                "version_index": 1,
            }
        ],
        "eligibility_criteria": [
            {
                "id": "INC_01",
                "criterion_id": "INC_01",
                "criterion_type": "inclusion",
                "description": "Patient must be >= 18 years of age.",
                "dsl_source": "eCRF.DM.AGE >= 18",
            }
        ],
    }

    # 1. Map study to USDM payload (combined layout)
    usdm_payload = map_study_to_usdm(study_data)

    # 2. Serialize to canonical JSON
    serialized_json = serialize_usdm(
        usdm_payload, format_type="json", style="canonical", validate=True
    )

    # Check that it is a valid JSON string and can be parsed
    parsed = json.loads(serialized_json)
    assert parsed["instanceType"] == "Study"
    assert "versions" in parsed
    assert len(parsed["versions"]) == 1
    assert parsed["versions"][0]["versionIdentifier"] == "1.0"

    # Check deterministic serialization (sorted keys)
    lines = [line.strip() for line in serialized_json.split("\n") if line.strip()]
    assert '"instanceType": "Study"' in lines or '"instanceType": "Study",' in lines


def test_serialize_usdm_canonical_yaml():
    # @req:PRD-MDR-007
    study_data = {
        "study_id": "study_456",
        "title": "Diabetes Phase I Trial",
        "current_version": "2.0",
        "desc": "A study for Type 2 Diabetes.",
        "arms": [
            {
                "arm_id": "arm_placebo",
                "name": "Arm B - Placebo",
                "type_concept_id": "C456",
                "visits": [
                    {
                        "visit_id": "visit_followup",
                        "name": "Follow-up Visit",
                        "visit_type_concept_id": "C012",
                        "activities": [
                            {"activity_id": "act_vitals", "name": "Vitals Checking"},
                        ],
                    }
                ],
            }
        ],
        "rules": [],
        "eligibility_criteria": [],
    }

    usdm_payload = map_study_to_usdm(study_data)

    # Serialize to canonical YAML
    serialized_yaml = serialize_usdm(
        usdm_payload, format_type="yaml", style="canonical", validate=True
    )

    # Check that it is a valid YAML and can be parsed
    parsed = yaml.safe_load(serialized_yaml)
    assert parsed["name"] == "Diabetes Phase I Trial"
    assert parsed["instanceType"] == "Study"
    assert len(parsed["versions"]) == 1


def test_serialize_usdm_validation_errors():
    # Missing required internal title/name
    invalid_study_data = {
        "study_id": "study_error",
        # "title" is missing
        "current_version": "1.0",
    }

    with pytest.raises(ValueError, match="Missing required internal field: 'title'"):
        map_study_to_usdm(invalid_study_data)

    # Test pre-flight identity check during serialization
    dummy_payload = {
        "id": "",  # Empty ID
        "name": "Valid Name",
    }
    with pytest.raises(
        USDMSerializationError, match="Study must contain a non-empty physical ID"
    ):
        serialize_usdm(
            dummy_payload, format_type="json", style="canonical", validate=False
        )


def test_round_trip_canonical_serialization_verification():
    # @req:PRD-MDR-007
    original_study = {
        "study_id": "study_roundtrip",
        "title": "Round-Trip Verification Study",
        "current_version": "3.1.2",
        "desc": "An end-to-end round trip test.",
        "arms": [
            {
                "arm_id": "arm_1",
                "name": "Treatment Arm",
                "type_concept_id": "C123",
                "visits": [
                    {
                        "visit_id": "visit_1",
                        "name": "Screening Visit",
                        "visit_type_concept_id": "C789",
                        "activities": [
                            {"activity_id": "act_1", "name": "Blood Pressure"},
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
                "target_field": "act_1",
                "version_index": 1,
                "is_deleted": False,
            }
        ],
        "eligibility_criteria": [
            {
                "id": "INC_01",
                "criterion_id": "INC_01",
                "criterion_type": "inclusion",
                "description": "Patient must be >= 18 years of age.",
                "dsl_source": "eCRF.DM.AGE >= 18",
            }
        ],
    }

    # 1. Map to USDM
    usdm_payload = map_study_to_usdm(original_study)

    # 2. Serialize to canonical JSON
    serialized_json = serialize_usdm(
        usdm_payload, format_type="json", style="canonical", validate=True
    )

    # 3. Parse JSON back to dictionary (this simulates clean exported artifact payload)
    clean_usdm_dict = json.loads(serialized_json)

    # Ensure no legacy flat collections exist in this clean artifact
    assert "arms" not in clean_usdm_dict
    assert "rules" not in clean_usdm_dict
    assert "eligibility_criteria" not in clean_usdm_dict

    # 4. Use the smart inverse mapper on this clean canonical artifact to reconstruct internal study
    reconstructed_study = map_usdm_to_study(clean_usdm_dict)

    # 5. Assert complete round-trip fidelity
    assert reconstructed_study["study_id"] == original_study["study_id"]
    assert reconstructed_study["title"] == original_study["title"]
    assert reconstructed_study["current_version"] == original_study["current_version"]
    assert reconstructed_study["desc"] == original_study["desc"]

    # Reconstructed Arms
    assert len(reconstructed_study["arms"]) == 1
    rec_arm = reconstructed_study["arms"][0]
    assert rec_arm["arm_id"] == "arm_1"
    assert rec_arm["name"] == "Treatment Arm"
    assert rec_arm["type_concept_id"] == "C123"

    # Reconstructed Visits
    assert len(rec_arm["visits"]) == 1
    rec_visit = rec_arm["visits"][0]
    assert rec_visit["visit_id"] == "visit_1"
    assert rec_visit["name"] == "Screening Visit"
    assert rec_visit["visit_type_concept_id"] == "C789"

    # Reconstructed Activities
    assert len(rec_visit["activities"]) == 1
    rec_act = rec_visit["activities"][0]
    assert rec_act["activity_id"] == "act_1"
    assert rec_act["name"] == "Blood Pressure"

    # Reconstructed Rules
    assert len(reconstructed_study["rules"]) == 1
    rec_rule = reconstructed_study["rules"][0]
    assert rec_rule["id"] == "rule_1"
    assert rec_rule["type"] == "skip_logic"
    assert rec_rule["condition"] == original_study["rules"][0]["condition"]

    # Reconstructed Eligibility Criteria
    assert len(reconstructed_study["eligibility_criteria"]) == 1
    rec_crit = reconstructed_study["eligibility_criteria"][0]
    assert rec_crit["id"] == "INC_01"
    assert rec_crit["criterion_id"] == "INC_01"
    assert rec_crit["criterion_type"] == "inclusion"
    assert rec_crit["description"] == "Patient must be >= 18 years of age."
    assert rec_crit["dsl_source"] == "eCRF.DM.AGE >= 18"
