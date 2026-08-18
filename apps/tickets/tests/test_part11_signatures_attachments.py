"""
Tests for 21 CFR Part 11 Electronic Signatures, RCA Validation, Evidence Attachments, and Comment Boundaries.
"""

import os
import time
import uuid

import pytest
import pytest_asyncio
from httpx import ASGITransport, AsyncClient
from jose import jwt

from apps.tickets.adapters.database import db_manager
from apps.tickets.adapters.models import (
    Base,
)
from apps.tickets.main import app
from packages.testing.security import generate_signature


@pytest_asyncio.fixture(autouse=True)
async def setup_tickets_db():
    """Setup in-memory Tickets database."""
    db_manager.init_db(
        "sqlite+aiosqlite:///:memory:",
        echo=False,
        execution_options={"schema_translate_map": {"audit_schema": None}},
    )
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    if db_manager.engine is not None:
        async with db_manager.engine.begin() as conn:
            await conn.run_sync(Base.metadata.drop_all)
        await db_manager.close()


def create_sig_token(user_id: str) -> str:
    """Helper to generate a valid Part 11 JWT re-authentication signature token."""
    secret = os.getenv(
        "GATEWAY_SECRET", "internal-gateway-secret-12345"
    )  # pragma: allowlist secret
    payload = {
        "sub": user_id,
        "exp": time.time() + 300,
        "jti": str(uuid.uuid4()),
    }
    return jwt.encode(payload, secret, algorithm="HS256")


def get_auth_headers(
    roles: str = "sponsor_admin,qa_lead",
    change_reason: str = "Part 11 verification",
    user_id: str = "qa_officer",
    site_id: str | None = None,
    sig_token: str | None = None,
    tenant_id: str = "tenant_default",
) -> dict:
    timestamp = str(time.time())
    sig = generate_signature(
        user_id,
        roles,
        timestamp,
        version="2",
        change_reason=change_reason,
        site_id=site_id,
        sig_token=sig_token,
        tenant_id=tenant_id,
    )
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
        "X-Tenant-Id": tenant_id,
    }
    if site_id:
        headers["X-Site-Id"] = site_id
    if sig_token:
        headers["X-Sig-Token"] = sig_token
        headers["X-Signature-Token"] = sig_token
    return headers


@pytest.mark.asyncio
async def test_rca_validation_on_major_and_critical_closure():
    """
    Validate that resolving or closing a MAJOR or CRITICAL ticket mandates
    Root Cause Analysis (RCA) classification and resolution code under 21 CFR Part 11.

    @req:PRD-TCK-001
    """
    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        headers = get_auth_headers()

        # 1. Create a Major Protocol Deviation
        res = await client.post(
            "/api/v1/tickets",
            json={
                "title": "Major Protocol Deviation: Missed Primary Endpoint ECG",
                "description": "ECG at Visit 3 was not performed prior to IMP dosing.",
                "category": "PROTOCOL_DEVIATION",
                "priority": "HIGH",
                "gxp_severity": "MAJOR",
            },
            headers=headers,
        )
        assert res.status_code == 201
        ticket_id = res.json()["id"]

        # 2. Attempt to resolve WITHOUT root cause or resolution code -> must fail with HTTP 400
        res_fail = await client.post(
            f"/api/v1/tickets/{ticket_id}/transition",
            json={
                "status": "RESOLVED",
                "version_index": 1,
            },
            headers=headers,
        )
        assert res_fail.status_code == 400
        assert "Root cause category (RCA) is required" in res_fail.json()["detail"]

        # 3. Resolve WITH valid RCA root cause and resolution code -> succeeds
        res_success = await client.post(
            f"/api/v1/tickets/{ticket_id}/transition",
            json={
                "status": "RESOLVED",
                "version_index": 1,
                "root_cause_category": "TRAINING_GAP",
                "root_cause_summary": "CRC was newly onboarded and missed the pre-dose ECG timing requirement in the schedule of activities.",
                "resolution_code": "CAPA_INITIATED",
            },
            headers=headers,
        )
        assert res_success.status_code == 200
        data = res_success.json()
        assert data["status"] == "RESOLVED"
        assert data["root_cause_category"] == "TRAINING_GAP"
        assert data["resolution_code"] == "CAPA_INITIATED"


@pytest.mark.asyncio
async def test_21cfr_part11_esignature_capture():
    """
    Validate capturing cryptographic 21 CFR Part 11 Electronic Signature on a ticket.

    @req:PRD-TCK-004
    """
    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        headers = get_auth_headers(user_id="dr_smith_pi")

        res_create = await client.post(
            "/api/v1/tickets",
            json={
                "title": "Critical Safety Escalation Sign-Off",
                "description": "Medical Monitor formal review of Grade 3 Neutropenia report.",
                "category": "SAFETY_ADVERSE_EVENT",
                "priority": "CRITICAL",
                "gxp_severity": "CRITICAL",
            },
            headers=headers,
        )
        ticket_id = res_create.json()["id"]

        sig_token = create_sig_token("dr_smith_pi")
        sign_headers = get_auth_headers(
            user_id="dr_smith_pi",
            sig_token=sig_token,
            change_reason="21 CFR Part 11 Electronic Signature Sign-off",
        )

        res_sign = await client.post(
            f"/api/v1/tickets/{ticket_id}/sign",
            json={
                "signature_token": sig_token,
                "meaning": "I approve the clinical assessment and corrective medical action.",
                "version_index": 1,
            },
            headers=sign_headers,
        )
        assert res_sign.status_code == 200, (
            f"Failed with {res_sign.status_code}: {res_sign.text}"
        )
        signed_ticket = res_sign.json()
        assert signed_ticket["signature_token"] == sig_token
        assert signed_ticket["signature_user"] == "dr_smith_pi"
        assert signed_ticket["signature_timestamp"] is not None


@pytest.mark.asyncio
async def test_audited_evidence_attachments():
    """
    Validate uploading audited file attachments with SHA-256 integrity verification.

    @req:PRD-TCK-004
    """
    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        headers = get_auth_headers()

        res_create = await client.post(
            "/api/v1/tickets",
            json={
                "title": "Temperature Logger Excursion Evidence",
                "description": "IMP storage chamber dropped to -2C for 45 minutes.",
                "category": "SUPPLY_EXCURSION",
                "priority": "HIGH",
            },
            headers=headers,
        )
        ticket_id = res_create.json()["id"]

        # Upload evidence file
        file_payload = b"TEMPLOGGER_CSV_DATA_TIMESTAMP_TEMP_1.8C"
        files = {"file": ("temp_log_site101.csv", file_payload, "text/csv")}
        data = {"reason_for_change": "Attaching cold-chain logger raw CSV download"}

        res_upload = await client.post(
            f"/api/v1/tickets/{ticket_id}/attachments",
            files=files,
            data=data,
            headers=headers,
        )
        assert res_upload.status_code == 201
        att_data = res_upload.json()
        assert att_data["filename"] == "temp_log_site101.csv"
        assert att_data["file_size_bytes"] == len(file_payload)
        assert len(att_data["sha256_hash"]) == 64
        assert att_data["deid_scrubbed"] is True

        # List attachments
        res_list = await client.get(
            f"/api/v1/tickets/{ticket_id}/attachments",
            headers=headers,
        )
        assert res_list.status_code == 200
        assert len(res_list.json()) == 1


@pytest.mark.asyncio
async def test_comment_visibility_boundaries():
    """
    Validate that INTERNAL_SPONSOR comments are hidden from Site CRC roles and only visible to Sponsor/CRA roles.
    """
    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        sponsor_headers = get_auth_headers(
            roles="sponsor_admin,cra",
            user_id="sponsor_cra_lead",
            site_id="SITE-101",
        )
        site_headers = get_auth_headers(
            roles="site_crc,investigator",
            user_id="site_coordinator_mary",
            site_id="SITE-101",
        )

        res_create = await client.post(
            "/api/v1/tickets",
            json={
                "title": "Site Query on Informed Consent",
                "description": "Question on assent form for pediatric cohort.",
                "site_id": "SITE-101",
            },
            headers=sponsor_headers,
        )
        ticket_id = res_create.json()["id"]

        # 1. Sponsor adds a Public comment
        await client.post(
            f"/api/v1/tickets/{ticket_id}/comments",
            json={"body": "Public clarification for site", "visibility": "PUBLIC"},
            headers=sponsor_headers,
        )

        # 2. Sponsor adds an Internal Note
        await client.post(
            f"/api/v1/tickets/{ticket_id}/comments",
            json={
                "body": "Internal Note: We need to check if amendment #2 was approved in this country.",
                "visibility": "INTERNAL_SPONSOR",
            },
            headers=sponsor_headers,
        )

        # 3. Sponsor reads comments -> should see BOTH
        res_sponsor_view = await client.get(
            f"/api/v1/tickets/{ticket_id}/comments",
            headers=sponsor_headers,
        )
        assert len(res_sponsor_view.json()) == 2

        # 4. Site CRC reads comments -> should see ONLY PUBLIC comment
        res_site_view = await client.get(
            f"/api/v1/tickets/{ticket_id}/comments",
            headers=site_headers,
        )
        assert len(res_site_view.json()) == 1
        assert res_site_view.json()[0]["body"] == "Public clarification for site"


@pytest.mark.asyncio
async def test_regulatory_audit_trail_export():
    """
    Validate exporting the immutable Part 11 audit ledger in JSON and CSV formats.
    """
    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        headers = get_auth_headers()

        # Create ticket to generate audit entries
        res = await client.post(
            "/api/v1/tickets",
            json={
                "title": "Audit Export Test",
                "description": "Validating export data generation.",
            },
            headers=headers,
        )
        ticket_id = res.json()["id"]

        # 1. JSON Export
        res_json = await client.get(
            f"/api/v1/tickets/export/audit-trail?ticket_id={ticket_id}&format=json",
            headers=headers,
        )
        assert res_json.status_code == 200
        json_data = res_json.json()
        assert "audit_trail" in json_data
        assert json_data["count"] >= 1

        # 2. CSV Export
        res_csv = await client.get(
            f"/api/v1/tickets/export/audit-trail?ticket_id={ticket_id}&format=csv",
            headers=headers,
        )
        assert res_csv.status_code == 200
        assert "text/csv" in res_csv.headers["content-type"]
        csv_text = res_csv.text
        assert "TICKET_CREATE" in csv_text
        assert "id,ticket_id,created_at,created_by" in csv_text
