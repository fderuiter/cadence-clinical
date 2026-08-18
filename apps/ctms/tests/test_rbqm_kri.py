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
    roles: str = "Data Manager",
    change_reason: str = "RBQM Centralized Monitoring",
    user_id: str = "central_monitor_01",
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


def test_rbqm_kri_breach_detection_and_adaptive_risk_scoring():
    """Validate Key Risk Indicator (KRI) calculation, QTL breach detection, and Site Risk Scoring.

    @req:PRD-CTMS-007, PRD-CTMS-004
    """
    client = TestClient(app)
    headers = get_auth_headers(
        roles="Data Manager", change_reason="Centralized Monitoring run"
    )
    study_id = "STUDY-RBQM-404"
    site_id = "SITE-201"

    # Step 1: Record normal KRI (Query Velocity)
    kri_norm = {
        "study_id": study_id,
        "site_id": site_id,
        "metric_type": "QUERY_VELOCITY",
        "metric_value": 1.2,
        "threshold_low": 0.5,
        "threshold_high": 3.0,
        "notes": "Query rate is within normal acceptable tolerance range",
    }
    res_k1 = client.post("/api/v1/ctms/rbqm/kri", json=kri_norm, headers=headers)
    assert res_k1.status_code == 201
    assert res_k1.json()["breach_status"] == "NORMAL"

    # Step 2: Record breached KRI (SAE Reporting Lag > 24 hours threshold)
    kri_breach = {
        "study_id": study_id,
        "site_id": site_id,
        "metric_type": "SAE_REPORTING_LAG_DAYS",
        "metric_value": 4.5,
        "threshold_low": 0.0,
        "threshold_high": 1.0,
        "notes": "Site delayed SAE reporting by 4.5 days, exceeding 24-hour regulatory limit",
    }
    res_k2 = client.post("/api/v1/ctms/rbqm/kri", json=kri_breach, headers=headers)
    assert res_k2.status_code == 201
    assert res_k2.json()["breach_status"] == "BREACHED"

    # Step 3: Record second breached KRI (SDV Backlog Rate > 30%)
    kri_sdv = {
        "study_id": study_id,
        "site_id": site_id,
        "metric_type": "SDV_BACKLOG_RATE",
        "metric_value": 45.0,
        "threshold_low": 0.0,
        "threshold_high": 20.0,
        "notes": "45% of critical verification forms unverified",
    }
    res_k3 = client.post("/api/v1/ctms/rbqm/kri", json=kri_sdv, headers=headers)
    assert res_k3.status_code == 201
    assert res_k3.json()["breach_status"] == "BREACHED"

    # Step 4: Evaluate Site Risk Score
    res_risk = client.post(
        f"/api/v1/ctms/rbqm/sites/{site_id}/evaluate-risk?study_id={study_id}",
        headers=headers,
    )
    assert res_risk.status_code == 200
    risk_data = res_risk.json()
    assert risk_data["risk_level"] == "HIGH"
    assert risk_data["recommended_monitoring_type"] == "TARGETED_FOR_CAUSE"
    assert risk_data["monitoring_interval_days"] == 14

    # Step 5: Query latest risk score endpoint
    res_get_risk = client.get(
        f"/api/v1/ctms/rbqm/sites/{site_id}/risk-score", headers=headers
    )
    assert res_get_risk.status_code == 200
    assert res_get_risk.json()["risk_level"] == "HIGH"
