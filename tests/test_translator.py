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


@pytest.mark.asyncio
async def test_rules_compilation_and_artifact_generation():
    from apps.designer.rules import ExpressionNode, FieldReference
    from apps.execution.translator import compile_condition_to_xpath

    # 1. Test unit-level translation of expressions
    node_const = ExpressionNode(type="constant", value=42)
    assert compile_condition_to_xpath(node_const) == "42"

    node_str = ExpressionNode(type="constant", value="test_val")
    assert compile_condition_to_xpath(node_str) == "'test_val'"

    node_bool = ExpressionNode(type="constant", value=True)
    assert compile_condition_to_xpath(node_bool) == "true()"

    node_field = ExpressionNode(
        type="field_ref", field_ref=FieldReference(field_id="Sys BP")
    )
    assert compile_condition_to_xpath(node_field) == "/Sys_BP"

    node_comp = ExpressionNode(
        type="comparison",
        operator="<",
        operands=[node_field, node_const],
    )
    assert compile_condition_to_xpath(node_comp) == "(/Sys_BP < 42)"

    node_is_empty = ExpressionNode(
        type="function",
        operator="is_empty",
        operands=[node_field],
    )
    assert compile_condition_to_xpath(node_is_empty) == "empty(/Sys_BP)"

    node_not_empty = ExpressionNode(
        type="function",
        operator="is_not_empty",
        operands=[node_field],
    )
    assert compile_condition_to_xpath(node_not_empty) == "not(empty(/Sys_BP))"

    # 2. Test publishing event with rules and verify compiled artifacts
    study_payload = {
        "study_id": "rules_study_999",
        "payload": {
            "name": "Rules Demonstration Trial",
            "protocol": {
                "items": [
                    {
                        "id": "sys_bp",
                        "name": "Systolic Blood Pressure",
                        "type": "int",
                    },
                    {
                        "id": "heart_rate",
                        "name": "Heart Rate",
                        "type": "int",
                        "rules": [
                            {
                                "id": "rule_hr_skip",
                                "type": "skip_logic",
                                "action": "show",
                                "condition": {
                                    "type": "comparison",
                                    "operator": "==",
                                    "operands": [
                                        {
                                            "type": "field_ref",
                                            "field_ref": {"field_id": "sys_bp"},
                                        },
                                        {"type": "constant", "value": 120},
                                    ],
                                },
                            },
                            {
                                "id": "rule_hr_constraint",
                                "type": "constraint",
                                "query_message": "Heart rate is too high!",
                                "condition": {
                                    "type": "comparison",
                                    "operator": "<",
                                    "operands": [
                                        {
                                            "type": "field_ref",
                                            "field_ref": {"field_id": "heart_rate"},
                                        },
                                        {"type": "constant", "value": 200},
                                    ],
                                },
                            },
                        ],
                    },
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
                    TranslationJob.study_id == "rules_study_999"
                )
            )
            job = result.mappings().first()
            if job and job["status"] in ("COMPLETED", "FAILED"):
                break
        await asyncio.sleep(0.1)

    assert job is not None
    assert job["status"] == "COMPLETED"

    # Check OpenRosa XML output and verify compiled rule attributes on xf:bind
    openrosa_xml = job["openrosa_payload"]
    assert 'xmlns:jr="http://openrosa.org/javarosa"' in openrosa_xml
    assert "&lt;" in openrosa_xml

    openrosa_root = ET.fromstring(openrosa_xml)
    ns = {"xf": "http://www.w3.org/2002/xforms", "jr": "http://openrosa.org/javarosa"}
    head = openrosa_root.find("{http://www.w3.org/1999/xhtml}head")
    model = head.find("xf:model", ns)

    # Verify sys_bp bind has NO rules or relevant/constraint attributes
    sys_bp_bind = model.find("xf:bind[@nodeset='/sys_bp']", ns)
    assert sys_bp_bind is not None
    assert sys_bp_bind.get("type") == "int"
    assert sys_bp_bind.get("relevant") is None
    assert sys_bp_bind.get("constraint") is None
    assert sys_bp_bind.get("jr:constraintMsg") is None

    # Verify heart_rate bind has relevant, constraint, and jr:constraintMsg attributes
    hr_bind = model.find("xf:bind[@nodeset='/heart_rate']", ns)
    assert hr_bind is not None
    assert hr_bind.get("type") == "int"
    assert hr_bind.get("relevant") == "(/sys_bp = 120)"
    assert hr_bind.get("constraint") == "(/heart_rate < 200)"
    assert (
        hr_bind.get("{http://openrosa.org/javarosa}constraintMsg")
        == "Heart rate is too high!"
    )

    # Check ODM XML output and verify Alias extensions
    odm_xml = job["odm_payload"]
    assert "&lt;" in odm_xml
    odm_root = ET.fromstring(odm_xml)
    odm_ns = ""
    if "}" in odm_root.tag:
        odm_ns = odm_root.tag.split("}")[0] + "}"

    study = odm_root.find(f"{odm_ns}Study")
    mdv = study.find(f"{odm_ns}MetaDataVersion")
    item_defs = mdv.findall(f"{odm_ns}ItemDef")

    # Find sys_bp
    sys_bp_def = [i for i in item_defs if i.get("OID") == "sys_bp"][0]
    assert sys_bp_def.get("Name") == "Systolic Blood Pressure"
    assert sys_bp_def.get("DataType") == "int"
    # should have no children Alias elements
    assert len(sys_bp_def.findall(f"{odm_ns}Alias")) == 0

    # Find heart_rate
    hr_def = [i for i in item_defs if i.get("OID") == "heart_rate"][0]
    assert hr_def.get("Name") == "Heart Rate"
    assert hr_def.get("DataType") == "int"

    aliases = hr_def.findall(f"{odm_ns}Alias")
    assert len(aliases) == 2

    relevant_alias = [a for a in aliases if a.get("Context") == "relevant"][0]
    assert relevant_alias.get("Name") == "(/sys_bp = 120)"

    constraint_alias = [a for a in aliases if a.get("Context") == "constraint"][0]
    assert constraint_alias.get("Name") == "(/heart_rate < 200)"

    # Verify parity assertions are fully retained
    odm_ids = [item.get("OID") for item in item_defs]
    binds = model.findall("xf:bind", ns)
    openrosa_ids = [bind.get("nodeset").replace("/", "") for bind in binds]
    assert set(odm_ids) == set(openrosa_ids)


@pytest.mark.asyncio
async def test_multi_language_localization_and_hint_system():
    """Verify that multi-language StudyDefinitionDocuments with localized NarrativeContent
    and NarrativeContentItems are parsed, mapped, and generated as correct ODM Descriptions
    and OpenRosa hints."""
    study_payload = {
        "study_id": "test_localization_study_123",
        "payload": {
            "name": "Acme Localized Clinical Trial",
            "protocol": {
                "items": [
                    {"id": "sys_bp", "name": "Systolic Blood Pressure", "type": "int"},
                    {"id": "heart_rate", "name": "Heart Rate", "type": "int"},
                ]
            },
            "documentedBy": [
                {
                    "id": "doc_en",
                    "name": "English Document",
                    "language": {"code": "en", "decode": "English"},
                    "versions": [
                        {
                            "id": "ver_en",
                            "version": "1.0",
                            "contents": [
                                {
                                    "id": "nc_sys_bp_en",
                                    "name": "sys_bp",
                                    "sectionTitle": "Systolic Blood Pressure (mmHg)",
                                    "contentItemId": "nci_sys_bp_desc_en",
                                    "childIds": ["nci_sys_bp_hint_en"],
                                },
                                {
                                    "id": "nc_heart_rate_en",
                                    "name": "heart_rate",
                                    "sectionTitle": "Heart Rate (bpm)",
                                    "contentItemId": "nci_heart_rate_desc_en",
                                    "childIds": ["nci_heart_rate_hint_en"],
                                },
                            ],
                            "narrativeContentItems": [
                                {
                                    "id": "nci_sys_bp_desc_en",
                                    "name": "sys_bp_desc",
                                    "text": "The systolic blood pressure measured in mmHg.",
                                },
                                {
                                    "id": "nci_sys_bp_hint_en",
                                    "name": "sys_bp_hint",
                                    "text": "Ensure patient is sitting down.",
                                },
                                {
                                    "id": "nci_heart_rate_desc_en",
                                    "name": "heart_rate_desc",
                                    "text": "Heart rate in beats per minute.",
                                },
                                {
                                    "id": "nci_heart_rate_hint_en",
                                    "name": "heart_rate_hint",
                                    "text": "Measure for a full minute.",
                                },
                            ],
                        }
                    ],
                    "instanceType": "StudyDefinitionDocument",
                },
                {
                    "id": "doc_es",
                    "name": "Spanish Document",
                    "language": {"code": "es", "decode": "Spanish"},
                    "versions": [
                        {
                            "id": "ver_es",
                            "version": "1.0",
                            "contents": [
                                {
                                    "id": "nc_sys_bp_es",
                                    "name": "sys_bp",
                                    "sectionTitle": "Presión Arterial Sistólica (mmHg)",
                                    "contentItemId": "nci_sys_bp_desc_es",
                                    "childIds": ["nci_sys_bp_hint_es"],
                                },
                                {
                                    "id": "nc_heart_rate_es",
                                    "name": "heart_rate",
                                    "sectionTitle": "Frecuencia Cardíaca (lpm)",
                                    "contentItemId": "nci_heart_rate_desc_es",
                                    "childIds": ["nci_heart_rate_hint_es"],
                                },
                            ],
                            "narrativeContentItems": [
                                {
                                    "id": "nci_sys_bp_desc_es",
                                    "name": "sys_bp_desc_es",
                                    "text": "La presión arterial sistólica medida en mmHg.",
                                },
                                {
                                    "id": "nci_sys_bp_hint_es",
                                    "name": "sys_bp_hint_es",
                                    "text": "Asegúrese de que el paciente esté sentado.",
                                },
                                {
                                    "id": "nci_heart_rate_desc_es",
                                    "name": "heart_rate_desc_es",
                                    "text": "Frecuencia cardíaca en latidos por minuto.",
                                },
                                {
                                    "id": "nci_heart_rate_hint_es",
                                    "name": "heart_rate_hint_es",
                                    "text": "Mida durante un minuto completo.",
                                },
                            ],
                        }
                    ],
                    "instanceType": "StudyDefinitionDocument",
                },
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

    import asyncio

    job = None
    for _ in range(50):
        async with db_manager.get_session_maker()() as session:
            result = await session.execute(
                TranslationJob.__table__.select().where(
                    TranslationJob.study_id == "test_localization_study_123"
                )
            )
            job = result.mappings().first()
            if job and job["status"] in ("COMPLETED", "FAILED"):
                break
        await asyncio.sleep(0.1)

    assert job is not None
    if job["status"] != "COMPLETED":
        print("JOB ERROR:", job["error_message"])
    assert job["status"] == "COMPLETED"

    odm_xml = job["odm_payload"]
    openrosa_xml = job["openrosa_payload"]

    # 1. Verify CDISC ODM XML elements
    odm_root = ET.fromstring(odm_xml)
    odm_ns = ""
    if "}" in odm_root.tag:
        odm_ns = odm_root.tag.split("}")[0] + "}"

    study = odm_root.find(f"{odm_ns}Study")
    mdv = study.find(f"{odm_ns}MetaDataVersion")
    item_defs = mdv.findall(f"{odm_ns}ItemDef")

    # Find sys_bp in ODM
    sys_bp_def = [i for i in item_defs if i.get("OID") == "sys_bp"][0]
    description = sys_bp_def.find(f"{odm_ns}Description")
    assert description is not None
    translated_texts = description.findall(f"{odm_ns}TranslatedText")
    assert len(translated_texts) == 2

    en_text = [t for t in translated_texts if t.get("Language") == "en"][0]
    assert en_text.text == "The systolic blood pressure measured in mmHg."

    es_text = [t for t in translated_texts if t.get("Language") == "es"][0]
    assert es_text.text == "La presión arterial sistólica medida en mmHg."

    # 2. Verify OpenRosa XML elements
    openrosa_root = ET.fromstring(openrosa_xml)
    ns = {"xf": "http://www.w3.org/2002/xforms"}
    body = openrosa_root.find("{http://www.w3.org/1999/xhtml}body")
    inputs = body.findall("xf:input", ns)

    # Find sys_bp input in OpenRosa
    sys_bp_input = [inp for inp in inputs if inp.get("ref") == "/sys_bp"][0]
    labels = sys_bp_input.findall("xf:label", ns)
    assert len(labels) == 2
    en_label = [
        lbl
        for lbl in labels
        if lbl.get("{http://www.w3.org/XML/1998/namespace}lang") == "en"
    ][0]
    assert en_label.text == "Systolic Blood Pressure (mmHg)"
    es_label = [
        lbl
        for lbl in labels
        if lbl.get("{http://www.w3.org/XML/1998/namespace}lang") == "es"
    ][0]
    assert es_label.text == "Presión Arterial Sistólica (mmHg)"

    hints = sys_bp_input.findall("xf:hint", ns)
    assert len(hints) == 2
    en_hint = [
        h for h in hints if h.get("{http://www.w3.org/XML/1998/namespace}lang") == "en"
    ][0]
    assert en_hint.text == "Ensure patient is sitting down."
    es_hint = [
        h for h in hints if h.get("{http://www.w3.org/XML/1998/namespace}lang") == "es"
    ][0]
    assert es_hint.text == "Asegúrese de que el paciente esté sentado."
