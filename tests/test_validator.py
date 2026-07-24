import uuid
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from apps.designer.validator import generate_alignment_report
from tests.test_content_assembly import base_study  # noqa: F401


@pytest.mark.asyncio
async def test_generate_alignment_report() -> None:
    """
    Test the generation of an alignment report for a mock study.

    Mocks the external HTTP call to the study registry to return a valid
    USDM payload and verifies the report evaluates structural completeness accurately.
    """
    study_id = str(uuid.uuid4())

    mock_payload = {
        "id": study_id,
        "name": "Test Study",
        "description": None,
        "label": None,
        "versions": [],
        "documentedBy": [],
        "instanceType": "Study",
    }

    mock_response = AsyncMock()
    mock_response.json = MagicMock(return_value=mock_payload)
    mock_response.raise_for_status = MagicMock()

    # Mock httpx.AsyncClient
    class MockAsyncClient:
        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc_val, exc_tb):
            pass

        async def get(self, url, timeout=None):
            return mock_response

    with patch("httpx.AsyncClient", return_value=MockAsyncClient()):
        report = await generate_alignment_report(study_id)

    assert report.study_id == study_id
    assert len(report.unmapped_activities) == 0
    assert len(report.complete_activities) == 0
    assert len(report.incomplete_activities) == 0


@pytest.mark.asyncio
async def test_generate_alignment_report_with_mappings(base_study) -> None:  # noqa: F811
    """
    Test the study alignment report using realistic nested USDM and ODM payloads,
    proving in-memory flattening and path matching function exactly as specified.
    """
    import json

    study_obj = base_study
    study_id = str(study_obj.id)

    usdm_payload = json.loads(study_obj.model_dump_json())

    # Dynamically inject the biomedicalConceptIds into activities to simulate mapped items
    for version in usdm_payload.get("versions", []):
        for design in version.get("studyDesigns", []):
            for act in design.get("activities", []):
                if act.get("id") == "act-vitals":
                    act["biomedicalConceptIds"] = ["sys_bp", "dia_bp"]
                elif act.get("id") == "act-blood":
                    act["biomedicalConceptIds"] = ["hemoglobin"]

    # Define an ODM payload mapping some items (sys_bp, dia_bp) but not others,
    # and containing an extra unmapped ODM item (heart_rate).
    odm_payload = {
        "forms": [
            {
                "id": "form-vitals",
                "itemGroups": [
                    {
                        "id": "group-vitals",
                        "items": [
                            {"id": "sys_bp", "name": "Systolic Blood Pressure"},
                            {"id": "dia_bp", "name": "Diastolic Blood Pressure"},
                            {"id": "heart_rate", "name": "Heart Rate"}
                        ]
                    }
                ]
            }
        ]
    }

    # We embed the ODM payload in the study registry payload itself as "odm_payload"
    usdm_payload["odm_payload"] = odm_payload

    mock_response = AsyncMock()
    mock_response.json = MagicMock(return_value=usdm_payload)
    mock_response.raise_for_status = MagicMock()

    class MockAsyncClient:
        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc_val, exc_tb):
            pass

        async def get(self, url, timeout=None):
            return mock_response

    with patch("httpx.AsyncClient", return_value=MockAsyncClient()):
        report = await generate_alignment_report(study_id)

    assert report.study_id == study_id

    # "act-vitals" has both its items mapped ("sys_bp", "dia_bp")
    assert len(report.complete_activities) == 2
    assert report.complete_activities[0].activity_def_id == "act-vitals"
    assert report.complete_activities[0].epoch_id == "epoch-tx"
    assert report.complete_activities[0].scheduled_event_id in ("enc-v1", "enc-v2")
    assert len(report.complete_activities[0].mapped_items) == 2
    assert len(report.complete_activities[0].unmapped_items) == 0

    # "act-blood" has its item unmapped ("hemoglobin")
    assert len(report.unmapped_activities) >= 1
    blood_act = [act for act in report.unmapped_activities if act.activity_def_id == "act-blood"][0]
    assert len(blood_act.unmapped_items) == 1
    assert blood_act.unmapped_items[0].item_id == "hemoglobin"

    # "heart_rate" exists in ODM but is not present in USDM activities/items
    assert len(report.unmapped_odm_items) >= 1
    unmapped_ids = [item["item_id"] for item in report.unmapped_odm_items]
    assert "heart_rate" in unmapped_ids

