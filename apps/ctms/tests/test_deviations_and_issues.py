import os
import time

import pytest_asyncio
from fastapi.testclient import TestClient
from jose import jwt
from sqlalchemy import event

from apps.ctms.adapters.database import db_manager
from apps.ctms.adapters.models import Base
from apps.ctms.main import app
from packages.testing.security import generate_signature

GATEWAY_SECRET = os.getenv(
    "GATEWAY_SECRET", default="internal-gateway-secret-12345"
).encode("utf-8")


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """Setup in-memory CTMS database with attached audit schema."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)

    @event.listens_for(db_manager.engine.sync_engine, "connect")
    def attach_audit_schema(dbapi_connection, connection_record):
        cursor = dbapi_connection.cursor()
        cursor.execute("ATTACH DATABASE ':memory:' AS audit_schema;")
        cursor.close()

    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_auth_headers(
    roles: str = "CRA",
    change_reason: str = "Deviation Investigation",
    action: str | None = None,
    user_id: str = "cra_monitor_01",
) -> dict:
    timestamp = str(time.time())
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
    if action:
        sig_payload = {
            "sub": user_id,
            "username": user_id,
            "action": action,
            "roles": [roles],
            "iat": time.time(),
            "exp": time.time() + 300.0,
        }
        headers["X-Sig-Token"] = jwt.encode(
            sig_payload, "internal-gateway-secret-12345", algorithm="HS256"
        )
    return headers


def test_protocol_deviation_lifecycle_and_capa_escalation():
    """Validate protocol deviation logging, 5-Why RCA, CAPA escalation, and resolution.

    @req:PRD-CTMS-006, PRD-CTMS-004
    """
    client = TestClient(app)
    headers = get_auth_headers(
        roles="CRA", change_reason="Eligibility deviation identified"
    )
    study_id = "STUDY-DEV-303"
    site_id = "SITE-101"

    # Step 1: Log a Major Protocol Deviation
    dev_payload = {
        "study_id": study_id,
        "site_id": site_id,
        "subject_id": "SUBJ-9001",
        "deviation_category": "ELIGIBILITY",
        "severity": "MAJOR",
        "title": "Subject enrolled with exclusionary ALT lab values",
        "description": "Baseline ALT was 3.5x ULN exceeding the protocol inclusion threshold of 2.0x ULN.",
        "date_occurred": "2026-07-10",
    }
    res_log = client.post("/api/v1/ctms/deviations", json=dev_payload, headers=headers)
    assert res_log.status_code == 201
    dev_data = res_log.json()
    assert dev_data["status"] == "IDENTIFIED"
    assert dev_data["severity"] == "MAJOR"
    dev_id = dev_data["id"]

    # Step 2: Submit 5-Why Root Cause Analysis (RCA) & CAPA Plans
    rca_payload = {
        "root_cause_5whys": [
            "Why 1: Subject enrolled? CRC misread lab normal ranges.",
            "Why 2: Why misread? Local lab report format differed from central lab template.",
            "Why 3: Why differed? Site used emergency local lab during weekend.",
            "Why 4: Why no cross-check? Site coordinator had not completed revised lab SOP training.",
            "Why 5: Root cause: Missing SOP retraining sign-off on emergency local lab verification.",
        ],
        "root_cause_summary": "Missing SOP training on emergency local lab reference range conversions.",
        "corrective_action_plan": "Subject discontinued from dosing per Medical Monitor review.",
        "preventive_action_plan": "Mandatory retraining of all site CRCs on local lab conversion checklist.",
    }
    res_rca = client.post(
        f"/api/v1/ctms/deviations/{dev_id}/rca", json=rca_payload, headers=headers
    )
    assert res_rca.status_code == 200
    assert res_rca.json()["status"] == "UNDER_REVIEW"
    assert len(res_rca.json()["root_cause_5whys"]) == 5

    # Step 3: Escalate to Quality CAPA
    res_capa = client.post(
        f"/api/v1/ctms/deviations/{dev_id}/escalate-capa", headers=headers
    )
    assert res_capa.status_code == 200
    assert res_capa.json()["status"] == "CAPA_ESCALATED"
    assert res_capa.json()["quality_capa_id"] is not None

    # Step 4: Create and Complete Remediation Action Item
    ai_payload = {
        "deviation_id": dev_id,
        "site_id": site_id,
        "description": "Conduct CRC retraining on local lab reference range checklist",
        "assignee_user_id": "crc_lead_05",
        "assignee_role": "Site Coordinator",
        "due_date": "2026-07-20",
    }
    res_ai = client.post(
        f"/api/v1/ctms/deviations/{dev_id}/action-items",
        json=ai_payload,
        headers=headers,
    )
    assert res_ai.status_code == 201
    ai_id = res_ai.json()["id"]

    res_ai_done = client.post(
        f"/api/v1/ctms/deviations/action-items/{ai_id}/complete",
        json={
            "resolution_notes": "All site coordinators completed training and signed attendance log."
        },
        headers=headers,
    )
    assert res_ai_done.status_code == 200
    assert res_ai_done.json()["status"] == "COMPLETED"

    # Step 5: Resolve Deviation Closeout
    res_resolve = client.post(
        f"/api/v1/ctms/deviations/{dev_id}/resolve", headers=headers
    )
    assert res_resolve.status_code == 200
    assert res_resolve.json()["status"] == "RESOLVED"
    assert res_resolve.json()["resolved_by"] == "cra_monitor_01"
