"""Unit test suite for CDISC USDM v2/v3 Pydantic data models.

Requirements: PRD-SYS-001
"""

import pytest
from cdisc.usdm_models import (
    Activity,
    EligibilityCriterion,
    Encounter,
    StudyArm,
    StudyDesign,
    StudyEpoch,
    USDMStudy,
)

import packages  # noqa: F401


@pytest.fixture
def sample_usdm_v3_dict() -> dict:
    """Fixture providing a standard sample USDM v3.0 JSON payload."""
    return {
        "id": "study_001_usdm",
        "name": "CADENCE-001",
        "protocolTitle": "A Phase II Randomized Clinical Trial",
        "usdmVersion": "3.0",
        "studyDesigns": [
            {
                "id": "sd_01",
                "name": "Main Study Design",
                "designType": "Parallel",
                "arms": [
                    {
                        "id": "arm_01",
                        "name": "Arm A (Drug 10mg)",
                        "armType": "Experimental",
                    },
                    {
                        "id": "arm_02",
                        "name": "Arm B (Placebo)",
                        "armType": "Placebo Comparator",
                    },
                ],
                "epochs": [
                    {
                        "id": "epoch_01",
                        "name": "Screening",
                        "epochType": "Screening",
                        "sequenceNumber": 1,
                    },
                    {
                        "id": "epoch_02",
                        "name": "Treatment",
                        "epochType": "Treatment",
                        "sequenceNumber": 2,
                    },
                ],
                "encounters": [
                    {
                        "id": "enc_01",
                        "name": "Visit 1 - Screening",
                        "encounterType": "Screening Visit",
                    },
                    {
                        "id": "enc_02",
                        "name": "Visit 2 - Baseline",
                        "encounterType": "Baseline Visit",
                    },
                ],
                "activities": [
                    {
                        "id": "act_01",
                        "name": "Vital Signs Measurement",
                        "description": "Blood pressure and pulse",
                    }
                ],
                "eligibilityCriteria": [
                    {
                        "id": "crit_01",
                        "name": "Age Requirement",
                        "criterionType": "Inclusion",
                        "text": "Subject must be >= 18 years of age",
                    }
                ],
            }
        ],
    }


def test_usdm_study_parsing(sample_usdm_v3_dict: dict) -> None:
    """Validate parsing sample USDM v3 payload into Pydantic USDMStudy model.

    Requirements: PRD-SYS-001
    """
    study = USDMStudy.model_validate(sample_usdm_v3_dict)

    assert study.id == "study_001_usdm"
    assert study.name == "CADENCE-001"
    assert study.protocol_title == "A Phase II Randomized Clinical Trial"
    assert study.usdm_version == "3.0"
    assert len(study.study_designs) == 1

    design = study.study_designs[0]
    assert isinstance(design, StudyDesign)
    assert design.id == "sd_01"
    assert len(design.arms) == 2
    assert isinstance(design.arms[0], StudyArm)
    assert design.arms[0].name == "Arm A (Drug 10mg)"

    assert len(design.epochs) == 2
    assert isinstance(design.epochs[0], StudyEpoch)
    assert design.epochs[0].sequence_number == 1

    assert len(design.encounters) == 2
    assert isinstance(design.encounters[0], Encounter)
    assert design.encounters[0].name == "Visit 1 - Screening"

    assert len(design.activities) == 1
    assert isinstance(design.activities[0], Activity)

    assert len(design.eligibility_criteria) == 1
    assert isinstance(design.eligibility_criteria[0], EligibilityCriterion)
    assert design.eligibility_criteria[0].criterion_type == "Inclusion"


def test_usdm_study_dump_by_alias(sample_usdm_v3_dict: dict) -> None:
    """Validate USDM study model dump produces matching JSON dict structure.

    Requirements: PRD-SYS-001
    """
    study = USDMStudy.model_validate(sample_usdm_v3_dict)
    dumped = study.model_dump(by_alias=True, exclude_none=True)

    assert dumped["id"] == sample_usdm_v3_dict["id"]
    assert dumped["protocolTitle"] == sample_usdm_v3_dict["protocolTitle"]
    assert (
        dumped["studyDesigns"][0]["eligibilityCriteria"][0]["criterionType"]
        == "Inclusion"
    )
