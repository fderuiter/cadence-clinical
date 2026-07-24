import hashlib
import hmac
import json
import os
import time

import defusedxml.ElementTree as ET
import httpx
import pytest
import pytest_asyncio

from apps.execution.database.context import (
    audit_context,
    current_change_reason,
    current_user_id,
)
from apps.execution.database.core import db_manager
from apps.execution.database.models import AuditLog, Base, TranslationJob
from apps.execution.main import app

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")


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
    import os

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
    assert response.status_code == 200
    assert response.json()["status"] == "accepted"

    import asyncio

    job = None
    for _ in range(50):
        async with db_manager.get_session_maker()() as session:
            result = await session.execute(
                TranslationJob.__table__.select().where(
                    TranslationJob.study_id == "test_study_123"
                )
            )
            job = result.mappings().first()
            if job and job["status"] in ("COMPLETED", "FAILED"):
                break
        await asyncio.sleep(0.1)

    assert job is not None
    if job["status"] != "COMPLETED":
        print("ERROR MESSAGE:", job["error_message"])
    assert job["status"] == "COMPLETED"
    assert job["odm_payload"] is not None
    assert job["openrosa_payload"] is not None

    async with db_manager.get_session_maker()() as session:
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
    assert response.status_code == 200

    import asyncio

    job = None
    for _ in range(50):
        async with db_manager.get_session_maker()() as session:
            result = await session.execute(
                TranslationJob.__table__.select().where(
                    TranslationJob.study_id == "test_study_invalid"
                )
            )
            job = result.mappings().first()
            if job and job["status"] in ("COMPLETED", "FAILED"):
                break
        await asyncio.sleep(0.1)

    assert job is not None
    assert job["status"] == "FAILED"
    assert "protocol" in job["error_message"]


@pytest.mark.asyncio
async def test_audit_safe_context_binds_and_cleans_up():
    # 1. Verify defaults before
    assert current_user_id.get() == "system"
    assert current_change_reason.get() == "system_operation"

    # 2. Bind custom user & reason
    with audit_context(user_id="user_abc", change_reason="publishing study"):
        assert current_user_id.get() == "user_abc"
        assert current_change_reason.get() == "publishing study"

    # 3. Verify they are restored and cleaned up
    assert current_user_id.get() == "system"
    assert current_change_reason.get() == "system_operation"


@pytest.mark.asyncio
async def test_audit_safe_context_cleans_up_on_error():
    assert current_user_id.get() == "system"
    assert current_change_reason.get() == "system_operation"

    with pytest.raises(ValueError):
        with audit_context(user_id="user_err", change_reason="testing errors"):
            assert current_user_id.get() == "user_err"
            assert current_change_reason.get() == "testing errors"
            raise ValueError("Intentional error")

    # Verify context was restored
    assert current_user_id.get() == "system"
    assert current_change_reason.get() == "system_operation"


@pytest.mark.asyncio
async def test_background_translation_records_user_audit():
    study_payload = {
        "study_id": "test_background_audit_study",
        "payload": {
            "name": "Audit Safe Background Study",
            "protocol": {
                "items": [
                    {"id": "bp", "name": "Blood Pressure", "type": "int"},
                ]
            },
        },
    }

    # Post with X-User-Id header as test_user_audit
    headers = get_auth_headers(
        user_id="test_user_audit", roles="researcher", change_reason="translation test"
    )

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        response = await client.post(
            "/events/study-published", json=study_payload, headers=headers
        )
    assert response.status_code == 200

    # Retrieve translation job and its audit logs
    import asyncio

    job = None
    for _ in range(50):
        async with db_manager.get_session_maker()() as session:
            result = await session.execute(
                TranslationJob.__table__.select().where(
                    TranslationJob.study_id == "test_background_audit_study"
                )
            )
            job = result.mappings().first()
            if job and job["status"] in ("COMPLETED", "FAILED"):
                break
        await asyncio.sleep(0.1)

    async with db_manager.get_session_maker()() as session:
        assert job is not None
        assert job["status"] == "COMPLETED"

        # Check audit log to verify the initiating user is captured
        audit_res = await session.execute(
            AuditLog.__table__.select().where(AuditLog.table_name == "translation_jobs")
        )
        logs = list(audit_res.mappings().all())
        assert len(logs) >= 1

        # At least one log should have the user_id matching test_user_audit and change_reason matching the passed header
        audit_records = [log for log in logs if log["record_id"] == job["id"]]
        assert len(audit_records) >= 1
        assert any(
            rec["user_id"] == "test_user_audit"
            and rec["change_reason"] == "translation test"
            for rec in audit_records
        )


@pytest.mark.asyncio
async def test_identifier_sanitization_during_translation():
    from apps.execution.translator import sanitize_identifier

    # Test unit behaviors
    assert sanitize_identifier("sys_bp") == "sys_bp"
    assert sanitize_identifier("heart rate") == "heart_rate"
    assert sanitize_identifier("1_systolic") == "item_1_systolic"
    assert sanitize_identifier("item-A") == "item_2dA"
    assert sanitize_identifier("item_A") == "item_A"
    assert sanitize_identifier("") != ""
    assert sanitize_identifier(None) != ""

    study_payload = {
        "study_id": "test_sanitization_study_123",
        "payload": {
            "name": "Sanitization Clinical Trial",
            "protocol": {
                "items": [
                    {"id": "heart rate", "name": "Heart Rate", "type": "int"},
                    {
                        "id": "1_systolic",
                        "name": "Systolic Blood Pressure",
                        "type": "int",
                    },
                    {"id": "item-A", "name": "Item A", "type": "string"},
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

    import asyncio

    job = None
    for _ in range(50):
        async with db_manager.get_session_maker()() as session:
            result = await session.execute(
                TranslationJob.__table__.select().where(
                    TranslationJob.study_id == "test_sanitization_study_123"
                )
            )
            job = result.mappings().first()
            if job and job["status"] in ("COMPLETED", "FAILED"):
                break
        await asyncio.sleep(0.1)

    assert job is not None
    if job["status"] != "COMPLETED":
        print("ERROR MESSAGE:", job["error_message"])
    assert job["status"] == "COMPLETED"
    assert job["odm_payload"] is not None
    assert job["openrosa_payload"] is not None

    # Parse and verify the XMLs
    import defusedxml.ElementTree as ET

    # 1. CDISC ODM
    odm_xml = job["odm_payload"]
    odm_root = ET.fromstring(odm_xml)
    odm_ns = ""
    if "}" in odm_root.tag:
        odm_ns = odm_root.tag.split("}")[0] + "}"

    study = odm_root.find(f"{odm_ns}Study")
    mdv = study.find(f"{odm_ns}MetaDataVersion")
    item_defs = mdv.findall(f"{odm_ns}ItemDef")
    odm_ids = [item.get("OID") for item in item_defs]

    # 2. OpenRosa XML
    openrosa_xml = job["openrosa_payload"]
    openrosa_root = ET.fromstring(openrosa_xml)
    ns = {"xf": "http://www.w3.org/2002/xforms"}
    head = openrosa_root.find("{http://www.w3.org/1999/xhtml}head")
    model = head.find("xf:model", ns)

    # Binds
    binds = model.findall("xf:bind", ns)
    openrosa_bind_ids = [bind.get("nodeset").replace("/", "") for bind in binds]

    # Inputs
    body = openrosa_root.find("{http://www.w3.org/1999/xhtml}body")
    inputs = body.findall("xf:input", ns)
    openrosa_input_refs = [inp.get("ref").replace("/", "") for inp in inputs]

    # Data elements in the instance
    instance = model.find("xf:instance", ns)
    data_elem = list(instance)[0]
    data_children_tags = [child.tag.split("}")[-1] for child in list(data_elem)]

    # Asserting that everything shares the exact same sanitized identifier string
    assert set(odm_ids) == {"heart_rate", "item_1_systolic", "item_2dA"}
    assert set(openrosa_bind_ids) == {"heart_rate", "item_1_systolic", "item_2dA"}
    assert set(openrosa_input_refs) == {"heart_rate", "item_1_systolic", "item_2dA"}
    assert set(data_children_tags) == {"heart_rate", "item_1_systolic", "item_2dA"}


@pytest.mark.asyncio
async def test_study_published_invalid_signature_rejection():
    """Verify that execution service rejects requests with a 403 Forbidden if the signature does not match the computed hash of the payload."""
    study_payload = {
        "study_id": "test_study_invalid_sig",
        "payload": {
            "name": "Acme Clinical Trial",
            "protocol": {
                "items": [
                    {"id": "sys_bp", "name": "Systolic Blood Pressure", "type": "int"},
                ]
            },
        },
    }

    headers = get_auth_headers(
        user_id="test_user", roles="admin", change_reason="system_operation"
    )
    # Tamper with the signature
    headers["X-Gateway-Signature"] = "a" * 64

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        response = await client.post(
            "/events/study-published", json=study_payload, headers=headers
        )
    assert response.status_code == 403
    assert response.json()["detail"] == "Invalid gateway signature"


@pytest.mark.asyncio
async def test_study_published_expired_timestamp_rejection():
    """Verify that execution service rejects requests where the timestamp is older than 300 seconds."""
    study_payload = {
        "study_id": "test_study_expired",
        "payload": {
            "name": "Acme Clinical Trial",
            "protocol": {
                "items": [
                    {"id": "sys_bp", "name": "Systolic Blood Pressure", "type": "int"},
                ]
            },
        },
    }

    # Generate headers with an expired timestamp
    timestamp = str(time.time() - 310)
    change_reason = "system_operation"
    payload = {
        "change_reason": change_reason,
        "roles": "admin",
        "timestamp": timestamp,
        "user_id": "test_user",
    }
    serialized = json.dumps(payload, sort_keys=True, separators=(",", ":"))
    signature = hmac.new(
        GATEWAY_SECRET.encode(), serialized.encode(), hashlib.sha256
    ).hexdigest()

    headers = {
        "X-User-Id": "test_user",
        "X-User-Roles": "admin",
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        response = await client.post(
            "/events/study-published", json=study_payload, headers=headers
        )
    assert response.status_code == 403
    assert response.json()["detail"] == "Gateway signature expired"
