"""Tests for Re-Consent Trigger Generation and Cohort Tracking."""

import time

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient

from apps.econsent.adapters.database import db_manager
from apps.econsent.adapters.models import (
    Base,
    SubjectConsent,
)
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
    user_id: str = "designer",
    roles: str = "investigator",
    change_reason: str = "Trigger Reconsent Test",
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
async def test_reconsent_trigger_and_pending_queries() -> None:
    """Test creating an amendment re-consent trigger and querying pending requirements."""
    async with db_manager.get_session_maker()() as session:
        # Seed 2 active subjects under study
        for sid in ("SUBJ-001", "SUBJ-002"):
            consent = SubjectConsent(
                subject_pseudonym=sid,
                study_id="STUDY-RECONSENT-99",
                site_id="SITE-01",
                template_id="tpl-rc-99",
                version_index=1,
                protocol_version="v1.0",
                source_content_identity="hash-v1",
                status="ACTIVE",
                signature_manifest={},
                created_by="patient",
                reason_for_change="v1 consent",
            )
            session.add(consent)
        await session.commit()

    client = TestClient(app)
    # 1. Trigger re-consent on template v2 amendment
    headers = get_auth_headers(
        change_reason="Protocol v2.0 Amendment Re-Consent Trigger"
    )
    trigger_payload = {
        "study_id": "STUDY-RECONSENT-99",
        "site_id": "SITE-01",
        "prior_version_index": 1,
        "new_version_index": 2,
        "change_summary": "Added safety alert regarding liver enzymes.",
        "substantive_changes": [
            {"clause": "Risks", "change": "LFT monitoring required"}
        ],
        "reason_for_change": "Protocol v2.0 Amendment Re-Consent Trigger",
    }
    res = client.post(
        "/api/v1/econsent/reconsent/trigger/tpl-rc-99",
        json=trigger_payload,
        headers=headers,
    )
    assert res.status_code == 201
    data = res.json()
    assert len(data) == 2
    subjs = [item["subject_pseudonym"] for item in data]
    assert "SUBJ-001" in subjs
    assert "SUBJ-002" in subjs
    assert all(item["status"] == "PENDING" for item in data)

    # 2. Query pending re-consents for study
    res_pending = client.get(
        "/api/v1/econsent/reconsent/pending/STUDY-RECONSENT-99",
        headers=headers,
    )
    assert res_pending.status_code == 200
    pending_list = res_pending.json()
    assert len(pending_list) == 2

    # 3. Query pending for individual subject
    res_subj = client.get(
        "/api/v1/econsent/reconsent/pending/STUDY-RECONSENT-99?subject_pseudonym=SUBJ-001",
        headers=headers,
    )
    assert res_subj.status_code == 200
    subj_list = res_subj.json()
    assert len(subj_list) == 1
    assert subj_list[0]["subject_pseudonym"] == "SUBJ-001"
