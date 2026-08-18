import asyncio
import hashlib
import hmac
import json
import os
import time

import httpx
import pytest
import pytest_asyncio

from apps.execution.database.context import (
    current_change_reason,
    current_session,
    current_user_id,
)
from apps.execution.database.core import db_manager
from apps.execution.database.models import Base
from apps.execution.main import app
from apps.execution.translator import process_translation

GATEWAY_SECRET = os.getenv(
    "GATEWAY_SECRET", "internal-gateway-secret-12345"
)  # pragma: allowlist secret


def get_auth_headers(
    user_id="test_user", roles="admin", change_reason="system_operation"
):
    timestamp = str(time.time())
    payload = {
        "change_reason": change_reason,
        "roles": roles,
        "timestamp": timestamp,
        "user_id": user_id,
    }
    serialized = json.dumps(payload, sort_keys=True, separators=(",", ":"))
    signature = hmac.new(
        GATEWAY_SECRET.encode(), serialized.encode(), hashlib.sha256
    ).hexdigest()
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }


@pytest_asyncio.fixture(autouse=True)
async def setup_test_db():
    db_manager.init_db(
        os.getenv(
            "TEST_DATABASE_URL",
            "sqlite+aiosqlite:///:memory:",
        )
    )
    async with db_manager.engine.begin() as conn:
        from sqlalchemy import text

        if db_manager.engine.dialect.name == "postgresql":
            await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_translation_status_and_listing_success():
    """Test standard flow: Post job, retrieve status by ID, and list history."""
    study_payload = {
        "study_id": "test_recovery_study",
        "payload": {
            "name": "Recovery Trial",
            "protocol": {
                "items": [
                    {"id": "q1", "name": "Question 1", "type": "string"},
                ]
            },
        },
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        response = await client.post(
            "/events/study-published", json=study_payload, headers=get_auth_headers()
        )
        assert response.status_code == 200
        res_json = response.json()
        assert res_json["status"] == "accepted"
        assert "job_id" in res_json
        job_id = res_json["job_id"]

        # Wait for the job to complete
        for _ in range(50):
            status_response = await client.get(
                f"/api/v1/execution/translation/jobs/{job_id}",
                headers=get_auth_headers(),
            )
            assert status_response.status_code == 200
            status_json = status_response.json()
            if status_json["status"] in ("COMPLETED", "FAILED"):
                break
            await asyncio.sleep(0.1)

        # Verify exact details of completed job
        assert status_json["status"] == "COMPLETED"
        assert status_json["study_id"] == "test_recovery_study"
        assert status_json["odm_payload"] is not None
        assert status_json["openrosa_payload"] is not None
        assert status_json["error_message"] is None
        assert isinstance(status_json["warnings"], list)

        # Verify listing endpoint
        list_response = await client.get(
            "/api/v1/execution/translation/jobs", headers=get_auth_headers()
        )
        assert list_response.status_code == 200
        list_json = list_response.json()
        assert len(list_json) >= 1
        matching_job = [j for j in list_json if j["id"] == job_id][0]
        assert matching_job["status"] == "COMPLETED"
        assert isinstance(matching_job["warnings"], list)


@pytest.mark.asyncio
async def test_audited_fallbacks_and_warning_propagation():
    """Verify that translation auto-populates missing fields with fallbacks, completes successfully,
    and records all generated fallbacks as structured warning metadata in the job record and API responses.
    """
    study_id = "draft_study_missing_fields_123"
    study_payload = {
        "study_id": study_id,
        "payload": {
            # 'id' and 'name' omitted to trigger study-level fallbacks
            "protocol": {
                "items": [
                    {"id": "sbp", "name": "Systolic BP", "type": "int"},
                ]
            },
            "documentedBy": [
                {
                    # Omit doc id, type, templateName, instanceType, language, versions id/status
                    "versions": [
                        {
                            "contents": [
                                {
                                    "displaySectionTitle": "Overview Section",
                                }
                            ]
                        }
                    ]
                }
            ],
        },
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        response = await client.post(
            "/events/study-published", json=study_payload, headers=get_auth_headers()
        )
        assert response.status_code == 200
        job_id = response.json()["job_id"]

        # Poll job status
        for _ in range(50):
            status_response = await client.get(
                f"/api/v1/execution/translation/jobs/{job_id}",
                headers=get_auth_headers(),
            )
            assert status_response.status_code == 200
            status_json = status_response.json()
            if status_json["status"] in ("COMPLETED", "FAILED"):
                break
            await asyncio.sleep(0.1)

        # Requirement 4: Translation completes successfully even with fallbacks
        assert status_json["status"] == "COMPLETED"
        assert status_json["odm_payload"] is not None
        assert status_json["openrosa_payload"] is not None

        # Requirement 2 & 3: Metadata attached and exposed via API
        warnings = status_json.get("warnings")
        assert isinstance(warnings, list)
        assert len(warnings) > 0

        # Requirement 1: Check recorded fields (study_id, name, documentedBy fields, language, versions)
        warning_fields = [w["field"] for w in warnings]
        assert "study_id" in warning_fields
        assert "name" in warning_fields
        assert "documentedBy.id" in warning_fields
        assert "documentedBy.type" in warning_fields
        assert "documentedBy.templateName" in warning_fields
        assert "documentedBy.instanceType" in warning_fields
        assert "documentedBy.language" in warning_fields or "documentedBy.language.id" in warning_fields
        assert "documentedBy.versions.id" in warning_fields
        assert "documentedBy.versions.status" in warning_fields
        assert "documentedBy.versions.contents.id" in warning_fields

        # Check structure of each warning object
        for w in warnings:
            assert "field" in w
            assert "generated_value" in w
            assert "warning_reason" in w
            assert len(w["warning_reason"]) > 0

        # Also verify job history list API endpoint contains warnings
        list_response = await client.get(
            "/api/v1/execution/translation/jobs", headers=get_auth_headers()
        )
        assert list_response.status_code == 200
        list_json = list_response.json()
        matching = [j for j in list_json if j["id"] == job_id][0]
        assert matching["warnings"] == warnings


@pytest.mark.asyncio
async def test_translation_error_status_and_rollback():
    """Test failure flow: invalid study triggers rollback and writes FAILED status.
    # @req:Trace-12
    """
    study_payload = {
        "study_id": "test_failed_study",
        "payload": {
            "name": "Failed Trial"
            # protocol is missing, will trigger ValueError
        },
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        response = await client.post(
            "/events/study-published", json=study_payload, headers=get_auth_headers()
        )
        assert response.status_code == 200
        res_json = response.json()
        job_id = res_json["job_id"]

        # Wait for job to fail
        for _ in range(50):
            status_response = await client.get(
                f"/api/v1/execution/translation/jobs/{job_id}",
                headers=get_auth_headers(),
            )
            assert status_response.status_code == 200
            status_json = status_response.json()
            if status_json["status"] in ("COMPLETED", "FAILED"):
                break
            await asyncio.sleep(0.1)

        # Verify job recorded failed status and details
        assert status_json["status"] == "FAILED"
        assert "protocol" in status_json["error_message"]


@pytest.mark.asyncio
async def test_security_gate_unauthenticated_requests():
    """Verify unauthenticated requests are blocked with authorized signature error."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Single job status with missing headers
        response = await client.get("/api/v1/execution/translation/jobs/some_id")
        assert response.status_code == 401
        assert "Missing gateway authentication headers" in response.json()["detail"]

        # List jobs with missing headers
        response2 = await client.get("/api/v1/execution/translation/jobs")
        assert response2.status_code == 401
        assert "Missing gateway authentication headers" in response2.json()["detail"]


@pytest.mark.asyncio
async def test_worker_context_and_session_cleanup():
    """Verify thread-local database session and security context variables are cleared after processing."""
    # Ensure starting in a clean state
    assert current_session.get() is None
    assert current_user_id.get() == "system"
    assert current_change_reason.get() == "system_operation"

    # Run processing with an invalid payload (will fail)
    await process_translation(
        study_id="failed_cleanup_test",
        payload={"invalid": "data"},
        session_factory=db_manager.get_session_maker(),
        user_id="special_user",
        change_reason="testing_cleanup",
    )

    # Verify context variables and sessions are fully reset even after a failure
    assert current_session.get() is None
    assert current_user_id.get() == "system"
    assert current_change_reason.get() == "system_operation"
