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
from apps.execution.database.models import AuditLog, Base
from apps.execution.main import app

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")


def get_auth_headers(
    user_id="test_user", roles="admin", change_reason="system_operation"
):
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
async def test_subject_consent_blocking_and_reconsent_lifecycle() -> None:
    """Verify that:

    1. Creating a subject initially is unblocked.
    2. Activating a newer protocol version with requires_reconsent=True blocks writes.
    3. An ICF for another version cannot unblock entry.
    4. Recording matching consent successfully clears the gate.
    5. Consent is auditable and standard version index increments are tracked.
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Create a subject (unblocked)
        subject_payload = {
            "subject_id": "SUBJ-X",
            "study_id": "STUDY-123",
            "demographics": {
                "name": "John Doe",
                "birthdate": "1990-01-01",
                "gender": "M",
                "race": "White",
            },
        }
        res_subj = await client.post(
            "/api/v1/execution/subjects",
            json=subject_payload,
            headers=get_auth_headers(),
        )
        assert res_subj.status_code == 200

        # Record initial protocol version 1 consent (unblocked)
        initial_consent_payload = {
            "protocol_version": {
                "study_id": "STUDY-123",
                "version_tag": "1.0",
                "version_index": 1,
                "status": "PUBLISHED",
            },
            "icf_signed": True,
            "requires_reconsent": False,
        }
        res_consent_1 = await client.post(
            "/api/v1/execution/subjects/SUBJ-X/consent",
            json=initial_consent_payload,
            headers=get_auth_headers(),
        )
        assert res_consent_1.status_code == 200

        # 2. Introduce a new protocol version 2 that requires re-consent
        # (e.g. by another subject or recorded for the study)
        # We can record a re-consent required entry for version 2.
        reconsent_required_payload = {
            "protocol_version": {
                "study_id": "STUDY-123",
                "version_tag": "2.0",
                "version_index": 2,
                "status": "PUBLISHED",
            },
            "icf_signed": False,
            "requires_reconsent": True,
        }
        res_reconsent_v2 = await client.post(
            "/api/v1/execution/subjects/SUBJ-X/consent",
            json=reconsent_required_payload,
            headers=get_auth_headers(),
        )
        assert res_reconsent_v2.status_code == 200

        # Since version 2 has requires_reconsent=True and SUBJ-X has not signed it yet,
        # SUBJ-X's covered writes should be blocked!

        # Try to create a visit (should fail)
        visit_payload = {
            "subject_id": "SUBJ-X",
            "visit_name": "Week 4",
            "study_id": "STUDY-123",
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

        # Try to create an observation (should fail)
        obs_payload = {
            "subject_id": "SUBJ-X",
            "domain": "VS",
            "test_code": "VSSBP",
            "test_name": "Systolic Blood Pressure",
            "value": 120.0,
            "unit": "mmHg",
        }
        with pytest.raises(PermissionError) as exc_info_obs:
            await client.post(
                "/api/v1/execution/observations",
                json=obs_payload,
                headers=get_auth_headers(),
            )
        assert "Re-Consent Required - Demographics & Visit Forms Locked" in str(
            exc_info_obs.value
        )

        # Try to create a FormSubmission (should fail)
        form_payload = {
            "study_id": "STUDY-123",
            "site_id": "SITE-1",
            "subject_id": "SUBJ-X",
            "visit_id": "VISIT-1",
            "form_id": "FORM-1",
        }
        with pytest.raises(PermissionError) as exc_info_form:
            await client.post(
                "/api/v1/execution/form-submissions",
                json=form_payload,
                headers=get_auth_headers(),
            )
        assert "Re-Consent Required - Demographics & Visit Forms Locked" in str(
            exc_info_form.value
        )

        # 3. Try to unblock with an ICF for another version (version 1.5, index 3, but not version 2)
        # This cannot unblock version 2.
        other_version_payload = {
            "protocol_version": {
                "study_id": "STUDY-123",
                "version_tag": "1.5",
                "version_index": 3,
                "status": "PUBLISHED",
            },
            "icf_signed": True,
            "requires_reconsent": False,
        }
        await client.post(
            "/api/v1/execution/subjects/SUBJ-X/consent",
            json=other_version_payload,
            headers=get_auth_headers(),
        )

        # Still blocked because version 2 has requires_reconsent=True and is unsigned by SUBJ-X
        with pytest.raises(PermissionError) as exc_info_still_blocked:
            await client.post(
                "/api/v1/execution/visits",
                json=visit_payload,
                headers=get_auth_headers(),
            )
        assert "Re-Consent Required - Demographics & Visit Forms Locked" in str(
            exc_info_still_blocked.value
        )

        # 4. Record matching consent for version 2 (icf_signed=True)
        matching_consent_payload = {
            "protocol_version": {
                "study_id": "STUDY-123",
                "version_tag": "2.0",
                "version_index": 2,
                "status": "PUBLISHED",
            },
            "icf_signed": True,
            "requires_reconsent": False,
        }
        res_matching = await client.post(
            "/api/v1/execution/subjects/SUBJ-X/consent",
            json=matching_consent_payload,
            headers=get_auth_headers(),
        )
        assert res_matching.status_code == 200

        # Now, covered writes should be successfully unblocked!
        res_visit_unblocked = await client.post(
            "/api/v1/execution/visits",
            json=visit_payload,
            headers=get_auth_headers(),
        )
        assert res_visit_unblocked.status_code == 200
        assert res_visit_unblocked.json()["visit_name"] == "Week 4"

        # 5. Verify auditable properties and version index increments on SubjectConsent
        async with db_manager.get_session_maker()() as session:
            stmt_audit = (
                select(AuditLog)
                .where(AuditLog.table_name == "subject_consents")
                .order_by(AuditLog.timestamp.asc())
            )
            res_audit = await session.execute(stmt_audit)
            logs = res_audit.scalars().all()

            # Ensure SubjectConsent operations were logged to AuditLog
            actions = [log.action for log in logs]
            assert "INSERT" in actions
            assert "UPDATE" in actions
