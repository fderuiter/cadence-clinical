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
    roles: str = "Grants Manager",
    change_reason: str = "Financial operations",
    action: str | None = None,
    user_id: str = "grants_lead_01",
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


def test_procedure_financials_and_invoice_disbursement():
    """Validate procedure payment grids, withholding/holdback rules, and batch invoice disbursement.

    @req:PRD-CTMS-008, PRD-CTMS-004
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="Grants Manager", change_reason="Budget setup")
    study_id = "STUDY-FIN-505"
    site_id = "SITE-301"

    # Step 1: Create an Investigator Grant
    grant_payload = {
        "study_id": study_id,
        "site_id": site_id,
        "total_budget": 500000.0,
        "currency": "USD",
    }
    res_grant = client.post("/api/v1/ctms/grants", json=grant_payload, headers=headers)
    assert res_grant.status_code == 201
    grant_id = res_grant.json()["id"]

    # Step 2: Add procedure payment grid items with 10% withholding (holdback) and 20% overhead
    grid_items = [
        {
            "grant_id": grant_id,
            "visit_name": "SCREENING",
            "procedure_code": "PROC-ECG-01",
            "procedure_name": "12-Lead Electrocardiogram",
            "base_amount": 250.0,
            "overhead_percentage": 20.0,
            "withholding_percentage": 10.0,
        },
        {
            "grant_id": grant_id,
            "visit_name": "SCREENING",
            "procedure_code": "PROC-BLOOD-01",
            "procedure_name": "Comprehensive Metabolic Panel",
            "base_amount": 150.0,
            "overhead_percentage": 20.0,
            "withholding_percentage": 10.0,
        },
    ]

    for item in grid_items:
        res_grid = client.post(
            "/api/v1/ctms/financials/procedure-grids", json=item, headers=headers
        )
        assert res_grid.status_code == 201

    # Step 3: Calculate visit payable for SCREENING:
    # Base = 400. Overhead (20%) = 80. Gross = 480.
    # Withholding (10% of 480) = 48. Net = 432.
    res_calc = client.get(
        f"/api/v1/ctms/financials/grants/{grant_id}/calculate-visit?visit_name=SCREENING",
        headers=headers,
    )
    assert res_calc.status_code == 200
    calc_data = res_calc.json()
    assert calc_data["gross_amount"] == 480.0
    assert calc_data["withholding_amount"] == 48.0
    assert calc_data["net_amount"] == 432.0

    # Step 4: Create Batch Invoice
    inv_payload = {
        "study_id": study_id,
        "site_id": site_id,
        "grant_id": grant_id,
        "invoice_type": "VISIT_PROCEDURE_BATCH",
        "gross_amount": 480.0,
        "withholding_amount": 48.0,
        "currency": "USD",
        "payable_ids": ["pay-001", "pay-002"],
    }
    res_inv = client.post(
        "/api/v1/ctms/financials/invoices", json=inv_payload, headers=headers
    )
    assert res_inv.status_code == 201
    inv_data = res_inv.json()
    assert inv_data["status"] == "DRAFT"
    assert inv_data["net_amount"] == 432.0
    inv_id = inv_data["id"]

    # Step 5: Approve and Disburse Invoice with eSignature authorization
    approve_action = f"/api/v1/ctms/financials/invoices/{inv_id}/approve"
    headers_approve = get_auth_headers(
        roles="Grants Manager",
        change_reason="Invoice approval and disbursement",
        action=approve_action,
    )
    res_disburse = client.post(approve_action, headers=headers_approve)
    assert res_disburse.status_code == 200
    assert res_disburse.json()["status"] == "DISBURSED"
    assert res_disburse.json()["approved_by"] == "grants_lead_01"
