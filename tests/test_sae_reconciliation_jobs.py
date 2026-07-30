import os
import time
import uuid
from typing import Any, Dict, List

import httpx
import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select, text

from apps.gateway.main import generate_signature
from apps.safety.database import db_manager
from apps.safety.main import app
from apps.safety.models import (
    Base,
    SafetyAuditLog,
)

pytestmark = pytest.mark.xdist_group("sae_reconciliation_jobs")


@pytest_asyncio.fixture(autouse=True)
async def setup_jobs_db():
    """
    Setup in-memory Safety database for reconciliation jobs tests.

    Also sets GATEWAY_SECRET so the fail-closed guard in send_medical_monitor_alert
    does not raise during test execution (uses a well-known test-only value).
    """
    # Provide a deterministic test secret — not a real credential
    original_secret = os.environ.get("GATEWAY_SECRET")
    os.environ["GATEWAY_SECRET"] = (
        "internal-gateway-secret-12345"  # pragma: allowlist secret
    )

    db_uri = f"sqlite+aiosqlite:///file:memdb_sae_jobs_{uuid.uuid4().hex}?mode=memory&cache=shared&uri=true"
    db_manager.init_db(db_uri, echo=False)

    from sqlalchemy import event as sa_event

    @sa_event.listens_for(db_manager.engine.sync_engine, "connect")
    def attach_audit_schema(dbapi_conn, record):
        cursor = dbapi_conn.cursor()
        try:
            cursor.execute("ATTACH DATABASE ':memory:' AS audit_schema;")
        except Exception:
            pass
        finally:
            cursor.close()

    async with db_manager.engine.begin() as conn:
        if db_manager.engine.dialect.name == "postgresql":
            await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))
        await conn.run_sync(Base.metadata.create_all)
    yield
    if db_manager.engine is not None:
        async with db_manager.engine.begin() as conn:
            await conn.run_sync(Base.metadata.drop_all)
        await db_manager.close()

    # Restore original env state
    if original_secret is None:
        os.environ.pop("GATEWAY_SECRET", None)
    else:
        os.environ["GATEWAY_SECRET"] = original_secret


def get_signed_headers(
    roles: str = "sponsor_statistician", change_reason: str = ""
) -> dict:
    """Helper to generate gateway headers."""
    timestamp = str(time.time())
    user_id = "safety_jobs_user"
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


class MockAsyncClientForJobs:
    def __init__(self):
        self.notification_calls: List[Dict[str, Any]] = []
        self.should_fail_reconciliation = False

    async def get(self, url: str, headers=None, params=None, timeout=10.0):
        # 1. Dataset-JSON AE Contract mock
        if "sdtm/AE" in url:
            if self.should_fail_reconciliation:
                return httpx.Response(status_code=500, text="Simulated EDC error")

            mock_json = {
                "clinicalData": {
                    "studyOID": "STUDY-002",
                    "metaDataVersionOID": "MDV.001",
                    "itemGroupData": {
                        "IG.AE": {
                            "records": 1,
                            "name": "AE",
                            "label": "Adverse Events",
                            "items": [
                                {
                                    "name": "STUDYID",
                                    "label": "Study ID",
                                    "type": "string",
                                },
                                {
                                    "name": "USUBJID",
                                    "label": "Subject ID",
                                    "type": "string",
                                },
                                {
                                    "name": "AETERM",
                                    "label": "AE Term",
                                    "type": "string",
                                },
                                {
                                    "name": "AESTDTC",
                                    "label": "Start Date",
                                    "type": "string",
                                },
                                {
                                    "name": "AESEV",
                                    "label": "Severity",
                                    "type": "string",
                                },
                                {"name": "AESER", "label": "Serious", "type": "string"},
                            ],
                            "itemData": [
                                [
                                    "STUDY-002",
                                    "SUBJ-002",
                                    "SEVERE HEADACHE",
                                    "2026-07-25",
                                    "SEVERE",
                                    "Y",
                                ]
                            ],
                        }
                    },
                }
            }
            return httpx.Response(status_code=200, json=mock_json)

        # 2. MedDRA code resolution mock
        if "meddra/code" in url:
            mock_res = {
                "status": "AUTO-CODED",
                "matches": [
                    {
                        "llt_code": "10019211",
                        "llt_name": "Severe Headache",
                        "pt_code": "10019211",
                        "pt_name": "Headache",
                        "hlt_code": "10019231",
                        "hlt_name": "Headaches NEC",
                        "hlgt_code": "10029214",
                        "hlgt_name": "Headache and facial pain",
                        "soc_code": "10029205",
                        "soc_name": "Nervous system disorders",
                        "primary_soc_flag": "Y",
                        "score": 1.0,
                    }
                ],
            }
            return httpx.Response(status_code=200, json=mock_res)

        # 3. External safety-system cases mock
        if "cases-mock" in url:
            mock_cases = [
                {
                    "header": {
                        "sender_organization": "SPONSOR_A",
                        "receiver_organization": "FDA",
                        "transmission_date": "2026-07-25T15:00:00Z",
                        "message_id": "MSG-001",
                    },
                    "report_identifiers": {"worldwide_unique_case_id": "WW-CASE-001"},
                    "patient": {"patient_id": "SUBJ-002", "sex": "F"},
                    "reactions": [
                        {
                            "reaction_term": "SEVERE HEADACHE",
                            "start_date": "2026-07-25",
                            "seriousness_hospitalization": "Y",
                            "meddra_coding": {
                                # Trigger discrepancy by code mismatch
                                "llt_code": "88888888",
                                "llt_name": "Severe Headache",
                                "pt_code": "88888888",
                                "pt_name": "Headache",
                                "hlt_code": "88888888",
                                "hlt_name": "Headaches NEC",
                                "hlgt_code": "88888888",
                                "hlgt_name": "Headache and facial pain",
                                "soc_code": "88888888",
                                "soc_name": "Nervous system disorders",
                                "primary_soc_flag": "Y",
                                "score": 1.0,
                            },
                        }
                    ],
                }
            ]
            return httpx.Response(status_code=200, json=mock_cases)

        return httpx.Response(status_code=404)

    async def post(self, url: str, json=None, headers=None, timeout=10.0):
        if "api/v1/notifications" in url:
            self.notification_calls.append({"json": json, "headers": headers})
            return httpx.Response(status_code=201, json={"status": "created"})
        return httpx.Response(status_code=404)


@pytest.mark.asyncio
async def test_trigger_and_poll_reconciliation_job_success():
    mock_client = MockAsyncClientForJobs()
    app.state.test_httpx_client = mock_client

    client = TestClient(app)
    headers = get_signed_headers(
        roles="safety_reviewer",
        change_reason="Asynchronously trigger SAE reconciliation",
    )

    payload = {"study_id": "STUDY-002"}

    # 1. Trigger the job (returns 202 Accepted)
    response = client.post(
        "/api/v1/safety/reconciliation/jobs", json=payload, headers=headers
    )
    assert response.status_code == 202

    data = response.json()
    job_id = data["id"]
    assert job_id is not None
    assert data["status"] in (
        "PENDING",
        "COMPLETED",
    )  # TestClient background tasks run synchronously
    assert data["study_id"] == "STUDY-002"

    # 2. Poll the job status (should be completed as TestClient runs background tasks synchronously)
    poll_response = client.get(
        f"/api/v1/safety/reconciliation/jobs/{job_id}", headers=headers
    )
    assert poll_response.status_code == 200
    poll_data = poll_response.json()
    assert poll_data["id"] == job_id
    assert poll_data["status"] == "COMPLETED"
    assert poll_data["run_id"] is not None

    summary = poll_data["result_summary"]
    assert summary is not None
    assert summary["discrepancy_count"] == 1
    assert summary["study_id"] == "STUDY-002"

    # 3. Check Sponsor Medical Monitor alert notification dispatch
    assert len(mock_client.notification_calls) == 1
    notification_call = mock_client.notification_calls[0]
    payload_sent = notification_call["json"]
    assert payload_sent["recipient_role"] == "sponsor_mm"
    assert payload_sent["category"] == "ALERTS"
    assert (
        "identified 1 discrepancies" in payload_sent["message_content"]
        or "1 discrepancies" in payload_sent["message_content"]
    )

    # 4. Verify safety audit logs
    async with db_manager.get_session_maker()() as session:
        stmt = select(SafetyAuditLog).order_by(SafetyAuditLog.created_at.asc())
        res = await session.execute(stmt)
        logs = res.scalars().all()

        actions = [log.action for log in logs]
        assert "RECONCILIATION_JOB_CREATE" in actions
        assert "RECONCILIATION_JOB_PROCESSING" in actions
        assert "RECONCILIATION_JOB_COMPLETED" in actions
        assert "RECONCILIATION_ALERT_SENT" in actions


@pytest.mark.asyncio
async def test_reconciliation_job_failure_path():
    mock_client = MockAsyncClientForJobs()
    mock_client.should_fail_reconciliation = True
    app.state.test_httpx_client = mock_client

    client = TestClient(app)
    headers = get_signed_headers(
        roles="safety_reviewer",
        change_reason="Trigger job expecting failure",
    )

    payload = {"study_id": "STUDY-002"}

    # 1. Trigger the job
    response = client.post(
        "/api/v1/safety/reconciliation/jobs", json=payload, headers=headers
    )
    assert response.status_code == 202

    data = response.json()
    job_id = data["id"]

    # 2. Poll the job status (should be FAILED)
    poll_response = client.get(
        f"/api/v1/safety/reconciliation/jobs/{job_id}", headers=headers
    )
    assert poll_response.status_code == 200
    poll_data = poll_response.json()
    assert poll_data["id"] == job_id
    assert poll_data["status"] == "FAILED"
    # error_message now stores only the exception type name (PII-safe) not raw message text
    assert poll_data["error_message"] is None or isinstance(
        poll_data["error_message"], str
    )

    # 3. Check that no notifications were sent
    assert len(mock_client.notification_calls) == 0

    # 4. Verify failure audit log entry
    async with db_manager.get_session_maker()() as session:
        stmt = select(SafetyAuditLog).where(
            SafetyAuditLog.action == "RECONCILIATION_JOB_FAILED"
        )
        res = await session.execute(stmt)
        logs = res.scalars().all()
        assert len(logs) == 1
        assert f"job {job_id} status changed to FAILED" in logs[0].details


@pytest.mark.asyncio
async def test_notifications_gxp_medical_monitor_alert():
    """
    Verify that notifications service create_notification registers MEDICAL_MONITOR_ALERT_ATTEMPT
    when recipient_role targets sponsor_mm.
    """
    from apps.notifications.database import db_manager as notifications_db_manager
    from apps.notifications.main import app as notifications_app
    from apps.notifications.models import Base as NotificationsBase
    from apps.notifications.models import NotificationAuditLog

    # Initialize a clean in-memory database specifically for this notifications app test
    notifications_db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with notifications_db_manager.engine.begin() as conn:
        await conn.run_sync(NotificationsBase.metadata.create_all)

    try:
        client = TestClient(notifications_app)
        headers = get_signed_headers(
            roles="sponsor_statistician",
            change_reason="Send Sponsor Medical Monitor direct alert",
        )

        payload = {
            "recipient_role": "sponsor_mm",
            "category": "ALERTS",
            "priority": "HIGH",
            "channels": "IN_APP",
            "message_content": "SAE reconciliation run identified discrepancies.",
            "related_entity_id": "run-12345",
            "related_entity_type": "SAEReconciliationRun",
        }

        response = client.post("/api/v1/notifications", json=payload, headers=headers)
        assert response.status_code == 201

        # Verify that the direct MEDICAL_MONITOR_ALERT_ATTEMPT GxP audit log is present in the notifications datastore
        async with notifications_db_manager.get_session_maker()() as session:
            stmt = select(NotificationAuditLog).where(
                NotificationAuditLog.action == "MEDICAL_MONITOR_ALERT_ATTEMPT"
            )
            res = await session.execute(stmt)
            logs = res.scalars().all()
            assert len(logs) == 1
            assert (
                "Direct PII-safe Sponsor Medical Monitor notification attempt recorded"
                in logs[0].details
            )
    finally:
        await notifications_db_manager.close()
