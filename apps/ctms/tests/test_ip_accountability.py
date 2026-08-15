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
    roles: str = "Site Coordinator",
    change_reason: str = "IP Accountability Operation",
    user_id: str = "site_crc_01",
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


def test_ip_shipment_receipt_dispensation_and_reconciliation():
    """Validate IP lot shipment receipt, dispensation, compliance unit reconciliation, and destruction.

    @req:PRD-CTMS-009, PRD-CTMS-004
    """
    client = TestClient(app)
    headers = get_auth_headers(
        roles="Site Coordinator", change_reason="IP kit operations"
    )
    study_id = "STUDY-IP-606"
    site_id = "SITE-401"

    # Step 1: Receive IP Shipment (3 kits)
    recv_payload = {
        "study_id": study_id,
        "site_id": site_id,
        "kit_numbers": ["KIT-001", "KIT-002", "KIT-003"],
        "lot_number": "LOT-2026-X9",
        "kit_type": "ACTIVE_DRUG",
        "shipment_tracking_number": "FEDEX-998877",
        "expiration_date": "2027-12-31",
    }
    res_recv = client.post(
        "/api/v1/ctms/ip/shipments/receive", json=recv_payload, headers=headers
    )
    assert res_recv.status_code == 201
    kits = res_recv.json()
    assert len(kits) == 3
    kit1_id = kits[0]["id"]
    kit2_id = kits[1]["id"]
    kit3_id = kits[2]["id"]

    # Step 2: Dispense Kit 1 to Subject
    disp_payload = {
        "subject_id": "SUBJ-801",
        "visit_id": "VISIT-W04",
    }
    res_disp = client.post(
        f"/api/v1/ctms/ip/kits/{kit1_id}/dispense",
        json=disp_payload,
        headers=headers,
    )
    assert res_disp.status_code == 200
    assert res_disp.json()["status"] == "DISPENSED"
    assert res_disp.json()["dispensed_subject_id"] == "SUBJ-801"

    # Step 3: Reconcile returned units for Kit 1 (Patient took 28/30 pills -> 2 returned, 30 expected)
    rec_payload = {
        "returned_units_count": 28,
        "expected_units_count": 30,
        "notes": "Patient missed 2 doses due to mild gastrointestinal discomfort",
    }
    res_rec = client.post(
        f"/api/v1/ctms/ip/kits/{kit1_id}/reconcile",
        json=rec_payload,
        headers=headers,
    )
    assert res_rec.status_code == 200
    rec_data = res_rec.json()
    assert rec_data["status"] == "RETURNED_TO_SITE"
    assert rec_data["compliance_percentage"] == 93.33

    # Step 4: Temperature Excursion on Kit 2 & Kit 3
    exc_payload = {
        "study_id": study_id,
        "site_id": site_id,
        "kit_ids": [kit2_id, kit3_id],
        "excursion_type": "STORAGE",
        "min_temp_celsius": 12.5,
        "max_temp_celsius": 18.0,
        "duration_hours": 36.0,
        "occurred_at": "2026-08-01T12:00:00",
    }
    res_exc = client.post(
        "/api/v1/ctms/ip/excursions", json=exc_payload, headers=headers
    )
    assert res_exc.status_code == 201
    assert res_exc.json()["disposition_status"] == "QUARANTINED"
    exc_id = res_exc.json()["id"]

    # Verify Kit 2 is QUARANTINED and cannot be dispensed (HTTP 422)
    res_disp_blocked = client.post(
        f"/api/v1/ctms/ip/kits/{kit2_id}/dispense",
        json={"subject_id": "SUBJ-802", "visit_id": "VISIT-W01"},
        headers=headers,
    )
    assert res_disp_blocked.status_code == 422
    assert "QUARANTINED" in res_disp_blocked.json()["detail"]

    # Step 5: QA Disposition - Reject for Destruction
    res_qa = client.post(
        f"/api/v1/ctms/ip/excursions/{exc_id}/disposition",
        json={
            "disposition_status": "QA_REJECTED_DESTROY",
            "qa_rationale": "Product degraded beyond stability limits",
        },
        headers=headers,
    )
    assert res_qa.status_code == 200
    assert res_qa.json()["disposition_status"] == "QA_REJECTED_DESTROY"

    # Step 6: Witnessed On-Site Destruction Certificate
    dest_payload = {
        "study_id": study_id,
        "site_id": site_id,
        "kit_ids": [kit2_id, kit3_id],
        "destruction_method": "ON_SITE_INCINERATION",
        "witness_user_id": "cra_monitor_01",
        "witness_role": "CRA / External Monitor",
        "pi_signature_hash": "sha256-pi-sig-hash-7890",
        "reason_for_destruction": "Excursion QA Rejection",
    }
    res_dest = client.post(
        "/api/v1/ctms/ip/destruction-certificates", json=dest_payload, headers=headers
    )
    assert res_dest.status_code == 201
    assert res_dest.json()["certificate_number"].startswith("COD-")
    assert res_dest.json()["pi_signature_hash"] == "sha256-pi-sig-hash-7890"
