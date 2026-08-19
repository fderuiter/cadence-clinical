"""Web contract and integration test suite for Dynamic Subject Enrollment & eCRF Visit Execution with Edit Checks.

Validates the end-to-end API communication lifecycle consumed by apps/web/src/views/EcrfView.vue
and apps/web/src/components/persona/CrcFormRenderer.vue.
Requirements: PRD-SYS-001, PRD-SUB-007, PRD-EDC-005
"""

import hashlib
import hmac
import json
import time
from collections.abc import AsyncGenerator
from unittest.mock import patch

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    AuditLog,
    Base,
    ClinicalObservation,
    ClinicalSubject,
)
from apps.execution.main import app as execution_app

GATEWAY_SECRET = "internal-gateway-secret-12345"  # pragma: allowlist secret


@pytest_asyncio.fixture(autouse=True)
async def setup_test_db() -> AsyncGenerator[None]:
    """Setup in-memory SQLite database before each test."""
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


def get_auth_headers(
    user_id: str = "crc_site101",
    roles: str = "site_crc,site_investigator",
    change_reason: str = "Subject enrollment & eCRF execution",
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


@pytest.mark.asyncio
async def test_subject_enrollment_modal_and_consent_assignment() -> None:
    """Validate dynamic subject enrollment assigning subject ID, site ID, consent date, and arm.

    @req:PRD-SYS-001, PRD-SUB-007
    """
    study_id = "CADENCE-101"
    subject_id = "SUBJ-101-011"
    site_id = "SITE-101"
    arm_id = "ARM-A"

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=execution_app), base_url="http://test"
    ) as client:
        # 1. Enroll new subject via POST /api/v1/execution/subjects
        enroll_payload = {
            "subject_id": subject_id,
            "study_id": study_id,
            "site_id": site_id,
            "arm_id": arm_id,
            "demographics": {
                "name": "Jane Doe",
                "birthdate": "1985-06-15",
                "gender": "F",
                "race": "Asian",
            },
        }
        res_enroll = await client.post(
            "/api/v1/execution/subjects",
            json=enroll_payload,
            headers=get_auth_headers(
                user_id="crc_site101",
                change_reason=f"Enroll new subject {subject_id} at {site_id} in {arm_id}",
            ),
        )
        assert res_enroll.status_code == 200
        enroll_data = res_enroll.json()
        assert enroll_data["subject_id"] == subject_id
        assert enroll_data["study_id"] == study_id
        assert enroll_data.get("site_id") == site_id
        assert enroll_data.get("treatment_group") == arm_id

        # 2. Record initial Protocol Version 1.0 consent
        consent_payload = {
            "protocol_version": {
                "study_id": study_id,
                "version_tag": "1.0.0",
                "version_index": 1,
                "status": "PUBLISHED",
            },
            "icf_signed": True,
            "requires_reconsent": False,
        }
        res_consent = await client.post(
            f"/api/v1/execution/subjects/{subject_id}/consent",
            json=consent_payload,
            headers=get_auth_headers(
                user_id="crc_site101",
                change_reason="Record baseline ICF v1.0.0 signature",
            ),
        )
        assert res_consent.status_code == 200
        consent_data = res_consent.json()
        assert consent_data["subject_id"] == subject_id
        assert consent_data["version_tag"] == "1.0.0"
        assert consent_data["icf_signed"] is True
        assert consent_data["requires_reconsent"] is False

        # 3. Verify subject record in database
        async with db_manager.get_session_maker()() as session:
            stmt = select(ClinicalSubject).where(
                ClinicalSubject.subject_id == subject_id
            )
            res = await session.execute(stmt)
            subj_db = res.scalars().first()
            assert subj_db is not None
            assert subj_db.site_id == site_id
            assert subj_db.treatment_group == arm_id


@pytest.mark.asyncio
async def test_form_submission_persists_observations_with_part11_audit_fields() -> None:
    """Validate saving eCRF form submissions persists observations to PostgreSQL with 21 CFR Part 11 audit fields.

    @req:PRD-SYS-001, PRD-EDC-005
    """
    study_id = "CADENCE-101"
    subject_id = "SUBJ-101-011"
    site_id = "SITE-101"
    form_id = "VS_DEMO"

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=execution_app), base_url="http://test"
    ) as client:
        # Setup subject & consent
        await client.post(
            "/api/v1/execution/subjects",
            json={"subject_id": subject_id, "study_id": study_id, "site_id": site_id},
            headers=get_auth_headers(),
        )
        await client.post(
            f"/api/v1/execution/subjects/{subject_id}/consent",
            json={
                "protocol_version": {
                    "study_id": study_id,
                    "version_tag": "1.0.0",
                    "version_index": 1,
                },
                "icf_signed": True,
                "requires_reconsent": False,
            },
            headers=get_auth_headers(),
        )

        # Create Visit
        v_res = await client.post(
            "/api/v1/execution/visits",
            json={
                "subject_id": subject_id,
                "visit_name": "Screening",
                "study_id": study_id,
            },
            headers=get_auth_headers(),
        )
        assert v_res.status_code == 200
        visit_db_id = v_res.json()["id"]

        # Submit eCRF form submission with observation payload
        form_payload = {
            "study_id": study_id,
            "site_id": site_id,
            "subject_id": subject_id,
            "visit_id": visit_db_id,
            "form_id": form_id,
            "protocol_version": "1.0.0",
            "payload": {
                "vssbp": 120.0,
                "vsdpb": 80.0,
                "pulse": 72.0,
                "weight": 70.0,
                "height": 1.75,
                "wbc": 6.5,
            },
        }
        res_sub = await client.post(
            "/api/v1/execution/form-submissions",
            json=form_payload,
            headers=get_auth_headers(
                user_id="crc_site101",
                change_reason="Submit Screening Visit 1 vital signs & labs",
            ),
        )
        assert res_sub.status_code == 201
        sub_data = res_sub.json()
        assert sub_data["status"] == "DRAFT"
        assert sub_data["subject_id"] == subject_id

        # Verify ClinicalObservation records persisted in database
        async with db_manager.get_session_maker()() as session:
            stmt_obs = select(ClinicalObservation).where(
                ClinicalObservation.subject_id == subject_id,
                ClinicalObservation.visit_id == visit_db_id,
            )
            res_obs = await session.execute(stmt_obs)
            observations = res_obs.scalars().all()

            # Ensure all vital signs and labs were converted and persisted
            test_codes = {o.test_code for o in observations}
            assert "VSSBP" in test_codes
            assert "VSDPB" in test_codes
            assert "VSHR" in test_codes
            assert "WT" in test_codes
            assert "HT" in test_codes
            assert "WBC" in test_codes

            # Verify 21 CFR Part 11 audit trail in audit_logs
            stmt_audit = (
                select(AuditLog)
                .where(
                    AuditLog.table_name.in_(
                        ["clinical_observations", "form_submissions"]
                    ),
                    AuditLog.user_id == "crc_site101",
                )
                .order_by(AuditLog.timestamp.asc())
            )
            res_audit = await session.execute(stmt_audit)
            audit_records = res_audit.scalars().all()
            assert len(audit_records) >= 1
            for record in audit_records:
                assert record.user_id == "crc_site101"
                assert (
                    record.change_reason
                    == "Submit Screening Visit 1 vital signs & labs"
                )
                assert record.timestamp is not None


@pytest.mark.asyncio
async def test_reconsent_gating_blocks_data_entry_and_unlocks_on_reconsent() -> None:
    """Validate that attempting to record observations for a subject with pending amendment re-consent triggers explicit Re-Consent Gate blocking.

    @req:PRD-SYS-001, PRD-SUB-007
    """
    study_id = "CADENCE-101"
    subject_id = "SUBJ-101-012"
    site_id = "SITE-101"

    mock_response = {
        "subject_pseudonym": subject_id,
        "study_id": study_id,
        "version_index": 1,
        "protocol_version": "1.0",
        "signed": True,
        "requires_reconsent": True,
    }

    async def mock_fetch(subject_pseudonym: str, study_id: str | None = None) -> dict:
        return mock_response

    with patch(
        "apps.execution.econsent_client.fetch_subject_consent_status",
        side_effect=mock_fetch,
    ):
        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=execution_app), base_url="http://test"
        ) as client:
            # 1. Setup subject
            await client.post(
                "/api/v1/execution/subjects",
                json={
                    "subject_id": subject_id,
                    "study_id": study_id,
                    "site_id": site_id,
                },
                headers=get_auth_headers(),
            )

            # 2. Record consent that requires re-consent under Protocol Version 2.0.0
            await client.post(
                f"/api/v1/execution/subjects/{subject_id}/consent",
                json={
                    "protocol_version": {
                        "study_id": study_id,
                        "version_tag": "1.0.0",
                        "version_index": 1,
                    },
                    "icf_signed": True,
                    "requires_reconsent": True,  # Amendment published requiring re-consent
                },
                headers=get_auth_headers(),
            )

            # 3. Attempting to record observations or form submissions MUST raise PermissionError / Re-Consent Gate
            with pytest.raises(PermissionError) as exc_info:
                await client.post(
                    "/api/v1/execution/form-submissions",
                    json={
                        "study_id": study_id,
                        "site_id": site_id,
                        "subject_id": subject_id,
                        "visit_id": "VISIT-002",
                        "form_id": "VS_DEMO",
                        "payload": {"vssbp": 125.0},
                    },
                    headers=get_auth_headers(),
                )
            assert "Re-Consent Required - Demographics & Visit Forms Locked" in str(
                exc_info.value
            )

            # 4. Clear the gate by recording signed ICF v2.0.0 (requires_reconsent=False)
            mock_response = {
                "subject_pseudonym": subject_id,
                "study_id": study_id,
                "version_index": 2,
                "protocol_version": "2.0",
                "signed": True,
                "requires_reconsent": False,
            }
            res_reconsent = await client.post(
                f"/api/v1/execution/subjects/{subject_id}/consent",
                json={
                    "protocol_version": {
                        "study_id": study_id,
                        "version_tag": "2.0.0",
                        "version_index": 2,
                        "status": "PUBLISHED",
                    },
                    "icf_signed": True,
                    "requires_reconsent": False,
                },
                headers=get_auth_headers(
                    user_id="crc_site101",
                    change_reason="Subject completed Re-Consent for Protocol v2.0.0",
                ),
            )
            assert res_reconsent.status_code == 200
            assert res_reconsent.json()["requires_reconsent"] is False
            assert res_reconsent.json()["version_tag"] == "2.0.0"

            # 5. Subsequent write succeeds cleanly!
            res_unblocked = await client.post(
                "/api/v1/execution/form-submissions",
                json={
                    "study_id": study_id,
                    "site_id": site_id,
                    "subject_id": subject_id,
                    "visit_id": "VISIT-002",
                    "form_id": "VS_DEMO",
                    "protocol_version": "2.0.0",
                    "payload": {"vssbp": 125.0},
                },
                headers=get_auth_headers(),
            )
            assert res_unblocked.status_code == 201
            assert res_unblocked.json()["status"] == "DRAFT"
            assert res_unblocked.json()["protocol_version"] == "2.0.0"
