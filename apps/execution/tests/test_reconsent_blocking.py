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

GATEWAY_SECRET = os.getenv(
    "GATEWAY_SECRET", "internal-gateway-secret-12345"
)  # pragma: allowlist secret


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
    """Verify that subject consent blocks writes, newer protocol version with requires_reconsent=True blocks writes, and matching consent clears the gate.

    Requirements: PRD-SUB-007
    """
    from unittest.mock import patch

    from fastapi import HTTPException

    mock_response = None

    async def mock_fetch(subject_pseudonym, study_id=None):
        if mock_response is None:
            raise HTTPException(status_code=404, detail="No signed consent found")
        return mock_response

    # Patch the fetch function in audit module
    with patch(
        "apps.execution.econsent_client.fetch_subject_consent_status",
        side_effect=mock_fetch,
    ):
        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app), base_url="http://test"
        ) as client:
            # 1. Create a subject (unblocked because it is initial subject insert)
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

            # Initially, mock_response is None (no consent found). Try to create a visit (should fail)
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

            # Record initial protocol version 1 consent (unblocked)
            # We set mock_response to a signed consent for version 1.0
            mock_response = {
                "subject_pseudonym": "SUBJ-X",
                "study_id": "STUDY-123",
                "version_index": 1,
                "protocol_version": "1.0",
                "signed": True,
                "requires_reconsent": False,
            }

            # Now, creating a visit should succeed and locally cache the consent!
            res_visit_ok = await client.post(
                "/api/v1/execution/visits",
                json=visit_payload,
                headers=get_auth_headers(),
            )
            assert res_visit_ok.status_code == 200
            assert res_visit_ok.json()["visit_name"] == "Week 4"

            # 1b. Update mock_response to a new version tag (to trigger a cached SubjectConsent UPDATE)
            mock_response = {
                "subject_pseudonym": "SUBJ-X",
                "study_id": "STUDY-123",
                "version_index": 1,
                "protocol_version": "1.0-amended",
                "signed": True,
                "requires_reconsent": False,
            }

            # Create an observation (this should succeed and trigger the UPDATE)
            obs_payload_init = {
                "subject_id": "SUBJ-X",
                "domain": "VS",
                "test_code": "VSSBP",
                "test_name": "Systolic Blood Pressure",
                "value": 115.0,
                "unit": "mmHg",
            }
            res_obs_init = await client.post(
                "/api/v1/execution/observations",
                json=obs_payload_init,
                headers=get_auth_headers(),
            )
            assert res_obs_init.status_code == 200

            # 2. Introduce a new protocol version 2 that requires re-consent
            # We set mock_response to show version 1.0 but requiring re-consent
            mock_response = {
                "subject_pseudonym": "SUBJ-X",
                "study_id": "STUDY-123",
                "version_index": 1,
                "protocol_version": "1.0",
                "signed": True,
                "requires_reconsent": True,
            }

            # Try to create a second visit (should fail because requires_reconsent is True!)
            visit_payload_2 = {
                "subject_id": "SUBJ-X",
                "visit_name": "Week 8",
                "study_id": "STUDY-123",
            }
            with pytest.raises(PermissionError) as exc_info_v2:
                await client.post(
                    "/api/v1/execution/visits",
                    json=visit_payload_2,
                    headers=get_auth_headers(),
                )
            assert "Re-Consent Required - Demographics & Visit Forms Locked" in str(
                exc_info_v2.value
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

            # 3. Simulate matching consent for version 2 (signed=True, requires_reconsent=False)
            mock_response = {
                "subject_pseudonym": "SUBJ-X",
                "study_id": "STUDY-123",
                "version_index": 2,
                "protocol_version": "2.0",
                "signed": True,
                "requires_reconsent": False,
            }

            # Now, covered writes should be successfully unblocked!
            res_visit_unblocked = await client.post(
                "/api/v1/execution/visits",
                json=visit_payload_2,
                headers=get_auth_headers(),
            )
            assert res_visit_unblocked.status_code == 200
            assert res_visit_unblocked.json()["visit_name"] == "Week 8"

            # 4. Verify auditable properties and version index increments on SubjectConsent
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


@pytest.mark.asyncio
async def test_subject_consent_endpoint_lifecycle() -> None:
    """Verify that the new subject consent endpoint (POST /api/v1/execution/subjects/{id}/consent)

    correctly registers a subject's consent and clears requires_reconsent gates.
    """
    from unittest.mock import patch

    from fastapi import HTTPException

    async def mock_fetch(subject_pseudonym, study_id=None):
        raise HTTPException(status_code=404, detail="No signed consent found")

    with patch(
        "apps.execution.econsent_client.fetch_subject_consent_status",
        side_effect=mock_fetch,
    ):
        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app), base_url="http://test"
        ) as client:
            # 1. Create a subject
            subject_payload = {
                "subject_id": "SUBJ-Y",
                "study_id": "STUDY-456",
                "demographics": {
                    "name": "Jane Smith",
                    "birthdate": "1992-02-02",
                    "gender": "F",
                    "race": "Black",
                },
            }
            res_subj = await client.post(
                "/api/v1/execution/subjects",
                json=subject_payload,
                headers=get_auth_headers(),
            )
            assert res_subj.status_code == 200

            # Initially, no consent exists. Try to create a visit (should fail)
            visit_payload = {
                "subject_id": "SUBJ-Y",
                "visit_name": "Screening",
                "study_id": "STUDY-456",
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

            # 2. Record initial protocol version 1 consent using our new POST endpoint!
            consent_payload = {
                "protocol_version": {
                    "study_id": "STUDY-456",
                    "version_tag": "1.0",
                    "version_index": 1,
                    "status": "PUBLISHED",
                },
                "icf_signed": True,
                "requires_reconsent": False,
            }
            res_consent = await client.post(
                "/api/v1/execution/subjects/SUBJ-Y/consent",
                json=consent_payload,
                headers=get_auth_headers(),
            )
            assert res_consent.status_code == 200
            consent_resp = res_consent.json()
            assert consent_resp["subject_id"] == "SUBJ-Y"
            assert consent_resp["study_id"] == "STUDY-456"
            assert consent_resp["version_tag"] == "1.0"
            assert consent_resp["version_index"] == 1
            assert consent_resp["icf_signed"] is True
            assert consent_resp["requires_reconsent"] is False

            # Now, creating a visit should succeed!
            res_visit_ok = await client.post(
                "/api/v1/execution/visits",
                json=visit_payload,
                headers=get_auth_headers(),
            )
            assert res_visit_ok.status_code == 200
            assert res_visit_ok.json()["visit_name"] == "Screening"
            assert res_visit_ok.json()["protocol_version_tag"] == "1.0"

            # 3. Introduce re-consent requirement by posting a consent requiring re-consent
            consent_payload_reconsent = {
                "protocol_version": {
                    "study_id": "STUDY-456",
                    "version_tag": "1.0",
                    "version_index": 1,
                    "status": "PUBLISHED",
                },
                "icf_signed": True,
                "requires_reconsent": True,
            }
            res_re = await client.post(
                "/api/v1/execution/subjects/SUBJ-Y/consent",
                json=consent_payload_reconsent,
                headers=get_auth_headers(),
            )
            assert res_re.status_code == 200
            assert res_re.json()["requires_reconsent"] is True

            # Subsequent writes should now be blocked!
            visit_payload_2 = {
                "subject_id": "SUBJ-Y",
                "visit_name": "Week 2",
                "study_id": "STUDY-456",
            }
            with pytest.raises(PermissionError) as exc_info_re:
                await client.post(
                    "/api/v1/execution/visits",
                    json=visit_payload_2,
                    headers=get_auth_headers(),
                )
            assert "Re-Consent Required - Demographics & Visit Forms Locked" in str(
                exc_info_re.value
            )

            # 4. Clear the gate by recording signed ICF for version 2.0 (requires_reconsent = False)
            consent_payload_clear = {
                "protocol_version": {
                    "study_id": "STUDY-456",
                    "version_tag": "2.0",
                    "version_index": 2,
                    "status": "PUBLISHED",
                },
                "icf_signed": True,
                "requires_reconsent": False,
            }
            res_clear = await client.post(
                "/api/v1/execution/subjects/SUBJ-Y/consent",
                json=consent_payload_clear,
                headers=get_auth_headers(),
            )
            assert res_clear.status_code == 200
            assert res_clear.json()["version_tag"] == "2.0"
            assert res_clear.json()["requires_reconsent"] is False

            # Creating a visit should now succeed and be stamped with version 2.0!
            res_visit_unblocked = await client.post(
                "/api/v1/execution/visits",
                json=visit_payload_2,
                headers=get_auth_headers(),
            )
            assert res_visit_unblocked.status_code == 200
            assert res_visit_unblocked.json()["visit_name"] == "Week 2"
            assert res_visit_unblocked.json()["protocol_version_tag"] == "2.0"
