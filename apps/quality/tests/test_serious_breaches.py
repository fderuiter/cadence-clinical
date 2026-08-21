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
    # Verify reporting deadline remains anchored to discovery date
    assert confirmed["reporting_deadline"] == create_res.json()["reporting_deadline"]

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
    assert (
        notified_res.json()["reporting_deadline"]
        == create_res.json()["reporting_deadline"]
    )

    # 4. List breaches
    list_res = client.get(
        "/api/v1/quality/serious-breaches?study_id=STUDY-BREACH-002", headers=headers
    )
    assert list_res.status_code == 200
    assert len(list_res.json()) >= 1


def test_statutory_discovery_clock_preservation_on_confirmation():
    """Validate that confirming a breach leaves the initial discovery-anchored deadline unchanged.

    @req:PRD-QLT-007
    """
    client = TestClient(app)
    headers = get_auth_headers(change_reason="Preserve statutory clock")
    now = datetime.now()
    discovery_dt = now - timedelta(days=3)

    # Report breach discovered 3 days ago
    create_res = client.post(
        "/api/v1/quality/serious-breaches",
        headers=headers,
        json={
            "study_id": "STUDY-BREACH-CLOCK",
            "title": "Unauthorized protocol deviation in dosing schedule",
            "summary": "Dosing frequency miscalculated across 5 subjects.",
            "event_date": (now - timedelta(days=4)).isoformat(),
            "discovery_date": discovery_dt.isoformat(),
            "affected_authorities": ["MHRA"],
        },
    )
    assert create_res.status_code == 201
    initial_breach = create_res.json()
    initial_deadline = initial_breach["reporting_deadline"]
    initial_hours = initial_breach["regulatory_clock_hours_remaining"]

    # Initial deadline should be ~96 hours remaining (7 days from discovery = 4 days from now)
    assert 90.0 < initial_hours < 100.0

    # Confirm breach (simulating state transition)
    confirm_res = client.post(
        f"/api/v1/quality/serious-breaches/{initial_breach['id']}/confirm",
        headers=headers,
        json={"affected_authorities": ["MHRA", "EMA"]},
    )
    assert confirm_res.status_code == 200
    confirmed = confirm_res.json()

    # Deadline MUST NOT be reset to now + 7 days (~168 hours)
    assert confirmed["reporting_deadline"] == initial_deadline
    assert confirmed["regulatory_clock_hours_remaining"] < 105.0


def test_regulatory_clock_approaching_and_overdue_indicators():
    """Validate 48-hour warning window and overdue indicators derived from discovery anchor.

    @req:PRD-QLT-007
    """
    client = TestClient(app)
    headers = get_auth_headers(change_reason="Clock status evaluation")
    now = datetime.now()

    # Scenario A: Approaching deadline (discovery 5.5 days ago -> 1.5 days = 36h remaining)
    disc_approaching = now - timedelta(days=5, hours=12)
    res_app = client.post(
        "/api/v1/quality/serious-breaches",
        headers=headers,
        json={
            "study_id": "STUDY-BREACH-WARN",
            "title": "Temperature excursion in drug storage",
            "summary": "Storage temp exceeded 25C for 12 hours.",
            "event_date": disc_approaching.isoformat(),
            "discovery_date": disc_approaching.isoformat(),
            "affected_authorities": ["FDA"],
        },
    )
    assert res_app.status_code == 201
    id_app = res_app.json()["id"]

    # Confirm breach
    client.post(
        f"/api/v1/quality/serious-breaches/{id_app}/confirm",
        headers=headers,
        json={"affected_authorities": ["FDA"]},
    )

    clock_app = client.get(
        f"/api/v1/quality/serious-breaches/{id_app}/clock", headers=headers
    ).json()
    assert clock_app["is_approaching_deadline"] is True
    assert clock_app["is_overdue"] is False
    assert 30.0 < clock_app["regulatory_clock_hours_remaining"] < 42.0

    # Scenario B: Overdue deadline (discovery 8 days ago -> -24h remaining)
    disc_overdue = now - timedelta(days=8)
    res_overdue = client.post(
        "/api/v1/quality/serious-breaches",
        headers=headers,
        json={
            "study_id": "STUDY-BREACH-OVERDUE",
            "title": "Late discovery of unblinded safety data leak",
            "summary": "Unblinded data sent to site coordinator.",
            "event_date": disc_overdue.isoformat(),
            "discovery_date": disc_overdue.isoformat(),
            "affected_authorities": ["MHRA"],
        },
    )
    assert res_overdue.status_code == 201
    id_overdue = res_overdue.json()["id"]

    # Confirm breach
    client.post(
        f"/api/v1/quality/serious-breaches/{id_overdue}/confirm",
        headers=headers,
        json={"affected_authorities": ["MHRA"]},
    )

    clock_overdue = client.get(
        f"/api/v1/quality/serious-breaches/{id_overdue}/clock", headers=headers
    ).json()
    assert clock_overdue["is_approaching_deadline"] is False
    assert clock_overdue["is_overdue"] is True
    assert clock_overdue["regulatory_clock_hours_remaining"] < 0
