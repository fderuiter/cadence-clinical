"""Integration test suite qualifying E2B(R3) XML generation and round-trip parsing.

Requirements: PRD-SYS-001
"""

import asyncio
import os
import time
from datetime import UTC, datetime
from typing import Any

import httpx
import pytest
import pytest_asyncio
from execution.safety_models import (
    CausalityEnum,
    SAECaseRecord,
    SeriousnessCriteriaEnum,
)
from httpx import ASGITransport, AsyncClient
from sqlalchemy import select

from apps.execution.exporters.e2b_xml_builder import E2BR3XMLBuilder
from apps.execution.services.e2b_parser import E2BR3Parser
from apps.gateway.main import generate_signature
from apps.safety.database import db_manager
from apps.safety.main import app
from apps.safety.models import Base, SafetyAuditLog


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """
    Setup in-memory Safety database for integration testing.
    Also configures GATEWAY_SECRET to prevent signature failures during tests.
    """
    original_secret = os.environ.get("GATEWAY_SECRET")
    os.environ["GATEWAY_SECRET"] = (
        "internal-gateway-secret-12345"  # pragma: allowlist secret
    )

    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    if db_manager.engine is not None:
        async with db_manager.engine.begin() as conn:
            await conn.run_sync(Base.metadata.drop_all)
        await db_manager.close()

    if original_secret is None:
        os.environ.pop("GATEWAY_SECRET", None)
    else:
        os.environ["GATEWAY_SECRET"] = original_secret


def get_auth_headers(
    roles: str = "safety_reviewer",
    change_reason: str = "",
    user_id: str = "safety_gateway_user",
) -> dict:
    """Helper to generate valid gateway V2 signed headers for testing."""
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


class MockAsyncClientForJobs:
    """Mock client simulating EDC dataset souting, MedDRA code resolution, and safety case persistence."""

    def __init__(self):
        self.notification_calls: list[dict[str, Any]] = []

    async def get(self, url: str, headers=None, params=None, timeout=10.0):
        # 1. Dataset-JSON AE Contract mock
        if "sdtm/AE" in url:
            mock_json = {
                "clinicalData": {
                    "studyOID": "STUDY-GATEWAY-TEST",
                    "metaDataVersionOID": "MDV.001",
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
                                    "STUDY-GATEWAY-TEST",
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
                                "llt_code": "88888888",  # Different code triggers discrepancy
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
async def test_safety_reconciliation_job_lifecycle_async():
    """Verify that triggering an asynchronous reconciliation job via ASGITransport and polling works properly with GxP auditing and zero PII leak."""
    mock_client = MockAsyncClientForJobs()
    app.state.test_httpx_client = mock_client

    headers = get_auth_headers(
        roles="safety_reviewer",
        change_reason="Asynchronously trigger SAE reconciliation from gateway test",
    )

    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        # 1. Trigger the background reconciliation job
        trigger_res = await client.post(
            "/api/v1/safety/reconciliation/jobs",
            json={"study_id": "STUDY-GATEWAY-TEST"},
            headers=headers,
        )
        assert trigger_res.status_code == 202
        job_data = trigger_res.json()
        job_id = job_data["id"]
        assert job_id is not None

        # 2. Poll the job status endpoint until COMPLETED
        status = "PENDING"
        for _ in range(20):
            poll_res = await client.get(
                f"/api/v1/safety/reconciliation/jobs/{job_id}",
                headers=headers,
            )
            assert poll_res.status_code == 200
            poll_data = poll_res.json()
            status = poll_data["status"]
            if status in ("COMPLETED", "FAILED"):
                break
            await asyncio.sleep(0.05)

        assert status == "COMPLETED"

        # 3. Assert on the immutable SafetyAuditLog ledger records
        async with db_manager.get_session_maker()() as session:
            stmt = select(SafetyAuditLog).order_by(SafetyAuditLog.created_at.desc())
            res = await session.execute(stmt)
            logs = res.scalars().all()

            assert len(logs) > 0
            # Ensure correct user and reason are populated in GxP logs
            assert any(log.created_by == "safety_gateway_user" for log in logs)
            assert any(
                log.reason_for_change
                == "Asynchronously trigger SAE reconciliation from gateway test"
                for log in logs
            )

            # Ensure NO raw patient PII/PHI (such as patient_id="SUBJ-002") was leaked in the audit logs
            for log in logs:
                assert "SUBJ-002" not in log.details


@pytest.mark.asyncio
async def test_safety_gateway_negative_signatures_async():
    """Verify that endpoints reject invalid, expired, or tampered signatures."""
    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        # Case 1: Invalid/garbage signature
        headers_bad_sig = {
            "X-User-Id": "safety_neg_user",
            "X-User-Roles": "sponsor_medical_monitor",
            "X-Gateway-Timestamp": str(time.time()),
            "X-Gateway-Signature": "garbage_signature_value",
            "X-Signature-Version": "2",
            "X-Change-Reason": "Legit mutation",
        }
        res_bad_sig = await client.post(
            "/api/v1/safety/reconciliation/runs",
            json={"study_id": "STUDY-NEG"},
            headers=headers_bad_sig,
        )
        assert res_bad_sig.status_code == 403
        assert "invalid gateway signature" in res_bad_sig.json()["detail"].lower()

        # Case 2: Expired gateway signature (backdated by 301 seconds)
        expired_ts = str(time.time() - 301)
        sig_expired = generate_signature(
            user_id="safety_neg_user",
            roles="sponsor_medical_monitor",
            timestamp=expired_ts,
            version="2",
            change_reason="Legit mutation",
        )
        headers_expired = {
            "X-User-Id": "safety_neg_user",
            "X-User-Roles": "sponsor_medical_monitor",
            "X-Gateway-Timestamp": expired_ts,
            "X-Gateway-Signature": sig_expired,
            "X-Signature-Version": "2",
            "X-Change-Reason": "Legit mutation",
        }
        res_expired = await client.post(
            "/api/v1/safety/reconciliation/runs",
            json={"study_id": "STUDY-NEG"},
            headers=headers_expired,
        )
        assert res_expired.status_code == 403
        assert "gateway signature expired" in res_expired.json()["detail"].lower()

        # Case 3: Tampered change reason
        signed_reason = "Original change reason"
        tampered_reason = "Modified change reason value"
        ts = str(time.time())
        sig_tampered = generate_signature(
            user_id="safety_neg_user",
            roles="sponsor_medical_monitor",
            timestamp=ts,
            version="2",
            change_reason=signed_reason,
        )
        headers_tampered = {
            "X-User-Id": "safety_neg_user",
            "X-User-Roles": "sponsor_medical_monitor",
            "X-Gateway-Timestamp": ts,
            "X-Gateway-Signature": sig_tampered,
            "X-Signature-Version": "2",
            "X-Change-Reason": tampered_reason,
        }
        res_tampered = await client.post(
            "/api/v1/safety/reconciliation/runs",
            json={"study_id": "STUDY-NEG"},
            headers=headers_tampered,
        )
        assert res_tampered.status_code == 403
        assert "invalid gateway signature" in res_tampered.json()["detail"].lower()


# Keep original roundtrip unit test for backward compatibility
def test_e2b_xml_generation_and_parser_roundtrip() -> None:
    """Validate E2BR3XMLBuilder generates valid XML that parses cleanly via E2BR3Parser.

    Requirements: PRD-SYS-001
    """
    now_iso = datetime.now(UTC).isoformat()
    original_case = SAECaseRecord(
        case_id="sae_rt_01",
        study_id="study_rt_01",
        subject_id="sub_rt_101",
        safety_report_id="US-RT-2026-0001",
        reaction_pt="Acute Kidney Injury",
        meddra_code="10000853",
        onset_date="2026-07-26",
        seriousness_criteria=SeriousnessCriteriaEnum.HOSPITALIZATION,
        causality=CausalityEnum.CERTAIN,
        expedited_reporting_required=True,
        parsed_at=now_iso,
    )

    builder = E2BR3XMLBuilder()
    generated_xml = builder.build_e2b_xml(original_case)

    assert "<?xml version=" in generated_xml
    assert "<safety_report_id>US-RT-2026-0001</safety_report_id>" in generated_xml
    assert "<reaction_pt>Acute Kidney Injury</reaction_pt>" in generated_xml

    # Parse generated XML back to case model
    parser = E2BR3Parser()
    reparsed_case = parser.parse_e2b_xml(generated_xml)

    assert reparsed_case.safety_report_id == original_case.safety_report_id
    assert reparsed_case.study_id == original_case.study_id
    assert reparsed_case.subject_id == original_case.subject_id
    assert reparsed_case.reaction_pt == original_case.reaction_pt
    assert reparsed_case.meddra_code == original_case.meddra_code
    assert reparsed_case.onset_date == original_case.onset_date
    assert reparsed_case.seriousness_criteria == original_case.seriousness_criteria
    assert reparsed_case.causality == original_case.causality
    assert reparsed_case.expedited_reporting_required is True
