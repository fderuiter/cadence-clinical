import hashlib
import hmac
import os
import time

import defusedxml.ElementTree as ET
import httpx
import pytest
import pytest_asyncio

from apps.execution.database.core import db_manager
from apps.execution.database.models import AuditLog, Base, TranslationJob
from apps.execution.main import app

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")


def get_auth_headers(user_id="test_user", roles="admin"):
    timestamp = str(time.time())
    message = f"{user_id}:{roles}:{timestamp}"
    signature = hmac.new(
        GATEWAY_SECRET.encode(), message.encode(), hashlib.sha256
    ).hexdigest()
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
    }


@pytest_asyncio.fixture(autouse=True)
async def setup_test_db():
    import os

    db_manager.init_db(
        os.getenv(
            "TEST_DATABASE_URL",
            "sqlite+aiosqlite:///:memory:",
        )
    )
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_study_published_event_triggers_translation():
    study_payload = {
        "study_id": "test_study_123",
        "payload": {
            "name": "Acme Clinical Trial",
            "protocol": {
                "items": [
                    {"id": "sys_bp", "name": "Systolic Blood Pressure", "type": "int"},
                    {"name": "Heart Rate", "type": "int"},
                ]
            },
        },
    }

    # Do not use `with TestClient(app)` to avoid triggering the lifespan which overwrites the test db
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        response = await client.post(
            "/events/study-published", json=study_payload, headers=get_auth_headers()
        )
    assert response.status_code == 202
    assert response.json()["status"] == "accepted"

    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            TranslationJob.__table__.select().where(
                TranslationJob.study_id == "test_study_123"
            )
        )
        job = result.mappings().first()

        assert job is not None
        if job["status"] != "COMPLETED":
            print("ERROR MESSAGE:", job["error_message"])
        assert job["status"] == "COMPLETED"
        assert job["odm_payload"] is not None
        assert job["openrosa_payload"] is not None

        odm_xml = job["odm_payload"]
        odm_root = ET.fromstring(odm_xml)
        assert "ODM" in odm_root.tag

        openrosa_xml = job["openrosa_payload"]
        openrosa_root = ET.fromstring(openrosa_xml)
        assert "html" in openrosa_root.tag

        # Determine the namespace for ODM dynamically if present
        odm_ns = ""
        if "}" in odm_root.tag:
            odm_ns = odm_root.tag.split("}")[0] + "}"

        study = odm_root.find(f"{odm_ns}Study")
        mdv = study.find(f"{odm_ns}MetaDataVersion")
        item_defs = mdv.findall(f"{odm_ns}ItemDef")
        odm_ids = [item.get("OID") for item in item_defs]

        ns = {"xf": "http://www.w3.org/2002/xforms"}
        head = openrosa_root.find("{http://www.w3.org/1999/xhtml}head")
        model = head.find("xf:model", ns)
        binds = model.findall("xf:bind", ns)

        openrosa_ids = [bind.get("nodeset").replace("/", "") for bind in binds]

        assert set(odm_ids) == set(openrosa_ids)
        assert "sys_bp" in odm_ids
        assert len(odm_ids) == 2

        audit_res = await session.execute(
            AuditLog.__table__.select().where(AuditLog.table_name == "translation_jobs")
        )
        logs = list(audit_res.mappings().all())
        assert len(logs) >= 1


@pytest.mark.asyncio
async def test_translation_validation_failure():
    study_payload = {
        "study_id": "test_study_invalid",
        "payload": {"name": "Invalid Trial"},
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        response = await client.post(
            "/events/study-published", json=study_payload, headers=get_auth_headers()
        )
    assert response.status_code == 202

    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            TranslationJob.__table__.select().where(
                TranslationJob.study_id == "test_study_invalid"
            )
        )
        job = result.mappings().first()
        assert job is not None
        assert job["status"] == "FAILED"
        assert "protocol" in job["error_message"]


@pytest.mark.asyncio
async def test_usdm_validation_failure_sync():
    """
    Verify that an invalid USDM study payload (e.g. missing required 'name')
    is rejected synchronously with an HTTP 400 Bad Request.
    """
    study_payload = {
        "study_id": "test_study_invalid_sync",
        "payload": {
            "instanceType": "Study",
            # Missing required field 'name'
        },
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        response = await client.post(
            "/events/study-published", json=study_payload, headers=get_auth_headers()
        )
    assert response.status_code == 400
    assert "Validation Failed" in response.json()["detail"]


@pytest.mark.asyncio
async def test_optimistic_sequencing_conflict():
    """
    Verify that sending a payload with an equal or lower version than
    the existing record returns an HTTP 409 Conflict.
    """
    study_id = "test_study_sequence_123"

    # Step 1: Send valid study with version 2 (should succeed)
    payload_v2 = {
        "study_id": study_id,
        "payload": {
            "name": "Acme Clinical Trial Seq",
            "instanceType": "Study",
            "version_index": 2,
            "protocol": {
                "items": [{"id": "q1", "name": "Question 1", "type": "string"}]
            },
        },
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        response = await client.post(
            "/events/study-published", json=payload_v2, headers=get_auth_headers()
        )
        assert response.status_code == 202

        # Step 2: Send study with version 2 (equal version -> should fail with 409)
        response_equal = await client.post(
            "/events/study-published", json=payload_v2, headers=get_auth_headers()
        )
        assert response_equal.status_code == 409
        assert "Conflict" in response_equal.json()["detail"]

        # Step 3: Send study with version 1 (lower version -> should fail with 409)
        payload_v1 = {
            "study_id": study_id,
            "payload": {
                "name": "Acme Clinical Trial Seq",
                "instanceType": "Study",
                "version_index": 1,
                "protocol": {
                    "items": [{"id": "q1", "name": "Question 1", "type": "string"}]
                },
            },
        }
        response_lower = await client.post(
            "/events/study-published", json=payload_v1, headers=get_auth_headers()
        )
        assert response_lower.status_code == 409
        assert "Conflict" in response_lower.json()["detail"]


@pytest.mark.asyncio
async def test_successful_sequencing_increment():
    """
    Verify that sending study versions in strictly increasing order
    succeeds (202), saves the correct version alongside translation output,
    and populates audit logs with the correct version index.
    """
    study_id = "test_study_seq_increment"

    # Step 1: Send valid study with version 5
    payload_v5 = {
        "study_id": study_id,
        "payload": {
            "name": "Acme Trial Increment",
            "instanceType": "Study",
            "version_index": 5,
            "protocol": {
                "items": [{"id": "q1", "name": "Question 1", "type": "string"}]
            },
        },
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        response = await client.post(
            "/events/study-published", json=payload_v5, headers=get_auth_headers()
        )
        assert response.status_code == 202

        # Step 2: Send study with version 6
        payload_v6 = {
            "study_id": study_id,
            "payload": {
                "name": "Acme Trial Increment",
                "instanceType": "Study",
                "version_index": 6,
                "protocol": {
                    "items": [{"id": "q1", "name": "Question 1", "type": "string"}]
                },
            },
        }
        response_next = await client.post(
            "/events/study-published", json=payload_v6, headers=get_auth_headers()
        )
        assert response_next.status_code == 202

    # Step 3: Check database models and audit log version indices
    async with db_manager.get_session_maker()() as session:
        # Check TranslationJobs
        result = await session.execute(
            TranslationJob.__table__.select()
            .where(TranslationJob.study_id == study_id)
            .order_by(TranslationJob.version)
        )
        jobs = list(result.mappings().all())
        assert len(jobs) == 2
        assert jobs[0]["version"] == 5
        assert jobs[1]["version"] == 6

        # Check AuditLogs for translation_jobs table
        audit_res = await session.execute(
            AuditLog.__table__.select()
            .where(AuditLog.table_name == "translation_jobs")
            .order_by(AuditLog.version_index)
        )
        logs = list(audit_res.mappings().all())
        # Filter for the specific records of our study
        filtered_logs = [
            log
            for log in logs
            if log["new_values"] and log["new_values"].get("study_id") == study_id
        ]

        assert len(filtered_logs) >= 2
        # Verify stored and indexed version index in audit trails
        assert filtered_logs[0]["version_index"] == 5
        assert filtered_logs[1]["version_index"] == 6
