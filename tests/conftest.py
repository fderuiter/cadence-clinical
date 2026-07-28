import asyncio
import os
import uuid
from typing import Any, Dict

import pytest
from neo4j.exceptions import TransientError

# Ensure offline terminology fallback is active for test isolation and speed
os.environ.setdefault("TERMINOLOGY_OFFLINE", "true")


# Identify and override Database URL for workers early, and ensure database isolation
def get_postgres_base_config():
    url = (
        os.environ.get("TEST_DATABASE_URL")
        or os.environ.get("DATABASE_URL")
        or "postgresql+asyncpg://cadence:cadence_password@localhost:5432/cadence_edc"  # pragma: allowlist secret
    )
    if "://" in url:
        scheme, remainder = url.split("://", 1)
        if "/" in remainder:
            base_part, _ = remainder.rsplit("/", 1)
        else:
            base_part = remainder
        return f"{scheme}://{base_part}/"
    return "postgresql+asyncpg://cadence:cadence_password@localhost:5432/"  # pragma: allowlist secret


async def create_databases_async(worker_suffix: str):
    from sqlalchemy import text
    from sqlalchemy.ext.asyncio import create_async_engine

    base_url = f"{get_postgres_base_config()}postgres"
    db_names = [
        f"cadence_edc{worker_suffix}",
        f"cadence_etmf{worker_suffix}",
        f"cadence_ctms{worker_suffix}",
        f"cadence_quality{worker_suffix}",
        f"cadence_interop{worker_suffix}",
        f"cadence_tickets{worker_suffix}",
        f"cadence_notifications{worker_suffix}",
        f"cadence_econsent{worker_suffix}",
        f"cadence_safety{worker_suffix}",
        f"cadence_org{worker_suffix}",
        f"cadence_eisf{worker_suffix}",
    ]

    engine = create_async_engine(base_url, isolation_level="AUTOCOMMIT")
    async with engine.connect() as conn:
        for db_name in db_names:
            res = await conn.execute(
                text(f"SELECT 1 FROM pg_database WHERE datname='{db_name}'")
            )
            if not res.scalar():
                try:
                    await conn.execute(text(f"CREATE DATABASE {db_name}"))
                    print(f"[conftest] Created isolated database: {db_name}")
                except Exception as e:
                    print(f"[conftest] Error creating database {db_name}: {e}")
    await engine.dispose()


async def drop_databases_async(worker_suffix: str):
    from sqlalchemy import text
    from sqlalchemy.ext.asyncio import create_async_engine

    base_url = f"{get_postgres_base_config()}postgres"
    db_names = [
        f"cadence_edc{worker_suffix}",
        f"cadence_etmf{worker_suffix}",
        f"cadence_ctms{worker_suffix}",
        f"cadence_quality{worker_suffix}",
        f"cadence_interop{worker_suffix}",
        f"cadence_tickets{worker_suffix}",
        f"cadence_notifications{worker_suffix}",
        f"cadence_econsent{worker_suffix}",
        f"cadence_safety{worker_suffix}",
        f"cadence_org{worker_suffix}",
        f"cadence_eisf{worker_suffix}",
    ]

    engine = create_async_engine(base_url, isolation_level="AUTOCOMMIT")
    async with engine.connect() as conn:
        for db_name in db_names:
            try:
                await conn.execute(
                    text(f"""
                    SELECT pg_terminate_backend(pg_stat_activity.pid)
                    FROM pg_stat_activity
                    WHERE pg_stat_activity.datname = '{db_name}'
                      AND pid <> pg_backend_pid()
                """)
                )
                await conn.execute(text(f"DROP DATABASE IF EXISTS {db_name}"))
                print(f"[conftest] Dropped isolated database: {db_name}")
            except Exception as e:
                print(f"[conftest] Error dropping database {db_name}: {e}")
    await engine.dispose()


def run_sync(coro):
    try:
        loop = asyncio.get_event_loop()
    except RuntimeError:
        loop = asyncio.new_event_loop()
        asyncio.set_event_loop(loop)

    if loop.is_running():
        import concurrent.futures

        with concurrent.futures.ThreadPoolExecutor() as executor:
            future = executor.submit(asyncio.run, coro)
            return future.result()
    else:
        return loop.run_until_complete(coro)


worker_id = os.environ.get("PYTEST_XDIST_WORKER")
worker_suffix = f"_{worker_id}" if worker_id else "_test"


# Patch and create databases
def patch_init_db():
    from apps.execution.database.core import DatabaseSessionManager
    from packages.database import RelationalDatabaseManager

    original_exec_init_db = DatabaseSessionManager.init_db
    original_rel_init_db = RelationalDatabaseManager.init_db

    service_map = {
        "Execution": "cadence_edc",
        "eTMF": "cadence_etmf",
        "CTMS": "cadence_ctms",
        "Quality": "cadence_quality",
        "Interop": "cadence_interop",
        "Tickets": "cadence_tickets",
        "Notifications": "cadence_notifications",
        "eConsent": "cadence_econsent",
        "Safety": "cadence_safety",
        "Organization": "cadence_org",
        "eISF": "cadence_eisf",
    }

    base_postgres_url = get_postgres_base_config()

    def patched_exec_init_db(self, database_url: str, **kwargs):
        if not database_url.startswith(("postgres", "postgresql")):
            return original_exec_init_db(self, database_url, **kwargs)
        db_name = f"cadence_edc{worker_suffix}"
        new_url = f"{base_postgres_url}{db_name}"
        return original_exec_init_db(self, new_url, **kwargs)

    def patched_rel_init_db(self, database_url: str, **kwargs):
        if not database_url.startswith(("postgres", "postgresql")):
            return original_rel_init_db(self, database_url, **kwargs)
        base_name = service_map.get(self.service_name, "cadence_edc")
        db_name = f"{base_name}{worker_suffix}"
        new_url = f"{base_postgres_url}{db_name}"
        return original_rel_init_db(self, new_url, **kwargs)

    DatabaseSessionManager.init_db = patched_exec_init_db
    RelationalDatabaseManager.init_db = patched_rel_init_db


databases_pre_created = False

# Create worker isolated databases and perform patching if PostgreSQL is available
try:
    run_sync(create_databases_async(worker_suffix))
    # Override the env var so any standard fallback uses isolated DB too
    os.environ["TEST_DATABASE_URL"] = (
        f"{get_postgres_base_config()}cadence_edc{worker_suffix}"
    )
    patch_init_db()
    databases_pre_created = True
except Exception as e:
    print(
        f"[conftest] Warning: PostgreSQL database is not available ({e}). Skipping worker-isolated DB setup and patching. Tests will fall back to SQLite or mocked states."
    )

# Ensure packages path injection is run before tests start
import packages  # noqa: F401, E402


class MockDatabaseState:
    def __init__(self):
        self.studies = {}  # study_id -> study_node
        self.library_objects = {}  # object_id -> list of version nodes
        self.actions = {}  # action_id -> action_node
        self.locks = {}  # node_id -> tx_id (the transaction holding the lock)

    def update_study_properties(
        self,
        study_id: str,
        user_id: str,
        change_reason: str,
        properties: Dict[str, Any],
        action_id: str,
        tx_id: str,
    ):
        study = self.studies.get(study_id)
        if not study:
            raise ValueError(f"Study {study_id} does not exist.")

        # Verify lock is held by this transaction
        current_lock = self.locks.get(study_id)
        if current_lock and current_lock != tx_id:
            raise TransientError(
                "Lock acquisition timeout: Study is locked by another transaction."
            )

        # Find current active properties
        old_props = (
            study["properties_history"][-1] if study["properties_history"] else None
        )

        # Create new properties
        new_props = dict(properties)
        study["properties_history"].append(new_props)

        # Create action
        action = {
            "id": action_id,
            "user_id": user_id,
            "change_reason": change_reason,
            "before": old_props,
            "after": new_props,
        }
        self.actions[action_id] = action
        study["actions"].append(action)

        return action_id

    def create_library_object_version(
        self, object_id: str, new_properties: Dict[str, Any], tx_id: str
    ):
        exists = object_id in self.library_objects

        if exists:
            # Verify lock is held by this transaction
            current_lock = self.locks.get(object_id)
            if current_lock and current_lock != tx_id:
                raise TransientError(
                    "Lock acquisition timeout: LibraryObject is locked by another transaction."
                )

            versions = self.library_objects[object_id]
            old_version = versions[-1]
            new_version_num = old_version.get("version", 1) + 1
            new_version = {"id": object_id, "version": new_version_num}
            new_version.update(new_properties)
            versions.append(new_version)
            return new_version
        else:
            new_version = {"id": object_id, "version": 1}
            new_version.update(new_properties)
            self.library_objects[object_id] = [new_version]
            return new_version


class MockResult:
    def __init__(self, records):
        self.records = records

    async def single(self):
        return self.records[0] if self.records else None


class MockTransaction:
    def __init__(self, session, state):
        self.session = session
        self.state = state
        self.tx_id = str(uuid.uuid4())
        self.acquired_locks = []

    async def run(self, query, **parameters):
        query_str = query.strip()

        # Check if it's study lock query
        if (
            "MATCH (s:Study {id: $study_id})" in query_str
            and "SET s._lock = true" in query_str
        ):
            study_id = parameters["study_id"]
            current_lock = self.state.locks.get(study_id)
            if current_lock and current_lock != self.tx_id:
                raise TransientError("Lock acquisition timeout: Study is locked.")
            self.state.locks[study_id] = self.tx_id
            self.acquired_locks.append(study_id)
            await asyncio.sleep(
                0.05
            )  # Hold lock briefly to force overlapping task to conflict
            return MockResult([{"id": study_id}])

        # Check if it's library lock query
        elif (
            "MATCH (old:LibraryObject {id: $object_id})" in query_str
            and "SET old._lock = true" in query_str
        ):
            object_id = parameters["object_id"]
            current_lock = self.state.locks.get(object_id)
            if current_lock and current_lock != self.tx_id:
                raise TransientError(
                    "Lock acquisition timeout: LibraryObject is locked."
                )
            self.state.locks[object_id] = self.tx_id
            self.acquired_locks.append(object_id)
            await asyncio.sleep(
                0.05
            )  # Hold lock briefly to force overlapping task to conflict
            return MockResult([{"id": object_id}])

        # Check if it's study properties update
        elif (
            "MATCH (s:Study {id: $study_id})" in query_str
            and "CREATE (a:Action" in query_str
        ):
            study_id = parameters["study_id"]
            action_id = parameters["action_id"]
            user_id = parameters["user_id"]
            change_reason = parameters["change_reason"]
            properties = parameters["properties"]

            act_id = self.state.update_study_properties(
                study_id, user_id, change_reason, properties, action_id, self.tx_id
            )
            return MockResult([{"action_id": act_id}])

        # Check if it's library version update (existing)
        elif (
            "MATCH (old:LibraryObject {id: $object_id})" in query_str
            and "CREATE (new:LibraryObject" in query_str
        ):
            object_id = parameters["object_id"]
            props = parameters["props"]
            new_props = self.state.create_library_object_version(
                object_id, props, self.tx_id
            )
            return MockResult([{"new_props": new_props}])

        # Check if it's library version creation (new/merge)
        elif "MERGE (new:LibraryObject {id: $object_id})" in query_str:
            object_id = parameters["object_id"]
            props = parameters["props"]
            new_props = self.state.create_library_object_version(
                object_id, props, self.tx_id
            )
            return MockResult([{"new_props": new_props}])

        # Check if it's check library object exists
        elif "MATCH (n:LibraryObject {id: $object_id}) RETURN n LIMIT 1" in query_str:
            object_id = parameters["object_id"]
            exists = object_id in self.state.library_objects
            return MockResult([{"n": exists}] if exists else [])

        # Check if it's create study root
        elif "MERGE (s:Study {id: $study_id})" in query_str:
            study_id = parameters["study_id"]
            if study_id not in self.state.studies:
                self.state.studies[study_id] = {
                    "id": study_id,
                    "properties_history": [],
                    "actions": [],
                }
            return MockResult([{"id": study_id}])

        else:
            return MockResult([])

    async def __aenter__(self):
        return self

    async def __aexit__(self, exc_type, exc_val, exc_tb):
        for lock in self.acquired_locks:
            if self.state.locks.get(lock) == self.tx_id:
                del self.state.locks[lock]


class MockSession:
    def __init__(self, state):
        self.state = state

    async def begin_transaction(self):
        return MockTransaction(self, self.state)

    async def __aenter__(self):
        return self

    async def __aexit__(self, exc_type, exc_val, exc_tb):
        pass


class MockDriver:
    def __init__(self, state):
        self.state = state

    def session(self):
        return MockSession(self.state)


class ConcurrencyRunner:
    def __init__(self):
        self.state = MockDatabaseState()
        self.driver = MockDriver(self.state)

    async def run_concurrent(self, *tasks):
        """Runs multiple asynchronous tasks concurrently and returns their results."""
        return await asyncio.gather(*tasks, return_exceptions=False)


@pytest.fixture
def concurrency_runner():
    """Provides a reusable database concurrency runner to validate concurrent execution safety."""
    return ConcurrencyRunner()


def pytest_sessionfinish(session, exitstatus):
    """
    Hook to run after the test session finishes to generate/update the
    Requirements Traceability Matrix (RTM) and GxP Qualification Report,
    and to drop worker-isolated databases.
    """
    # Clean up worker-isolated databases at the end of the session.
    # We bypass this teardown if called from a mock session (e.g., inside tests
    # like test_rtm_generation_conftest_hook_detection in test_cli_etmf_archival.py)
    # to prevent early database dropping of active parallel worker databases.
    if databases_pre_created and session.__class__.__name__ != "MockSession":
        worker_id = os.environ.get("PYTEST_XDIST_WORKER")
        worker_suffix = f"_{worker_id}" if worker_id else "_test"
        try:
            run_sync(drop_databases_async(worker_suffix))
        except Exception as e:
            print(f"[conftest] Error tearing down databases: {e}")

    # Skip report generation if inside a pytest-xdist worker process
    config = getattr(session, "config", None)
    if config and hasattr(config, "workerinput"):
        return

    import subprocess
    import sys

    print(
        "\n--- Running Automated Requirements Traceability Matrix (RTM) Generator ---"
    )
    try:
        cmd = [sys.executable, "scripts/generate_rtm.py"]

        # Check for output dir environment variable
        output_dir = os.environ.get("RTM_OUTPUT_DIR") or os.environ.get(
            "GENERATE_RTM_OUTPUT_DIR"
        )
        if output_dir:
            cmd.extend(["--output-dir", output_dir])

        # Check for dynamic timestamp environment variable
        dynamic_val = os.environ.get("RTM_DYNAMIC_TIMESTAMP") or os.environ.get(
            "GENERATE_RTM_DYNAMIC_TIMESTAMP"
        )
        if dynamic_val is not None:
            if dynamic_val.lower() not in ("", "0", "false", "no", "off"):
                cmd.append("--dynamic-timestamp")

        # Run the script using the same python interpreter
        result = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            check=False,
        )
        print(result.stdout)
        if result.stderr:
            print("Errors from RTM Generator:")
            print(result.stderr)
    except Exception as e:
        print(f"Error executing RTM generator: {e}")
