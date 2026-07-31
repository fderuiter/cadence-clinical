import pytest
from apps.designer.usdm_adapter import resolve_usdm_version, normalize_usdm_payload
from apps.designer.usdm_ingestion import safe_parse_payload, validate_usdm_payload


def test_resolve_usdm_version_heuristics_v2_study_arms():
    # Nested studyArms indicative of v2
    payload = {
        "id": "00000000-0000-0000-0000-000000000001",
        "studyVersions": [
            {
                "id": "00000000-0000-0000-0000-000000000002",
                "studyDesigns": [
                    {
                        "id": "00000000-0000-0000-0000-000000000003",
                        "studyArms": [{"id": "00000000-0000-0000-0000-000000000004"}]
                    }
                ]
            }
        ]
    }
    version, evidence = resolve_usdm_version(payload)
    assert version == "v2"
    assert any("studyArms" in ev for ev in evidence)


def test_resolve_usdm_version_heuristics_v2_study_epochs():
    # Nested studyEpochs indicative of v2
    payload = {
        "id": "00000000-0000-0000-0000-000000000001",
        "studyVersions": [
            {
                "id": "00000000-0000-0000-0000-000000000002",
                "studyDesigns": [
                    {
                        "id": "00000000-0000-0000-0000-000000000003",
                        "studyEpochs": [{"id": "00000000-0000-0000-0000-000000000005"}]
                    }
                ]
            }
        ]
    }
    version, evidence = resolve_usdm_version(payload)
    assert version == "v2"
    assert any("studyEpochs" in ev for ev in evidence)


def test_resolve_usdm_version_heuristics_v2_designs():
    # 'designs' nested inside version object indicative of v2
    payload = {
        "id": "00000000-0000-0000-0000-000000000001",
        "versions": [
            {
                "id": "00000000-0000-0000-0000-000000000002",
                "designs": [{"id": "00000000-0000-0000-0000-000000000003"}]
            }
        ]
    }
    version, evidence = resolve_usdm_version(payload)
    assert version == "v2"
    assert any("designs" in ev for ev in evidence)


def test_resolve_usdm_version_heuristics_v3_arms():
    # Nested arms/epochs indicative of v3
    payload = {
        "id": "00000000-0000-0000-0000-000000000001",
        "versions": [
            {
                "id": "00000000-0000-0000-0000-000000000002",
                "studyDesigns": [
                    {
                        "id": "00000000-0000-0000-0000-000000000003",
                        "arms": [{"id": "00000000-0000-0000-0000-000000000004"}],
                        "epochs": [{"id": "00000000-0000-0000-0000-000000000005"}]
                    }
                ]
            }
        ]
    }
    version, evidence = resolve_usdm_version(payload)
    assert version == "v3"
    assert any("arms" in ev or "epochs" in ev for ev in evidence)


def test_resolve_usdm_version_heuristics_default():
    # Payload with no indicative keys defaults sensibly to v3
    payload = {
        "id": "00000000-0000-0000-0000-000000000001"
    }
    version, evidence = resolve_usdm_version(payload)
    assert version == "v3"
    assert any("No version-specific keys" in ev for ev in evidence)


def test_safe_parse_payload_empty_error():
    with pytest.raises(ValueError, match="Empty payload"):
        safe_parse_payload("")


def test_safe_parse_payload_not_dict_json():
    with pytest.raises(ValueError, match="JSON payload must be a dictionary"):
        safe_parse_payload("[]")


def test_safe_parse_payload_not_dict_yaml():
    with pytest.raises(ValueError, match="Parsed payload is not a dictionary"):
        safe_parse_payload("string_value")


def test_safe_parse_payload_invalid_yaml_and_json():
    with pytest.raises(ValueError, match="Payload parsing failed as both JSON and YAML"):
        safe_parse_payload(" { invalid json ")


def test_validate_usdm_payload_custom_extensible_elements():
    # Test warnings for custom keys (and use a valid UUID to satisfy Pydantic structure)
    payload = """
id: 00000000-0000-0000-0000-000000000001
name: Test Study
versions: []
customExtensionKey: some-value
"""
    report = validate_usdm_payload(payload)
    assert report.validity is True
    assert any(w.field == "customExtensionKey" for w in report.warnings)


def test_resolve_usdm_version_invalid_override_ignored():
    # Explicit override that is invalid (ignored and falls back to heuristics)
    payload = {
        "studyVersions": []
    }
    version, evidence = resolve_usdm_version(payload, override="v5")
    assert version == "v2"
    assert any("Ignored invalid override 'v5'" in ev for ev in evidence)
