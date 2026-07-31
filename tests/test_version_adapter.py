"""
Unit tests for the USDM Version Adapter module.
Examines heuristics, overrides, normalization, evidence tracking, and defaults.
"""

from apps.designer.version_adapter import (
    infer_usdm_version,
    normalize_payload_to_canonical,
    scan_indicators,
)


def test_scan_indicators_v2():
    payload = {
        "id": "study-1",
        "studyVersions": [
            {
                "id": "v-1",
                "studyDesign": [
                    {
                        "id": "design-1",
                        "studyArms": [{"id": "arm-1"}],
                        "studyEpochs": [{"id": "epoch-1"}],
                    }
                ],
            }
        ],
    }
    v2, v3, evidence = scan_indicators(payload)
    assert v2 >= 4
    assert v3 == 0
    assert any("studyVersions" in e for e in evidence)
    assert any("studyArms" in e for e in evidence)


def test_scan_indicators_v3():
    payload = {
        "id": "study-1",
        "versions": [
            {
                "id": "v-1",
                "studyDesigns": [
                    {
                        "id": "design-1",
                        "arms": [{"id": "arm-1"}],
                        "epochs": [{"id": "epoch-1"}],
                    }
                ],
            }
        ],
    }
    v2, v3, evidence = scan_indicators(payload)
    assert v2 == 0
    assert v3 >= 4
    assert any("versions" in e for e in evidence)
    assert any("arms" in e for e in evidence)


def test_infer_usdm_version_override():
    payload = {"studyVersions": []}
    # With override "v3" on a v2 structure
    version, evidence = infer_usdm_version(payload, override="v3")
    assert version == "v3"
    assert any("override applied" in e.lower() for e in evidence)

    # With invalid override
    version, evidence = infer_usdm_version(payload, override="invalid")
    assert version == "v2"
    assert any("ignored invalid override" in e.lower() for e in evidence)


def test_infer_usdm_version_heuristics():
    # Mostly v2 indicators
    payload = {
        "id": "study-1",
        "studyDesign": [],
        "studyArms": [],
    }
    version, evidence = infer_usdm_version(payload)
    assert version == "v2"
    assert any("heuristic inference: usdm v2" in e.lower() for e in evidence)


def test_infer_usdm_version_default():
    # Empty payload or ambiguous
    payload = {"id": "some-id", "name": "some-name"}
    version, evidence = infer_usdm_version(payload)
    assert version == "v3"
    assert any("ambiguous" in e.lower() for e in evidence)


def test_normalize_payload_to_canonical_v2():
    v2_payload = {
        "id": "study-1",
        "name": "USDM v2 Trial",
        "studyVersions": [
            {
                "id": "ver-1",
                "versionIdentifier": "1.0",
                "studyDesign": [
                    {
                        "id": "design-1",
                        "name": "Parallel Design",
                        "studyArms": [{"id": "arm-1", "name": "Treatment Arm"}],
                        "studyEpochs": [{"id": "epoch-1", "name": "Screening"}],
                        "studyPopulations": [{"id": "pop-1", "name": "Adults"}],
                        "studyEstimands": [{"id": "est-1"}],
                        "studyIndications": [{"id": "ind-1"}],
                        "studyObjectives": [{"id": "obj-1"}],
                        "studyInterventions": [{"id": "int-1"}],
                    }
                ],
            }
        ],
    }
    normalized = normalize_payload_to_canonical(v2_payload, "v2")

    assert "versions" in normalized
    assert "studyVersions" not in normalized

    version_item = normalized["versions"][0]
    assert "studyDesigns" in version_item
    assert "studyDesign" not in version_item

    design_item = version_item["studyDesigns"][0]
    assert "arms" in design_item
    assert "studyArms" not in design_item
    assert design_item["arms"][0]["name"] == "Treatment Arm"

    assert "epochs" in design_item
    assert "studyEpochs" not in design_item
    assert design_item["epochs"][0]["name"] == "Screening"

    assert "population" in design_item
    assert design_item["population"]["name"] == "Adults"

    assert "estimands" in design_item
    assert "indications" in design_item
    assert "objectives" in design_item
    assert "studyInterventionIds" in design_item


def test_normalize_payload_to_canonical_v3():
    v3_payload = {
        "id": "study-1",
        "name": "USDM v3 Trial",
        "versions": [
            {
                "id": "ver-1",
                "versionIdentifier": "1.0",
                "studyDesigns": [
                    {
                        "id": "design-1",
                        "name": "Parallel Design",
                        "arms": [{"id": "arm-1", "name": "Treatment Arm"}],
                        "epochs": [{"id": "epoch-1", "name": "Screening"}],
                        "population": {"id": "pop-1", "name": "Adults"},
                    }
                ],
            }
        ],
    }
    normalized = normalize_payload_to_canonical(v3_payload, "v3")

    assert (
        normalized["versions"][0]["studyDesigns"][0]["arms"][0]["name"]
        == "Treatment Arm"
    )
    assert (
        normalized["versions"][0]["studyDesigns"][0]["population"]["name"] == "Adults"
    )
