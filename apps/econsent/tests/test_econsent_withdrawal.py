"""Tests for Consent Revocation / Withdrawal Workflow and Status Locking."""

import time

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.econsent.adapters.database import db_manager
from apps.econsent.adapters.models import (
    Base,
    ConsentAuditLog,
    SubjectConsent,
)
from apps.econsent.domain.entities import WithdrawalScope
from apps.econsent.main import app
from packages.testing.security import generate_signature


@pytest_asyncio.fixture(autouse=True)
async def setup_test_db():
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_auth_headers(
    user_id: str = "investigator.smith",
    roles: str = "investigator",
    change_reason: str = "Formal Subject Consent Revocation",
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


@pytest.mark.asyncio
async def test_subject_consent_withdrawal_lifecycle() -> None:
    """Test withdrawing subject consent and verifying active records transition to WITHDRAWN."""
    async with db_manager.get_session_maker()() as session:
        consent = SubjectConsent(
            subject_pseudonym="SUBJ-WITHDRAW-007",
            study_id="STUDY-WITHDRAW-01",
            site_id="SITE-101",
            template_id="tpl-001",
            version_index=1,
            protocol_version="v1.0",
            source_content_identity="hash-01",
            status="ACTIVE",
            signature_manifest={},
            created_by="patient",
            reason_for_change="Initial consent",
        )
        session.add(consent)
        await session.commit()

    client = TestClient(app)
    headers = get_auth_headers()
    withdrawal_payload = {
        "study_id": "STUDY-WITHDRAW-01",
        "site_id": "SITE-101",
        "subject_pseudonym": "SUBJ-WITHDRAW-007",
        "template_id": "tpl-001",
        "reason_category": "Adverse Event",
        "reason_detail": "Subject experienced persistent grade 2 fatigue and opted to withdraw.",
        "scope": WithdrawalScope.STOP_ALL_DATA_COLLECTION,
        "investigator_id": "investigator.smith",
        "reason_for_change": "Formal Subject Consent Revocation",
    }

    # 1. Execute withdrawal endpoint
    res = client.post(
        "/api/v1/econsent/withdrawal",
        json=withdrawal_payload,
        headers=headers,
    )
    assert res.status_code == 201
    data = res.json()
    assert data["subject_pseudonym"] == "SUBJ-WITHDRAW-007"
    assert data["reason_category"] == "Adverse Event"
    assert data["scope"] == "STOP_ALL_DATA_COLLECTION"
    assert data["acknowledged_by_investigator"] is True

    # 2. Query withdrawal status
    res_get = client.get(
        "/api/v1/econsent/withdrawal/STUDY-WITHDRAW-01/SUBJ-WITHDRAW-007",
        headers=headers,
    )
    assert res_get.status_code == 200
    get_data = res_get.json()
    assert get_data["subject_pseudonym"] == "SUBJ-WITHDRAW-007"

    # 3. Verify SubjectConsent transitioned to WITHDRAWN in DB
    async with db_manager.get_session_maker()() as session:
        stmt = select(SubjectConsent).where(
            SubjectConsent.subject_pseudonym == "SUBJ-WITHDRAW-007"
        )
        res_c = await session.execute(stmt)
        c = res_c.scalars().first()
        assert c.status == "WITHDRAWN"

        # 4. Verify 21 CFR Part 11 Audit Log recorded
        stmt_a = select(ConsentAuditLog).where(
            ConsentAuditLog.action == "WITHDRAW_CONSENT"
        )
        res_a = await session.execute(stmt_a)
        audit = res_a.scalars().first()
        assert audit is not None
        assert "SUBJ-WITHDRAW-007" in audit.details
