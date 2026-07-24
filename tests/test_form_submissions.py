import hashlib
import hmac
import os
import time

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import AuditLog, Base
from apps.execution.main import app
from apps.execution.trial_lock import TrialLockManager

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")


def get_auth_headers(
    user_id="test_user", roles="admin", change_reason="system_operation", action=None
):
    """Generate Gateway signature-compliant authentication headers."""
    import json
    from jose import jwt

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
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }
    if action:
        sig_payload = {
            "sub": user_id,
            "username": "test_user",
            "action": action,
            "roles": [roles],
            "iat": time.time(),
            "exp": time.time() + 300.0,
        }
        sig_token = jwt.encode(sig_payload, GATEWAY_SECRET, algorithm="HS256")
        headers["X-Sig-Token"] = sig_token
    return headers


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
    TrialLockManager.reset()
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_form_submission_lifecycle_happy_path() -> None:
    """Verify standard happy-path lifecycle transition of a FormSubmission:

    DRAFT -> COMPLETED -> APPROVED.
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Create form submission (default: DRAFT)
        payload = {
            "study_id": "STUDY-001",
            "site_id": "SITE-001",
            "subject_id": "SUBJ-101",
            "visit_id": "VISIT-201",
            "form_id": "FORM-301",
        }
        res = await client.post(
            "/api/v1/execution/form-submissions",
            json=payload,
            headers=get_auth_headers(roles="coordinator"),
        )
        assert res.status_code == 201
        data = res.json()
        assert data["study_id"] == "STUDY-001"
        assert data["site_id"] == "SITE-001"
        assert data["subject_id"] == "SUBJ-101"
        assert data["visit_id"] == "VISIT-201"
        assert data["form_id"] == "FORM-301"
        assert data["status"] == "DRAFT"
        assert data["signature_manifest"] is None
        assert data["version"] == 1
        submission_id = data["id"]

        # 2. Complete form submission (DRAFT -> COMPLETED)
        res_comp = await client.post(
            f"/api/v1/execution/form-submissions/{submission_id}/complete",
            headers=get_auth_headers(roles="coordinator"),
        )
        assert res_comp.status_code == 200
        data_comp = res_comp.json()
        assert data_comp["status"] == "COMPLETED"
        assert data_comp["version"] == 2

        # 3. Approve form submission (COMPLETED -> APPROVED)
        approve_payload = {
            "signature_manifest": {
                "signer_id": "user_pi_1",
                "signer_name": "Dr. Smith",
                "signed_at": "2026-08-01T12:00:00Z",
            },
            "signing_reason": "I attest that this data is accurate and complete.",
        }
        res_app = await client.post(
            f"/api/v1/execution/form-submissions/{submission_id}/approve",
            json=approve_payload,
            headers=get_auth_headers(roles="investigator", action=f"/api/v1/execution/form-submissions/{submission_id}/approve"),
        )
        assert res_app.status_code == 200
        data_app = res_app.json()
        assert data_app["status"] == "APPROVED"
        assert data_app["signature_manifest"] == approve_payload["signature_manifest"]
        assert data_app["version"] == 3

        # 4. Fetch the form submission
        res_get = await client.get(
            f"/api/v1/execution/form-submissions/{submission_id}",
            headers=get_auth_headers(),
        )
        assert res_get.status_code == 200
        assert res_get.json()["status"] == "APPROVED"

        # 5. List with filters
        res_list = await client.get(
            "/api/v1/execution/form-submissions",
            params={"subject_id": "SUBJ-101", "form_id": "FORM-301"},
            headers=get_auth_headers(),
        )
        assert res_list.status_code == 200
        subs = res_list.json()
        assert len(subs) == 1
        assert subs[0]["id"] == submission_id


@pytest.mark.asyncio
async def test_form_submission_invalid_transitions() -> None:
    """Verify that invalid lifecycle status transitions are blocked:

    - Cannot approve directly from DRAFT.
    - Cannot transition from APPROVED back to DRAFT or COMPLETED.
    - Cannot transition from COMPLETED to COMPLETED again.
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Create a FormSubmission
        payload = {
            "study_id": "STUDY-001",
            "site_id": "SITE-001",
            "subject_id": "SUBJ-101",
            "visit_id": "VISIT-201",
            "form_id": "FORM-301",
        }
        res = await client.post(
            "/api/v1/execution/form-submissions",
            json=payload,
            headers=get_auth_headers(),
        )
        submission_id = res.json()["id"]

        # Attempt PI Approval directly from DRAFT (must fail)
        approve_payload = {
            "signature_manifest": {"signer_id": "pi_1"},
            "signing_reason": "PI approval and sign-off.",
        }
        res_app_fail = await client.post(
            f"/api/v1/execution/form-submissions/{submission_id}/approve",
            json=approve_payload,
            headers=get_auth_headers(roles="investigator", action=f"/api/v1/execution/form-submissions/{submission_id}/approve"),
        )
        assert res_app_fail.status_code == 400
        assert (
            "PI approval must only be possible from COMPLETED"
            in res_app_fail.json()["detail"]
        )

        # Transition to COMPLETED
        res_comp = await client.post(
            f"/api/v1/execution/form-submissions/{submission_id}/complete",
            headers=get_auth_headers(),
        )
        assert res_comp.status_code == 200

        # Attempt to complete again (must fail)
        res_comp_fail = await client.post(
            f"/api/v1/execution/form-submissions/{submission_id}/complete",
            headers=get_auth_headers(),
        )
        assert res_comp_fail.status_code == 400
        assert "only be completed from DRAFT" in res_comp_fail.json()["detail"]

        # Transition to APPROVED
        res_app = await client.post(
            f"/api/v1/execution/form-submissions/{submission_id}/approve",
            json=approve_payload,
            headers=get_auth_headers(roles="investigator", action=f"/api/v1/execution/form-submissions/{submission_id}/approve"),
        )
        assert res_app.status_code == 200

        # Attempt to complete a completed/approved form (must fail)
        res_comp_approved_fail = await client.post(
            f"/api/v1/execution/form-submissions/{submission_id}/complete",
            headers=get_auth_headers(),
        )
        assert res_comp_approved_fail.status_code == 400


@pytest.mark.asyncio
async def test_form_submission_validation() -> None:
    """Verify signature_manifest and signing_reason validations."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Create a FormSubmission
        payload = {
            "study_id": "STUDY-001",
            "site_id": "SITE-001",
            "subject_id": "SUBJ-101",
            "visit_id": "VISIT-201",
            "form_id": "FORM-301",
        }
        res = await client.post(
            "/api/v1/execution/form-submissions",
            json=payload,
            headers=get_auth_headers(),
        )
        submission_id = res.json()["id"]

        # Complete the FormSubmission
        await client.post(
            f"/api/v1/execution/form-submissions/{submission_id}/complete",
            headers=get_auth_headers(),
        )

        # 1. Try to approve with an invalid signing reason
        bad_reason_payload = {
            "signature_manifest": {"signer_id": "pi_1"},
            "signing_reason": "Because I felt like it.",
        }
        res_bad_reason = await client.post(
            f"/api/v1/execution/form-submissions/{submission_id}/approve",
            json=bad_reason_payload,
            headers=get_auth_headers(roles="investigator", action=f"/api/v1/execution/form-submissions/{submission_id}/approve"),
        )
        assert res_bad_reason.status_code == 400
        assert "Invalid signing reason" in res_bad_reason.json()["detail"]

        # 2. Try to approve with empty signature manifest
        empty_manifest_payload = {
            "signature_manifest": {},
            "signing_reason": "PI approval and sign-off.",
        }
        res_empty_manifest = await client.post(
            f"/api/v1/execution/form-submissions/{submission_id}/approve",
            json=empty_manifest_payload,
            headers=get_auth_headers(roles="investigator", action=f"/api/v1/execution/form-submissions/{submission_id}/approve"),
        )
        assert res_empty_manifest.status_code == 400
        assert "Signature manifest is required" in res_empty_manifest.json()["detail"]


@pytest.mark.asyncio
async def test_form_submission_locks() -> None:
    """Verify that lock enforcement (trial, site, and visit) applies to form submissions."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Create a FormSubmission normally
        payload = {
            "study_id": "STUDY-001",
            "site_id": "SITE-LOCKED",
            "subject_id": "SUBJ-101",
            "visit_id": "VISIT-LOCKED",
            "form_id": "FORM-301",
        }
        res = await client.post(
            "/api/v1/execution/form-submissions",
            json=payload,
            headers=get_auth_headers(),
        )
        assert res.status_code == 201
        submission_id = res.json()["id"]

        # 1. Test Site Lock
        TrialLockManager.lock_site("SITE-LOCKED")
        try:
            with pytest.raises(
                PermissionError, match="SITE-LOCKED is currently locked"
            ):
                await client.post(
                    f"/api/v1/execution/form-submissions/{submission_id}/complete",
                    headers=get_auth_headers(),
                )
        finally:
            TrialLockManager.unlock_site("SITE-LOCKED")

        # After unlocking, complete should work
        res_comp_success = await client.post(
            f"/api/v1/execution/form-submissions/{submission_id}/complete",
            headers=get_auth_headers(),
        )
        assert res_comp_success.status_code == 200

        # 2. Test Visit Lock
        TrialLockManager.lock_visit("VISIT-LOCKED")
        try:
            approve_payload = {
                "signature_manifest": {"signer_id": "pi_1"},
                "signing_reason": "PI approval and sign-off.",
            }
            with pytest.raises(
                PermissionError, match="VISIT-LOCKED is currently locked"
            ):
                await client.post(
                    f"/api/v1/execution/form-submissions/{submission_id}/approve",
                    json=approve_payload,
                    headers=get_auth_headers(roles="investigator", action=f"/api/v1/execution/form-submissions/{submission_id}/approve"),
                )
        finally:
            TrialLockManager.unlock_visit("VISIT-LOCKED")

        # 3. Test Trial Lock
        TrialLockManager.lock_trial("Security breach simulation")
        try:
            # Creation of new submission should fail under trial lock
            with pytest.raises(PermissionError, match="Trial is currently locked"):
                await client.post(
                    "/api/v1/execution/form-submissions",
                    json=payload,
                    headers=get_auth_headers(),
                )
        finally:
            TrialLockManager.reset()


@pytest.mark.asyncio
async def test_form_submission_audit_logging() -> None:
    """Verify that form submissions operations write audits to AuditLog and version increments correctly."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        payload = {
            "study_id": "STUDY-001",
            "site_id": "SITE-001",
            "subject_id": "SUBJ-101",
            "visit_id": "VISIT-201",
            "form_id": "FORM-301",
        }
        res = await client.post(
            "/api/v1/execution/form-submissions",
            json=payload,
            headers=get_auth_headers(
                user_id="user_coordinator", change_reason="Form initial draft"
            ),
        )
        submission_id = res.json()["id"]

        # Complete it
        await client.post(
            f"/api/v1/execution/form-submissions/{submission_id}/complete",
            headers=get_auth_headers(
                user_id="user_coordinator", change_reason="Form data completed"
            ),
        )

        # Check Audit logs
        async with db_manager.get_session_maker()() as session:
            stmt = (
                select(AuditLog)
                .where(AuditLog.table_name == "form_submissions")
                .order_by(AuditLog.timestamp.asc())
            )
            result = await session.execute(stmt)
            logs = result.scalars().all()

            assert len(logs) == 2
            assert logs[0].action == "INSERT"
            assert logs[0].user_id == "user_coordinator"
            assert logs[0].change_reason == "Form initial draft"
            assert logs[0].version_index == 1

            assert logs[1].action == "UPDATE"
            assert logs[1].user_id == "user_coordinator"
            assert logs[1].change_reason == "Form data completed"
            assert logs[1].version_index == 2
