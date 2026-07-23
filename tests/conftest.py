import asyncio
import uuid
from typing import Any, Dict

import pytest
from neo4j.exceptions import TransientError


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
    Requirements Traceability Matrix (RTM) and GxP Qualification Report.
    """
    import subprocess
    import sys
    print("\n--- Running Automated Requirements Traceability Matrix (RTM) Generator ---")
    try:
        # Run the script using the same python interpreter
        result = subprocess.run(
            [sys.executable, "scripts/generate_rtm.py"],
            capture_output=True,
            text=True,
            check=False
        )
        print(result.stdout)
        if result.stderr:
            print("Errors from RTM Generator:")
            print(result.stderr)
    except Exception as e:
        print(f"Error executing RTM generator: {e}")
