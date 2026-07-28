import pytest
from fastapi.testclient import TestClient

from apps.designer.comparison import compare_payloads, flatten_dict
from apps.designer.main import app as designer_app
from apps.designer.orchestration import execute_round_trip


@pytest.fixture
def client():
    return TestClient(designer_app)


def test_flatten_dict_complex():
    nested = {
        "id": "study_1",
        "nested_dict": {"a": 1, "b": "hello"},
        "nested_list": [{"x": 10}, {"y": 20}],
    }
    flat = flatten_dict(nested)
    assert flat["id"] == "study_1"
    assert flat["nested_dict.a"] == 1
    assert flat["nested_dict.b"] == "hello"
    assert flat["nested_list.[0].x"] == 10
    assert flat["nested_list.[1].y"] == 20


def test_compare_payloads_lossless_equivalence():
    original = {
        "study_id": "study_1",
        "title": "Oncology Phase III",
        "version": "1.0.0",
        "description": "Fasting required  ",
    }
    # Semantically identical with whitespace and version padding difference
    round_tripped = {
        "study_id": "study_1",
        "title": "Oncology Phase III",
        "version": "1.0",
        "description": "Fasting required",
    }

    report = compare_payloads(original, round_tripped)
    assert report["lossless"] is True
    assert report["material_difference_count"] == 0
    assert len(report["altered"]) == 0


def test_compare_payloads_lossy_mismatch():
    original = {
        "study_id": "study_1",
        "title": "Oncology Phase III",
        "version": "1.0.0",
        "description": "Fasting required",
    }
    # Material difference (title altered, version changed)
    round_tripped = {
        "study_id": "study_1",
        "title": "Oncology Phase IV",
        "version": "2.0.0",
        "description": "Fasting required",
    }

    report = compare_payloads(original, round_tripped)
    assert report["lossless"] is False
    assert report["material_difference_count"] == 2

    fields = {item["field"] for item in report["altered"]}
    assert "title" in fields
    assert "version" in fields


def test_orchestrate_internal_to_usdm_to_internal_lossless():
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
                "condition": {
                    "type": "comparison",
                    "operator": "==",
                    "operands": [
                        {"type": "field_ref", "field_ref": {"field_id": "act_blood"}},
                        {"type": "constant", "value": True},
                    ],
                },
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

    report = execute_round_trip(study_data)
    assert report["classification"] == "lossless"
    assert report["source_format"] == "internal"
    assert report["direction"] == "internal_to_USDM_to_internal"


def test_orchestrate_usdm_to_internal_to_usdm_lossless():
    # Build standard USDM payload
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
        "rules": [],
        "eligibility_criteria": [],
    }

    # Map to USDM format first
    from apps.designer.mapper import map_study_to_usdm

    usdm_payload = map_study_to_usdm(study_data)

    report = execute_round_trip(usdm_payload)
    assert report["classification"] == "lossless"
    assert report["source_format"] == "USDM"
    assert report["direction"] == "USDM_to_internal_to_USDM"


def test_orchestrate_circular_skip_logic_lossy():
    # Payload with circular skip logic
    study_data = {
        "study_id": "study_123",
        "title": "Oncology Trial",
        "current_version": "1.0",
        "arms": [],
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
            },
        ],
    }

    report = execute_round_trip(study_data)
    assert report["classification"] == "lossy"
    assert any(
        "Circular skip-logic dependency" in item
        for item in report["mapping_diagnostics"]["unsupported_constructs"]
    )


def test_orchestrate_stochastic_operator_lossy():
    # Payload with stochastic/complex operator
    study_data = {
        "study_id": "study_123",
        "title": "Oncology Trial",
        "current_version": "1.0",
        "arms": [],
        "rules": [
            {
                "id": "rule_1",
                "type": "skip_logic",
                "condition": {
                    "type": "comparison",
                    "operator": "STOCHASTIC_RANDOM_SELECT",  # stochastic operator
                    "operands": [
                        {"type": "constant", "value": 1},
                    ],
                },
                "target_field": "act_1",
            }
        ],
    }

    report = execute_round_trip(study_data)
    assert report["classification"] == "lossy"
    assert any(
        "STOCHASTIC_RANDOM_SELECT" in item
        for item in report["mapping_diagnostics"]["unsupported_constructs"]
    )


def test_api_round_trip_endpoint_internal_success(client):
    study_data = {
        "study_id": "study_api",
        "title": "API Round Trip Trial",
        "current_version": "1.0",
        "desc": "Testing API",
        "arms": [],
        "rules": [],
    }

    # Since we have mock middleware, we don't need real gateway signature to run basic test
    # unless authentication middleware intercepts and rejects it.
    # Let's see: some endpoints require Gateway headers or authentications.
    # We can pass authentications in headers using standard helper.
    from tests.test_designer_differences import get_auth_headers

    headers = get_auth_headers()

    response = client.post(
        "/api/v1/designer/round-trip", json=study_data, headers=headers
    )
    assert response.status_code == 200
    data = response.json()
    assert "classification" in data
    assert data["source_format"] == "internal"
    assert "fidelity_details" in data
