"""Unit test suite for USDM JSON parser and Study Designer graph importer.

Requirements: PRD-SYS-001
"""

import pytest
from cdisc.usdm_models import USDMStudy

from apps.designer.importers.usdm_importer import USDMImporter, USDMImportResult


@pytest.fixture
def valid_usdm_payload() -> dict:
    """Fixture providing valid USDM v3 protocol dictionary."""
    return {
        "id": "study_test_100",
        "name": "STUDY-100",
        "protocolTitle": "Oncology Phase I Protocol",
        "usdmVersion": "3.0",
        "studyDesigns": [
            {
                "id": "sd_100",
                "name": "Design 100",
                "arms": [{"id": "arm_1", "name": "Arm 1", "armType": "Treatment"}],
                "epochs": [
                    {
                        "id": "ep_1",
                        "name": "Screening",
                        "epochType": "Screening",
                        "sequenceNumber": 1,
                    }
                ],
                "encounters": [
                    {
                        "id": "enc_1",
                        "name": "Visit 1",
                        "encounterType": "Visit",
                    }
                ],
                "activities": [
                    {"id": "act_1", "name": "Lab Test", "description": "CBC"}
                ],
                "eligibilityCriteria": [
                    {
                        "id": "crit_1",
                        "name": "Age",
                        "criterionType": "Inclusion",
                        "text": ">= 18",
                    }
                ],
            }
        ],
    }


@pytest.mark.asyncio
async def test_usdm_importer_valid_dict(valid_usdm_payload: dict) -> None:
    """Validate importing valid USDM protocol dict returns expected nodes and relationships.

    Requirements: PRD-SYS-001
    """
    importer = USDMImporter()
    result = await importer.import_usdm(valid_usdm_payload)

    assert isinstance(result, USDMImportResult)
    assert result.study_id == "study_test_100"
    # 1 Study + 1 Design + 1 Arm + 1 Epoch + 1 Encounter + 1 Activity + 1 Criterion = 7 nodes
    assert result.nodes_created == 7
    # 1 HAS_DESIGN + 5 child relationships = 6 relationships
    assert result.relationships_created == 6
    assert len(result.validation_warnings) == 0


@pytest.mark.asyncio
async def test_usdm_importer_valid_model(valid_usdm_payload: dict) -> None:
    """Validate importing USDMStudy model directly.

    Requirements: PRD-SYS-001
    """
    study_model = USDMStudy.model_validate(valid_usdm_payload)
    importer = USDMImporter()
    result = await importer.import_usdm(study_model)

    assert result.study_id == "study_test_100"
    assert result.nodes_created == 7


@pytest.mark.asyncio
async def test_usdm_importer_warning_empty_designs() -> None:
    """Validate warning emitted when USDM payload has 0 study designs.

    Requirements: PRD-SYS-001
    """
    payload = {
        "id": "study_empty",
        "name": "EMPTY-001",
        "protocolTitle": "Empty Protocol",
        "usdmVersion": "3.0",
        "studyDesigns": [],
    }
    importer = USDMImporter()
    result = await importer.import_usdm(payload)

    assert result.study_id == "study_empty"
    assert result.nodes_created == 1
    assert len(result.validation_warnings) == 1
    assert "0 study designs" in result.validation_warnings[0]


@pytest.mark.asyncio
async def test_usdm_importer_invalid_payload_raises() -> None:
    """Validate invalid USDM dict raises ValueError.

    Requirements: PRD-SYS-001
    """
    invalid_payload = {"invalid_key": "no id or protocol title"}
    importer = USDMImporter()

    with pytest.raises(ValueError, match="Invalid USDM payload structure"):
        await importer.import_usdm(invalid_payload)
