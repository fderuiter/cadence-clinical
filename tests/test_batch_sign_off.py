import hashlib
import hmac
import os
import time

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import AuditLog, Base, FormSubmission
from apps.execution.main import app
from apps.execution.trial_lock import TrialLockManager

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")


def get_auth_headers(
    user_id="test_user",
    roles="admin",
    change_reason="system_operation",
    action=None,
    sig_token_custom=None,
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
    if sig_token_custom:
        headers["X-Sig-Token"] = sig_token_custom
    elif action:
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
async def test_batch_sign_off_happy_path_form() -> None:
    """Test successful batch sign-off using FORM target resolution."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Pre-populate two completed form submissions and one draft submission
        async with db_manager.get_session_maker()() as session:
            sub1 = FormSubmission(
                study_id="STUDY-001",
                site_id="SITE-001",
                subject_id="SUBJ-001",
                visit_id="VISIT-001",
                form_id="FORM-001",
                status="COMPLETED",
            )
            sub2 = FormSubmission(
                study_id="STUDY-001",
                site_id="SITE-001",
                subject_id="SUBJ-001",
                visit_id="VISIT-001",
                form_id="FORM-002",
                status="COMPLETED",
            )
            sub3 = FormSubmission(
                study_id="STUDY-001",
                site_id="SITE-001",
                subject_id="SUBJ-001",
                visit_id="VISIT-001",
                form_id="FORM-003",
                status="DRAFT",
            )
            session.add_all([sub1, sub2, sub3])
            await session.commit()
            id1, id2, id3 = sub1.id, sub2.id, sub3.id

        action_path = "/api/v1/execution/batch-sign-off"

        # 2. Call batch sign-off as PI (roles="pi")
        payload = {
            "study_id": "STUDY-001",
            "target_type": "FORM",
            "target_ids": [id1, id2, id3, "non-existent-id"],
            "signing_reason": "PI approval and sign-off.",
        }

        res = await client.post(
            action_path,
            json=payload,
            headers=get_auth_headers(roles="pi", action=action_path),
        )
        assert res.status_code == 200
        res_data = res.json()
        assert res_data["status"] == "success"
        assert id1 in res_data["approved_submission_ids"]
        assert id2 in res_data["approved_submission_ids"]
        assert id3 in res_data["skipped_submission_ids"]
        assert "non-existent-id" in res_data["skipped_targets"]

        # 3. Verify database state
        async with db_manager.get_session_maker()() as session:
            stmt = select(FormSubmission).where(FormSubmission.id.in_([id1, id2, id3]))
            res_db = await session.execute(stmt)
            subs = {s.id: s for s in res_db.scalars().all()}

            assert subs[id1].status == "APPROVED"
            assert subs[id2].status == "APPROVED"
            assert subs[id3].status == "DRAFT"

            # Check that signature manifest exists and matches pre-approval version hash
            m1 = subs[id1].signature_manifest
            assert m1["signer_id"] == "test_user"
            assert m1["signing_reason"] == "PI approval and sign-off."
            assert "canonical_signature_hash" in m1
            assert m1["signed_version"] == 2  # Incremented from 1


@pytest.mark.asyncio
async def test_batch_sign_off_visit_resolution() -> None:
    """Test successful batch sign-off using VISIT target resolution."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        async with db_manager.get_session_maker()() as session:
            sub1 = FormSubmission(
                study_id="STUDY-001",
                site_id="SITE-001",
                subject_id="SUBJ-001",
                visit_id="VISIT-A",
                form_id="FORM-001",
                status="COMPLETED",
            )
            sub2 = FormSubmission(
                study_id="STUDY-001",
                site_id="SITE-001",
                subject_id="SUBJ-002",
                visit_id="VISIT-A",
                form_id="FORM-002",
                status="COMPLETED",
            )
            sub3 = FormSubmission(
                study_id="STUDY-001",
                site_id="SITE-001",
                subject_id="SUBJ-002",
                visit_id="VISIT-B",
                form_id="FORM-002",
                status="COMPLETED",
            )
            session.add_all([sub1, sub2, sub3])
            await session.commit()
            id1, id2, id3 = sub1.id, sub2.id, sub3.id

        action_path = "/api/v1/execution/batch-sign-off"

        # Sign-off VISIT-A
        payload = {
            "study_id": "STUDY-001",
            "target_type": "VISIT",
            "target_ids": ["VISIT-A", "VISIT-EMPTY"],
            "signing_reason": "I attest that this data is accurate and complete.",
        }

        res = await client.post(
            action_path,
            json=payload,
            headers=get_auth_headers(
                roles="principal investigator", action=action_path
            ),
        )
        assert res.status_code == 200
        res_data = res.json()
        assert id1 in res_data["approved_submission_ids"]
        assert id2 in res_data["approved_submission_ids"]
        assert id3 not in res_data["approved_submission_ids"]
        assert "VISIT-EMPTY" in res_data["skipped_targets"]

        # Check DB
        async with db_manager.get_session_maker()() as session:
            stmt = select(FormSubmission).where(FormSubmission.id.in_([id1, id2, id3]))
            res_db = await session.execute(stmt)
            subs = {s.id: s for s in res_db.scalars().all()}
            assert subs[id1].status == "APPROVED"
            assert subs[id2].status == "APPROVED"
            assert subs[id3].status == "COMPLETED"


@pytest.mark.asyncio
async def test_batch_sign_off_subject_resolution() -> None:
    """Test successful batch sign-off using SUBJECT target resolution."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        async with db_manager.get_session_maker()() as session:
            sub1 = FormSubmission(
                study_id="STUDY-001",
                site_id="SITE-001",
                subject_id="SUBJ-X",
                visit_id="VISIT-001",
                form_id="FORM-001",
                status="COMPLETED",
            )
            sub2 = FormSubmission(
                study_id="STUDY-001",
                site_id="SITE-001",
                subject_id="SUBJ-Y",
                visit_id="VISIT-001",
                form_id="FORM-002",
                status="COMPLETED",
            )
            session.add_all([sub1, sub2])
            await session.commit()
            id1, id2 = sub1.id, sub2.id

        action_path = "/api/v1/execution/batch-sign-off"

        # Sign-off SUBJ-X
        payload = {
            "study_id": "STUDY-001",
            "target_type": "SUBJECT",
            "target_ids": ["SUBJ-X"],
            "signing_reason": "Review and confirmation.",
        }

        res = await client.post(
            action_path,
            json=payload,
            headers=get_auth_headers(roles="pi", action=action_path),
        )
        assert res.status_code == 200
        res_data = res.json()
        assert id1 in res_data["approved_submission_ids"]
        assert id2 not in res_data["approved_submission_ids"]


@pytest.mark.asyncio
async def test_batch_sign_off_pi_only() -> None:
    """Test that unauthorized non-PI roles are rejected with HTTP 403."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        action_path = "/api/v1/execution/batch-sign-off"
        payload = {
            "study_id": "STUDY-001",
            "target_type": "FORM",
            "target_ids": ["some-id"],
            "signing_reason": "PI approval and sign-off.",
        }

        # Roles without PI role
        for bad_role in ["coordinator", "cra", "data manager"]:
            res = await client.post(
                action_path,
                json=payload,
                headers=get_auth_headers(roles=bad_role, action=action_path),
            )
            assert res.status_code == 403
            assert "Only a Principal Investigator" in res.json()["detail"]


@pytest.mark.asyncio
async def test_batch_sign_off_token_replay() -> None:
    """Test that signature token can be used exactly once and replay returns HTTP 401."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        action_path = "/api/v1/execution/batch-sign-off"
        payload = {
            "study_id": "STUDY-001",
            "target_type": "FORM",
            "target_ids": [],
            "signing_reason": "PI approval and sign-off.",
        }

        # Generate a standard token and use it once
        from jose import jwt

        sig_payload = {
            "sub": "test_user",
            "username": "test_user",
            "action": action_path,
            "roles": ["pi"],
            "iat": time.time(),
            "exp": time.time() + 300.0,
            "jti": "unique-replay-token-123",
        }
        sig_token = jwt.encode(sig_payload, GATEWAY_SECRET, algorithm="HS256")

        headers = get_auth_headers(roles="pi", sig_token_custom=sig_token)

        # First attempt should succeed (empty target ids just returns 200 with empty arrays)
        res1 = await client.post(action_path, json=payload, headers=headers)
        assert res1.status_code == 200

        # Second attempt with the exact same token should fail due to replay prevention
        res2 = await client.post(action_path, json=payload, headers=headers)
        assert res2.status_code == 401
        assert "already been used" in res2.json()["message"]


@pytest.mark.asyncio
async def test_batch_sign_off_locks_and_atomic_rollback() -> None:
    """Test that site/visit locks reject sign-off write and roll back everything atomically (no partial approvals)."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Pre-populate two completed form submissions
        async with db_manager.get_session_maker()() as session:
            sub1 = FormSubmission(
                study_id="STUDY-001",
                site_id="SITE-OK",
                subject_id="SUBJ-001",
                visit_id="VISIT-OK",
                form_id="FORM-001",
                status="COMPLETED",
            )
            sub2 = FormSubmission(
                study_id="STUDY-001",
                site_id="SITE-LOCKED",
                subject_id="SUBJ-002",
                visit_id="VISIT-002",
                form_id="FORM-002",
                status="COMPLETED",
            )
            session.add_all([sub1, sub2])
            await session.commit()
            id1, id2 = sub1.id, sub2.id

        action_path = "/api/v1/execution/batch-sign-off"
        payload = {
            "study_id": "STUDY-001",
            "target_type": "FORM",
            "target_ids": [id1, id2],
            "signing_reason": "PI approval and sign-off.",
        }

        # Lock the site of sub2
        TrialLockManager.lock_site("SITE-LOCKED")

        # When we attempt batch sign-off, it should hit the lock check, raise PermissionError,
        # and rollback the entire transaction, leaving BOTH submissions as COMPLETED (none APPROVED).
        with pytest.raises(PermissionError, match="SITE-LOCKED is currently locked"):
            await client.post(
                action_path,
                json=payload,
                headers=get_auth_headers(roles="pi", action=action_path),
            )

        # Verify that sub1 was NOT approved (proper rollback occurred!)
        async with db_manager.get_session_maker()() as session:
            stmt = select(FormSubmission).where(FormSubmission.id.in_([id1, id2]))
            res_db = await session.execute(stmt)
            subs = {s.id: s for s in res_db.scalars().all()}
            assert subs[id1].status == "COMPLETED"
            assert subs[id2].status == "COMPLETED"


@pytest.mark.asyncio
async def test_batch_sign_off_audit_log_capture() -> None:
    """Verify that successful batch sign-off writes accurate entries to AuditLog.

    Each approved submission must record its new signature manifest and incremented version.
    """
    # @req:PRD-SYS-001
    # @req:PRD-SYS-002
    # @req:Trace-1
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        async with db_manager.get_session_maker()() as session:
            sub = FormSubmission(
                study_id="STUDY-001",
                site_id="SITE-001",
                subject_id="SUBJ-001",
                visit_id="VISIT-001",
                form_id="FORM-001",
                status="COMPLETED",
            )
            session.add(sub)
            await session.commit()
            sub_id = sub.id

        action_path = "/api/v1/execution/batch-sign-off"
        payload = {
            "study_id": "STUDY-001",
            "target_type": "FORM",
            "target_ids": [sub_id],
            "signing_reason": "PI approval and sign-off.",
        }

        res = await client.post(
            action_path,
            json=payload,
            headers=get_auth_headers(roles="pi", action=action_path, change_reason="Batch Sign-off audit test"),
        )
        assert res.status_code == 200

        # Query and verify the AuditLog entry
        async with db_manager.get_session_maker()() as session:
            stmt = select(AuditLog).where(
                AuditLog.table_name == "form_submissions",
                AuditLog.record_id == str(sub_id),
                AuditLog.action == "UPDATE"
            )
            res_audit = await session.execute(stmt)
            audits = res_audit.scalars().all()
            assert len(audits) >= 1
            audit_record = audits[-1]

            assert audit_record.change_reason == "Batch Sign-off audit test"
            assert audit_record.user_id == "test_user"
            assert audit_record.version_index == 2

            # Verify new_values contains the signature manifest and updated version
            new_vals = audit_record.new_values
            assert new_vals is not None
            assert new_vals["status"] == "APPROVED"
            assert new_vals["version"] == 2

            manifest = new_vals["signature_manifest"]
            assert manifest is not None
            assert manifest["signer_id"] == "test_user"
            assert manifest["signing_reason"] == "PI approval and sign-off."
            assert "canonical_signature_hash" in manifest


@pytest.mark.asyncio
async def test_batch_sign_off_trial_lock_rejection() -> None:
    """Verify that a global trial lock rejects batch sign-off and rolls back transactionally."""
    # @req:PRD-SYS-003
    # @req:Trace-3
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        async with db_manager.get_session_maker()() as session:
            sub1 = FormSubmission(
                study_id="STUDY-001",
                site_id="SITE-001",
                subject_id="SUBJ-001",
                visit_id="VISIT-001",
                form_id="FORM-001",
                status="COMPLETED",
            )
            session.add(sub1)
            await session.commit()
            id1 = sub1.id

        action_path = "/api/v1/execution/batch-sign-off"
        payload = {
            "study_id": "STUDY-001",
            "target_type": "FORM",
            "target_ids": [id1],
            "signing_reason": "PI approval and sign-off.",
        }

        # Lock trial globally
        TrialLockManager.lock_trial(reason="Global compromise simulation")

        # The lock check raises PermissionError in audit.py which is mapped to 500 when called via ASGITransport or propagates.
        # Let's inspect that it properly raises the lock warning or PermissionError.
        with pytest.raises(PermissionError, match="Trial is currently locked"):
            await client.post(
                action_path,
                json=payload,
                headers=get_auth_headers(roles="pi", action=action_path),
            )

        # Check DB to confirm rollback (still COMPLETED, not APPROVED)
        async with db_manager.get_session_maker()() as session:
            stmt = select(FormSubmission).where(FormSubmission.id == id1)
            res_db = await session.execute(stmt)
            sub = res_db.scalars().first()
            assert sub.status == "COMPLETED"


@pytest.mark.asyncio
async def test_batch_sign_off_visit_lock_rejection() -> None:
    """Verify that a visit lock rejects batch sign-off and rolls back transactionally."""
    # @req:PRD-SYS-003
    # @req:Trace-3
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        async with db_manager.get_session_maker()() as session:
            sub1 = FormSubmission(
                study_id="STUDY-001",
                site_id="SITE-001",
                subject_id="SUBJ-001",
                visit_id="VISIT-001",
                form_id="FORM-001",
                status="COMPLETED",
            )
            sub2 = FormSubmission(
                study_id="STUDY-001",
                site_id="SITE-001",
                subject_id="SUBJ-001",
                visit_id="VISIT-LOCKED",
                form_id="FORM-002",
                status="COMPLETED",
            )
            session.add_all([sub1, sub2])
            await session.commit()
            id1, id2 = sub1.id, sub2.id

        action_path = "/api/v1/execution/batch-sign-off"
        payload = {
            "study_id": "STUDY-001",
            "target_type": "FORM",
            "target_ids": [id1, id2],
            "signing_reason": "PI approval and sign-off.",
        }

        # Lock the visit of sub2
        TrialLockManager.lock_visit("VISIT-LOCKED")

        with pytest.raises(PermissionError, match="Visit VISIT-LOCKED is currently locked"):
            await client.post(
                action_path,
                json=payload,
                headers=get_auth_headers(roles="pi", action=action_path),
            )

        # Confirm rollback for sub1
        async with db_manager.get_session_maker()() as session:
            stmt = select(FormSubmission).where(FormSubmission.id.in_([id1, id2]))
            res_db = await session.execute(stmt)
            subs = {s.id: s for s in res_db.scalars().all()}
            assert subs[id1].status == "COMPLETED"
            assert subs[id2].status == "COMPLETED"


@pytest.mark.asyncio
async def test_batch_sign_off_subject_lock_rejection() -> None:
    """Verify that a subject lock rejects batch sign-off and rolls back transactionally."""
    # @req:PRD-SYS-003
    # @req:Trace-3
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        async with db_manager.get_session_maker()() as session:
            sub1 = FormSubmission(
                study_id="STUDY-001",
                site_id="SITE-001",
                subject_id="SUBJ-001",
                visit_id="VISIT-001",
                form_id="FORM-001",
                status="COMPLETED",
            )
            sub2 = FormSubmission(
                study_id="STUDY-001",
                site_id="SITE-001",
                subject_id="SUBJ-LOCKED",
                visit_id="VISIT-001",
                form_id="FORM-002",
                status="COMPLETED",
            )
            session.add_all([sub1, sub2])
            await session.commit()
            id1, id2 = sub1.id, sub2.id

        action_path = "/api/v1/execution/batch-sign-off"
        payload = {
            "study_id": "STUDY-001",
            "target_type": "FORM",
            "target_ids": [id1, id2],
            "signing_reason": "PI approval and sign-off.",
        }

        # Lock the subject of sub2
        TrialLockManager.lock_subject("SUBJ-LOCKED")

        with pytest.raises(PermissionError, match="Subject SUBJ-LOCKED is currently locked"):
            await client.post(
                action_path,
                json=payload,
                headers=get_auth_headers(roles="pi", action=action_path),
            )

        # Confirm rollback for sub1
        async with db_manager.get_session_maker()() as session:
            stmt = select(FormSubmission).where(FormSubmission.id.in_([id1, id2]))
            res_db = await session.execute(stmt)
            subs = {s.id: s for s in res_db.scalars().all()}
            assert subs[id1].status == "COMPLETED"
            assert subs[id2].status == "COMPLETED"


@pytest.mark.asyncio
async def test_batch_sign_off_form_lock_rejection() -> None:
    """Verify that a form lock rejects batch sign-off and rolls back transactionally."""
    # @req:PRD-SYS-003
    # @req:Trace-3
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        async with db_manager.get_session_maker()() as session:
            sub1 = FormSubmission(
                study_id="STUDY-001",
                site_id="SITE-001",
                subject_id="SUBJ-001",
                visit_id="VISIT-001",
                form_id="FORM-001",
                status="COMPLETED",
            )
            sub2 = FormSubmission(
                study_id="STUDY-001",
                site_id="SITE-001",
                subject_id="SUBJ-001",
                visit_id="VISIT-001",
                form_id="FORM-LOCKED",
                status="COMPLETED",
            )
            session.add_all([sub1, sub2])
            await session.commit()
            id1, id2 = sub1.id, sub2.id

        action_path = "/api/v1/execution/batch-sign-off"
        payload = {
            "study_id": "STUDY-001",
            "target_type": "FORM",
            "target_ids": [id1, id2],
            "signing_reason": "PI approval and sign-off.",
        }

        # Lock the form of sub2
        TrialLockManager.lock_form("FORM-LOCKED")

        with pytest.raises(PermissionError, match="Form FORM-LOCKED is currently locked"):
            await client.post(
                action_path,
                json=payload,
                headers=get_auth_headers(roles="pi", action=action_path),
            )

        # Confirm rollback for sub1
        async with db_manager.get_session_maker()() as session:
            stmt = select(FormSubmission).where(FormSubmission.id.in_([id1, id2]))
            res_db = await session.execute(stmt)
            subs = {s.id: s for s in res_db.scalars().all()}
            assert subs[id1].status == "COMPLETED"
            assert subs[id2].status == "COMPLETED"


@pytest.mark.asyncio
async def test_batch_sign_off_token_user_mismatch() -> None:
    """Verify that a signature token with a mismatched user subject is rejected."""
    # @req:PRD-SYS-001
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        action_path = "/api/v1/execution/batch-sign-off"
        payload = {
            "study_id": "STUDY-001",
            "target_type": "FORM",
            "target_ids": [],
            "signing_reason": "PI approval and sign-off.",
        }

        from jose import jwt
        sig_payload = {
            "sub": "some_other_user",  # mismatched sub
            "username": "some_other_user",
            "action": action_path,
            "roles": ["pi"],
            "iat": time.time(),
            "exp": time.time() + 300.0,
        }
        sig_token = jwt.encode(sig_payload, GATEWAY_SECRET, algorithm="HS256")

        headers = get_auth_headers(user_id="test_user", roles="pi", sig_token_custom=sig_token)
        res = await client.post(action_path, json=payload, headers=headers)
        assert res.status_code == 401
        assert "user mismatch" in res.json()["message"].lower()


@pytest.mark.asyncio
async def test_batch_sign_off_token_action_mismatch() -> None:
    """Verify that a signature token bound to a different endpoint/action is rejected."""
    # @req:PRD-SYS-001
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        action_path = "/api/v1/execution/batch-sign-off"
        payload = {
            "study_id": "STUDY-001",
            "target_type": "FORM",
            "target_ids": [],
            "signing_reason": "PI approval and sign-off.",
        }

        from jose import jwt
        sig_payload = {
            "sub": "test_user",
            "username": "test_user",
            "action": "/api/v1/execution/form-submissions/999/approve",  # mismatched action
            "roles": ["pi"],
            "iat": time.time(),
            "exp": time.time() + 300.0,
        }
        sig_token = jwt.encode(sig_payload, GATEWAY_SECRET, algorithm="HS256")

        headers = get_auth_headers(user_id="test_user", roles="pi", sig_token_custom=sig_token)
        res = await client.post(action_path, json=payload, headers=headers)
        assert res.status_code == 401
        assert "action mismatch" in res.json()["message"].lower()


@pytest.mark.asyncio
async def test_batch_sign_off_token_expired() -> None:
    """Verify that an expired signature token is rejected."""
    # @req:PRD-SYS-001
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        action_path = "/api/v1/execution/batch-sign-off"
        payload = {
            "study_id": "STUDY-001",
            "target_type": "FORM",
            "target_ids": [],
            "signing_reason": "PI approval and sign-off.",
        }

        from jose import jwt
        sig_payload = {
            "sub": "test_user",
            "username": "test_user",
            "action": action_path,
            "roles": ["pi"],
            "iat": time.time() - 3600.0,
            "exp": time.time() - 1800.0,  # expired
        }
        sig_token = jwt.encode(sig_payload, GATEWAY_SECRET, algorithm="HS256")

        headers = get_auth_headers(user_id="test_user", roles="pi", sig_token_custom=sig_token)
        res = await client.post(action_path, json=payload, headers=headers)
        assert res.status_code == 401
        assert "invalid signature token" in res.json()["message"].lower()


@pytest.mark.asyncio
async def test_batch_sign_off_token_invalid_signature() -> None:
    """Verify that a signature token with an invalid cryptographic signature is rejected."""
    # @req:PRD-SYS-001
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        action_path = "/api/v1/execution/batch-sign-off"
        payload = {
            "study_id": "STUDY-001",
            "target_type": "FORM",
            "target_ids": [],
            "signing_reason": "PI approval and sign-off.",
        }

        from jose import jwt
        sig_payload = {
            "sub": "test_user",
            "username": "test_user",
            "action": action_path,
            "roles": ["pi"],
            "iat": time.time(),
            "exp": time.time() + 300.0,
        }
        # Signed with a different key / wrong secret
        sig_token = jwt.encode(sig_payload, "completely-wrong-secret-key-98765", algorithm="HS256")

        headers = get_auth_headers(user_id="test_user", roles="pi", sig_token_custom=sig_token)
        res = await client.post(action_path, json=payload, headers=headers)
        assert res.status_code == 401
        assert "invalid signature token" in res.json()["message"].lower()


@pytest.mark.asyncio
async def test_batch_sign_off_general_exception_rollback() -> None:
    """Verify that if an unexpected exception occurs during execution, the entire transaction is rolled back."""
    # @req:PRD-SYS-002
    from unittest.mock import patch

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        async with db_manager.get_session_maker()() as session:
            sub = FormSubmission(
                study_id="STUDY-001",
                site_id="SITE-001",
                subject_id="SUBJ-001",
                visit_id="VISIT-001",
                form_id="FORM-001",
                status="COMPLETED",
            )
            session.add(sub)
            await session.commit()
            sub_id = sub.id

        action_path = "/api/v1/execution/batch-sign-off"
        payload = {
            "study_id": "STUDY-001",
            "target_type": "FORM",
            "target_ids": [sub_id],
            "signing_reason": "PI approval and sign-off.",
        }

        # Mock the signature helper to raise an error
        with patch("apps.execution.main.generate_canonical_signature", side_effect=RuntimeError("Simulated DB or serialization failure")):
            with pytest.raises(RuntimeError, match="Simulated DB or serialization failure"):
                await client.post(
                    action_path,
                    json=payload,
                    headers=get_auth_headers(roles="pi", action=action_path),
                )

        # Verify database was rolled back and status is still COMPLETED, not APPROVED
        async with db_manager.get_session_maker()() as session:
            stmt = select(FormSubmission).where(FormSubmission.id == sub_id)
            res_db = await session.execute(stmt)
            refetched = res_db.scalars().first()
            assert refetched.status == "COMPLETED"
