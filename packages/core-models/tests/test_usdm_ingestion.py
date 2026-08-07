import hashlib
import hmac
import json
import time

import pytest
from fastapi.testclient import TestClient

from apps.designer.main import app as designer_app
from apps.designer.usdm_ingestion import (
    normalize_usdm_payload,
    resolve_usdm_version,
    safe_parse_payload,
    validate_usdm_payload,
)

client = TestClient(designer_app)


def get_auth_headers():
    user_id = "test-user"
    roles = "sponsor_designer"
    change_reason = "system_operation"
    timestamp = str(time.time())
    payload = {
        "change_reason": change_reason,
        "roles": roles,
        "timestamp": timestamp,
        "user_id": user_id,
    }
    serialized = json.dumps(payload, sort_keys=True, separators=(",", ":"))
    signature = hmac.new(
        b"internal-gateway-secret-12345", serialized.encode(), hashlib.sha256
    ).hexdigest()
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }


def test_safe_parse_payload_json():
    text = '{"id": "00000000-0000-0000-0000-000000000001", "name": "Test JSON"}'
    parsed, format_type = safe_parse_payload(text)
    assert format_type == "JSON"
    assert parsed["id"] == "00000000-0000-0000-0000-000000000001"


def test_safe_parse_payload_yaml():
    text = """
id: 00000000-0000-0000-0000-000000000001
name: Test YAML
"""
    parsed, format_type = safe_parse_payload(text)
    assert format_type == "YAML"
    assert parsed["id"] == "00000000-0000-0000-0000-000000000001"


def test_safe_parse_payload_invalid():
    with pytest.raises(ValueError):
        safe_parse_payload("  { invalid_json  ")


def test_resolve_usdm_version_v3():
    payload = {"id": "00000000-0000-0000-0000-000000000001", "versions": []}
    version, evidence = resolve_usdm_version(payload)
    assert version == "v3"
    assert any("versions" in e for e in evidence)


def test_resolve_usdm_version_v2():
    payload = {"id": "00000000-0000-0000-0000-000000000001", "studyVersions": []}
    version, evidence = resolve_usdm_version(payload)
    assert version == "v2"
    assert any("studyVersions" in e for e in evidence)


def test_resolve_usdm_version_override():
    payload = {"id": "00000000-0000-0000-0000-000000000001", "studyVersions": []}
    version, evidence = resolve_usdm_version(payload, override="v3")
    assert version == "v3"
    assert any("override" in e for e in evidence)


def test_normalize_usdm_payload_v2_to_v3():
    payload = {
        "id": "00000000-0000-0000-0000-000000000001",
        "studyVersions": [
            {
                "id": "00000000-0000-0000-0000-000000000002",
                "studyDesign": [
                    {
                        "id": "00000000-0000-0000-0000-000000000003",
                        "studyArms": [{"id": "00000000-0000-0000-0000-000000000004"}],
                    }
                ],
            }
        ],
    }
    normalized = normalize_usdm_payload(payload, "v2")
    assert "versions" in normalized
    assert "studyVersions" not in normalized
    assert (
        normalized["versions"][0]["studyDesigns"][0]["arms"][0]["id"]
        == "00000000-0000-0000-0000-000000000004"
    )


def test_validate_usdm_payload_valid_v3():
    # A complete valid USDM v3 Study representation
    valid_payload = """
id: 00000000-0000-0000-0000-000000000001
name: Completely Valid Study
instanceType: Study
versions:
  - id: 00000000-0000-0000-0000-000000000002
    versionIdentifier: "1.0"
    rationale: "Initial Version"
    studyIdentifiers: []
    titles: []
    instanceType: StudyVersion
    studyDesigns: []
"""
    report = validate_usdm_payload(valid_payload)
    assert report.validity is True
    assert report.format == "YAML"
    assert report.version == "v3"
    assert len(report.errors) == 0


def test_validate_usdm_payload_invalid_structure():
    # Missing required 'name' field and has empty 'id'
    invalid_payload = """
id: ""
versions: []
"""
    report = validate_usdm_payload(invalid_payload)
    assert report.validity is False
    assert any("non-empty physical name/title" in err.reason for err in report.errors)
    assert any("non-empty physical ID" in err.reason for err in report.errors)


def test_validate_usdm_payload_duplicate_ids():
    # Duplicate ID across elements (UUID validation must also be satisfied)
    duplicate_id_payload = """
id: 00000000-0000-0000-0000-000000000001
name: Duplicate ID Study
versions:
  - id: 00000000-0000-0000-0000-000000000001
    versionIdentifier: "1.0"
    rationale: "Initial Version"
    studyIdentifiers: []
    titles: []
    instanceType: StudyVersion
"""
    report = validate_usdm_payload(duplicate_id_payload)
    assert report.validity is False
    assert any("Duplicate physical ID" in err.reason for err in report.errors)


def test_validate_usdm_payload_warnings_custom_elements():
    # Contains a custom extensible element
    custom_payload = """
id: 00000000-0000-0000-0000-000000000001
name: Study with Custom Tags
custom_tag_x: custom_value_y
versions: []
"""
    report = validate_usdm_payload(custom_payload)
    assert report.validity is True
    assert any("custom_tag_x" in warn.reason for warn in report.warnings)


def test_validate_usdm_payload_stochastic_math_operators():
    # Rule contains unsupported trigonometric/stochastic operator 'SIN'
    stochastic_payload = """
id: 00000000-0000-0000-0000-000000000001
name: Rules Study
versions:
  - id: 00000000-0000-0000-0000-000000000002
    versionIdentifier: "1.0"
    rationale: "Initial Version"
    studyIdentifiers: []
    titles: []
    instanceType: StudyVersion
    studyDesigns:
      - id: 00000000-0000-0000-0000-000000000003
        instanceType: InterventionalStudyDesign
        name: "Design"
        studyCells: []
        rationale: "None"
        epochs: []
        population:
          id: "00000000-0000-0000-0000-000000000006"
          name: "Pop"
          instanceType: StudyDesignPopulation
        arms: []
        activities:
          - id: 00000000-0000-0000-0000-000000000004
            name: Blood Pressure
            instanceType: Activity
            rules:
              - id: rule_stochastic
                type: skip_logic
                target_field: 00000000-0000-0000-0000-000000000004
                action: hide
                condition:
                  type: function
                  operator: SIN
                  operands:
                    - type: constant
                      value: 45
"""
    report = validate_usdm_payload(stochastic_payload)
    assert report.validity is False
    assert any(
        "Unsupported or complex operator/function 'SIN'" in err.reason
        for err in report.errors
    )


def test_validate_usdm_payload_circular_skip_logic():
    # Circular skip logic between fields
    circular_payload = """
id: 00000000-0000-0000-0000-000000000001
name: Circular Rules Study
versions:
  - id: 00000000-0000-0000-0000-000000000002
    versionIdentifier: "1.0"
    rationale: "Initial Version"
    studyIdentifiers: []
    titles: []
    instanceType: StudyVersion
    studyDesigns:
      - id: 00000000-0000-0000-0000-000000000003
        instanceType: InterventionalStudyDesign
        name: "Design"
        studyCells: []
        rationale: "None"
        epochs: []
        population:
          id: "00000000-0000-0000-0000-000000000006"
          name: "Pop"
          instanceType: StudyDesignPopulation
        arms: []
        activities:
          - id: 00000000-0000-0000-0000-000000000004
            name: Field 1
            instanceType: Activity
            rules:
              - id: rule_1
                type: skip_logic
                target_field: 00000000-0000-0000-0000-000000000004
                action: hide
                condition:
                  type: field_ref
                  field_ref:
                    field_id: 00000000-0000-0000-0000-000000000005
          - id: 00000000-0000-0000-0000-000000000005
            name: Field 2
            instanceType: Activity
            rules:
              - id: rule_2
                type: skip_logic
                target_field: 00000000-0000-0000-0000-000000000005
                action: hide
                condition:
                  type: field_ref
                  field_ref:
                    field_id: 00000000-0000-0000-0000-000000000004
"""
    report = validate_usdm_payload(circular_payload)
    assert report.validity is False
    assert any(
        "Circular skip-logic dependency detected" in err.reason for err in report.errors
    )


def test_api_validate_usdm_endpoint_valid():
    valid_payload = """
id: 00000000-0000-0000-0000-000000000001
name: API Valid Study
instanceType: Study
versions: []
"""
    response = client.post(
        "/api/v1/designer/usdm/validate",
        content=valid_payload,
        headers=get_auth_headers(),
    )
    assert response.status_code == 200
    data = response.json()
    assert data["validity"] is True
    assert data["format"] == "YAML"
    assert data["version"] == "v3"


def test_api_validate_usdm_endpoint_invalid_422():
    invalid_payload = """
id: ""
name: ""
"""
    response = client.post(
        "/api/v1/designer/usdm/validate",
        content=invalid_payload,
        headers=get_auth_headers(),
    )
    assert response.status_code == 422
    problem = response.json()
    assert problem["code"] == "USDM_VALIDATION_ERROR"
    assert len(problem["invalid_params"]) > 0
