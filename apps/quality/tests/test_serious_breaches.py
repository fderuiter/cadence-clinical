"""Tests for Serious Breach Escalations, Regulatory 7-Day Notification Clock, and Health Authority Dispatches."""

from datetime import datetime, timedelta

import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import event

from apps.quality.adapters.database import db_manager
from apps.quality.adapters.models import Base
from apps.quality.main import app
from packages.security.rbac_helpers import build_gateway_headers


@pytest_asyncio.fixture(autouse=True)
async def setup_quality_db():
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)

    @event.listens_for(db_manager.engine.sync_engine, "connect")
    def attach_audit_schema(dbapi_connection, connection_record):
        cursor = dbapi_connection.cursor()
        cursor.execute("ATTACH DATABASE ':memory:' AS audit_schema;")
        cursor.execute("""
            CREATE TABLE IF NOT EXISTS audit_schema.audit_logs (
                id VARCHAR(36) PRIMARY KEY,
                table_name VARCHAR(255) NOT NULL,
                record_id VARCHAR(255) NOT NULL,
                action VARCHAR(50) NOT NULL,
                user_id VARCHAR(255) NOT NULL,
                ip_address VARCHAR(45),
                timestamp TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                old_values JSON,
                new_values JSON,
                version_index INTEGER DEFAULT 1,
                change_reason TEXT,
                cryptographic_seal VARCHAR(64)
            );
        """)
        cursor.close()

    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    if db_manager.engine is not None:
        async with db_manager.engine.begin() as conn:
            await conn.run_sync(Base.metadata.drop_all)
        await db_manager.close()


def get_auth_headers(
    roles: str = "quality_oversight,quality_manager",
    change_reason: str = "Serious breach escalation protocol",
) -> dict[str, str]:
    return build_gateway_headers(
        user_id="lead.qa@cadence.io",
        roles=roles,
        change_reason=change_reason,
    )


def test_serious_breach_reporting_and_initial_clock():
    """Validate Serious Breach reporting and automatic 7-day (168-hour) clock initialization.

    @req:PRD-QLT-007
    """
    client = TestClient(app)
    headers = get_auth_headers(change_reason="Initial breach report")
    now = datetime.now()
    event_dt = (now - timedelta(days=2)).isoformat()
    discovery_dt = now.isoformat()

    res = client.post(
        "/api/v1/quality/serious-breaches",
        headers=headers,
        json={
            "study_id": "STUDY-BREACH-001",
            "site_id": "SITE-303",
            "title": "Systemic failure to obtain re-consent on revised safety IB",
            "summary": "15 active subjects received investigational drug without signing approved informed consent version 3 detailing new cardiac safety warnings.",
            "event_date": event_dt,
            "discovery_date": discovery_dt,
            "affected_authorities": ["MHRA", "EMA"],
        },
    )
    assert res.status_code == 201, res.text
    breach = res.json()
    breach_id = breach["id"]
    assert breach["status"] == "UNDER_EVALUATION"
    assert breach["affected_authorities"] == ["MHRA", "EMA"]

    # Check regulatory clock
    clock_res = client.get(
        f"/api/v1/quality/serious-breaches/{breach_id}/clock", headers=headers
    )
    assert clock_res.status_code == 200
    clock = clock_res.json()
    assert clock["status"] == "UNDER_EVALUATION"
    assert clock["regulatory_clock_hours_remaining"] > 160.0
    assert clock["is_overdue"] is False


def test_serious_breach_confirmation_and_status_progression():
    """Validate Serious Breach confirmation and regulatory notification status lifecycle.

    @req:PRD-QLT-007
    """
    client = TestClient(app)
    headers = get_auth_headers(change_reason="Breach confirmation")
    now = datetime.now()

    # 1. Report breach
    create_res = client.post(
        "/api/v1/quality/serious-breaches",
        headers=headers,
        json={
            "study_id": "STUDY-BREACH-002",
            "title": "Widespread fabrication of laboratory centrifuge logs",
            "summary": "Audit revealed centrifuge maintenance records were falsified across 2 study centers.",
            "event_date": (now - timedelta(days=5)).isoformat(),
            "discovery_date": now.isoformat(),
            "affected_authorities": ["FDA"],
        },
    )
    assert create_res.status_code == 201, create_res.text
    breach_id = create_res.json()["id"]

    # 2. Confirm breach
    confirm_res = client.post(
        f"/api/v1/quality/serious-breaches/{breach_id}/confirm",
        headers=headers,
        json={
            "affected_authorities": ["FDA", "MHRA", "EMA"],
        },
    )
    assert confirm_res.status_code == 200
    confirmed = confirm_res.json()
    assert confirmed["status"] == "CONFIRMED_BREACH"
    assert confirmed["confirmation_date"] is not None
    assert "FDA" in confirmed["affected_authorities"]

    # 3. Update status to AUTHORITY_NOTIFIED
    notified_res = client.put(
        f"/api/v1/quality/serious-breaches/{breach_id}/status",
        headers=headers,
        json={
            "status": "AUTHORITY_NOTIFIED",
        },
    )
    assert notified_res.status_code == 200
    assert notified_res.json()["status"] == "AUTHORITY_NOTIFIED"

    # 4. List breaches
    list_res = client.get(
        "/api/v1/quality/serious-breaches?study_id=STUDY-BREACH-002", headers=headers
    )
    assert list_res.status_code == 200
    assert len(list_res.json()) >= 1
