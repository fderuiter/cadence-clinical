import asyncio
import time
import uuid

import pytest
import pytest_asyncio
from httpx import ASGITransport, AsyncClient

from apps.gateway.main import generate_signature
from apps.quality.database import db_manager
from apps.quality.main import app
from apps.quality.models import (
    Base,
)


def get_auth_headers(roles: str = "admin", change_reason: str = "") -> dict:
    """
    Helper to generate valid gateway V2 signed headers for testing.
    """
    timestamp = str(time.time())
    user_id = "quality_test_user"
    sig = generate_signature(
        user_id, roles, timestamp, version="2", change_reason=change_reason
    )
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
    }
    if change_reason:
        headers["X-Change-Reason"] = change_reason
    return headers


@pytest_asyncio.fixture(autouse=True)
async def setup_quality_concurrency_db():
    """
    Setup a shared-cache in-memory database configuration to enable safe
    concurrent connections for parallel integration test executions.
    """
    import os

    from sqlalchemy.pool import NullPool

    db_uri = f"sqlite+aiosqlite:///file:quality_concurrency_{uuid.uuid4().hex}?mode=memory&cache=shared&uri=true"

    # Export QUALITY_DATABASE_URL to env so the FastAPI lifespan loads the exact same DB
    os.environ["QUALITY_DATABASE_URL"] = db_uri
    db_manager.init_db(db_uri, echo=False, poolclass=NullPool)

    # Keep a connection open to prevent the shared-cache database from being destroyed
    keepalive = await db_manager.engine.connect()

    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)

    yield

    # Clean up environment override
    os.environ.pop("QUALITY_DATABASE_URL", None)

    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)

    await keepalive.close()
    await db_manager.close()


@pytest.mark.asyncio
async def test_parallel_capa_status_transitions_concurrency():
    """
    Scenario: Parallel CAPA Status Transitions
    User Intent: Two quality assurance managers attempt to transition the status of the same corrective action simultaneously.
    Desired Experience: One update succeeds (200), and all other concurrent attempts fail safely with an HTTP 409 Conflict.
    """
    headers = get_auth_headers(roles="admin", change_reason="Setup Deviation and CAPA")

    # 1. Create a deviation via direct HTTP API call
    dev_payload = {
        "study_id": "study_concurrency_capa",
        "site_id": "site_concurrency_capa",
        "title": "CAPA Concurrency Event",
        "description": "Deviation to test parallel CAPA status transitions",
        "severity": "CRITICAL",
        "type": "OTHER",
    }

    async with AsyncClient(
        transport=ASGITransport(app=app), base_url="http://test"
    ) as client:
        res = await client.post(
            "/api/v1/quality/deviations", json=dev_payload, headers=headers
        )
        assert res.status_code == 201, f"Failed to create deviation: {res.text}"
        dev_id = res.json()["id"]

        # 2. Create associated CAPA Record via direct HTTP API call
        capa_payload = {
            "deviation_id": dev_id,
            "capa_type": "CORRECTIVE",
            "action_plan": "Execute high-load parallel tests",
        }
        res = await client.post(
            "/api/v1/quality/capas", json=capa_payload, headers=headers
        )
        assert res.status_code == 201, f"Failed to create CAPA: {res.text}"
        capa_data = res.json()
        capa_id = capa_data["id"]
        version_index = capa_data["version_index"]
        assert version_index == 1

        # 3. Simulate multiple parallel transition requests concurrently hitting the status transition endpoint
        # Each manager tries to transition to UNDER_REVIEW using the same initial version_index
        num_parallel_requests = 5
        tasks = []
        for i in range(num_parallel_requests):
            task_headers = get_auth_headers(
                roles="admin", change_reason=f"Concurrent CAPA transition manager {i}"
            )
            transition_payload = {
                "to_status": "UNDER_REVIEW",
                "version_index": version_index,
            }
            tasks.append(
                client.post(
                    f"/api/v1/quality/capas/{capa_id}/transition",
                    json=transition_payload,
                    headers=task_headers,
                )
            )

        # Trigger parallel task collection using asyncio.gather
        responses = await asyncio.gather(*tasks, return_exceptions=True)

        # 4. Assert exactly one operation completes successfully (200 OK) and the rest are HTTP 409 Conflict
        success_count = 0
        conflict_count = 0
        unexpected_responses = []

        for idx, r in enumerate(responses):
            if isinstance(r, Exception):
                unexpected_responses.append(f"Client raised Python exception: {r}")
                continue
            print(
                f"DEBUG CAPA Transition {idx} - Status: {r.status_code}, Body: {r.json() if r.status_code == 200 else r.text}"
            )
            if r.status_code == 200:
                success_count += 1
            elif r.status_code == 409:
                conflict_count += 1
            else:
                unexpected_responses.append(
                    f"Unexpected HTTP status {r.status_code}: {r.text}"
                )

        print(
            f"CAPA status transition concurrency results - Successes: {success_count}, Conflicts: {conflict_count}"
        )
        if unexpected_responses:
            print(f"Unexpected responses / failures: {unexpected_responses}")

        assert success_count == 1, (
            f"Expected exactly 1 success, got {success_count}. Unexpected: {unexpected_responses}"
        )
        assert conflict_count == num_parallel_requests - 1, (
            f"Expected {num_parallel_requests - 1} conflicts, got {conflict_count}."
        )


@pytest.mark.asyncio
async def test_parallel_rca_updates_concurrency():
    """
    Scenario: Simultaneous Root Cause Analysis Edits
    User Intent: Two investigators submit conflicting updates to the same Root Cause Analysis (RCA) record at the same time.
    Desired Experience: One update succeeds (200), and all other concurrent attempts fail safely with an HTTP 409 Conflict.
    """
    headers = get_auth_headers(roles="admin", change_reason="Setup Deviation and RCA")

    # 1. Create a deviation via direct HTTP API call
    dev_payload = {
        "study_id": "study_concurrency_rca",
        "site_id": "site_concurrency_rca",
        "title": "RCA Concurrency Event",
        "description": "Deviation to test parallel RCA updates",
        "severity": "MAJOR",
        "type": "OTHER",
    }

    async with AsyncClient(
        transport=ASGITransport(app=app), base_url="http://test"
    ) as client:
        res = await client.post(
            "/api/v1/quality/deviations", json=dev_payload, headers=headers
        )
        assert res.status_code == 201, f"Failed to create deviation: {res.text}"
        dev_id = res.json()["id"]

        # 2. Create the initial Root Cause Analysis (RCA) record
        rca_payload = {
            "methodology": "5 Whys",
            "investigation_details": "Initial investigation details under check",
            "root_cause_summary": "Initial root cause summary identified",
        }
        res = await client.post(
            f"/api/v1/quality/deviations/{dev_id}/rca",
            json=rca_payload,
            headers=headers,
        )
        assert res.status_code == 200, f"Failed to create RCA: {res.text}"
        rca_data = res.json()
        version_index = rca_data["version_index"]
        assert version_index == 1

        # 3. Simulate multiple parallel update requests concurrently hitting the RCA update endpoint
        # Each investigator tries to edit the RCA details using the same initial version_index
        num_parallel_requests = 5
        tasks = []
        for i in range(num_parallel_requests):
            task_headers = get_auth_headers(
                roles="admin", change_reason=f"Concurrent RCA investigator edit {i}"
            )
            update_payload = {
                "methodology": "Fishbone Diagram" if i % 2 == 0 else "5 Whys",
                "investigation_details": f"Parallel update text from investigator {i}",
                "root_cause_summary": f"Identified parallel cause index {i}",
                "version_index": version_index,
            }
            # The RCA creation/update endpoint supports both PUT and POST
            tasks.append(
                client.put(
                    f"/api/v1/quality/deviations/{dev_id}/rca",
                    json=update_payload,
                    headers=task_headers,
                )
            )

        # Trigger parallel task collection using asyncio.gather
        responses = await asyncio.gather(*tasks, return_exceptions=True)

        # 4. Assert exactly one operation completes successfully (200 OK) and the rest are HTTP 409 Conflict
        success_count = 0
        conflict_count = 0
        unexpected_responses = []

        for idx, r in enumerate(responses):
            if isinstance(r, Exception):
                unexpected_responses.append(f"Client raised Python exception: {r}")
                continue
            print(
                f"DEBUG RCA Update {idx} - Status: {r.status_code}, Body: {r.json() if r.status_code == 200 else r.text}"
            )
            if r.status_code == 200:
                success_count += 1
            elif r.status_code == 409:
                conflict_count += 1
            else:
                unexpected_responses.append(
                    f"Unexpected HTTP status {r.status_code}: {r.text}"
                )

        print(
            f"RCA update concurrency results - Successes: {success_count}, Conflicts: {conflict_count}"
        )
        if unexpected_responses:
            print(f"Unexpected responses / failures: {unexpected_responses}")

        assert success_count == 1, (
            f"Expected exactly 1 success, got {success_count}. Unexpected: {unexpected_responses}"
        )
        assert conflict_count == num_parallel_requests - 1, (
            f"Expected {num_parallel_requests - 1} conflicts, got {conflict_count}."
        )
