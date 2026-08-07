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

    tx_mock = AsyncMock()
    tx_mock.__aenter__.return_value = tx_mock
    session_mock.begin_transaction.return_value = tx_mock

    result_mock = AsyncMock()
    result_mock.single.return_value = {"id": "study_1"}
    tx_mock.run.return_value = result_mock

    res = await create_study_root(driver_mock, "study_1")
    assert res == "study_1"
    tx_mock.run.assert_called_once()
    assert "MERGE (s:Study {id: $study_id})" in tx_mock.run.call_args[0][0]


@pytest.mark.asyncio
async def test_create_library_object_version_existing():
    driver_mock = MagicMock()
    session_mock = AsyncMock()
    session_ctx = AsyncMock()
    session_ctx.__aenter__.return_value = session_mock
    driver_mock.session.return_value = session_ctx

    tx_mock = AsyncMock()
    tx_mock.__aenter__.return_value = tx_mock
    session_mock.begin_transaction.return_value = tx_mock

    check_res_mock = AsyncMock()
    check_res_mock.single.return_value = True

    lock_res_mock = AsyncMock()
    lock_res_mock.single.return_value = {"id": "lib_1"}

    result_mock = AsyncMock()
    result_mock.single.return_value = {"new_props": {"name": "Test Lib V2"}}

    # Setting side effect to return check_res_mock, lock_res_mock, then result_mock
    tx_mock.run.side_effect = [check_res_mock, lock_res_mock, result_mock]

    res = await create_library_object_version(
        driver_mock, "lib_1", {"name": "Test Lib V2"}
    )
    assert res == {"name": "Test Lib V2"}
    assert tx_mock.run.call_count == 3
    assert "MATCH (old:LibraryObject" in tx_mock.run.call_args_list[2][0][0]


@pytest.mark.asyncio
async def test_create_library_object_version_new():
    driver_mock = MagicMock()
    session_mock = AsyncMock()
    session_ctx = AsyncMock()
    session_ctx.__aenter__.return_value = session_mock
    driver_mock.session.return_value = session_ctx

    tx_mock = AsyncMock()
    tx_mock.__aenter__.return_value = tx_mock
    session_mock.begin_transaction.return_value = tx_mock

    check_res_mock = AsyncMock()
    check_res_mock.single.return_value = False

    result_mock = AsyncMock()
    result_mock.single.return_value = {
        "new_props": {"name": "Test Lib V1", "version": 1}
    }

    tx_mock.run.side_effect = [check_res_mock, result_mock]

    res = await create_library_object_version(
        driver_mock, "lib_2", {"name": "Test Lib V1"}
    )
    assert res == {"name": "Test Lib V1", "version": 1}
    assert tx_mock.run.call_count == 2
    assert "MERGE (new:LibraryObject" in tx_mock.run.call_args_list[1][0][0]


@pytest.mark.asyncio
async def test_update_study_properties():
    driver_mock = MagicMock()
    session_mock = AsyncMock()
    session_ctx = AsyncMock()
    session_ctx.__aenter__.return_value = session_mock
    driver_mock.session.return_value = session_ctx

    tx_mock = AsyncMock()
    tx_mock.__aenter__.return_value = tx_mock
    session_mock.begin_transaction.return_value = tx_mock

    lock_res_mock = AsyncMock()
    lock_res_mock.single.return_value = {"id": "study_1"}

    result_mock = AsyncMock()
    result_mock.single.return_value = {"action_id": "action_uuid"}
    tx_mock.run.side_effect = [lock_res_mock, result_mock]

    res = await update_study_properties(
        driver_mock, "study_1", "user_1", "change reason", {"title": "New Title"}
    )

    assert res == "action_uuid"
    assert tx_mock.run.call_count == 2
    assert "CREATE (a:Action" in tx_mock.run.call_args_list[1][0][0]


@pytest.mark.asyncio
async def test_get_study_differences():
    driver_mock = MagicMock()
    session_mock = AsyncMock()
    session_ctx = (
        MagicMock()
    )  # Note: get_study_differences uses standard synchronous block as it is read-only
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


# --- Concurrency & Retry Safety Tests using Shared Fixture ---


@pytest.mark.asyncio
async def test_concurrent_study_saves_serialization(concurrency_runner):
    """
    Validates that concurrent saves to a single study are serialized cleanly,
    producing a perfectly linear chain of actions and resolving transient lock
    conflicts via transparent auto-retries.
    """
    driver = concurrency_runner.driver
    state = concurrency_runner.state

    # 1. Initialize study root
    await create_study_root(driver, "study_A")

    # 2. Fire overlapping saves concurrently
    # The first save will acquire the lock and sleep briefly.
    # The second save will hit the lock, raise TransientError, retry transparently,
    # and execute successfully after the first save completes and releases the lock.
    task1 = update_study_properties(
        driver, "study_A", "user_1", "first edit", {"title": "Version 1", "phase": "I"}
    )
    task2 = update_study_properties(
        driver,
        "study_A",
        "user_2",
        "second edit",
        {"title": "Version 2", "phase": "II"},
    )

    results = await concurrency_runner.run_concurrent(task1, task2)

    # 3. Assertions
    # Both saves must succeed without exposing internal transaction retries
    assert len(results) == 2
    assert all(r is not None for r in results)

    # State verification
    study = state.studies["study_A"]
    actions = study["actions"]

    # There should be exactly two historical actions
    assert len(actions) == 2

    # Verify linear BEFORE/AFTER history sequence:
    # First action: old_props is None, new_props is {"title": "Version 1", "phase": "I"} (or Version 2 depending on scheduling)
    # Second action: old_props must exactly match the first action's new_props!
    first_action, second_action = actions[0], actions[1]
    assert second_action["before"] == first_action["after"]


@pytest.mark.asyncio
async def test_concurrent_library_version_increments(concurrency_runner):
    """
    Validates that concurrent writes to a library template increment version properties
    step-by-step without generating duplicate version numbers, using transaction-level locking.
    """
    driver = concurrency_runner.driver
    state = concurrency_runner.state

    # 1. Initialize library object version 1
    await create_library_object_version(
        driver, "lib_tmpl", {"name": "Template V1", "type": "Form"}
    )

    # 2. Fire concurrent updates to increment library versions
    task1 = create_library_object_version(driver, "lib_tmpl", {"name": "Template V2"})
    task2 = create_library_object_version(driver, "lib_tmpl", {"name": "Template V3"})

    results = await concurrency_runner.run_concurrent(task1, task2)

    # 3. Assertions
    assert len(results) == 2
    versions = state.library_objects["lib_tmpl"]

    # Total versions should be 3 (initial V1 + two increments)
    assert len(versions) == 3

    # Check version numbers increment step-by-step (e.g., 1, 2, 3)
    version_numbers = [v["version"] for v in versions]
    assert version_numbers == [1, 2, 3]


@pytest.mark.asyncio
async def test_reorder_visits_mock():
    from apps.designer.db import MOCK_STUDY_VERSIONS
    from apps.designer.delta import MOCK_SOA_DATA, create_visit, reorder_visits

    MOCK_SOA_DATA.clear()
    MOCK_STUDY_VERSIONS.clear()

    study_version_id = "sv_reorder_visits"
    MOCK_STUDY_VERSIONS["study_x"] = [
        {
            "id": study_version_id,
            "version_tag": "1.0",
            "status": "DRAFT",
            "version_index": 1,
            "created_by": "designer",
        }
    ]

    # Create visits
    await create_visit(
        None,
        study_version_id,
        "user1",
        "create v1",
        "visit_1",
        {"name": "Visit 1", "sequence": 1},
    )
    await create_visit(
        None,
        study_version_id,
        "user1",
        "create v2",
        "visit_2",
        {"name": "Visit 2", "sequence": 2},
    )

    # Reorder
    res = await reorder_visits(
        None,
        study_version_id,
        "user1",
        "reorder visits change reason",
        ["visit_2", "visit_1"],
    )
    assert res is True

    # Assert new sequence/version_index/change metadata
    store = MOCK_SOA_DATA[study_version_id]["visits"]
    assert store["visit_2"]["sequence"] == 1
    assert store["visit_2"]["version_index"] == 2
    assert store["visit_2"]["created_by"] == "user1"
    assert store["visit_2"]["reason_for_change"] == "reorder visits change reason"

    assert store["visit_1"]["sequence"] == 2
    assert store["visit_1"]["version_index"] == 2

    # Assert action log
    actions = MOCK_SOA_DATA[study_version_id]["actions"]
    reorder_action = next(a for a in actions if a.get("type") == "REORDER")
    assert reorder_action["before_order"] == {"visit_2": 2, "visit_1": 1}
    assert reorder_action["after_order"] == {"visit_2": 1, "visit_1": 2}

    # Expect ValueError for unknown/deleted visit id
    with pytest.raises(ValueError):
        await reorder_visits(
            None, study_version_id, "user1", "fail", ["visit_2", "visit_unknown"]
        )


@pytest.mark.asyncio
async def test_reorder_visits_real():
    from apps.designer.delta import reorder_visits

    driver_mock = MagicMock()
    session_mock = AsyncMock()
    session_ctx = AsyncMock()
    session_ctx.__aenter__.return_value = session_mock
    driver_mock.session.return_value = session_ctx

    tx_mock = AsyncMock()
    tx_mock.__aenter__.return_value = tx_mock
    session_mock.begin_transaction.return_value = tx_mock

    # Mock existing visits
    existing_res_mock = AsyncMock()
    existing_res_mock.all.return_value = [{"id": "visit_1"}, {"id": "visit_2"}]

    tx_mock.run.side_effect = [
        AsyncMock(),  # lock StudyVersion
        existing_res_mock,  # check visits
        AsyncMock(),  # create Action
        AsyncMock(),  # resequence first visit
        AsyncMock(),  # resequence second visit
    ]

    res = await reorder_visits(
        driver_mock, "sv_123", "user_1", "change sequence", ["visit_2", "visit_1"]
    )
    assert res is True
    assert tx_mock.run.call_count == 5

    # Check queries
    calls = tx_mock.run.call_args_list
    assert (
        "MATCH (sv:StudyVersion {id: $study_version_id}) SET sv._lock = true"
        in calls[0][0][0]
    )
    assert (
        "MATCH (sv:StudyVersion {id: $study_version_id})-[:HAS_VISIT]->(v:Visit)"
        in calls[1][0][0]
    )
    assert "CREATE (a:Action" in calls[2][0][0]
    assert "CREATE (new_v:Visit" in calls[3][0][0]
    assert "CREATE (new_v:Visit" in calls[4][0][0]


@pytest.mark.asyncio
async def test_assign_activities_to_visit_mock():
    from apps.designer.db import MOCK_STUDY_VERSIONS
    from apps.designer.delta import (
        MOCK_SOA_DATA,
        assign_activities_to_visit,
        create_procedure,
        create_visit,
    )

    MOCK_SOA_DATA.clear()
    MOCK_STUDY_VERSIONS.clear()

    study_version_id = "sv_assign_mock"
    MOCK_STUDY_VERSIONS["study_abc"] = [
        {
            "id": study_version_id,
            "version_tag": "1.0",
            "status": "DRAFT",
            "version_index": 1,
            "created_by": "designer",
        }
    ]

    await create_visit(
        None, study_version_id, "user1", "create visit", "visit_1", {"name": "Visit 1"}
    )
    await create_procedure(
        None, study_version_id, "user1", "create p1", "proc_1", {"name": "Proc 1"}
    )
    await create_procedure(
        None, study_version_id, "user1", "create p2", "proc_2", {"name": "Proc 2"}
    )

    res = await assign_activities_to_visit(
        None,
        study_version_id,
        "user1",
        "assign proceds",
        "visit_1",
        ["proc_1", "proc_2"],
    )
    assert res is True

    # Assert they are linked in MOCK_SOA_DATA
    links = MOCK_SOA_DATA[study_version_id]["links"]
    assert {"type": "visit_procedure", "from_id": "visit_1", "to_id": "proc_1"} in links
    assert {"type": "visit_procedure", "from_id": "visit_1", "to_id": "proc_2"} in links


@pytest.mark.asyncio
async def test_assign_activities_to_visit_real():
    from apps.designer.delta import assign_activities_to_visit

    driver_mock = MagicMock()
    session_mock = AsyncMock()
    session_ctx = AsyncMock()
    session_ctx.__aenter__.return_value = session_mock
    driver_mock.session.return_value = session_ctx

    tx_mock = AsyncMock()
    tx_mock.__aenter__.return_value = tx_mock
    session_mock.begin_transaction.return_value = tx_mock

    # Each link_visit_to_procedure does lock, merge/action
    result_mock1 = AsyncMock()
    result_mock1.single.return_value = {"success": True}
    result_mock2 = AsyncMock()
    result_mock2.single.return_value = {"success": True}

    tx_mock.run.side_effect = [
        AsyncMock(),  # lock (proc 1)
        result_mock1,  # merge & action (proc 1)
        AsyncMock(),  # lock (proc 2)
        result_mock2,  # merge & action (proc 2)
    ]

    res = await assign_activities_to_visit(
        driver_mock,
        "sv_real_assign",
        "user1",
        "assign proceds",
        "visit_1",
        ["proc_1", "proc_2"],
    )
    assert res is True
    assert tx_mock.run.call_count == 4

    calls = tx_mock.run.call_args_list
    assert "MERGE (v)-[r:HAS_PROCEDURE]->(p)" in calls[1][0][0]
    assert "MERGE (v)-[r:HAS_PROCEDURE]->(p)" in calls[3][0][0]
