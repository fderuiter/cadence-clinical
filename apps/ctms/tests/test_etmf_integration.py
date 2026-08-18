import os
import time

import pytest_asyncio
from fastapi.testclient import TestClient
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
    change_reason: str = "eTMF synchronization",
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
    return headers


def test_etmf_artifact_synchronization():
    """Validate automated artifact pushing to eTMF mapped to DIA TMF Reference Model.

    @req:PRD-CTMS-010, PRD-CTMS-004
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="CRA", change_reason="Archiving MVR report")
    study_id = "STUDY-TMF-707"
    site_id = "SITE-501"

    sync_payload = {
        "study_id": study_id,
        "site_id": site_id,
        "artifact_type": "MVR_REPORT",
        "source_record_id": "MVR-VISIT-9901",
        "title": "Interim Monitoring Visit Report - Visit 3",
        "content_text": "Executive Summary: Site compliance confirmed. 12 CRFs verified with 100% SDV.",
        "dia_zone": "05",
        "dia_section": "05.02",
        "dia_artifact": "Monitoring Visit Report",
    }

    res_sync = client.post("/api/v1/ctms/etmf-sync", json=sync_payload, headers=headers)
    assert res_sync.status_code == 201
    sync_data = res_sync.json()
    assert sync_data["sync_status"] == "SYNCED"
    assert sync_data["dia_zone"] == "05"
    assert sync_data["dia_section"] == "05.02"
    assert sync_data["etmf_document_id"] is not None

    # List eTMF sync records
    res_list = client.get(
        f"/api/v1/ctms/etmf-sync?study_id={study_id}", headers=headers
    )
    assert res_list.status_code == 200
    assert len(res_list.json()) == 1
    assert res_list.json()[0]["source_record_id"] == "MVR-VISIT-9901"
