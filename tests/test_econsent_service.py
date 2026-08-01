"""Unit test suite for eConsent signature processing and workflow engine.

Requirements: PRD-SYS-001 | GxP 21 CFR Part 11 Regulated
"""

import os
import time
from datetime import datetime

import httpx
import pytest
import pytest_asyncio
from httpx import ASGITransport

from apps.econsent.services.econsent_service import (
    EConsentSignRequest,
    EConsentWorkflowEngine,
    process_econsent_signature,
)
from packages.security.signing import generate_gateway_signature

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")


def get_auth_headers(
    user_id="test_dm",
    roles="Data Manager",
    change_reason="system_operation",
):
    timestamp = str(time.time())
    sig = generate_gateway_signature(
        user_id=user_id or "",
        roles=roles or "",
        timestamp=timestamp,
        secret=GATEWAY_SECRET.encode(),
        change_reason=change_reason,
    )
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }


@pytest_asyncio.fixture(autouse=True)
async def setup_execution_db(monkeypatch):
    """Setup in-memory SQLite database containing Execution base tables for eConsent testing."""
    from apps.execution.database.core import db_manager as exec_db_manager
    from apps.execution.database.models import Base as ExecBase
    from apps.execution.main import app as execution_app

    exec_db_manager.init_db(
        "sqlite+aiosqlite:///:memory:",
        echo=False,
        execution_options={"schema_translate_map": {"audit_schema": None}},
    )
    async with exec_db_manager.engine.begin() as conn:
        from sqlalchemy import text

        await conn.execute(text("ATTACH DATABASE ':memory:' AS audit_schema;"))
        await conn.run_sync(ExecBase.metadata.create_all)

    # Monkeypatch AsyncClient to route requests to execution_app in-memory
    original_client_init = httpx.AsyncClient.__init__

    def mocked_client_init(self, *args, **kwargs):
        kwargs["transport"] = ASGITransport(app=execution_app)
        kwargs["base_url"] = "http://test"
        original_client_init(self, *args, **kwargs)

    monkeypatch.setattr(httpx.AsyncClient, "__init__", mocked_client_init)

    yield

    async with exec_db_manager.engine.begin() as conn:
        await conn.run_sync(ExecBase.metadata.drop_all)
    await exec_db_manager.close()


@pytest.mark.asyncio
async def test_successful_signature_capture():
    """Verify that a valid signature capture creates a signed PDF and updates DB state to SIGNED.

    Requirements: PRD-SYS-001
    """
    # 1. Seed a passing comprehension quiz result via API
    async with httpx.AsyncClient() as client:
        quiz_res = await client.post(
            "/api/v1/execution/signatures/quiz-result",
            headers=get_auth_headers(),
            json={
                "subject_id": "SUBJ-001",
                "icf_version_id": "ICF-V1.0",
                "score": 95.0,
                "passed": True,
            },
        )
        assert quiz_res.status_code == 201

    # 2. Prepare valid capture signature request
    payload = EConsentSignRequest(
        subject_id="SUBJ-001",
        icf_version_id="ICF-V1.0",
        printed_name="John Doe",
        relationship_to_subject="SELF",
        signature_svg="<svg><path d='M 10 10 L 20 20'/></svg>",
        otp_auth_code="123456",
        reason_for_change="I consent to join this trial.",
    )

    # 3. Process the signature
    from packages.security.audit_logger import audit_logger_engine

    audit_logger_engine._chain.clear()

    response = await process_econsent_signature(None, payload)

    assert response.consent_record_id is not None
    assert response.signed_pdf_url.startswith("file:///tmp/consent_pdfs/")
    assert isinstance(response.signature_timestamp_utc, datetime)
    assert len(response.verification_hash) == 64

    # 4. Verify DB updates via API
    async with httpx.AsyncClient() as client:
        forms_res = await client.get(
            "/api/v1/execution/signatures/form-records", headers=get_auth_headers()
        )
        assert forms_res.status_code == 200
        forms = forms_res.json()
        assert len(forms) > 0
        record = [f for f in forms if f["id"] == response.consent_record_id][0]
        assert record["status"] == "SIGNED"
        assert record["is_verified"] is True
        assert record["printed_name"] == "John Doe"

        sigs_res = await client.get(
            "/api/v1/execution/signatures/consent-signatures",
            headers=get_auth_headers(),
        )
        assert sigs_res.status_code == 200
        sigs = sigs_res.json()
        assert len(sigs) > 0
        sig = [s for s in sigs if s["subject_id"] == "SUBJ-001"][0]
        assert sig["status"] == "SIGNED"
        assert sig["verification_hash"] == response.verification_hash

    # 5. Verify central audit logger events are recorded correctly
    assert len(audit_logger_engine._chain) == 1
    audit_log = audit_logger_engine._chain[0]
    assert audit_log.service_name == "econsent"
    assert audit_log.action_type == "SIGN"
    assert audit_log.entity_name == "ConsentFormRecord"
    assert audit_log.entity_id == response.consent_record_id
    assert audit_log.reason_for_change == "I consent to join this trial."
    assert audit_log.details["event_type"] == "ECONSENT_SIGNED"
    assert audit_log.details["subject_id"] == "SUBJ-001"
    assert audit_log.details["icf_version_id"] == "ICF-V1.0"


@pytest.mark.asyncio
async def test_failed_comprehension_quiz_blocks_signature():
    """Verify that a failed comprehension quiz blocks signature submission.

    Requirements: PRD-SYS-001
    """
    # Seed a failed comprehension quiz (score < 80%) via API
    async with httpx.AsyncClient() as client:
        quiz_res = await client.post(
            "/api/v1/execution/signatures/quiz-result",
            headers=get_auth_headers(),
            json={
                "subject_id": "SUBJ-001",
                "icf_version_id": "ICF-V1.0",
                "score": 75.0,
                "passed": True,
            },
        )
        assert quiz_res.status_code == 201

    payload = EConsentSignRequest(
        subject_id="SUBJ-001",
        icf_version_id="ICF-V1.0",
        printed_name="John Doe",
        relationship_to_subject="SELF",
        signature_svg="<svg></svg>",
        otp_auth_code="123456",
        reason_for_change="I consent.",
    )

    with pytest.raises(
        ValueError, match="Comprehension quiz not passed with required score >= 80%"
    ):
        await process_econsent_signature(None, payload)


@pytest.mark.asyncio
async def test_incomplete_comprehension_quiz_blocks_signature():
    """Verify that an incomplete/missing comprehension quiz blocks signature submission.

    Requirements: PRD-SYS-001
    """
    # No quiz seeded in DB at all!
    payload = EConsentSignRequest(
        subject_id="SUBJ-001",
        icf_version_id="ICF-V1.0",
        printed_name="John Doe",
        relationship_to_subject="SELF",
        signature_svg="<svg></svg>",
        otp_auth_code="123456",
        reason_for_change="I consent.",
    )

    with pytest.raises(
        ValueError, match="Comprehension quiz not passed with required score >= 80%"
    ):
        await process_econsent_signature(None, payload)


@pytest.mark.asyncio
async def test_invalid_otp_auth_code_blocks_signature():
    """Verify that an invalid OTP authentication code blocks signature submission.

    Requirements: PRD-SYS-001
    """
    # Seed a passing comprehension quiz result via API
    async with httpx.AsyncClient() as client:
        quiz_res = await client.post(
            "/api/v1/execution/signatures/quiz-result",
            headers=get_auth_headers(),
            json={
                "subject_id": "SUBJ-001",
                "icf_version_id": "ICF-V1.0",
                "score": 100.0,
                "passed": True,
            },
        )
        assert quiz_res.status_code == 201

    payload = EConsentSignRequest(
        subject_id="SUBJ-001",
        icf_version_id="ICF-V1.0",
        printed_name="John Doe",
        relationship_to_subject="SELF",
        signature_svg="<svg></svg>",
        otp_auth_code="wrong_code",  # invalid
        reason_for_change="I consent.",
    )

    with pytest.raises(ValueError, match="Invalid OTP authentication code"):
        await process_econsent_signature(None, payload)


@pytest.mark.asyncio
async def test_workflow_engine_legacy_signature_capture():
    """Verify that EConsentWorkflowEngine successfully executes raw signature capture.

    Requirements: PRD-SYS-001
    """
    engine = EConsentWorkflowEngine(None)
    signature = await engine.execute_signature_capture(
        subject_id="SUBJ-002",
        icf_version_id="ICF-V2.0",
        printed_name="Jane Smith",
        signature_svg="<svg><path d='M0 0 L10 10'/></svg>",
        reason_for_change="Captured via tablet UI",
    )

    assert signature.id is not None
    assert signature.subject_id == "SUBJ-002"
    assert signature.icf_version_id == "ICF-V2.0"
    assert signature.printed_name == "Jane Smith"
    assert signature.status == "SIGNED"
    assert len(signature.verification_hash) == 64

    # Verify database entry via API
    async with httpx.AsyncClient() as client:
        sigs_res = await client.get(
            "/api/v1/execution/signatures/consent-signatures",
            headers=get_auth_headers(),
        )
        assert sigs_res.status_code == 200
        sigs = sigs_res.json()
        assert len(sigs) > 0
        db_sig = [s for s in sigs if s["subject_id"] == "SUBJ-002"][0]
        assert db_sig["id"] == signature.id
