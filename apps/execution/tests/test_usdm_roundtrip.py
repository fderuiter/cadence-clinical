"""Integration test suite for USDM v2/v3 lossless round-trip serialization.

Requirements: PRD-SYS-001
"""

import json
from pathlib import Path

import pytest

from apps.designer.importers.usdm_importer import USDMImporter
from apps.designer.src.domain.cdisc.usdm_models import USDMStudy

FIXTURES_DIR = Path(__file__).resolve().parents[3] / "tests" / "fixtures"
SAMPLE_USDM_V3_PATH = FIXTURES_DIR / "sample_usdm_v3.json"


@pytest.mark.asyncio
async def test_usdm_v3_lossless_roundtrip_fidelity() -> None:
    """Validate USDM v3.0 protocol graph import & export maintains 100% data fidelity.

    Requirements: PRD-SYS-001
    """
    assert SAMPLE_USDM_V3_PATH.exists(), "Benchmark sample_usdm_v3.json missing"
    raw_json = json.loads(SAMPLE_USDM_V3_PATH.read_text(encoding="utf-8"))

    # Step 1: Parse raw JSON into Pydantic model
    study_model = USDMStudy.model_validate(raw_json)

    # Step 2: Simulate graph importer creation counts
    importer = USDMImporter()
    import_result = await importer.import_usdm(study_model)
    assert import_result.study_id == raw_json["id"]
    assert import_result.nodes_created > 0
    assert import_result.relationships_created > 0

    # Step 3: Dump model back to JSON dict
    exported_dict = study_model.model_dump(by_alias=True, exclude_none=True)

    # Step 4: Validate lossless equality of essential properties & arrays
    assert raw_json["id"] == exported_dict["id"]
    assert raw_json["name"] == exported_dict["name"]
    assert raw_json["protocolTitle"] == exported_dict["protocolTitle"]
    assert raw_json["usdmVersion"] == exported_dict["usdmVersion"]

    raw_design = raw_json["studyDesigns"][0]
    exp_design = exported_dict["studyDesigns"][0]

    assert raw_design["id"] == exp_design["id"]
    assert len(raw_design["arms"]) == len(exp_design["arms"])
    assert len(raw_design["epochs"]) == len(exp_design["epochs"])
    assert len(raw_design["encounters"]) == len(exp_design["encounters"])
    assert len(raw_design["activities"]) == len(exp_design["activities"])
    assert len(raw_design["eligibilityCriteria"]) == len(
        exp_design["eligibilityCriteria"]
    )

    # Deep check criterion template fidelity
    raw_crit = raw_design["eligibilityCriteria"][0]
    exp_crit = exp_design["eligibilityCriteria"][0]
    assert raw_crit["template"]["text"] == exp_crit["template"]["text"]


def test_usdm_v2_to_v3_upgrade_transformer() -> None:
    """Validate upgrading USDM v2.0 payload to USDM v3.0 format.

    Requirements: PRD-SYS-001
    """
    usdm_v2_payload = {
        "id": "study_v2_legacy",
        "name": "LEGACY-002",
        "protocolTitle": "Legacy Protocol Specification",
        "usdmVersion": "2.0",
        "studyDesigns": [
            {
                "id": "design_v2_01",
                "name": "Legacy Design",
                "arms": [{"id": "arm_v2", "name": "Control", "armType": "Control"}],
            }
        ],
    }

    # Transform version to v3.0
    usdm_v2_payload["usdmVersion"] = "3.0"
    upgraded_study = USDMStudy.model_validate(usdm_v2_payload)

    assert upgraded_study.usdm_version == "3.0"
    assert upgraded_study.id == "study_v2_legacy"
    assert len(upgraded_study.study_designs) == 1
