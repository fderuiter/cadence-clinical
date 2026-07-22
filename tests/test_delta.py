from unittest.mock import AsyncMock, MagicMock

import pytest

from apps.designer.delta import (
    create_library_object_version,
    create_study_root,
    get_study_differences,
    update_study_properties,
)


@pytest.mark.asyncio
async def test_create_study_root():
    driver_mock = MagicMock()
    session_mock = AsyncMock()
    session_ctx = AsyncMock()
    session_ctx.__aenter__.return_value = session_mock
    driver_mock.session.return_value = session_ctx

    result_mock = AsyncMock()
    result_mock.single.return_value = {"id": "study_1"}
    session_mock.run.return_value = result_mock

    res = await create_study_root(driver_mock, "study_1")
    assert res == "study_1"
    session_mock.run.assert_called_once()
    assert "MERGE (s:Study {id: $study_id})" in session_mock.run.call_args[0][0]


@pytest.mark.asyncio
async def test_create_library_object_version_existing():
    driver_mock = MagicMock()
    session_mock = AsyncMock()
    session_ctx = AsyncMock()
    session_ctx.__aenter__.return_value = session_mock
    driver_mock.session.return_value = session_ctx

    check_res_mock = AsyncMock()
    check_res_mock.single.return_value = True

    result_mock = AsyncMock()
    result_mock.single.return_value = {"new_props": {"name": "Test Lib V2"}}

    # Setting side effect to return check_res_mock first, then result_mock
    session_mock.run.side_effect = [check_res_mock, result_mock]

    res = await create_library_object_version(
        driver_mock, "lib_1", {"name": "Test Lib V2"}
    )
    assert res == {"name": "Test Lib V2"}
    assert session_mock.run.call_count == 2
    assert "MATCH (old:LibraryObject" in session_mock.run.call_args_list[1][0][0]


@pytest.mark.asyncio
async def test_create_library_object_version_new():
    driver_mock = MagicMock()
    session_mock = AsyncMock()
    session_ctx = AsyncMock()
    session_ctx.__aenter__.return_value = session_mock
    driver_mock.session.return_value = session_ctx

    check_res_mock = AsyncMock()
    check_res_mock.single.return_value = False

    result_mock = AsyncMock()
    result_mock.single.return_value = {
        "new_props": {"name": "Test Lib V1", "version": 1}
    }

    session_mock.run.side_effect = [check_res_mock, result_mock]

    res = await create_library_object_version(
        driver_mock, "lib_2", {"name": "Test Lib V1"}
    )
    assert res == {"name": "Test Lib V1", "version": 1}
    assert session_mock.run.call_count == 2
    assert "MERGE (new:LibraryObject" in session_mock.run.call_args_list[1][0][0]


@pytest.mark.asyncio
async def test_update_study_properties():
    driver_mock = MagicMock()
    session_mock = AsyncMock()
    session_ctx = AsyncMock()
    session_ctx.__aenter__.return_value = session_mock
    driver_mock.session.return_value = session_ctx

    result_mock = AsyncMock()
    result_mock.single.return_value = {"action_id": "action_uuid"}
    session_mock.run.return_value = result_mock

    res = await update_study_properties(
        driver_mock, "study_1", "user_1", "change reason", {"title": "New Title"}
    )

    assert res == "action_uuid"
    session_mock.run.assert_called_once()
    assert "CREATE (a:Action" in session_mock.run.call_args[0][0]


@pytest.mark.asyncio
async def test_get_study_differences():
    driver_mock = MagicMock()
    session_mock = AsyncMock()
    session_ctx = AsyncMock()
    session_ctx.__aenter__.return_value = session_mock
    driver_mock.session.return_value = session_ctx

    result_mock = AsyncMock()
    result_mock.single.return_value = {
        "p1": {"title": "Old Title", "phase": "I", "unchanged": "value"},
        "p2": {
            "title": "New Title",
            "phase": "II",
            "unchanged": "value",
            "new_field": "val",
        },
        "t1": 1,
        "t2": 2,
    }
    session_mock.run.return_value = result_mock

    diffs = await get_study_differences(driver_mock, "study_1", "action_1", "action_2")

    # We expect 'title', 'phase', and 'new_field' to be in differences
    assert len(diffs) == 3

    fields = [d["field"] for d in diffs]
    assert "title" in fields
    assert "phase" in fields
    assert "new_field" in fields

    for d in diffs:
        if d["field"] == "title":
            assert d["old_value"] == "Old Title"
            assert d["new_value"] == "New Title"
        elif d["field"] == "phase":
            assert d["old_value"] == "I"
            assert d["new_value"] == "II"
        elif d["field"] == "new_field":
            assert d["old_value"] is None
            assert d["new_value"] == "val"
