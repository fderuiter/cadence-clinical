import pytest
from unittest.mock import AsyncMock, MagicMock
from apps.designer.validator import (
    get_unmapped_odm_items,
    get_unmapped_crf_item_values,
    evaluate_epoch_activities,
    generate_alignment_report
)

@pytest.mark.asyncio
async def test_get_unmapped_odm_items():
    session_mock = AsyncMock()
    
    # Mock result iteration
    async def mock_records():
        yield {"internal_id": 1, "node_id": "odm_1", "node_labels": ["ODMItem"]}
        yield {"internal_id": 2, "node_id": "odm_2", "node_labels": ["CDISCODMItem"]}

    result_mock = AsyncMock()
    result_mock.__aiter__.side_effect = lambda: mock_records()
    session_mock.run.return_value = result_mock

    items = await get_unmapped_odm_items(session_mock)

    assert len(items) == 2
    assert items[0]["internal_id"] == 1
    assert items[0]["node_id"] == "odm_1"
    assert items[1]["node_id"] == "odm_2"
    session_mock.run.assert_called_once()

@pytest.mark.asyncio
async def test_get_unmapped_crf_item_values():
    session_mock = AsyncMock()
    
    # Mock result iteration
    async def mock_records():
        yield {"internal_id": 3, "node_id": "crf_1", "node_labels": ["CRFItemValue"]}

    result_mock = AsyncMock()
    result_mock.__aiter__.side_effect = lambda: mock_records()
    session_mock.run.return_value = result_mock

    items = await get_unmapped_crf_item_values(session_mock)

    assert len(items) == 1
    assert items[0]["internal_id"] == 3
    assert items[0]["node_id"] == "crf_1"
    session_mock.run.assert_called_once()

@pytest.mark.asyncio
async def test_evaluate_epoch_activities():
    session_mock = AsyncMock()
    
    # We want to return 3 records testing complete, incomplete, unmapped statuses
    async def mock_records():
        # Complete
        yield {
            "epoch_id": "ep1", "epoch_internal_id": 10,
            "scheduled_event_id": "se1", "sei_internal_id": 11,
            "activity_def_id": "ad1", "ad_internal_id": 12,
            "items": [
                {"item_id": "ai1", "internal_id": 100, "is_mapped": True},
                {"item_id": "ai2", "internal_id": 101, "is_mapped": True}
            ]
        }
        # Incomplete
        yield {
            "epoch_id": "ep1", "epoch_internal_id": 10,
            "scheduled_event_id": "se2", "sei_internal_id": 13,
            "activity_def_id": "ad2", "ad_internal_id": 14,
            "items": [
                {"item_id": "ai3", "internal_id": 102, "is_mapped": True},
                {"item_id": "ai4", "internal_id": 103, "is_mapped": False}
            ]
        }
        # Unmapped (items but none mapped)
        yield {
            "epoch_id": "ep2", "epoch_internal_id": 20,
            "scheduled_event_id": "se3", "sei_internal_id": 21,
            "activity_def_id": "ad3", "ad_internal_id": 22,
            "items": [
                {"item_id": "ai5", "internal_id": 104, "is_mapped": False}
            ]
        }
        # Unmapped (no items)
        yield {
            "epoch_id": "ep3", "epoch_internal_id": 30,
            "scheduled_event_id": "se4", "sei_internal_id": 31,
            "activity_def_id": "ad4", "ad_internal_id": 32,
            "items": []
        }

    result_mock = AsyncMock()
    result_mock.__aiter__.side_effect = lambda: mock_records()
    session_mock.run.return_value = result_mock

    complete, incomplete, unmapped = await evaluate_epoch_activities(session_mock, "study_xyz")

    assert len(complete) == 1
    assert complete[0].status == "complete"
    assert len(complete[0].mapped_items) == 2
    
    assert len(incomplete) == 1
    assert incomplete[0].status == "incomplete"
    assert len(incomplete[0].unmapped_items) == 1
    assert len(incomplete[0].mapped_items) == 1
    
    assert len(unmapped) == 2
    assert unmapped[0].status == "unmapped"
    assert len(unmapped[0].unmapped_items) == 1
    assert unmapped[1].status == "unmapped"

    session_mock.run.assert_called_once()
    args, kwargs = session_mock.run.call_args
    assert kwargs["study_id"] == "study_xyz"

@pytest.mark.asyncio
async def test_generate_alignment_report():
    driver_mock = MagicMock()
    session_mock = AsyncMock()
    
    # We need driver.session() context manager to return session_mock
    session_ctx = AsyncMock()
    session_ctx.__aenter__.return_value = session_mock
    driver_mock.session.return_value = session_ctx

    async def odm_records():
        yield {"internal_id": 1, "node_id": "odm_1", "node_labels": ["ODMItem"]}

    async def crf_records():
        yield {"internal_id": 2, "node_id": "crf_1", "node_labels": ["CRFItem"]}

    async def eval_records():
        yield {
            "epoch_id": "ep1", "epoch_internal_id": 10,
            "scheduled_event_id": "se1", "sei_internal_id": 11,
            "activity_def_id": "ad1", "ad_internal_id": 12,
            "items": [
                {"item_id": "ai1", "internal_id": 100, "is_mapped": True}
            ]
        }

    # Custom side effect for run depending on the query
    def run_side_effect(query, **kwargs):
        res_mock = AsyncMock()
        if "AND NOT (odm)-[]->(:ActivityItem" in query:
            res_mock.__aiter__.side_effect = lambda: odm_records()
        elif "AND NOT (crf)-[]->(:ActivityDefinition)" in query:
            res_mock.__aiter__.side_effect = lambda: crf_records()
        else:
            res_mock.__aiter__.side_effect = lambda: eval_records()
        return res_mock

    session_mock.run.side_effect = run_side_effect

    report = await generate_alignment_report(driver_mock, "study_123")

    assert report.study_id == "study_123"
    assert len(report.unmapped_odm_items) == 1
    assert len(report.unmapped_crf_item_values) == 1
    assert len(report.complete_activities) == 1
    assert len(report.incomplete_activities) == 0
    assert len(report.unmapped_activities) == 0

    assert report.unmapped_odm_items[0]["node_id"] == "odm_1"
