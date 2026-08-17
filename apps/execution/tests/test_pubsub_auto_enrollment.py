"""Test suite for asynchronous eConsent Pub/Sub auto-enrollment, compliance milestone validation, paper override, and write guards.

Enforces GxP requirements for:
- Auto-creation of screening subjects on digital signature completion (PRD-SUB-007)
- Site activation compliance checks before enrollment transitions (PRD-SUB-007, PRD-SYS-001)
- Paper override consent to bypass electronic consent gates (PRD-SUB-007)
- Centralized write gates blocking data entry without active consent (PRD-SUB-007)
"""

import hashlib
import hmac
import json
import os
import time
from datetime import UTC, datetime
from unittest.mock import patch

import httpx
import pytest
import pytest_asyncio
from fastapi import HTTPException
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    AuditLog,
    Base,
    ClinicalSubject,
    SubjectConsent,
)
from apps.execution.database.models.compliance import SiteComplianceCache
from apps.execution.main import app
from apps.execution.workers.consent_subscriber import handle_consent_completed_message

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")


def get_auth_headers(
    user_id: str = "test_user",
    roles: str = "admin",
    change_reason: str = "system_operation",
) -> dict[str, str]:
    """Generate Gateway signature-compliant authentication headers.

    Args:
        user_id: User identifier.
        roles: Enrolled roles for the user.
        change_reason: Reason for the database change.

    Returns:
        dict[str, str]: Authentication and Gateway signature headers.
    """
    timestamp = str(time.time())
    payload = {
        "change_reason": change_reason,
        "roles": roles,
        "timestamp": timestamp,
        "user_id": user_id,
    }
    serialized = json.dumps(payload, sort_keys=True, separators=(",", ":"))
    signature = hmac.new(
        GATEWAY_SECRET.encode(), serialized.encode(), hashlib.sha256
    ).hexdigest()
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }


@pytest_asyncio.fixture(autouse=True)
async def setup_test_db():
    """Setup in-memory SQLite database before each test and clear down after."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:")
    async with db_manager.engine.begin() as conn:
        from sqlalchemy import text

        if db_manager.engine.dialect.name == "postgresql":
            await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_pubsub_auto_enrollment_screening() -> None:
    """Verify background consent-completed event automatically initializes screening subject and consent cache.

    @req:PRD-SUB-007
    """
    # 1. Trigger eConsent completed event processing
    event_data = {
        "action": "consent_completed",
        "subject_id": "SUBJ-AUTO-01",
        "study_id": "STUDY-AUTO",
        "site_id": "SITE-AUTO-01",
        "version_tag": "1.0",
        "version_index": 1,
    }

    # Dispatch to background event handler directly
    await handle_consent_completed_message(event_data, db_manager.get_session_maker())

    # 2. Verify subject was initialized in screening state
    async with db_manager.get_session_maker()() as session:
        stmt_subj = select(ClinicalSubject).where(
            ClinicalSubject.subject_id == "SUBJ-AUTO-01"
        )
        subj = (await session.execute(stmt_subj)).scalars().first()

        assert subj is not None
        assert subj.status == "SCREENING"
        assert subj.study_id == "STUDY-AUTO"
        assert subj.site_id == "SITE-AUTO-01"
        assert subj.enrollment_index == 0

        # Verify audit logs
        stmt_audit = select(AuditLog).where(
            AuditLog.table_name == "clinical_subjects",
            AuditLog.record_id == subj.id,
            AuditLog.action == "INSERT",
        )
        audit_subj = (await session.execute(stmt_audit)).scalars().first()
        assert audit_subj is not None

        # Verify cached SubjectConsent was created
        stmt_consent = select(SubjectConsent).where(
            SubjectConsent.subject_id == "SUBJ-AUTO-01"
        )
        consent = (await session.execute(stmt_consent)).scalars().first()
        assert consent is not None
        assert consent.icf_signed is True
        assert consent.is_paper_override is False
        assert consent.requires_reconsent is False


@pytest.mark.asyncio
async def test_site_activation_compliance_validation() -> None:
    """Verify that promoting subject to ENROLLED state fails if site activation milestone is missing in compliance cache.

    @req:PRD-SUB-007
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Initialize subject
        subject_payload = {
            "subject_id": "SUBJ-GATED",
            "study_id": "STUDY-GATED",
            "demographics": {
                "name": "Alex Hunter",
                "birthdate": "1988-08-08",
                "gender": "M",
                "race": "Asian",
            },
        }
        res_subj = await client.post(
            "/api/v1/execution/subjects",
            json=subject_payload,
            headers=get_auth_headers(),
        )
        assert res_subj.status_code == 200

        # Update subject's site_id directly in DB
        async with db_manager.get_session_maker()() as session:
            async with session.begin():
                stmt = select(ClinicalSubject).where(
                    ClinicalSubject.subject_id == "SUBJ-GATED"
                )
                db_subj = (await session.execute(stmt)).scalars().first()
                assert db_subj is not None
                db_subj.site_id = "SITE-COMPLIANT-01"
                session.add(db_subj)

        # Ensure Subject has signed consent cached locally so we bypass Write Guard to state endpoint
        async with db_manager.get_session_maker()() as session:
            consent_db = SubjectConsent(
                subject_id="SUBJ-GATED",
                study_id="STUDY-GATED",
                version_tag="1.0",
                version_index=1,
                icf_signed=True,
                icf_signed_date=datetime.now(UTC).replace(tzinfo=None),
                requires_reconsent=False,
            )
            session.add(consent_db)
            await session.commit()

        # 2. Attempt patching status to ENROLLED -> Must fail because site activation compliance is missing
        state_payload = {"status": "ENROLLED"}
        res_transition = await client.patch(
            "/api/v1/execution/subjects/SUBJ-GATED/state",
            json=state_payload,
            headers=get_auth_headers(),
        )
        assert res_transition.status_code == 400
        assert "is not compliant" in res_transition.json()["detail"]

        # 3. Verify security audit log record was written
        async with db_manager.get_session_maker()() as session:
            stmt_audit = select(AuditLog).where(
                AuditLog.table_name == "clinical_subjects",
                AuditLog.action == "BLOCKED_ENROLLMENT",
            )
            blocked_log = (await session.execute(stmt_audit)).scalars().first()
            assert blocked_log is not None
            assert (
                "Blocked enrollment of subject SUBJ-GATED" in blocked_log.change_reason
            )

        # 4. Supply compliance milestone to compliance cache
        async with db_manager.get_session_maker()() as session:
            milestone = SiteComplianceCache(
                study_id="STUDY-GATED",
                site_id="SITE-COMPLIANT-01",
                milestone="SITE_ACTIVATION",
                is_complete=True,
            )
            session.add(milestone)
            await session.commit()

        # 5. Re-attempt transition -> Should succeed now
        res_transition_ok = await client.patch(
            "/api/v1/execution/subjects/SUBJ-GATED/state",
            json=state_payload,
            headers=get_auth_headers(),
        )
        assert res_transition_ok.status_code == 200

        # Check subject status in DB
        async with db_manager.get_session_maker()() as session:
            stmt_subj = select(ClinicalSubject).where(
                ClinicalSubject.subject_id == "SUBJ-GATED"
            )
            db_subj = (await session.execute(stmt_subj)).scalars().first()
            assert db_subj.status == "ENROLLED"


@pytest.mark.asyncio
async def test_manual_physical_paper_consent_override() -> None:
    """Verify site coordinators can bypass digital consent and unblock writes using manual physical paper override.

    @req:PRD-SUB-007
    """

    async def mock_fetch_not_found(subject_pseudonym, study_id=None):
        raise HTTPException(status_code=404, detail="No signed consent found")

    with patch(
        "apps.execution.econsent_client.fetch_subject_consent_status",
        side_effect=mock_fetch_not_found,
    ):
        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app), base_url="http://test"
        ) as client:
            # 1. Create subject
            subject_payload = {
                "subject_id": "SUBJ-PAPER",
                "study_id": "STUDY-PAPER",
                "demographics": {
                    "name": "Jamie Lee",
                    "birthdate": "1994-04-14",
                    "gender": "F",
                    "race": "White",
                },
            }
            res_subj = await client.post(
                "/api/v1/execution/subjects",
                json=subject_payload,
                headers=get_auth_headers(),
            )
            assert res_subj.status_code == 200

            # Attempt creating a visit -> Must fail initially because no active consent/override exists
            visit_payload = {
                "subject_id": "SUBJ-PAPER",
                "visit_name": "Week 1",
                "study_id": "STUDY-PAPER",
            }
            with pytest.raises(PermissionError) as exc_info:
                await client.post(
                    "/api/v1/execution/visits",
                    json=visit_payload,
                    headers=get_auth_headers(),
                )
            assert "Re-Consent Required - Demographics & Visit Forms Locked" in str(
                exc_info.value
            )

            # 2. Record manual physical paper consent override
            consent_payload = {
                "protocol_version": {
                    "study_id": "STUDY-PAPER",
                    "version_tag": "1.0",
                    "version_index": 1,
                    "status": "PUBLISHED",
                },
                "icf_signed": True,
                "requires_reconsent": False,
                "is_paper_override": True,
            }
            res_consent = await client.post(
                "/api/v1/execution/subjects/SUBJ-PAPER/consent",
                json=consent_payload,
                headers=get_auth_headers(),
            )
            assert res_consent.status_code == 200
            assert res_consent.json()["is_paper_override"] is True

            # 3. Try creating a visit again -> Must succeed now because physical paper override unblocked execution writes
            res_visit = await client.post(
                "/api/v1/execution/visits",
                json=visit_payload,
                headers=get_auth_headers(),
            )
            assert res_visit.status_code == 200
            assert res_visit.json()["visit_name"] == "Week 1"
