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
    roles: str = "Sponsor Admin",
    change_reason: str = "Site Startup Operation",
    user_id: str = "sponsor_admin_01",
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


def test_site_startup_and_regulatory_milestones():
    """Validate country regulatory milestone tracking under Part 11 audit trails.

    @req:PRD-CTMS-005, PRD-CTMS-004
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="Sponsor Admin", change_reason="CTA Submission")

    payload = {
        "study_id": "STUDY-STARTUP-101",
        "country_code": "US",
        "milestone_type": "CTA_SUBMISSION",
        "status": "APPROVED",
        "planned_date": "2026-06-01T00:00:00",
        "actual_date": "2026-06-02T00:00:00",
        "approval_number": "IND-123456",
        "authority_name": "US FDA",
    }

    res = client.post("/api/v1/ctms/startup/milestones", json=payload, headers=headers)
    assert res.status_code == 201
    data = res.json()
    assert data["country_code"] == "US"
    assert data["milestone_type"] == "CTA_SUBMISSION"
    assert data["approval_number"] == "IND-123456"

    # List milestones
    res_list = client.get(
        "/api/v1/ctms/startup/milestones?study_id=STUDY-STARTUP-101", headers=headers
    )
    assert res_list.status_code == 200
    assert len(res_list.json()) == 1


def test_site_greenlight_gatekeeper_workflow():
    """Validate essential document review and automated greenlight gate blocking/approval.

    @req:PRD-CTMS-005, PRD-CTMS-004
    """
    client = TestClient(app)
    headers = get_auth_headers(
        roles="Sponsor Admin", change_reason="Greenlight verification"
    )
    study_id = "STUDY-STARTUP-202"
    site_id = "SITE-GL-01"

    # Step 1: Submit partial document (only SITE_CONTRACT)
    doc_payload = {
        "study_id": study_id,
        "site_id": site_id,
        "document_type": "SITE_CONTRACT",
        "file_name": "Site_Agreement_Signed.pdf",
        "file_hash": "a1b2c3d4e5f6",
        "expiration_date": "2028-12-31T00:00:00",
    }
    res_doc = client.post(
        "/api/v1/ctms/startup/documents", json=doc_payload, headers=headers
    )
    assert res_doc.status_code == 201
    doc_id = res_doc.json()["id"]

    # Approve SITE_CONTRACT
    res_review = client.put(
        f"/api/v1/ctms/startup/documents/{doc_id}/review",
        json={"status": "APPROVED", "review_notes": "Fully executed contract"},
        headers=headers,
    )
    assert res_review.status_code == 200

    # Step 2: Evaluate Greenlight - should be PENDING because IRB & 1572 are missing
    res_gl = client.get(
        f"/api/v1/ctms/startup/sites/{site_id}/greenlight?study_id={study_id}",
        headers=headers,
    )
    assert res_gl.status_code == 200
    assert res_gl.json()["overall_status"] == "PENDING"
    assert res_gl.json()["contract_approved"] is True
    assert res_gl.json()["irb_approved"] is False

    # Attempting to certify greenlight before prerequisite approval must fail (HTTP 422)
    res_cert_fail = client.post(
        f"/api/v1/ctms/startup/sites/{site_id}/greenlight/certify?study_id={study_id}",
        headers=headers,
    )
    assert res_cert_fail.status_code == 422
    assert "Missing mandatory approvals" in res_cert_fail.json()["detail"]

    # Step 3: Submit and approve remaining mandatory documents (LOCAL_IRB_APPROVAL, FDA_1572)
    for doc_type, file_name in [
        ("LOCAL_IRB_APPROVAL", "IRB_Approval.pdf"),
        ("FDA_1572", "FDA_1572_Signed.pdf"),
    ]:
        r_d = client.post(
            "/api/v1/ctms/startup/documents",
            json={
                "study_id": study_id,
                "site_id": site_id,
                "document_type": doc_type,
                "file_name": file_name,
                "file_hash": "hash12345",
            },
            headers=headers,
        )
        d_id = r_d.json()["id"]
        client.put(
            f"/api/v1/ctms/startup/documents/{d_id}/review",
            json={"status": "APPROVED", "review_notes": "Approved by Regulatory Lead"},
            headers=headers,
        )

    # Step 4: Certify Greenlight - must succeed now
    res_cert_ok = client.post(
        f"/api/v1/ctms/startup/sites/{site_id}/greenlight/certify?study_id={study_id}",
        headers=headers,
    )
    assert res_cert_ok.status_code == 200
    data_ok = res_cert_ok.json()
    assert data_ok["overall_status"] == "APPROVED"
    assert data_ok["contract_approved"] is True
    assert data_ok["irb_approved"] is True
    assert data_ok["form_1572_approved"] is True
    assert data_ok["greenlight_certified_by"] == "sponsor_admin_01"
