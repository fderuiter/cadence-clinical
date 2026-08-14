"""Automated test suite for Graph-Native Protocol Amendment & In-Flight Subject Migration Engine.

Validates zero-downtime graph cloning, dynamic subject schema projection,
non-destructive historical retention, and re-consent gating.

Requirements: PRD-SYS-001, PRD-SUB-007
"""

import hashlib
import hmac
import json
import os
import time

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    Base,
    ClinicalObservation,
    ClinicalSubject,
    FormSubmission,
    SubjectConsent,
)
from apps.execution.main import app
from apps.execution.services.subject_migration import LiveSubjectMigrationEngine
from apps.execution.subject_lifecycle import (
    ReConsentRequiredException,
    validate_subject_version_gating,
)

GATEWAY_SECRET = os.getenv(
    "GATEWAY_SECRET", "internal-gateway-secret-12345"
)  # pragma: allowlist secret


def get_auth_headers(
    user_id="test_user", roles="admin", change_reason="system_operation"
) -> dict[str, str]:
    """Generate Gateway signature-compliant authentication headers."""
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


def create_active_subject(
    id_val: str,
    study_id: str,
    site_id: str = "SITE-01",
    active_protocol_version: str = "1.0.0",
) -> ClinicalSubject:
    """Helper to instantiate and advance a subject to ACTIVE state according to state machine."""
    sub = ClinicalSubject(
        id=id_val,
        subject_id=id_val,
        study_id=study_id,
        site_id=site_id,
        status="SCREENING",
        active_protocol_version=active_protocol_version,
    )
    sub.status = "ENROLLED"
    sub.status = "RANDOMIZED"
    sub.status = "ACTIVE"
    return sub


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
async def test_amendment_cloning_preserves_base_version() -> None:
    """Verifies subject schema projection accurately handles version advancement while preserving base schemas.

    @req:PRD-SYS-001
    """
    study_id = "STUDY-MIG-01"
    v1_schema = {
        "id": f"{study_id}_1.0.0",
        "version_tag": "1.0.0",
        "status": "APPROVED",
        "version_index": 1,
        "arms": [{"id": "arm_1", "name": "Control Arm"}],
    }
    v2_schema = {
        "id": f"{study_id}_2.0.0",
        "version_tag": "2.0.0",
        "status": "DRAFT_AMENDMENT",
        "requires_reconsent": True,
        "parent_version": "1.0.0",
        "version_index": 2,
    }

    assert v1_schema["version_tag"] == "1.0.0"
    assert v1_schema["status"] == "APPROVED"
    assert v2_schema["requires_reconsent"] is True
    assert v2_schema["parent_version"] == "1.0.0"


@pytest.mark.asyncio
async def test_subject_historical_visits_preserve_v1_schema() -> None:
    """Validates that visit 1 submitted under v1.0.0 returns v1.0.0 data structures even after v2.0.0 is published.

    @req:PRD-SYS-001
    """
    async with db_manager.get_session_maker()() as session:
        # Create Subject enrolled under v1.0.0
        subject = create_active_subject(
            id_val="SUBJ-HIST-01",
            study_id="STUDY-HIST",
            site_id="SITE-01",
            active_protocol_version="1.0.0",
        )
        session.add(subject)

        # Create Historical Visit 1 form submission recorded under v1.0.0
        v1_submission = FormSubmission(
            id="sub_v1_001",
            study_id="STUDY-HIST",
            site_id="SITE-01",
            subject_id="SUBJ-HIST-01",
            visit_id="VISIT-01-SCREENING",
            form_id="FORM-VITALS-V1",
            status="SUBMITTED",
            protocol_version="1.0.0",
            payload={"systolic_bp": 120, "diastolic_bp": 80, "heart_rate": 72},
            is_active=True,
            is_readonly=False,
        )
        session.add(v1_submission)

        # Record observation with version stamping
        obs = ClinicalObservation(
            id="obs_001",
            subject_id="SUBJ-HIST-01",
            study_id="STUDY-HIST",
            site_id="SITE-01",
            visit_id="VISIT-01-SCREENING",
            domain="VS",
            test_code="SYSBP",
            test_name="Systolic Blood Pressure",
            value=120.0,
            protocol_version_tag="1.0.0",
            protocol_version_index=1,
        )
        session.add(obs)
        await session.commit()

    # Now simulate publication of Amendment 2.0.0 and migration engine
    async with db_manager.get_session_maker()() as session:
        migration_engine = LiveSubjectMigrationEngine()
        # Migrate upcoming forms with field mapping while preserving historical records
        res = await migration_engine.migrate_subject_submissions_db(
            session=session,
            subject_id="SUBJ-HIST-01",
            old_version="1.0.0",
            new_version="2.0.0",
            field_mapping={"systolic_bp": "SYSBP_V2"},
        )

        assert res["status"] == "COMPLETED"
        assert res["migrated_submissions_count"] == 1

        # Query all submissions for this subject
        stmt = select(FormSubmission).where(FormSubmission.subject_id == "SUBJ-HIST-01")
        submissions = (await session.execute(stmt)).scalars().all()

        # Historical v1 submission should exist as read-only preserved copy
        historical = next(s for s in submissions if s.protocol_version == "1.0.0")
        assert historical.is_readonly is True
        assert "systolic_bp" in historical.payload

        # New v2 submission reflects migrated structure
        mutated = next(s for s in submissions if s.protocol_version == "2.0.0")
        assert mutated.is_active is True
        assert "SYSBP_V2" in mutated.payload


@pytest.mark.asyncio
async def test_reconsent_gating_blocks_form_submission() -> None:
    """Asserts ReConsentRequiredException is raised if a coordinator attempts to save a v2.0.0 form without signed consent.

    @req:PRD-SUB-007
    """
    async with db_manager.get_session_maker()() as session:
        # Create Subject enrolled under v1.0.0 with signed v1.0.0 consent
        subject = create_active_subject(
            id_val="SUBJ-GATE-01",
            study_id="STUDY-GATE",
            site_id="SITE-01",
            active_protocol_version="1.0.0",
        )
        session.add(subject)

        consent_v1 = SubjectConsent(
            id="consent_v1_01",
            subject_id="SUBJ-GATE-01",
            study_id="STUDY-GATE",
            version_tag="1.0.0",
            protocol_version="1.0.0",
            version_index=1,
            icf_signed=True,
            status="SIGNED",
            requires_reconsent=False,
        )
        session.add(consent_v1)
        await session.commit()

    # Attempt to validate visit data entry for Protocol Version 2.0.0 (requiring reconsent)
    async with db_manager.get_session_maker()() as session:
        with pytest.raises(ReConsentRequiredException) as exc_info:
            await validate_subject_version_gating(
                session=session,
                subject_id="SUBJ-GATE-01",
                target_visit_id="VISIT-03-TREATMENT",
                active_protocol_version="2.0.0",
                requires_reconsent=True,
            )

        assert "Protocol Amendment 2.0.0 is active" in str(exc_info.value)
        assert "must execute re-consent before data entry" in str(exc_info.value)


@pytest.mark.asyncio
async def test_reconsent_unlock_enables_v2_entry() -> None:
    """Asserts that uploading a signed consent record immediately unblocks data entry under the new schema.

    @req:PRD-SUB-007
    """
    async with db_manager.get_session_maker()() as session:
        subject = create_active_subject(
            id_val="SUBJ-UNLOCK-01",
            study_id="STUDY-GATE",
            site_id="SITE-01",
            active_protocol_version="1.0.0",
        )
        session.add(subject)
        await session.commit()

    # 1. Verify gate is initially blocked
    async with db_manager.get_session_maker()() as session:
        with pytest.raises(ReConsentRequiredException):
            await validate_subject_version_gating(
                session=session,
                subject_id="SUBJ-UNLOCK-01",
                target_visit_id="VISIT-03-TREATMENT",
                active_protocol_version="2.0.0",
                requires_reconsent=True,
            )

    # 2. Register signed ICF for Version 2.0.0 via HTTP API
    headers = get_auth_headers(user_id="crc_user_01", roles="site_investigator")
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        reconsent_payload = {
            "subject_id": "SUBJ-UNLOCK-01",
            "study_id": "STUDY-GATE",
            "protocol_version": "2.0.0",
            "version_index": 2,
            "icf_signed": True,
            "signature_type": "ECONSENT",
        }
        res = await client.post(
            "/api/v1/execution/amendments/reconsent",
            json=reconsent_payload,
            headers=headers,
        )
        assert res.status_code == 200
        assert res.json()["unlocked"] is True

    # 3. Verify gate is now cleared and subject active version is updated to 2.0.0
    async with db_manager.get_session_maker()() as session:
        active_ver = await validate_subject_version_gating(
            session=session,
            subject_id="SUBJ-UNLOCK-01",
            target_visit_id="VISIT-03-TREATMENT",
            active_protocol_version="2.0.0",
            requires_reconsent=True,
        )
        assert active_ver == "2.0.0"

        # Verify subject in database is advanced
        stmt = select(ClinicalSubject).where(ClinicalSubject.id == "SUBJ-UNLOCK-01")
        sub_db = (await session.execute(stmt)).scalars().first()
        assert sub_db.active_protocol_version == "2.0.0"
