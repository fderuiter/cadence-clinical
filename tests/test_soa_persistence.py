from unittest.mock import AsyncMock, MagicMock

import pytest

from apps.designer.db import MOCK_STUDY_VERSIONS
from apps.designer.delta import (
    MOCK_SOA_DATA,
    ImmutabilityViolationError,
    assert_mock_study_version_mutable,
    create_epoch,
    create_form,
    create_procedure,
    create_study_arm,
    create_timing_window,
    create_visit,
    get_soa_matrix_projection,
    link_arm_applicability,
    link_epoch_to_visit,
    link_visit_or_procedure_to_timing,
    link_visit_to_form,
    link_visit_to_procedure,
    update_epoch,
    update_procedure,
    update_study_arm,
    update_timing_window,
    update_visit,
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


# --- New Persistence Tests ---


class FakeRecord:
    def __init__(self, data):
        self._data = data

    def get(self, key):
        return self._data.get(key)


class FakeTransaction:
    def __init__(self, record_data):
        self.record_data = record_data
        self.run_called = []

    async def run(self, query, **kwargs):
        self.run_called.append((query, kwargs))
        res = AsyncMock()
        res.single.return_value = FakeRecord({"version_props": self.record_data})
        return res


@pytest.mark.asyncio
async def test_assert_study_version_mutable():
    from apps.designer.delta import assert_study_version_mutable

    # 1. Mutable state
    tx_mutable = FakeTransaction({"status": "DRAFT"})
    await assert_study_version_mutable(tx_mutable, "sv_draft")
    assert len(tx_mutable.run_called) == 1

    # 2. Immutable state (LOCKED)
    tx_locked = FakeTransaction({"status": "LOCKED"})
    with pytest.raises(ImmutabilityViolationError):
        await assert_study_version_mutable(tx_locked, "sv_locked")

    # 3. Immutable state (PUBLISHED)
    tx_published = FakeTransaction({"status": "PUBLISHED"})
    with pytest.raises(ImmutabilityViolationError):
        await assert_study_version_mutable(tx_published, "sv_published")

    # 4. Immutable state (ARCHIVED)
    tx_archived = FakeTransaction({"status": "ARCHIVED"})
    with pytest.raises(ImmutabilityViolationError):
        await assert_study_version_mutable(tx_archived, "sv_archived")


@pytest.mark.asyncio
async def test_with_transaction_retry_success_after_retries():
    from neo4j.exceptions import TransientError

    from apps.designer.delta import with_transaction_retry

    call_count = 0

    @with_transaction_retry(max_retries=3, initial_delay=0.001, backoff_factor=1.5)
    async def dummy_func():
        nonlocal call_count
        call_count += 1
        if call_count < 3:
            raise TransientError("Transient database lock conflict")
        return "success"

    res = await dummy_func()
    assert res == "success"
    assert call_count == 3


@pytest.mark.asyncio
async def test_with_transaction_retry_failure_exceeded():
    from neo4j.exceptions import TransientError

    from apps.designer.delta import with_transaction_retry

    call_count = 0

    @with_transaction_retry(max_retries=2, initial_delay=0.001, backoff_factor=1.5)
    async def dummy_func():
        nonlocal call_count
        call_count += 1
        raise TransientError("Transient error always")

    with pytest.raises(TransientError):
        await dummy_func()
    assert call_count == 3


def setup_driver_mock():
    driver_mock = MagicMock()
    session_mock = AsyncMock()
    session_ctx = AsyncMock()
    session_ctx.__aenter__.return_value = session_mock
    driver_mock.session.return_value = session_ctx

    tx_mock = AsyncMock()
    tx_mock.__aenter__.return_value = tx_mock
    session_mock.begin_transaction.return_value = tx_mock

    return driver_mock, tx_mock


@pytest.mark.asyncio
async def test_update_study_arm_neo4j():
    driver_mock, tx_mock = setup_driver_mock()

    lock_res = AsyncMock()

    check_res = AsyncMock()
    check_record = MagicMock()
    check_record.__getitem__.return_value = "arm_1"
    check_res.single.return_value = check_record

    update_res = AsyncMock()
    update_record = MagicMock()
    update_record.__getitem__.return_value = "arm_1"
    update_res.single.return_value = update_record

    tx_mock.run.side_effect = [lock_res, check_res, update_res]

    arm_id = await update_study_arm(
        driver=driver_mock,
        study_version_id="sv_123",
        user_id="user_cra",
        change_reason="Updating arm details for trial",
        arm_id="arm_1",
        properties={"name": "Arm B"},
    )

    assert arm_id == "arm_1"
    assert tx_mock.run.call_count == 3

    # Verify lock check
    lock_query = tx_mock.run.call_args_list[0][0][0]
    assert "MATCH (sv:StudyVersion {id: $study_version_id})" in lock_query
    assert "SET sv._lock = true" in lock_query

    # Verify check old arm exists query
    check_query = tx_mock.run.call_args_list[1][0][0]
    assert "-[r:HAS_ARM]->(old_arm:StudyArm {id: $arm_id})" in check_query

    # Verify update query asserting literal Cypher fragments
    update_query = tx_mock.run.call_args_list[2][0][0]
    assert "CREATE (new_arm:StudyArm" in update_query
    assert "DELETE r" in update_query
    assert "CREATE (new_arm)-[:PREVIOUS_VERSION]->(old_arm)" in update_query
    assert "CREATE (a:Action" in update_query
    assert "CREATE (a)-[:AFTER]->(new_arm)" in update_query
    assert "CREATE (a)-[:BEFORE]->(old_arm)" in update_query


@pytest.mark.asyncio
async def test_epoch_neo4j():
    driver_mock, tx_mock = setup_driver_mock()

    # 1. Test create_epoch
    tx_mock.run.side_effect = [
        AsyncMock(),  # lock sv
        AsyncMock(single=AsyncMock(return_value=None)),  # check no duplicate
        AsyncMock(single=AsyncMock(return_value={"id": "epoch_1"})),  # create Epoch
    ]

    epoch_id = await create_epoch(
        driver=driver_mock,
        study_version_id="sv_123",
        user_id="user_cra",
        change_reason="Add screening epoch",
        epoch_id="epoch_1",
        properties={"epoch_name": "Screening", "sequence": 1},
    )
    assert epoch_id == "epoch_1"
    create_query = tx_mock.run.call_args_list[2][0][0]
    assert "CREATE (ep:Epoch" in create_query
    assert "CREATE (sv)-[:HAS_EPOCH]->(ep)" in create_query

    # 2. Test update_epoch
    driver_mock, tx_mock = setup_driver_mock()
    tx_mock.run.side_effect = [
        AsyncMock(),  # lock sv
        AsyncMock(single=AsyncMock(return_value={"id": "epoch_1"})),  # check exists
        AsyncMock(single=AsyncMock(return_value={"id": "epoch_1"})),  # update Epoch
    ]

    updated_id = await update_epoch(
        driver=driver_mock,
        study_version_id="sv_123",
        user_id="user_cra",
        change_reason="Modify screening details",
        epoch_id="epoch_1",
        properties={"epoch_name": "Screening (Updated)", "sequence": 1},
    )
    assert updated_id == "epoch_1"
    update_query = tx_mock.run.call_args_list[2][0][0]
    assert "CREATE (new_ep:Epoch" in update_query
    assert "DELETE r" in update_query
    assert "CREATE (new_ep)-[:PREVIOUS_VERSION]->(old_ep)" in update_query
    assert "CREATE (a)-[:BEFORE]->(old_ep)" in update_query


@pytest.mark.asyncio
async def test_visit_neo4j():
    driver_mock, tx_mock = setup_driver_mock()

    tx_mock.run.side_effect = [
        AsyncMock(),  # lock sv
        AsyncMock(single=AsyncMock(return_value=None)),  # check no duplicate
        AsyncMock(single=AsyncMock(return_value={"id": "visit_1"})),  # create Visit
    ]

    visit_id = await create_visit(
        driver=driver_mock,
        study_version_id="sv_123",
        user_id="user_cra",
        change_reason="Add visit 1",
        visit_id="visit_1",
        properties={"encounter_name": "Week 1"},
    )
    assert visit_id == "visit_1"
    assert "CREATE (v:Visit" in tx_mock.run.call_args_list[2][0][0]

    # update visit
    driver_mock, tx_mock = setup_driver_mock()
    tx_mock.run.side_effect = [
        AsyncMock(),
        AsyncMock(single=AsyncMock(return_value={"id": "visit_1"})),
        AsyncMock(single=AsyncMock(return_value={"id": "visit_1"})),
    ]
    updated_id = await update_visit(
        driver=driver_mock,
        study_version_id="sv_123",
        user_id="user_cra",
        change_reason="Update visit 1",
        visit_id="visit_1",
        properties={"encounter_name": "Week 1 (Updated)"},
    )
    assert updated_id == "visit_1"
    assert "CREATE (new_v:Visit" in tx_mock.run.call_args_list[2][0][0]
    assert (
        "CREATE (new_v)-[:PREVIOUS_VERSION]->(old_v)"
        in tx_mock.run.call_args_list[2][0][0]
    )


@pytest.mark.asyncio
async def test_procedure_neo4j():
    driver_mock, tx_mock = setup_driver_mock()

    tx_mock.run.side_effect = [
        AsyncMock(),  # lock sv
        AsyncMock(single=AsyncMock(return_value=None)),  # check no duplicate
        AsyncMock(single=AsyncMock(return_value={"id": "proc_1"})),  # create Procedure
    ]

    proc_id = await create_procedure(
        driver=driver_mock,
        study_version_id="sv_123",
        user_id="user_cra",
        change_reason="Add blood draw",
        procedure_id="proc_1",
        properties={"activity_name": "Blood Draw"},
    )
    assert proc_id == "proc_1"
    assert "CREATE (p:Procedure" in tx_mock.run.call_args_list[2][0][0]

    # update procedure
    driver_mock, tx_mock = setup_driver_mock()
    tx_mock.run.side_effect = [
        AsyncMock(),
        AsyncMock(single=AsyncMock(return_value={"id": "proc_1"})),
        AsyncMock(single=AsyncMock(return_value={"id": "proc_1"})),
    ]
    updated_id = await update_procedure(
        driver=driver_mock,
        study_version_id="sv_123",
        user_id="user_cra",
        change_reason="Update blood draw details",
        procedure_id="proc_1",
        properties={"activity_name": "Blood Draw (Fasting)"},
    )
    assert updated_id == "proc_1"
    assert "CREATE (new_p:Procedure" in tx_mock.run.call_args_list[2][0][0]
    assert (
        "CREATE (new_p)-[:PREVIOUS_VERSION]->(old_p)"
        in tx_mock.run.call_args_list[2][0][0]
    )


@pytest.mark.asyncio
async def test_timing_window_neo4j():
    driver_mock, tx_mock = setup_driver_mock()

    tx_mock.run.side_effect = [
        AsyncMock(),  # lock sv
        AsyncMock(single=AsyncMock(return_value=None)),  # check no duplicate
        AsyncMock(single=AsyncMock(return_value={"id": "tw_1"})),  # create TimingWindow
    ]

    tw_id = await create_timing_window(
        driver=driver_mock,
        study_version_id="sv_123",
        user_id="user_cra",
        change_reason="Add 3 day window",
        timing_id="tw_1",
        properties={"name": "3 days"},
    )
    assert tw_id == "tw_1"
    assert "CREATE (t:TimingWindow" in tx_mock.run.call_args_list[2][0][0]

    # update timing window
    driver_mock, tx_mock = setup_driver_mock()
    tx_mock.run.side_effect = [
        AsyncMock(),
        AsyncMock(single=AsyncMock(return_value={"id": "tw_1"})),
        AsyncMock(single=AsyncMock(return_value={"id": "tw_1"})),
    ]
    updated_id = await update_timing_window(
        driver=driver_mock,
        study_version_id="sv_123",
        user_id="user_cra",
        change_reason="Update 3 day window",
        timing_id="tw_1",
        properties={"name": "3 days +/- 1"},
    )
    assert updated_id == "tw_1"
    assert "CREATE (new_t:TimingWindow" in tx_mock.run.call_args_list[2][0][0]
    assert (
        "CREATE (new_t)-[:PREVIOUS_VERSION]->(old_t)"
        in tx_mock.run.call_args_list[2][0][0]
    )


@pytest.mark.asyncio
async def test_form_neo4j():
    driver_mock, tx_mock = setup_driver_mock()

    tx_mock.run.side_effect = [
        AsyncMock(),  # lock sv
        AsyncMock(single=AsyncMock(return_value=None)),  # check no duplicate
        AsyncMock(single=AsyncMock(return_value={"id": "form_1"})),  # create Form
    ]

    form_id = await create_form(
        driver=driver_mock,
        study_version_id="sv_123",
        user_id="user_cra",
        change_reason="Add vitals form",
        form_id="form_1",
        properties={"form_key": "vitals"},
    )
    assert form_id == "form_1"
    assert "CREATE (f:Form" in tx_mock.run.call_args_list[2][0][0]


@pytest.mark.asyncio
async def test_links_neo4j():
    driver_mock, tx_mock = setup_driver_mock()

    # 1. link_epoch_to_visit
    tx_mock.run.side_effect = [
        AsyncMock(),
        AsyncMock(single=AsyncMock(return_value={"success": True})),
    ]
    res = await link_epoch_to_visit(
        driver=driver_mock,
        study_version_id="sv_123",
        user_id="user_cra",
        change_reason="Link epoch and visit",
        epoch_id="epoch_1",
        visit_id="visit_1",
    )
    assert res is True
    query1 = tx_mock.run.call_args_list[1][0][0]
    assert "MERGE (ep)-[r:HAS_VISIT]->(v)" in query1

    # 2. link_visit_to_procedure
    driver_mock, tx_mock = setup_driver_mock()
    tx_mock.run.side_effect = [
        AsyncMock(),
        AsyncMock(single=AsyncMock(return_value={"success": True})),
    ]
    res = await link_visit_to_procedure(
        driver=driver_mock,
        study_version_id="sv_123",
        user_id="user_cra",
        change_reason="Link visit and procedure",
        visit_id="visit_1",
        procedure_id="proc_1",
    )
    assert res is True
    query2 = tx_mock.run.call_args_list[1][0][0]
    assert "MERGE (v)-[r:HAS_PROCEDURE]->(p)" in query2

    # 3. link_visit_or_procedure_to_timing
    driver_mock, tx_mock = setup_driver_mock()
    tx_mock.run.side_effect = [
        AsyncMock(),
        AsyncMock(single=AsyncMock(return_value={"success": True})),
    ]
    res = await link_visit_or_procedure_to_timing(
        driver=driver_mock,
        study_version_id="sv_123",
        user_id="user_cra",
        change_reason="Link timing",
        source_id="proc_1",
        timing_id="tw_1",
        source_type="procedure",
    )
    assert res is True
    query3 = tx_mock.run.call_args_list[1][0][0]
    assert "MERGE (src)-[r:HAS_TIMING]->(t)" in query3

    # 4. link_arm_applicability
    driver_mock, tx_mock = setup_driver_mock()
    tx_mock.run.side_effect = [
        AsyncMock(),
        AsyncMock(single=AsyncMock(return_value={"success": True})),
    ]
    res = await link_arm_applicability(
        driver=driver_mock,
        study_version_id="sv_123",
        user_id="user_cra",
        change_reason="Link arm applicability",
        arm_id="arm_1",
        target_id="visit_1",
        target_type="visit",
    )
    assert res is True
    query4 = tx_mock.run.call_args_list[1][0][0]
    assert "MERGE (arm)-[r:APPLICABLE_TO]->(tgt)" in query4

    # 5. link_visit_to_form
    driver_mock, tx_mock = setup_driver_mock()
    tx_mock.run.side_effect = [
        AsyncMock(),
        AsyncMock(single=AsyncMock(return_value={"success": True})),
    ]
    res = await link_visit_to_form(
        driver=driver_mock,
        study_version_id="sv_123",
        user_id="user_cra",
        change_reason="Link visit to form",
        visit_id="visit_1",
        form_id="form_1",
    )
    assert res is True
    query5 = tx_mock.run.call_args_list[1][0][0]
    assert "MERGE (v)-[r:HAS_FORM]->(f)" in query5


@pytest.mark.asyncio
async def test_get_soa_matrix_projection_neo4j():
    driver_mock = MagicMock()
    session_mock = AsyncMock()
    session_ctx = AsyncMock()
    session_ctx.__aenter__.return_value = session_mock
    driver_mock.session.return_value = session_ctx

    result_mock = AsyncMock()
    record_mock = MagicMock()
    record_mock.get.side_effect = lambda key, default=None: {
        "epochs": [{"id": "epoch_tx", "name": "Treatment", "sequence": 1}],
        "encounters": [{"id": "visit_v1", "name": "Week 1", "sequence": 1}],
        "procedures": [{"id": "proc_vitals", "name": "Vitals"}],
        "arms": [{"id": "arm_1", "name": "Arm A"}],
        "epoch_visit_links": [{"epoch_id": "epoch_tx", "visit_id": "visit_v1"}],
        "visit_proc_links": [{"visit_id": "visit_v1", "procedure_id": "proc_vitals"}],
        "visit_timing": [],
        "proc_timing": [{"procedure_id": "proc_vitals", "timing_name": "timing_w1"}],
    }.get(key, default)

    result_mock.single.return_value = record_mock
    session_mock.run.return_value = result_mock

    projection = await get_soa_matrix_projection(driver_mock, "sv_123")

    assert len(projection["epochs"]) == 1
    assert projection["epochs"][0]["epoch_id"] == "epoch_tx"
    assert projection["epochs"][0]["epoch_name"] == "Treatment"

    assert len(projection["encounters"]) == 1
    assert projection["encounters"][0]["encounter_id"] == "visit_v1"

    assert len(projection["rows"]) == 1
    row = projection["rows"][0]
    assert row["activity_id"] == "proc_vitals"
    assert row["activity_name"] == "Vitals"
    assert len(row["cells"]) == 1
    assert row["cells"][0]["encounter_id"] == "visit_v1"
    assert row["cells"][0]["is_applicable"] is True
    assert row["cells"][0]["details"] == "timing_w1"
