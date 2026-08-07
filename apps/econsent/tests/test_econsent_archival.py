import time
from unittest.mock import AsyncMock, patch

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.econsent.database import db_manager
from apps.econsent.main import app, poll_and_dispatch
from apps.econsent.models import (
    Base,
    ConsentAuditLog,
    ConsentSignature,
    ConsentTemplate,
    EtmfArchivalDelivery,
)
from apps.gateway.main import generate_signature


@pytest_asyncio.fixture(autouse=True)
async def setup_econsent_db():
    """
    Setup in-memory eConsent database for unit and integration testing.
    """
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_auth_headers(
    user_id: str = "consent_test_user",
    roles: str = "investigator",
    change_reason: str = "eConsent initial creation",
) -> dict:
    """
    Helper to generate valid gateway V2 signed headers for eConsent testing.
    """
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


@pytest.mark.asyncio
async def test_icf_sign_and_archival_queueing():
    """
    Verify that signing an ICF template version successfully saves ConsentSignature
    and queues a PENDING EtmfArchivalDelivery row along with correct audit entries.
    """
    client = TestClient(app)

    # 1. Pre-create template
    async with db_manager.get_session_maker()() as session:
        template = ConsentTemplate(
            template_id="template-123",
            study_id="study-abc",
            template_name="Main ICF",
            protocol_version="1.0",
            is_published=True,
            requires_reconsent=True,
            version_index=1,
            clauses=[],
            workflow_steps=[
                {"type": "signature_placeholder", "role": "subject"},
            ],
            created_by="system",
            reason_for_change="Seed template",
        )
        session.add(template)
        await session.commit()

    headers = get_auth_headers(
        user_id="subject_pseudonym_999",
        roles="subject",
        change_reason="Subject signing ICF",
    )

    sign_payload = {
        "subject_pseudonym": "subject_pseudonym_999",
        "signature_data": "base64-signature-drawing-data",
        "reason_for_change": "I consent to clinical trial participation",
        "site_id": "site-111",
    }

    response = client.post(
        "/api/v1/econsent/templates/template-123/versions/1/sign",
        json=sign_payload,
        headers=headers,
    )
    assert response.status_code == 200
    data = response.json()
    assert data["subject_pseudonym"] == "subject_pseudonym_999"

    # 2. Assert ConsentSignature and EtmfArchivalDelivery rows are created in DB
    async with db_manager.get_session_maker()() as session:
        # Check Signature
        sig_stmt = select(ConsentSignature).where(
            ConsentSignature.template_id == "template-123"
        )
        sig_res = await session.execute(sig_stmt)
        signature = sig_res.scalars().first()
        assert signature is not None
        assert signature.subject_pseudonym == "subject_pseudonym_999"

        # Check Delivery
        del_stmt = select(EtmfArchivalDelivery).where(
            EtmfArchivalDelivery.template_id == "template-123"
        )
        del_res = await session.execute(del_stmt)
        delivery = del_res.scalars().first()
        assert delivery is not None
        assert delivery.status == "PENDING"
        assert delivery.correlation_id == "template-123:1:subject_pseudonym_999"
        assert delivery.site_id == "site-111"
        assert delivery.study_id == "study-abc"
        assert "manifest" in delivery.artifact_content

        # Check ConsentAuditLog for ARCHIVAL_QUEUED
        audit_stmt = select(ConsentAuditLog).where(
            ConsentAuditLog.action == "ARCHIVAL_QUEUED"
        )
        audit_res = await session.execute(audit_stmt)
        audit = audit_res.scalars().first()
        assert audit is not None
        assert "template-123:1:subject_pseudonym_999" in audit.details


@pytest.mark.asyncio
async def test_poll_and_dispatch_success():
    """
    Verify that poll_and_dispatch successfully forwards ICF to eTMF, updates status
    to SUCCESS, populates etmf_document_id, and registers correct audit logs.
    """
    async with db_manager.get_session_maker()() as session:
        delivery = EtmfArchivalDelivery(
            status="PENDING",
            correlation_id="temp-abc:1:subj-99",
            template_id="temp-abc",
            version_index=1,
            subject_pseudonym="subj-99",
            study_id="study-abc",
            site_id="site-111",
            artifact_content="{'some_manifest': true}",
            created_by="subj-99",
            reason_for_change="Sign consent",
        )
        session.add(delivery)
        await session.commit()
        delivery_id = delivery.id

    # Mock forward_icf_to_etmf to return a mocked document_id
    with patch(
        "apps.econsent.etmf_client.forward_icf_to_etmf", new_callable=AsyncMock
    ) as mock_forward:
        mock_forward.return_value = "etmf-doc-uuid-888"

        await poll_and_dispatch()

        mock_forward.assert_called_once()

    # Re-fetch from DB and assert state
    async with db_manager.get_session_maker()() as session:
        stmt = select(EtmfArchivalDelivery).where(
            EtmfArchivalDelivery.id == delivery_id
        )
        res = await session.execute(stmt)
        updated = res.scalars().first()
        assert updated.status == "SUCCESS"
        assert updated.etmf_document_id == "etmf-doc-uuid-888"
        assert updated.completed_at is not None

        # Verify ConsentAuditLog contains ARCHIVAL_ACCEPTED
        audit_stmt = select(ConsentAuditLog).where(
            ConsentAuditLog.action == "ARCHIVAL_ACCEPTED"
        )
        audit_res = await session.execute(audit_stmt)
        audit = audit_res.scalars().first()
        assert audit is not None
        assert "etmf-doc-uuid-888" in audit.details


@pytest.mark.asyncio
async def test_poll_and_dispatch_failure_and_retry_backoff():
    """
    Verify that poll_and_dispatch handles transient failures, schedules retry with
    exponential backoff, and flags retry_eligible=False when cap is reached.
    """
    async with db_manager.get_session_maker()() as session:
        delivery = EtmfArchivalDelivery(
            status="PENDING",
            correlation_id="temp-abc:1:subj-error",
            template_id="temp-abc",
            version_index=1,
            subject_pseudonym="subj-error",
            study_id="study-abc",
            site_id="site-111",
            artifact_content="{'some_manifest': true}",
            created_by="subj-error",
            reason_for_change="Sign consent",
        )
        session.add(delivery)
        await session.commit()
        delivery_id = delivery.id

    # 1st Attempt: forward fails with exception
    with patch(
        "apps.econsent.etmf_client.forward_icf_to_etmf",
        side_effect=Exception("eTMF service down"),
    ):
        await poll_and_dispatch()

    async with db_manager.get_session_maker()() as session:
        stmt = select(EtmfArchivalDelivery).where(
            EtmfArchivalDelivery.id == delivery_id
        )
        res = await session.execute(stmt)
        delivery_1 = res.scalars().first()
        assert delivery_1.status == "FAILED"
        assert delivery_1.attempts == 1
        assert "eTMF service down" in delivery_1.last_error
        assert delivery_1.retry_eligible is True
        assert delivery_1.next_retry_at is not None

        # Reset next_retry_at to past to simulate time passed and set attempts to 4 to reach cap
        delivery_1.next_retry_at = None
        delivery_1.attempts = 4
        await session.commit()

    # 5th Attempt: forward fails again, reaches cap of 5
    with patch(
        "apps.econsent.etmf_client.forward_icf_to_etmf",
        side_effect=Exception("Terminal down"),
    ):
        await poll_and_dispatch()

    async with db_manager.get_session_maker()() as session:
        stmt = select(EtmfArchivalDelivery).where(
            EtmfArchivalDelivery.id == delivery_id
        )
        res = await session.execute(stmt)
        delivery_5 = res.scalars().first()
        assert delivery_5.status == "FAILED"
        assert delivery_5.attempts == 5
        assert delivery_5.retry_eligible is False  # Dead-lettered!


@pytest.mark.asyncio
async def test_archival_status_endpoints():
    """
    Verify GET /api/v1/econsent/archival-status retrieves status correctly via path
    or via template_id/version_index/subject_pseudonym query parameters.
    """
    async with db_manager.get_session_maker()() as session:
        delivery = EtmfArchivalDelivery(
            status="SUCCESS",
            correlation_id="temp-abc:1:subj-query",
            template_id="temp-abc",
            version_index=1,
            subject_pseudonym="subj-query",
            study_id="study-abc",
            site_id="site-111",
            artifact_content="{'some_manifest': true}",
            etmf_document_id="etmf-uuid-111",
            created_by="subj-query",
            reason_for_change="Sign consent",
        )
        session.add(delivery)
        await session.commit()

    client = TestClient(app)
    headers = get_auth_headers()

    # 1. Fetch by path (correlation_id)
    resp1 = client.get(
        "/api/v1/econsent/archival-status/temp-abc:1:subj-query", headers=headers
    )
    assert resp1.status_code == 200
    assert resp1.json()["status"] == "SUCCESS"
    assert resp1.json()["etmf_document_id"] == "etmf-uuid-111"

    # 2. Fetch by query params
    resp2 = client.get(
        "/api/v1/econsent/archival-status?template_id=temp-abc&version_index=1&subject_pseudonym=subj-query",
        headers=headers,
    )
    assert resp2.status_code == 200
    assert resp2.json()["correlation_id"] == "temp-abc:1:subj-query"

    # 3. Bad request with missing parameters
    resp3 = client.get(
        "/api/v1/econsent/archival-status?template_id=temp-abc", headers=headers
    )
    assert resp3.status_code == 400
