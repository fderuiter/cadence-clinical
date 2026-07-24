import datetime
from unittest.mock import AsyncMock, MagicMock

import pytest

from apps.designer.db import MOCK_LIBRARY_OBJECTS
from apps.designer.delta import (
    ImmutabilityViolationError,
    create_library_object_version,
    get_latest_library_object,
    get_library_object_by_version,
    get_library_object_history,
    list_library_objects,
)


@pytest.fixture(autouse=True)
def clean_mock_library():
    MOCK_LIBRARY_OBJECTS.clear()
    yield
    MOCK_LIBRARY_OBJECTS.clear()


@pytest.mark.asyncio
async def test_mock_flow_library_version_chain_and_immutability():
    """
    Verifies creation of library object version chain with correct PREVIOUS_VERSION relationships,
    immutability controls, GxP audit parameters, tenant isolation, and pagination using mock fallback.
    """
    object_id = "lib_form_001"
    sponsor_id_1 = "spon_active"
    sponsor_id_2 = "spon_other"

    # 1. Create version 1 (DRAFT)
    props_v1 = {
        "object_type": "FORM",
        "sponsor_id": sponsor_id_1,
        "status": "DRAFT",
        "created_at": datetime.datetime.now().isoformat(),
        "created_by": "user_a",
        "change_reason": "Initial drafting of vital signs form",
        "payload": {
            "items": [
                {
                    "item_id": "vssbp",
                    "name": "VSSBP",
                    "question_text": "Systolic BP",
                    "data_type": "integer",
                    "required": True,
                }
            ]
        },
    }

    res_v1 = await create_library_object_version(None, object_id, props_v1)
    assert res_v1["version"] == 1
    assert res_v1["status"] == "DRAFT"
    assert res_v1["payload"]["items"][0]["item_id"] == "vssbp"
    assert res_v1["change_reason"] == "Initial drafting of vital signs form"

    # 2. Create version 2 (APPROVED)
    props_v2 = {
        "object_type": "FORM",
        "sponsor_id": sponsor_id_1,
        "status": "APPROVED",
        "created_at": datetime.datetime.now().isoformat(),
        "created_by": "user_b",
        "change_reason": "Add diastolic blood pressure question",
        "payload": {
            "items": [
                {
                    "item_id": "vssbp",
                    "name": "VSSBP",
                    "question_text": "Systolic BP",
                    "data_type": "integer",
                    "required": True,
                },
                {
                    "item_id": "vsdbp",
                    "name": "VSDBP",
                    "question_text": "Diastolic BP",
                    "data_type": "integer",
                    "required": True,
                },
            ]
        },
    }

    res_v2 = await create_library_object_version(None, object_id, props_v2)
    assert res_v2["version"] == 2
    assert res_v2["status"] == "APPROVED"
    assert len(res_v2["payload"]["items"]) == 2
    assert res_v2["change_reason"] == "Add diastolic blood pressure question"

    # 3. Create version 3 (LOCKED/ARCHIVED)
    props_v3 = {
        "object_type": "FORM",
        "sponsor_id": sponsor_id_1,
        "status": "ARCHIVED",
        "created_at": datetime.datetime.now().isoformat(),
        "created_by": "user_c",
        "change_reason": "Archive outdated form specification",
        "payload": res_v2["payload"],
    }
    res_v3 = await create_library_object_version(None, object_id, props_v3)
    assert res_v3["version"] == 3
    assert res_v3["status"] == "ARCHIVED"

    # 4. Attempting to create another version after ARCHIVED status raises ImmutabilityViolationError
    props_v4 = {
        "object_type": "FORM",
        "sponsor_id": sponsor_id_1,
        "status": "DRAFT",
        "created_at": datetime.datetime.now().isoformat(),
        "created_by": "user_d",
        "change_reason": "Attempting modification",
        "payload": res_v2["payload"],
    }
    with pytest.raises(ImmutabilityViolationError):
        await create_library_object_version(None, object_id, props_v4)

    # 5. Verify read/list/history tenant isolation
    # Latest lookup
    latest = await get_latest_library_object(None, object_id, sponsor_id_1)
    assert latest is not None
    assert latest["version"] == 3
    assert latest["status"] == "ARCHIVED"

    # Tenant Isolation: query latest with sponsor_id_2 should be None
    latest_tenant_2 = await get_latest_library_object(None, object_id, sponsor_id_2)
    assert latest_tenant_2 is None

    # Version lookup
    v1_lookup = await get_library_object_by_version(None, object_id, sponsor_id_1, 1)
    assert v1_lookup is not None
    assert v1_lookup["version"] == 1
    assert v1_lookup["change_reason"] == "Initial drafting of vital signs form"

    v1_lookup_tenant_2 = await get_library_object_by_version(
        None, object_id, sponsor_id_2, 1
    )
    assert v1_lookup_tenant_2 is None

    # History lookup
    history = await get_library_object_history(None, object_id, sponsor_id_1)
    assert len(history) == 3
    assert history[0]["version"] == 1
    assert history[1]["version"] == 2
    assert history[2]["version"] == 3

    history_tenant_2 = await get_library_object_history(None, object_id, sponsor_id_2)
    assert len(history_tenant_2) == 0


@pytest.mark.asyncio
async def test_mock_list_filtering_and_pagination():
    """
    Verifies that listing library objects filters correctly by object_type, yields deterministic ordering,
    and supports Stripe-style cursor-based pagination.
    """
    sponsor_id = "spon_pharma"

    # Insert a set of library objects of different types
    objects = [
        ("lib_arm_01", "ARM", "First Arm definition"),
        ("lib_arm_02", "ARM", "Second Arm definition"),
        ("lib_form_01", "FORM", "First Form definition"),
        ("lib_form_02", "FORM", "Second Form definition"),
        ("lib_visit_01", "VISIT", "First Visit definition"),
    ]

    for oid, otype, reason in objects:
        await create_library_object_version(
            None,
            oid,
            {
                "object_type": otype,
                "sponsor_id": sponsor_id,
                "status": "APPROVED",
                "change_reason": reason,
                "payload": {"attributes": {}},
            },
        )

    # 1. List all (no type filter) - should be ordered deterministically by ID ascending
    all_objs = await list_library_objects(None, sponsor_id, limit=10)
    assert len(all_objs) == 5
    assert [x["id"] for x in all_objs] == [
        "lib_arm_01",
        "lib_arm_02",
        "lib_form_01",
        "lib_form_02",
        "lib_visit_01",
    ]

    # 2. List with object_type filter = FORM
    forms = await list_library_objects(None, sponsor_id, object_type="FORM")
    assert len(forms) == 2
    assert [x["id"] for x in forms] == ["lib_form_01", "lib_form_02"]

    # 3. Stripe-style cursor-based pagination using starting_after
    page_1 = await list_library_objects(None, sponsor_id, limit=2)
    assert len(page_1) == 2
    assert [x["id"] for x in page_1] == ["lib_arm_01", "lib_arm_02"]

    cursor = page_1[-1]["id"]  # "lib_arm_02"
    page_2 = await list_library_objects(
        None, sponsor_id, limit=2, starting_after=cursor
    )
    assert len(page_2) == 2
    assert [x["id"] for x in page_2] == ["lib_form_01", "lib_form_02"]

    cursor_2 = page_2[-1]["id"]  # "lib_form_02"
    page_3 = await list_library_objects(
        None, sponsor_id, limit=2, starting_after=cursor_2
    )
    assert len(page_3) == 1
    assert [x["id"] for x in page_3] == ["lib_visit_01"]


@pytest.mark.asyncio
async def test_neo4j_library_object_version_chain_queries():
    """
    Simulates the Neo4j driver interactions to verify that the Cypher queries
    produced by all read, list, and history retrieval functions are correct, structured,
    and process driver responses cleanly.
    """
    driver_mock = MagicMock()
    session_mock = AsyncMock()
    session_ctx = AsyncMock()
    session_ctx.__aenter__.return_value = session_mock
    driver_mock.session.return_value = session_ctx

    # 1. Test get_latest_library_object with Mock neo4j records
    result_mock = AsyncMock()
    result_mock.single.return_value = {
        "props": {
            "id": "lib_form_001",
            "version": 2,
            "sponsor_id": "spon_abc",
            "object_type": "FORM",
            "payload_json": '{"items": []}',
        }
    }
    session_mock.run.return_value = result_mock

    latest = await get_latest_library_object(driver_mock, "lib_form_001", "spon_abc")
    assert latest is not None
    assert latest["id"] == "lib_form_001"
    assert latest["version"] == 2
    assert latest["payload"] == {"items": []}

    # Verify Cypher query structure
    called_query = session_mock.run.call_args[0][0]
    assert (
        "MATCH (n:LibraryObject {id: $object_id, sponsor_id: $sponsor_id})"
        in called_query
    )
    assert "WHERE NOT (n)<-[:PREVIOUS_VERSION]-()" in called_query

    # 2. Test get_library_object_by_version
    result_mock_v = AsyncMock()
    result_mock_v.single.return_value = {
        "props": {
            "id": "lib_form_001",
            "version": 1,
            "sponsor_id": "spon_abc",
            "object_type": "FORM",
            "payload_json": '{"items": []}',
        }
    }
    session_mock.run.return_value = result_mock_v

    v1_obj = await get_library_object_by_version(
        driver_mock, "lib_form_001", "spon_abc", 1
    )
    assert v1_obj is not None
    assert v1_obj["version"] == 1

    called_query_v = session_mock.run.call_args[0][0]
    assert (
        "MATCH (n:LibraryObject {id: $object_id, sponsor_id: $sponsor_id, version: $version})"
        in called_query_v
    )

    # 3. Test get_library_object_history
    result_mock_hist = AsyncMock()
    result_mock_hist.all.return_value = [
        {"props": {"id": "lib_001", "version": 1, "payload_json": "{}"}},
        {"props": {"id": "lib_001", "version": 2, "payload_json": "{}"}},
    ]
    session_mock.run.return_value = result_mock_hist

    history = await get_library_object_history(driver_mock, "lib_001", "spon_abc")
    assert len(history) == 2
    assert history[0]["version"] == 1
    assert history[1]["version"] == 2

    called_query_hist = session_mock.run.call_args[0][0]
    assert "ORDER BY n.version ASC" in called_query_hist

    # 4. Test list_library_objects
    result_mock_list = AsyncMock()
    result_mock_list.all.return_value = [
        {"props": {"id": "lib_001", "sponsor_id": "spon_abc", "object_type": "FORM"}},
        {"props": {"id": "lib_002", "sponsor_id": "spon_abc", "object_type": "FORM"}},
    ]
    session_mock.run.return_value = result_mock_list

    listed = await list_library_objects(
        driver_mock,
        sponsor_id="spon_abc",
        object_type="FORM",
        limit=10,
        starting_after="lib_000",
    )
    assert len(listed) == 2
    assert listed[0]["id"] == "lib_001"

    called_query_list = session_mock.run.call_args_list[-1][0][0]
    assert "n.sponsor_id = $sponsor_id" in called_query_list
    assert "n.object_type = $object_type" in called_query_list
    assert "n.id > $starting_after" in called_query_list
    assert "ORDER BY n.id ASC" in called_query_list
    assert "LIMIT $limit" in called_query_list
