import asyncio
from unittest.mock import MagicMock

import pytest

from tests.conftest import (
    SERVICE_DB_PREFIXES,
    build_worker_suffix,
    get_run_uid,
    get_worker_id,
    is_live_db_requested,
    is_xdist_controller,
    run_sync,
    should_provision_postgres,
)


def test_build_worker_suffix_uniqueness():
    """Verify that different run UIDs and worker IDs generate disjoint suffixes.

    @req:PRD-SYS-004
    """
    s1 = build_worker_suffix("run1a2b3", "gw0")
    s2 = build_worker_suffix("run1a2b3", "gw1")
    s3 = build_worker_suffix("run4c5d6", "gw0")

    assert s1 == "_run1a2b3_gw0"
    assert s2 == "_run1a2b3_gw1"
    assert s3 == "_run4c5d6_gw0"
    assert s1 != s2
    assert s1 != s3
    assert s2 != s3


def test_service_database_names_length_and_format():
    """Verify all service database names with suffix satisfy PostgreSQL naming constraints (<= 63 bytes).

    @req:PRD-SYS-004
    """
    suffix = build_worker_suffix("a1b2c3d4", "gw15")
    for prefix in SERVICE_DB_PREFIXES:
        db_name = f"{prefix}{suffix}"
        assert len(db_name) <= 63
        assert db_name.islower()
        assert db_name.startswith("cadence_")


def test_get_run_uid_from_config():
    """Verify get_run_uid extracts from workerinput if present."""
    mock_config = MagicMock()
    mock_config.workerinput = {"cadence_run_uid": "testrun1"}

    run_uid = get_run_uid(mock_config)
    assert run_uid == "testrun1"


def test_get_worker_id_from_config():
    """Verify get_worker_id extracts workerid from xdist workerinput."""
    mock_config = MagicMock()
    mock_config.workerinput = {"workerid": "gw3"}

    worker_id = get_worker_id(mock_config)
    assert worker_id == "gw3"


def test_is_xdist_controller_detection():
    """Verify is_xdist_controller correctly identifies master controller vs worker vs single runner."""
    # Worker config has workerinput
    worker_config = MagicMock()
    worker_config.workerinput = {"workerid": "gw0"}
    assert not is_xdist_controller(worker_config)

    # Controller config has no workerinput and numprocesses > 0
    controller_config = MagicMock(spec=["option", "pluginmanager"])
    controller_config.option.numprocesses = 4
    controller_config.option.dist = "worksteal"
    assert is_xdist_controller(controller_config)

    # Single-process config has no workerinput and numprocesses is None / 0
    single_config = MagicMock(spec=["option", "pluginmanager"])
    single_config.option.numprocesses = 0
    single_config.option.dist = "no"
    assert not is_xdist_controller(single_config)


def test_should_provision_postgres_controller_boundary():
    """Verify xdist controller is never permitted to provision PostgreSQL databases.

    @req:PRD-SYS-004
    """
    controller_config = MagicMock(spec=["option", "pluginmanager"])
    controller_config.option.numprocesses = 4
    controller_config.option.dist = "worksteal"

    # Even if live DB is requested, controller should NOT provision
    with pytest.MonkeyPatch.context() as mp:
        mp.setenv("USE_LIVE_DB", "true")
        assert is_live_db_requested()
        assert not should_provision_postgres(controller_config)


def test_should_provision_postgres_worker():
    """Verify xdist worker or single-process runner provisions when live DB is requested.

    @req:PRD-SYS-004
    """
    worker_config = MagicMock()
    worker_config.workerinput = {"workerid": "gw0", "cadence_run_uid": "abc12345"}

    with pytest.MonkeyPatch.context() as mp:
        mp.setenv("USE_LIVE_DB", "true")
        assert should_provision_postgres(worker_config)

    with pytest.MonkeyPatch.context() as mp:
        mp.delenv("USE_LIVE_DB", raising=False)
        mp.delenv("TEST_DATABASE_URL", raising=False)
        assert not should_provision_postgres(worker_config)


def test_run_sync_timeout_enforcement():
    """Verify run_sync enforces bounded timeout and does not hang indefinitely."""

    async def infinite_hang():
        await asyncio.sleep(10.0)

    with pytest.raises((TimeoutError, asyncio.TimeoutError)):
        run_sync(infinite_hang(), timeout=0.1)


def test_simulated_concurrent_runs_have_disjoint_databases():
    """Simulate two concurrent test runs with 4 workers each and verify complete database isolation.

    @req:PRD-SYS-004
    """
    run_1_uid = "run11111"
    run_2_uid = "run22222"

    run_1_dbs = set()
    for worker_idx in range(4):
        suffix = build_worker_suffix(run_1_uid, f"gw{worker_idx}")
        for prefix in SERVICE_DB_PREFIXES:
            run_1_dbs.add(f"{prefix}{suffix}")

    run_2_dbs = set()
    for worker_idx in range(4):
        suffix = build_worker_suffix(run_2_uid, f"gw{worker_idx}")
        for prefix in SERVICE_DB_PREFIXES:
            run_2_dbs.add(f"{prefix}{suffix}")

    # No database name in run 1 can exist in run 2
    intersection = run_1_dbs.intersection(run_2_dbs)
    assert len(intersection) == 0
    assert len(run_1_dbs) == 4 * len(SERVICE_DB_PREFIXES)
    assert len(run_2_dbs) == 4 * len(SERVICE_DB_PREFIXES)
