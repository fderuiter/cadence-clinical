import asyncio
import os
import uuid
from typing import Any

import pytest
from neo4j.exceptions import TransientError

# Ensure offline terminology fallback is active for test isolation and speed
os.environ.setdefault("TERMINOLOGY_OFFLINE", "true")
os.environ.setdefault("ALLOW_MOCK_SIGNATURES", "1")
os.environ.setdefault("GATEWAY_SECRET", "internal-gateway-secret-12345")
os.environ.setdefault("SIGNING_SECRET", "designer-amendment-secure-key-12345")
os.environ.setdefault(
    "AUDIT_LOG_SECRET_KEY", "test-gxp-audit-secret-key-placeholder-abc"
)
os.environ.setdefault(
    "INBOUND_EMAIL_HMAC_SECRET", "test-email-hmac-secret-placeholder-xyz"
)


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
    import asyncpg

    postgres_url = get_postgres_base_config()
    clean_url = (
        postgres_url.replace("postgresql+asyncpg://", "postgresql://").rstrip("/")
        + "/postgres"
    )

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

    conn = await asyncpg.connect(clean_url)
    try:
        for db_name in db_names:
            await conn.execute(f"""
                SELECT pg_terminate_backend(pid)
                FROM pg_stat_activity
                WHERE datname = '{db_name}'
                  AND pid <> pg_backend_pid();
            """)
            await conn.execute(f"DROP DATABASE IF EXISTS {db_name};")
            for attempt in range(5):
                try:
                    await conn.execute(f"CREATE DATABASE {db_name};")
                    print(f"[conftest] Created clean isolated database: {db_name}")
                    break
                except Exception:
                    if attempt == 4:
                        raise
                    await asyncio.sleep(0.3)
    finally:
        await conn.close()


async def drop_databases_async(worker_suffix: str):
    import asyncpg

    postgres_url = get_postgres_base_config()
    clean_url = (
        postgres_url.replace("postgresql+asyncpg://", "postgresql://").rstrip("/")
        + "/postgres"
    )

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

    try:
        conn = await asyncpg.connect(clean_url)
        try:
            for db_name in db_names:
                try:
                    await conn.execute(f"""
                        SELECT pg_terminate_backend(pid)
                        FROM pg_stat_activity
                        WHERE datname = '{db_name}'
                          AND pid <> pg_backend_pid();
                    """)
                    await conn.execute(f"DROP DATABASE IF EXISTS {db_name};")
                    print(f"[conftest] Dropped isolated database: {db_name}")
                except Exception as e:
                    print(f"[conftest] Error dropping database {db_name}: {e}")
        finally:
            await conn.close()
    except Exception as e:
        print(f"[conftest] Error connecting to postgres for drop: {e}")


def run_sync(coro):
    try:
        loop = asyncio.get_running_loop()
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


def verify_live_db_connections():
    from neo4j import AsyncGraphDatabase
    from sqlalchemy import text
    from sqlalchemy.ext.asyncio import create_async_engine

    # Check Postgres connection
    base_url = f"{get_postgres_base_config()}postgres"
    print(f"[conftest] Checking PostgreSQL connection: {base_url}")
    try:
        engine = create_async_engine(base_url, isolation_level="AUTOCOMMIT")

        async def check_pg():
            async with engine.connect() as conn:
                await conn.execute(text("SELECT 1"))

        run_sync(check_pg())
        run_sync(engine.dispose())
    except Exception as e:
        import pytest

        pytest.exit(
            f"Database connection error: PostgreSQL instance is unreachable. {e}"
        )

    # Check Neo4j connection
    uri = os.getenv("NEO4J_URI", "bolt://localhost:7687")
    user = os.getenv("NEO4J_USER", "neo4j")
    password = os.getenv("NEO4J_PASSWORD", "password")
    print(f"[conftest] Checking Neo4j connection: {uri}")
    try:

        async def check_neo():
            async with AsyncGraphDatabase.driver(uri, auth=(user, password)) as driver:
                await driver.verify_connectivity()

        run_sync(check_neo())
    except Exception as e:
        import pytest

        pytest.exit(f"Database connection error: Neo4j instance is unreachable. {e}")


async def create_all_schemas_async(worker_suffix: str):
    from sqlalchemy.ext.asyncio import create_async_engine

    from apps.ctms.migrate import run_migrations as run_ctms_migrations
    from apps.ctms.models import Base as CTMSBase
    from apps.econsent.models import Base as EConsentBase
    from apps.eisf.database.migrate import run_migrations as run_eisf_migrations
    from apps.eisf.models import Base as EISFBase
    from apps.etmf.database.migrate import run_migrations as run_etmf_migrations
    from apps.etmf.models import Base as ETMFBase

    # Migrations
    from apps.execution.database.migrate import run_migrations as run_exec_migrations

    # Import bases
    from apps.execution.database.models import Base as ExecBase
    from apps.interop.models import Base as InteropBase
    from apps.notifications.models import Base as NotificationsBase
    from apps.org.models import Base as OrgBase
    from apps.quality.migrate import run_migrations as run_quality_migrations
    from apps.quality.models import Base as QualityBase
    from apps.safety.models import Base as SafetyBase
    from apps.tickets.models import Base as TicketsBase

    service_bases = {
        "cadence_edc": (ExecBase, run_exec_migrations),
        "cadence_etmf": (ETMFBase, run_etmf_migrations),
        "cadence_ctms": (CTMSBase, run_ctms_migrations),
        "cadence_quality": (QualityBase, run_quality_migrations),
        "cadence_interop": (InteropBase, None),
        "cadence_tickets": (TicketsBase, None),
        "cadence_notifications": (NotificationsBase, None),
        "cadence_econsent": (EConsentBase, None),
        "cadence_safety": (SafetyBase, None),
        "cadence_org": (OrgBase, None),
        "cadence_eisf": (EISFBase, run_eisf_migrations),
    }

    base_postgres_url = get_postgres_base_config()
    for db_prefix, (base, migration_func) in service_bases.items():
        db_name = f"{db_prefix}{worker_suffix}"
        db_url = f"{base_postgres_url}{db_name}"

        if migration_func is not None:
            await migration_func(db_url)
        else:
            engine = create_async_engine(db_url)
            async with engine.begin() as conn:
                await conn.run_sync(base.metadata.create_all)
            await engine.dispose()


if os.environ.get("USE_LIVE_DB") == "true":
    verify_live_db_connections()


# Patch and create databases
_initialized_databases = set()


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
        if os.environ.get("USE_LIVE_DB") == "true" or database_url.startswith(
            ("postgres", "postgresql")
        ):
            db_name = f"cadence_edc{worker_suffix}"
            _initialized_databases.add("cadence_edc")
            new_url = f"{base_postgres_url}{db_name}"
            return original_exec_init_db(self, new_url, **kwargs)
        return original_exec_init_db(self, database_url, **kwargs)

    def patched_rel_init_db(self, database_url: str, **kwargs):
        if os.environ.get("USE_LIVE_DB") == "true" or database_url.startswith(
            ("postgres", "postgresql")
        ):
            base_name = service_map.get(self.service_name, "cadence_edc")
            db_name = f"{base_name}{worker_suffix}"
            _initialized_databases.add(base_name)
            new_url = f"{base_postgres_url}{db_name}"
            return original_rel_init_db(self, new_url, **kwargs)
        return original_rel_init_db(self, database_url, **kwargs)

    from sqlalchemy import MetaData

    original_drop_all = MetaData.drop_all

    def patched_drop_all(self, bind=None, tables=None, checkfirst=True):
        if os.environ.get("USE_LIVE_DB") == "true" or os.environ.get(
            "TEST_DATABASE_URL", ""
        ).startswith(("postgres", "postgresql")):
            return None
        return original_drop_all(self, bind=bind, tables=tables, checkfirst=checkfirst)

    MetaData.drop_all = patched_drop_all

    DatabaseSessionManager.init_db = patched_exec_init_db
    RelationalDatabaseManager.init_db = patched_rel_init_db


databases_pre_created = False

# Create worker isolated databases and perform patching if PostgreSQL is available
try:
    from filelock import FileLock

    lock_path = "/tmp/postgres_db_creation.lock"
    with FileLock(lock_path, timeout=180):
        run_sync(create_databases_async(worker_suffix))
        # Override the env var so any standard fallback uses isolated DB too
        os.environ["TEST_DATABASE_URL"] = (
            f"{get_postgres_base_config()}cadence_edc{worker_suffix}"
        )
        patch_init_db()
        databases_pre_created = True

        if os.environ.get("USE_LIVE_DB") == "true" or os.environ.get(
            "TEST_DATABASE_URL", ""
        ).startswith(("postgres", "postgresql")):
            print(
                f"[conftest] Initializing all PostgreSQL schemas for worker {worker_suffix}..."
            )
            run_sync(create_all_schemas_async(worker_suffix))
except Exception as e:
    if os.environ.get("GITHUB_ACTIONS") == "true":
        print(f"[conftest] ERROR: Database initialization failed in CI: {e}")
        if os.environ.get("USE_LIVE_DB") == "true" or os.environ.get(
            "TEST_DATABASE_URL", ""
        ).startswith(("postgres", "postgresql")):
            import pytest

            pytest.exit(
                f"Database connection error: PostgreSQL instance is unreachable. {e}"
            )
        raise
    elif os.environ.get("USE_LIVE_DB") == "true" or os.environ.get(
        "TEST_DATABASE_URL", ""
    ).startswith(("postgres", "postgresql")):
        import pytest

        pytest.exit(
            f"Database connection error: Failed to create/initialize isolated PostgreSQL databases: {e}"
        )
    else:
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
        properties: dict[str, Any],
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
        self, object_id: str, new_properties: dict[str, Any], tx_id: str
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
        if (
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
        if (
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
        if (
            "MATCH (old:LibraryObject {id: $object_id})" in query_str
            and "CREATE (new:LibraryObject" in query_str
        ) or "MERGE (new:LibraryObject {id: $object_id})" in query_str:
            object_id = parameters["object_id"]
            props = parameters["props"]
            new_props = self.state.create_library_object_version(
                object_id, props, self.tx_id
            )
            return MockResult([{"new_props": new_props}])

        # Check if it's check library object exists
        if "MATCH (n:LibraryObject {id: $object_id}) RETURN n LIMIT 1" in query_str:
            object_id = parameters["object_id"]
            exists = object_id in self.state.library_objects
            return MockResult([{"n": exists}] if exists else [])

        # Check if it's create study root
        if "MERGE (s:Study {id: $study_id})" in query_str:
            study_id = parameters["study_id"]
            if study_id not in self.state.studies:
                self.state.studies[study_id] = {
                    "id": study_id,
                    "properties_history": [],
                    "actions": [],
                }
            return MockResult([{"id": study_id}])

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


def pytest_unconfigure(config):
    """Clean up worker-isolated databases when the test process unconfigures."""
    if databases_pre_created:
        worker_id = os.environ.get("PYTEST_XDIST_WORKER")
        worker_suffix = f"_{worker_id}" if worker_id else "_test"
        try:
            run_sync(drop_databases_async(worker_suffix))
        except Exception as e:
            print(f"[conftest] Error tearing down databases: {e}")


def pytest_sessionfinish(session, exitstatus):
    """
    Hook to run after the test session finishes to generate/update the
    Requirements Traceability Matrix (RTM) and GxP Qualification Report.
    """
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

        # Check for draft environment variable
        draft_val = os.environ.get("RTM_DRAFT") or os.environ.get("GENERATE_RTM_DRAFT")
        if draft_val is not None:
            if draft_val.lower() not in ("", "0", "false", "no", "off"):
                cmd.append("--draft")

        # To prevent local test execution speed penalties, avoid running the check
        # synchronously during local pytest executions (when GITHUB_ACTIONS is not 'true').
        # However, we run it synchronously if a custom output directory is set (such as
        # in test_rtm_generation_conftest_hook_detection) or in CI.
        is_ci = os.environ.get("GITHUB_ACTIONS") == "true"
        is_test = output_dir is not None or dynamic_val is not None

        if not is_ci and not is_test:
            print(
                "Local pytest execution detected: launching RTM Generator asynchronously in background."
            )
            subprocess.Popen(
                cmd,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
            )
        else:
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


# =========================================================================
# Shared multi-service RBAC test harness fixtures
# =========================================================================

import contextlib
from unittest.mock import AsyncMock, MagicMock, patch

import httpx
import pytest_asyncio

from apps.designer.main import app as designer_app
from apps.etmf.database import db_manager as etmf_db_manager
from apps.etmf.main import app as etmf_app
from apps.etmf.models import Base as ETMFBase
from apps.execution.database.core import db_manager as exec_db_manager
from apps.execution.database.models import Base as ExecBase
from apps.execution.main import app as exec_app


@pytest_asyncio.fixture
async def shared_sqlite_dbs():
    """
    Setup in-memory SQLite databases for execution and etmf using their own db_manager/Base singletons.
    Follows the init_db + create_all + teardown pattern.
    """
    etmf_db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with etmf_db_manager.engine.begin() as conn:
        await conn.run_sync(ETMFBase.metadata.create_all)

    exec_db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with exec_db_manager.engine.begin() as conn:
        await conn.run_sync(ExecBase.metadata.create_all)

    yield

    try:
        async with etmf_db_manager.engine.begin() as conn:
            await conn.run_sync(ETMFBase.metadata.drop_all)
        await etmf_db_manager.close()
    except Exception:
        pass

    try:
        async with exec_db_manager.engine.begin() as conn:
            await conn.run_sync(ExecBase.metadata.drop_all)
        await exec_db_manager.close()
    except Exception:
        pass


@pytest.fixture
def mock_designer_driver():
    """
    Injects a mock or fake Neo4j driver into designer_app.state.driver after client creation,
    and restores the original driver on teardown.
    """
    if os.environ.get("USE_LIVE_DB") == "true":
        from neo4j import AsyncGraphDatabase

        uri = os.getenv("NEO4J_URI", "bolt://localhost:7687")
        user = os.getenv("NEO4J_USER", "neo4j")
        password = os.getenv("NEO4J_PASSWORD", "password")
        real_driver = AsyncGraphDatabase.driver(uri, auth=(user, password))

        original_driver = getattr(designer_app.state, "driver", None)
        designer_app.state.driver = real_driver

        yield real_driver

        run_sync(real_driver.close())
        designer_app.state.driver = original_driver
    else:
        driver_mock = MagicMock()
        session_mock = AsyncMock()
        session_ctx = AsyncMock()
        session_ctx.__aenter__.return_value = session_mock
        driver_mock.session.return_value = session_ctx

        tx_mock = AsyncMock()
        tx_mock.__aenter__.return_value = tx_mock
        session_mock.begin_transaction.return_value = tx_mock

        driver_mock._tx_mock = tx_mock
        driver_mock._session_mock = session_mock

        original_driver = getattr(designer_app.state, "driver", None)
        designer_app.state.driver = driver_mock

        yield driver_mock

        designer_app.state.driver = original_driver


@pytest_asyncio.fixture
async def execution_client():
    """Expose httpx.AsyncClient instance with ASGITransport for the execution app."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=exec_app), base_url="http://test"
    ) as client:
        yield client


@pytest_asyncio.fixture
async def etmf_client():
    """Expose httpx.AsyncClient instance with ASGITransport for the etmf app."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=etmf_app), base_url="http://test"
    ) as client:
        yield client


@pytest_asyncio.fixture
async def designer_client(mock_designer_driver):
    """Expose httpx.AsyncClient instance with ASGITransport for the designer app (mocked Neo4j driver injected)."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=designer_app), base_url="http://test"
    ) as client:
        yield client


@pytest.fixture
def intercept_cross_service_calls():
    """
    Patch httpx.AsyncClient.send globally to route service-to-service HTTP calls
    to the target in-process app (execution, etmf, or designer) while keeping signed headers intact.
    """
    original_send = httpx.AsyncClient.send

    async def mock_send(
        self, request: httpx.Request, *args, **kwargs
    ) -> httpx.Response:
        url_str = str(request.url)
        target_app = None

        if (
            "localhost:8002" in url_str
            or "api/v1/execution" in url_str
            or "api/v1/subjects" in url_str
        ):
            target_app = exec_app
        elif "localhost:8003" in url_str or "api/v1/etmf" in url_str:
            target_app = etmf_app
        elif (
            "localhost:8001" in url_str
            or "api/v1/studies" in url_str
            or "api/v2/studies" in url_str
            or "api/v1/terminology" in url_str
            or "soa-projection" in url_str
        ):
            target_app = designer_app

        if target_app is not None:
            transport = httpx.ASGITransport(app=target_app)
            async with httpx.AsyncClient(transport=transport) as local_client:
                return await original_send(local_client, request, *args, **kwargs)

        return await original_send(self, request, *args, **kwargs)

    with patch("httpx.AsyncClient.send", mock_send):
        yield


@pytest.fixture
def signed_headers():
    """
    Factory fixture to build valid V2 gateway-signed headers for testing.
    Resolves GATEWAY_SECRET from env, defaulting to 'internal-gateway-secret-12345'.
    Always includes tenant_id in both the signed payload and X-Tenant-Id header.
    Supports a 'tamper' mode by passing tamper_tenant_id to sign with a different tenant_id.
    """
    import os
    import time

    from packages.security.signing import generate_gateway_signature

    def _factory(
        user_id: str,
        roles: str,
        change_reason: str,
        tenant_id: str = "tenant_default",
        site_id: str | None = None,
        sponsor_id: str | None = None,
        unblinded_access: bool = False,
        sig_token: str | None = None,
        study_id: str | None = None,
        tamper_tenant_id: str | None = None,
    ) -> dict[str, str]:
        secret_env = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")
        secret_bytes = (
            secret_env.encode("utf-8") if isinstance(secret_env, str) else secret_env
        )
        timestamp = str(time.time())

        # Support tamper mode: sign with tamper_tenant_id but send tenant_id in X-Tenant-Id
        sign_tenant = tamper_tenant_id if tamper_tenant_id is not None else tenant_id

        signature = generate_gateway_signature(
            user_id=user_id,
            roles=roles,
            timestamp=timestamp,
            secret=secret_bytes,
            change_reason=change_reason,
            site_id=site_id,
            sponsor_id=sponsor_id,
            unblinded_access=unblinded_access,
            tenant_id=sign_tenant,
            sig_token=sig_token,
        )

        headers = {
            "X-User-Id": user_id,
            "X-User-Roles": roles,
            "X-Gateway-Timestamp": timestamp,
            "X-Gateway-Signature": signature,
            "X-Signature-Version": "2",
            "X-Change-Reason": change_reason,
            "X-Tenant-Id": tenant_id,
        }

        if site_id is not None:
            headers["X-Site-Id"] = site_id
        if sponsor_id is not None:
            headers["X-Sponsor-Id"] = sponsor_id
        if sig_token is not None:
            headers["X-Sig-Token"] = sig_token
        if study_id is not None:
            headers["X-Study-Id"] = study_id
        if unblinded_access:
            headers["X-Unblinded-Access"] = "true"

        return headers

    return _factory


@pytest.fixture
def capture_cross_service_calls():
    """
    Fixture to patch httpx.AsyncClient.request to capture outbound requests,
    exposing them to the test, and providing a helper to replay them.
    """
    import json as json_lib
    from unittest.mock import patch

    import httpx

    class CrossServiceCallCapture:
        def __init__(self):
            self.calls = []
            self.default_response_json = {"status": "ok"}
            self.default_response_status = 200
            self.passthrough = False

        def clear(self):
            self.calls.clear()

        async def replay(
            self, client: httpx.AsyncClient, captured_call: dict, **kwargs
        ) -> httpx.Response:
            method = captured_call.get("method", "GET")
            path = captured_call.get("path", "/")
            headers = dict(captured_call.get("headers", {}))

            if "headers" in kwargs:
                headers.update(kwargs.pop("headers"))

            json_payload = kwargs.pop("json", captured_call.get("json"))
            content = kwargs.pop("content", captured_call.get("body"))

            return await client.request(
                method=method,
                url=path,
                headers=headers,
                json=json_payload,
                content=content,
                **kwargs,
            )

    capture_obj = CrossServiceCallCapture()
    original_request = httpx.AsyncClient.request

    async def mock_request(self, method: str, url, *args, **kwargs):
        headers = kwargs.get("headers") or {}
        headers_dict = dict(headers)

        json_val = kwargs.get("json")
        body_val = kwargs.get("content") or kwargs.get("data")

        from httpx import URL

        parsed_url = URL(url)
        path = parsed_url.path
        if parsed_url.query:
            path = f"{path}?{parsed_url.query.decode('utf-8')}"

        call_info = {
            "method": method.upper(),
            "url": str(url),
            "path": path,
            "headers": headers_dict,
            "body": body_val,
            "json": json_val,
        }
        capture_obj.calls.append(call_info)

        if capture_obj.passthrough:
            return await original_request(self, method, url, *args, **kwargs)

        resp_json = capture_obj.default_response_json
        resp_status = capture_obj.default_response_status

        return httpx.Response(
            status_code=resp_status,
            content=json_lib.dumps(resp_json).encode("utf-8"),
            headers={"content-type": "application/json"},
            request=httpx.Request(method, url),
        )

    with patch.object(httpx.AsyncClient, "request", mock_request):
        yield capture_obj


async def clean_neo4j_graph():
    import os

    from neo4j import AsyncGraphDatabase

    uri = os.getenv("NEO4J_URI", "bolt://localhost:7687")
    user = os.getenv("NEO4J_USER", "neo4j")
    password = os.getenv("NEO4J_PASSWORD", "password")

    try:
        async with AsyncGraphDatabase.driver(uri, auth=(user, password)) as driver:
            async with driver.session() as session:
                await session.run("MATCH (n) DETACH DELETE n")
        print("[conftest] Live Neo4j graph database cleared successfully.")
    except Exception as e:
        print(f"[conftest] Error clearing live Neo4j graph database: {e}")


async def clean_postgres_databases():
    from sqlalchemy import text
    from sqlalchemy.ext.asyncio import create_async_engine

    from apps.ctms.models import Base as CTMSBase
    from apps.econsent.models import Base as EConsentBase
    from apps.eisf.models import Base as EISFBase
    from apps.etmf.models import Base as ETMFBase

    # Import bases
    from apps.execution.database.models import Base as ExecBase
    from apps.interop.models import Base as InteropBase
    from apps.notifications.models import Base as NotificationsBase
    from apps.org.models import Base as OrgBase
    from apps.quality.models import Base as QualityBase
    from apps.safety.models import Base as SafetyBase
    from apps.tickets.models import Base as TicketsBase

    service_bases = {
        "cadence_edc": ExecBase,
        "cadence_etmf": ETMFBase,
        "cadence_ctms": CTMSBase,
        "cadence_quality": QualityBase,
        "cadence_interop": InteropBase,
        "cadence_tickets": TicketsBase,
        "cadence_notifications": NotificationsBase,
        "cadence_econsent": EConsentBase,
        "cadence_safety": SafetyBase,
        "cadence_org": OrgBase,
        "cadence_eisf": EISFBase,
    }

    import unittest.mock

    base_postgres_url = get_postgres_base_config()
    is_mocked = isinstance(create_async_engine, unittest.mock.Mock)
    for db_prefix, base in service_bases.items():
        if (
            os.environ.get("USE_LIVE_DB") != "true"
            and not is_mocked
            and db_prefix not in _initialized_databases
        ):
            continue
        db_name = f"{db_prefix}{worker_suffix}"
        db_url = f"{base_postgres_url}{db_name}"
        engine = create_async_engine(db_url)
        try:
            async with engine.begin() as conn:
                # Disable triggers and foreign keys for safe, trigger-free TRUNCATE of audited/restricted tables
                await conn.execute(text("SET session_replication_role = 'replica';"))

                # Truncate all tables in a single statement for extreme speedup, with fallback
                table_names = [
                    f'"{table.name}"' for table in base.metadata.sorted_tables
                ]
                if table_names:
                    truncate_query = f"TRUNCATE TABLE {', '.join(table_names)} RESTART IDENTITY CASCADE;"
                    try:
                        await conn.execute(text(truncate_query))
                    except Exception:
                        # Fallback to per-table truncate/delete if combined fails
                        for table in reversed(base.metadata.sorted_tables):
                            try:
                                await conn.execute(
                                    text(
                                        f'TRUNCATE TABLE "{table.name}" RESTART IDENTITY CASCADE;'
                                    )
                                )
                            except Exception:
                                with contextlib.suppress(Exception):
                                    await conn.execute(table.delete())

                # Also truncate audit schema tables if they exist
                with contextlib.suppress(Exception):
                    await conn.execute(
                        text(
                            'TRUNCATE TABLE "audit_schema"."audit_logs" RESTART IDENTITY CASCADE;'
                        )
                    )
                with contextlib.suppress(Exception):
                    await conn.execute(
                        text(
                            'TRUNCATE TABLE "audit_schema"."audit_ledger_seals" RESTART IDENTITY CASCADE;'
                        )
                    )

                # Restore triggers/fk checks back to normal
                await conn.execute(text("SET session_replication_role = 'origin';"))
            print(f"[conftest] PostgreSQL database {db_name} cleaned successfully.")
        except Exception as e:
            print(f"[conftest] Error cleaning database {db_name}: {e}")
        finally:
            await engine.dispose()


@pytest_asyncio.fixture(autouse=True)
async def cleanup_databases_fixture():
    """
    Autouse fixture that clears live Neo4j and PostgreSQL databases
    before and after every single test case when USE_LIVE_DB=true is active.
    """
    is_postgres = os.environ.get("TEST_DATABASE_URL", "").startswith(
        ("postgres", "postgresql")
    )
    is_live_db = os.environ.get("USE_LIVE_DB") == "true"

    if not is_live_db and not is_postgres:
        yield
        return

    if is_live_db or is_postgres:
        await clean_postgres_databases()
    if is_live_db:
        await clean_neo4j_graph()

    yield

    if is_live_db or is_postgres:
        await clean_postgres_databases()
    if is_live_db:
        await clean_neo4j_graph()
