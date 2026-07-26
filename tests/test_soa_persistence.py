from unittest.mock import AsyncMock, MagicMock

import pytest

from apps.designer.db import MOCK_STUDY_VERSIONS
from apps.designer.delta import (
    MOCK_SOA_DATA,
    ImmutabilityViolationError,
    assert_mock_study_version_mutable,
    create_epoch,
    create_procedure,
    create_study_arm,
    create_timing_window,
    create_visit,
    get_soa_matrix_projection,
    link_arm_applicability,
    link_epoch_to_visit,
    link_visit_or_procedure_to_timing,
    link_visit_to_procedure,
    update_study_arm,
)


@pytest.fixture(autouse=True)
def clean_mock_data():
    """Clears MOCK_SOA_DATA and MOCK_STUDY_VERSIONS before each test to ensure test isolation."""
    MOCK_SOA_DATA.clear()
    MOCK_STUDY_VERSIONS.clear()


@pytest.mark.asyncio
async def test_mock_soa_entity_lifecycle():
    """
    Verifies creation, update, linking, audit trail, and projection queries
    using the in-memory mock fallback (driver=None).
    """
    study_version_id = "v_draft"

    # 1. Register study version as DRAFT
    MOCK_STUDY_VERSIONS["study_1"] = [
        {
            "id": study_version_id,
            "version_tag": "1.0",
            "status": "DRAFT",
            "version_index": 1,
            "created_by": "designer",
        }
    ]

    # 2. Mutability guard passes for DRAFT
    assert_mock_study_version_mutable(study_version_id)

    # 3. Create StudyArm
    arm_id = await create_study_arm(
        driver=None,
        study_version_id=study_version_id,
        user_id="user_1",
        change_reason="Add treatment arm",
        arm_id="arm_1",
        properties={"name": "Arm A", "type": "Active"},
    )
    assert arm_id == "arm_1"
    assert MOCK_SOA_DATA[study_version_id]["arms"]["arm_1"]["name"] == "Arm A"
    assert MOCK_SOA_DATA[study_version_id]["arms"]["arm_1"]["version_index"] == 1

    # Check Action log BEFORE/AFTER on create
    actions = MOCK_SOA_DATA[study_version_id]["actions"]
    assert len(actions) == 1
    assert actions[0]["user_id"] == "user_1"
    assert actions[0]["change_reason"] == "Add treatment arm"
    assert actions[0]["before"] is None
    assert actions[0]["after"]["name"] == "Arm A"

    # 4. Update StudyArm
    await update_study_arm(
        driver=None,
        study_version_id=study_version_id,
        user_id="user_1",
        change_reason="Update arm details",
        arm_id="arm_1",
        properties={"name": "Arm A (Modified)", "type": "Active"},
    )
    assert (
        MOCK_SOA_DATA[study_version_id]["arms"]["arm_1"]["name"] == "Arm A (Modified)"
    )
    assert MOCK_SOA_DATA[study_version_id]["arms"]["arm_1"]["version_index"] == 2

    # Check Action log BEFORE/AFTER on update
    assert len(actions) == 2
    assert actions[1]["before"]["name"] == "Arm A"
    assert actions[1]["after"]["name"] == "Arm A (Modified)"

    # 5. Create Epoch, Visit, Procedure, and TimingWindow
    await create_epoch(
        driver=None,
        study_version_id=study_version_id,
        user_id="user_1",
        change_reason="Add epoch",
        epoch_id="epoch_tx",
        properties={"epoch_name": "Treatment", "sequence": 1},
    )
    await create_visit(
        driver=None,
        study_version_id=study_version_id,
        user_id="user_1",
        change_reason="Add visit",
        visit_id="visit_v1",
        properties={"encounter_name": "Week 1", "sequence": 1},
    )
    await create_procedure(
        driver=None,
        study_version_id=study_version_id,
        user_id="user_1",
        change_reason="Add procedure",
        procedure_id="proc_vitals",
        properties={"activity_name": "Vitals"},
    )
    await create_timing_window(
        driver=None,
        study_version_id=study_version_id,
        user_id="user_1",
        change_reason="Add timing",
        timing_id="timing_w1",
        properties={"name": "Standard collection window"},
    )

    # 6. Link entities
    await link_epoch_to_visit(
        None, study_version_id, "user_1", "link ep to visit", "epoch_tx", "visit_v1"
    )
    await link_visit_to_procedure(
        None,
        study_version_id,
        "user_1",
        "link visit to proc",
        "visit_v1",
        "proc_vitals",
    )
    await link_visit_or_procedure_to_timing(
        None,
        study_version_id,
        "user_1",
        "link proc to timing",
        "proc_vitals",
        "timing_w1",
        source_type="procedure",
    )
    await link_arm_applicability(
        None,
        study_version_id,
        "user_1",
        "link arm to visit",
        "arm_1",
        "visit_v1",
        target_type="visit",
    )

    # 7. Query projection matrix
    projection = await get_soa_matrix_projection(None, study_version_id)
    assert len(projection["epochs"]) == 1
    assert projection["epochs"][0]["epoch_id"] == "epoch_tx"
    assert projection["epochs"][0]["epoch_name"] == "Treatment"

    assert len(projection["encounters"]) == 1
    assert projection["encounters"][0]["encounter_id"] == "visit_v1"
    assert projection["encounters"][0]["epoch_id"] == "epoch_tx"

    assert len(projection["rows"]) == 1
    row = projection["rows"][0]
    assert row["activity_id"] == "proc_vitals"
    assert row["activity_name"] == "Vitals"
    assert len(row["cells"]) == 1
    assert row["cells"][0]["encounter_id"] == "visit_v1"
    assert row["cells"][0]["is_applicable"] is True
    assert row["cells"][0]["details"] == "timing_w1"


@pytest.mark.asyncio
async def test_mutability_guard_rejects_locked_versions():
    """
    Verifies that write mutations fail on LOCKED, PUBLISHED, or ARCHIVED StudyVersion nodes.
    """
    study_version_id = "v_locked"

    # Register locked study version
    MOCK_STUDY_VERSIONS["study_1"] = [
        {
            "id": study_version_id,
            "version_tag": "1.0",
            "status": "LOCKED",
            "version_index": 1,
            "created_by": "designer",
        }
    ]

    with pytest.raises(ImmutabilityViolationError):
        assert_mock_study_version_mutable(study_version_id)

    with pytest.raises(ImmutabilityViolationError):
        await create_study_arm(
            driver=None,
            study_version_id=study_version_id,
            user_id="user_1",
            change_reason="Try write locked",
            arm_id="arm_1",
            properties={"name": "Arm A"},
        )


@pytest.mark.asyncio
async def test_neo4j_driver_operations():
    """
    Mocks the Neo4j driver and transaction context to verify correct Cypher queries,
    root locking, and transactional behavior are executed.
    """
    driver_mock = MagicMock()
    session_mock = AsyncMock()
    session_ctx = AsyncMock()
    session_ctx.__aenter__.return_value = session_mock
    driver_mock.session.return_value = session_ctx

    tx_mock = AsyncMock()
    tx_mock.__aenter__.return_value = tx_mock
    session_mock.begin_transaction.return_value = tx_mock

    # Setup database query mock results
    # 1. Lock check / SET lock
    lock_res = AsyncMock()

    # 2. Duplicate checks (none found)
    duplicate_res = AsyncMock()
    duplicate_res.single.return_value = None

    # 3. Create operation result
    create_record_mock = MagicMock()
    create_record_mock.__getitem__.return_value = "arm_1"
    create_res = AsyncMock()
    create_res.single.return_value = create_record_mock

    tx_mock.run.side_effect = [
        lock_res,  # locking StudyVersion root node
        duplicate_res,  # duplicate check
        create_res,  # create query
    ]

    arm_id = await create_study_arm(
        driver=driver_mock,
        study_version_id="sv_123",
        user_id="user_cra",
        change_reason="Transaction test",
        arm_id="arm_1",
        properties={"name": "Active Arm"},
    )

    assert arm_id == "arm_1"
    assert tx_mock.run.call_count == 3

    # Verify root locking query was run
    lock_call = tx_mock.run.call_args_list[0][0][0]
    assert "MATCH (sv:StudyVersion {id: $study_version_id})" in lock_call
    assert "SET sv._lock = true" in lock_call

    # Verify duplicate check was run
    check_call = tx_mock.run.call_args_list[1][0][0]
    assert "MATCH (sv:StudyVersion {id: $study_version_id})-[:HAS_ARM]->" in check_call

    # Verify create query was run
    create_call = tx_mock.run.call_args_list[2][0][0]
    assert "CREATE (arm:StudyArm" in create_call
    assert "CREATE (a:Action" in create_call
