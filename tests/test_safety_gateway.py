"""Integration and endpoint test suite for Safety Gateway routing, signature validation, and async background reconciliation.

Requirements: PRD-SYS-001
"""

import asyncio
import os
import time
import uuid
from typing import Any, Dict, List

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.gateway.main import generate_signature
from apps.safety.database import db_manager
from apps.safety.main import app as safety_app
from apps.safety.models import (
    Base,
    SAEReconciliationJob,
    SAEReconciliationRun,
    SafetyAuditLog,
)

pytestmark = pytest.mark.xdist_group("safety_gateway_endpoints")


@pytest_asyncio.fixture(autouse=True)
async def setup_safety_db_fixture():
    """Autouse fixture that initializes the safety service's db_manager on in-memory SQLite and tears it down.

    Requirements: PRD-SYS-001
    """
    # Set GATEWAY_SECRET to a mock secret for consistent testing across environments
    original_secret = os.environ.get("GATEWAY_SECRET")
    os.environ["GATEWAY_SECRET"] = "internal-gateway-secret-12345"

    db_uri = f"sqlite+aiosqlite:///file:memdb_safety_gateway_{uuid.uuid4().hex}?mode=memory&cache=shared&uri=true"
    db_manager.init_db(db_uri, echo=False)

    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)

    yield

    if db_manager.engine is not None:
        async with db_manager.engine.begin() as conn:
            await conn.run_sync(Base.metadata.drop_all)
        await db_manager.close()

    # Restore original environment secret
    if original_secret is None:
        os.environ.pop("GATEWAY_SECRET", None)
    else:
        os.environ["GATEWAY_SECRET"] = original_secret


def get_v2_signed_headers(
    user_id: str = "safety_gateway_test_user",
    roles: str = "admin",
    change_reason: str = "",
    timestamp: str = None,
) -> Dict[str, str]:
    """Helper to generate Gateway V2 signed headers for mutations and read endpoints."""
    if timestamp is None:
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


class MockAsyncClientForReconciliation:
    """Mock client that simulates downstream clinical observation (EDC AE) and external safety databases."""

    async def get(self, url: str, headers=None, params=None, timeout=10.0):
        if "sdtm/AE" in url:
            mock_ae = {
                "clinicalData": {
                    "studyOID": "STUDY-GATEWAY-1",
                    "metaDataVersionOID": "MDV.01",
                    "itemGroupData": {
                        "IG.AE": {
                            "records": 1,
                            "name": "AE",
                            "label": "Adverse Events",
                            "items": [
                                {"name": "STUDYID", "type": "string"},
                                {"name": "USUBJID", "type": "string"},
                                {"name": "AETERM", "type": "string"},
                                {"name": "AESTDTC", "type": "string"},
                                {"name": "AESEV", "type": "string"},
                                {"name": "AESER", "type": "string"},
                            ],
                            "itemData": [
                                [
                                    "STUDY-GATEWAY-1",
                                    "SUBJ-PII-9999",  # Direct patient PII ID
                                    "MYOCARDIAL INFARCTION",
                                    "2026-08-28",
                                    "SEVERE",
                                    "Y",
                                ]
                            ],
                        }
                    },
                }
            }
            return httpx.Response(status_code=200, json=mock_ae)

        if "meddra/code" in url:
            mock_meddra = {
                "status": "AUTO-CODED",
                "matches": [
                    {
                        "llt_code": "10028596",
                        "llt_name": "Myocardial Infarction",
                        "pt_code": "10028596",
                        "pt_name": "Myocardial Infarction",
                        "hlt_code": "10028595",
                        "hlt_name": "Ischemic heart disease",
                        "hlgt_code": "10028594",
                        "hlgt_name": "Coronary artery disorders",
                        "soc_code": "10022891",
                        "soc_name": "Cardiac disorders",
                        "primary_soc_flag": "Y",
                        "score": 1.0,
                    }
                ],
            }
            return httpx.Response(status_code=200, json=mock_meddra)

        if "cases-mock" in url:
            mock_external_cases = [
                {
                    "header": {
                        "sender_organization": "SPONSOR-A",
                        "receiver_organization": "FDA",
                        "transmission_date": "2026-08-28T12:00:00Z",
                        "message_id": "MSG-001",
                    },
                    "report_identifiers": {
                        "worldwide_unique_case_id": "WW-CASE-9999"
                    },
                    "patient": {"patient_id": "SUBJ-PII-9999", "sex": "F"},
                    "reactions": [
                        {
                            "reaction_term": "MYOCARDIAL INFARCTION",
                            "start_date": "2026-08-28",
                            "seriousness_hospitalization": "Y",
                            "meddra_coding": {
                                # Use mismatched LLT to trigger a discrepancy
                                "llt_code": "11111111",
                                "llt_name": "Myocardial Infarction",
                                "pt_code": "10028596",
                                "pt_name": "Myocardial Infarction",
                                "hlt_code": "10028595",
                                "hlt_name": "Ischemic heart disease",
                                "hlgt_code": "10028594",
                                "hlgt_name": "Coronary artery disorders",
                                "soc_code": "10022891",
                                "soc_name": "Cardiac disorders",
                                "primary_soc_flag": "Y",
                                "score": 1.0,
                            },
                        }
                    ],
                }
            ]
            return httpx.Response(status_code=200, json=mock_external_cases)

        return httpx.Response(status_code=404)

    async def post(self, url: str, json=None, headers=None, timeout=10.0):
        if "api/v1/notifications" in url:
            return httpx.Response(status_code=201, json={"status": "created"})
        return httpx.Response(status_code=404)


@pytest.mark.asyncio
async def test_reconciliation_background_job_polling_and_audit() -> None:
    """Validate POST to reconciliation/jobs, polling for COMPLETED status, and audit-log assertions.

    Requirements: PRD-SYS-001
    """
    mock_client = MockAsyncClientForReconciliation()
    safety_app.state.test_httpx_client = mock_client

    headers = get_v2_signed_headers(
        user_id="reconciler_user",
        roles="safety_reviewer",
        change_reason="Trigger background study reconciliation job",
    )

    # 1. Trigger the background reconciliation job using httpx.AsyncClient + ASGITransport
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=safety_app), base_url="http://test"
    ) as client:
        trigger_resp = await client.post(
            "/api/v1/safety/reconciliation/jobs",
            json={"study_id": "STUDY-GATEWAY-1"},
            headers=headers,
        )
        assert trigger_resp.status_code == 202
        job_data = trigger_resp.json()
        job_id = job_data["id"]
        assert job_id is not None
        assert job_data["status"] in ("PENDING", "PROCESSING", "COMPLETED")

        # 2. Poll the job status using DB / GET polling until status is terminal
        max_attempts = 30
        completed = False
        for _ in range(max_attempts):
            poll_resp = await client.get(
                f"/api/v1/safety/reconciliation/jobs/{job_id}",
                headers=headers,
            )
            assert poll_resp.status_code == 200
            poll_data = poll_resp.json()
            if poll_data["status"] == "COMPLETED":
                completed = True
                assert poll_data["result_summary"] is not None
                assert poll_data["result_summary"]["discrepancy_count"] == 1
                break
            elif poll_data["status"] == "FAILED":
                pytest.fail(f"Background reconciliation job failed: {poll_data['error_message']}")
            await asyncio.sleep(0.05)

        assert completed is True

    # 3. Verify the GxP audit ledger trails (SafetyAuditLog)
    async with db_manager.get_session_maker()() as session:
        # Assertions on captured user_id, action, and change reason
        stmt = select(SafetyAuditLog).order_by(SafetyAuditLog.created_at.asc())
        res = await session.execute(stmt)
        logs = res.scalars().all()

        actions = [log.action for log in logs]
        assert "RECONCILIATION_JOB_CREATE" in actions
        assert "RECONCILIATION_JOB_PROCESSING" in actions
        assert "RECONCILIATION_JOB_COMPLETED" in actions

        # Check correct user_id/reason_for_change propagation
        create_log = [log for log in logs if log.action == "RECONCILIATION_JOB_CREATE"][0]
        assert create_log.created_by == "reconciler_user"
        assert create_log.reason_for_change == "Trigger background study reconciliation job"

        # Assert no patient PII leakage inside the audit log trails
        # Raw patient ID "SUBJ-PII-9999" must NOT exist in any audit log details
        for log in logs:
            assert "SUBJ-PII-9999" not in log.details


@pytest.mark.asyncio
async def test_mutation_missing_change_reason_fails() -> None:
    """Verify that mutations (POST) fail with 403 Forbidden when X-Change-Reason is missing.

    Requirements: PRD-SYS-001
    """
    # Missing change_reason parameter
    headers = get_v2_signed_headers(
        user_id="reconciler_user",
        roles="safety_reviewer",
        change_reason="",
    )

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=safety_app), base_url="http://test"
    ) as client:
        response = await client.post(
            "/api/v1/safety/reconciliation/jobs",
            json={"study_id": "STUDY-GATEWAY-1"},
            headers=headers,
        )
        assert response.status_code == 403
        assert "Missing change justification reason" in response.json()["detail"]


@pytest.mark.asyncio
async def test_gateway_signature_negative_paths() -> None:
    """Validate safety endpoints enforce gateway authorization and reject tampered, invalid, or expired signatures.

    Requirements: PRD-SYS-001
    """
    mutation_endpoint = "/api/v1/safety/reconciliation/jobs"
    mutation_payload = {"study_id": "STUDY-SIG-TEST"}

    read_endpoint = "/api/v1/safety/reconciliation/jobs"

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=safety_app), base_url="http://test"
    ) as client:
        # Case 1: Invalid Signature (mutations -> 403 Forbidden)
        headers_bad_sig = {
            "X-User-Id": "test_user",
            "X-User-Roles": "safety_reviewer",
            "X-Gateway-Timestamp": str(time.time()),
            "X-Gateway-Signature": "tampered_and_totally_invalid_signature_string",
            "X-Signature-Version": "2",
            "X-Change-Reason": "Valid mutation reason",
        }
        res_mut_bad = await client.post(
            mutation_endpoint, json=mutation_payload, headers=headers_bad_sig
        )
        assert res_mut_bad.status_code == 403
        assert "invalid gateway signature" in res_mut_bad.json()["detail"].lower()

        # Case 2: Expired Signature (mutations -> 403 Forbidden)
        expired_ts = str(time.time() - 310)  # > 300 seconds ago
        sig_expired = generate_signature(
            user_id="test_user",
            roles="safety_reviewer",
            timestamp=expired_ts,
            version="2",
            change_reason="Valid mutation reason",
        )
        headers_expired = {
            "X-User-Id": "test_user",
            "X-User-Roles": "safety_reviewer",
            "X-Gateway-Timestamp": expired_ts,
            "X-Gateway-Signature": sig_expired,
            "X-Signature-Version": "2",
            "X-Change-Reason": "Valid mutation reason",
        }
        res_mut_expired = await client.post(
            mutation_endpoint, json=mutation_payload, headers=headers_expired
        )
        assert res_mut_expired.status_code == 403
        assert "gateway signature expired" in res_mut_expired.json()["detail"].lower()

        # Case 3: Tampered Field in Signature (mutations -> 403 Forbidden)
        signed_reason = "Original change reason"
        tampered_reason = "Tampered/Modified change reason"
        ts = str(time.time())
        sig_tampered = generate_signature(
            user_id="test_user",
            roles="safety_reviewer",
            timestamp=ts,
            version="2",
            change_reason=signed_reason,
        )
        headers_tampered = {
            "X-User-Id": "test_user",
            "X-User-Roles": "safety_reviewer",
            "X-Gateway-Timestamp": ts,
            "X-Gateway-Signature": sig_tampered,
            "X-Signature-Version": "2",
            "X-Change-Reason": tampered_reason,
        }
        res_mut_tampered = await client.post(
            mutation_endpoint, json=mutation_payload, headers=headers_tampered
        )
        assert res_mut_tampered.status_code == 403
        assert "invalid gateway signature" in res_mut_tampered.json()["detail"].lower()

        # Case 4: Invalid Signature (reads -> 401 Unauthorized)
        headers_read_bad_sig = headers_bad_sig.copy()
        headers_read_bad_sig.pop("X-Change-Reason", None)
        res_read_bad = await client.get(
            read_endpoint, headers=headers_read_bad_sig
        )
        assert res_read_bad.status_code == 401
        assert "invalid gateway signature" in res_read_bad.json()["detail"].lower()

        # Case 5: Expired Signature (reads -> 401 Unauthorized)
        headers_read_expired = headers_expired.copy()
        headers_read_expired.pop("X-Change-Reason", None)
        res_read_expired = await client.get(
            read_endpoint, headers=headers_read_expired
        )
        assert res_read_expired.status_code == 401
        assert "gateway signature expired" in res_read_expired.json()["detail"].lower()
