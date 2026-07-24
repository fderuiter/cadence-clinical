from unittest.mock import patch

import pytest
from fastapi import HTTPException

from apps.designer.validator import (
    CodeValidationState,
    validate_concept_codes,
    validate_study_terminology,
)


def test_validate_concept_codes_success():
    """
    Test validate_concept_codes with a mix of valid and invalid codes.
    """
    # MOCK_TERMINOLOGY has C123 and C456 as valid entries.
    # Let's mock a scenario with standard valid, invalid, and invalid-attribute cases.
    with patch("apps.designer.db.terminology_cache.get") as mock_get:

        def side_effect(code):
            if code == "C123":
                return {
                    "code": "C123",
                    "decode": "Treatment Arm",
                    "system": "NCI",
                    "valid": True,
                }
            if code == "C456":
                return {
                    "code": "C456",
                    "decode": "Placebo Arm",
                    "system": "NCI",
                    "valid": False,
                }
            return None

        mock_get.side_effect = side_effect

        reports = validate_concept_codes(["C123", "C456", "C999"])

        assert len(reports) == 3
        # Sorted order of unique codes: C123, C456, C999
        assert reports[0].concept_code == "C123"
        assert reports[0].state == CodeValidationState.VALID
        assert reports[0].decode == "Treatment Arm"

        assert reports[1].concept_code == "C456"
        assert reports[1].state == CodeValidationState.INVALID
        assert reports[1].error_message == "Concept code 'C456' is marked as invalid."

        assert reports[2].concept_code == "C999"
        assert reports[2].state == CodeValidationState.INVALID
        assert (
            reports[2].error_message
            == "Concept code 'C999' not found in terminology database."
        )


def test_validate_concept_codes_degraded():
    """
    Test validate_concept_codes where service failure leads to DEGRADED state.
    """
    with patch(
        "apps.designer.db.terminology_cache.get",
        side_effect=Exception("Database connection timeout"),
    ):
        reports = validate_concept_codes(["C123"])
        assert len(reports) == 1
        assert reports[0].concept_code == "C123"
        assert reports[0].state == CodeValidationState.DEGRADED
        assert "Database connection timeout" in reports[0].error_message


def test_validate_study_terminology():
    """
    Test validate_study_terminology traverses study structure and identifies elements correctly.
    """
    mock_study = {
        "study_id": "test_study_123",
        "title": "Oncology Study",
        "arms": [
            {
                "arm_id": "arm_active",
                "name": "Active Arm",
                "type_concept_id": "C123",
                "visits": [
                    {
                        "visit_id": "visit_screening",
                        "name": "Screening Visit",
                        "visit_type_concept_id": "C789",
                    }
                ],
            },
            {
                "arm_id": "arm_placebo",
                "name": "Placebo Arm",
                "type_concept_id": "C456",
                "visits": [
                    {
                        "visit_id": "visit_followup",
                        "name": "Follow-up Visit",
                        "visit_type_concept_id": "C012",
                    }
                ],
            },
        ],
    }

    with patch("apps.designer.db.terminology_cache.get") as mock_get:

        def side_effect(code):
            # C123, C789 are valid. C456 is invalid. C012 will trigger degraded/exception.
            if code == "C123":
                return {
                    "code": "C123",
                    "decode": "Treatment Arm",
                    "system": "NCI",
                    "valid": True,
                }
            if code == "C789":
                return {
                    "code": "C789",
                    "decode": "Screening Visit",
                    "system": "NCI",
                    "valid": True,
                }
            if code == "C456":
                return {
                    "code": "C456",
                    "decode": "Placebo Arm",
                    "system": "NCI",
                    "valid": False,
                }
            if code == "C012":
                raise Exception("Upstream EVS server unresponsive")
            return None

        mock_get.side_effect = side_effect

        report = validate_study_terminology("test_study_123", study_data=mock_study)

        assert report.study_id == "test_study_123"
        assert report.is_valid is False  # Has invalid & degraded concepts
        assert report.total_concepts == 4
        assert report.valid_count == 2
        assert report.invalid_count == 1
        assert report.degraded_count == 1

        # Check details of each concept report
        concept_reports = {c.concept_code: c for c in report.concepts}

        # C123
        c123_rep = concept_reports["C123"]
        assert c123_rep.state == CodeValidationState.VALID
        assert len(c123_rep.references) == 1
        assert c123_rep.references[0].element_type == "arm"
        assert c123_rep.references[0].element_id == "arm_active"
        assert c123_rep.references[0].element_name == "Active Arm"
        assert c123_rep.references[0].attribute == "type_concept_id"

        # C789
        c789_rep = concept_reports["C789"]
        assert c789_rep.state == CodeValidationState.VALID
        assert len(c789_rep.references) == 1
        assert c789_rep.references[0].element_type == "visit"
        assert c789_rep.references[0].element_id == "visit_screening"
        assert c789_rep.references[0].element_name == "Screening Visit"
        assert c789_rep.references[0].attribute == "visit_type_concept_id"

        # C456 (invalid)
        c456_rep = concept_reports["C456"]
        assert c456_rep.state == CodeValidationState.INVALID
        assert len(c456_rep.references) == 1
        assert c456_rep.references[0].element_type == "arm"
        assert c456_rep.references[0].element_id == "arm_placebo"

        # C012 (degraded)
        c012_rep = concept_reports["C012"]
        assert c012_rep.state == CodeValidationState.DEGRADED
        assert len(c012_rep.references) == 1
        assert c012_rep.references[0].element_type == "visit"
        assert c012_rep.references[0].element_id == "visit_followup"
        assert "Upstream EVS server unresponsive" in c012_rep.error_message


def test_validate_study_terminology_fully_valid():
    """
    Test validate_study_terminology for a study with completely valid concept codes.
    """
    mock_study = {
        "study_id": "test_study_valid",
        "title": "Oncology Study",
        "arms": [
            {
                "arm_id": "arm_active",
                "name": "Active Arm",
                "type_concept_id": "C123",
                "visits": [
                    {
                        "visit_id": "visit_screening",
                        "name": "Screening Visit",
                        "visit_type_concept_id": "C789",
                    }
                ],
            }
        ],
    }

    with patch("apps.designer.db.terminology_cache.get") as mock_get:

        def side_effect(code):
            if code == "C123":
                return {
                    "code": "C123",
                    "decode": "Treatment Arm",
                    "system": "NCI",
                    "valid": True,
                }
            if code == "C789":
                return {
                    "code": "C789",
                    "decode": "Screening Visit",
                    "system": "NCI",
                    "valid": True,
                }
            return None

        mock_get.side_effect = side_effect

        report = validate_study_terminology("test_study_valid", study_data=mock_study)
        assert report.is_valid is True
        assert report.total_concepts == 2
        assert report.valid_count == 2
        assert report.invalid_count == 0
        assert report.degraded_count == 0


@pytest.mark.asyncio
async def test_validate_study_terminology_endpoint_success():
    """
    Test the FastAPI router endpoint logic for validate_study_terminology_endpoint.
    """
    from apps.designer.main import validate_study_terminology_endpoint

    mock_study = {
        "study_id": "study_1",
        "title": "Oncology Phase II",
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
                    }
                ],
            }
        ],
    }

    with (
        patch("apps.designer.validator.get_study_projection", return_value=mock_study),
        patch("apps.designer.db.terminology_cache.get") as mock_get,
    ):
        mock_get.return_value = {
            "code": "C123",
            "decode": "Test",
            "system": "NCI",
            "valid": True,
        }

        report = await validate_study_terminology_endpoint("study_1")
        assert report.study_id == "study_1"
        assert report.total_concepts == 2


@pytest.mark.asyncio
async def test_validate_study_terminology_endpoint_not_found():
    """
    Test that the endpoint raises HTTP 404 if the study is not found.
    """
    from apps.designer.main import validate_study_terminology_endpoint

    with patch("apps.designer.validator.get_study_projection", return_value=None):
        with pytest.raises(HTTPException) as exc_info:
            await validate_study_terminology_endpoint("non_existent")
        assert exc_info.value.status_code == 404
        assert "Study with ID" in exc_info.value.detail
