import hashlib
import hmac
import os
import time

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import Base, FormSubmission
from apps.execution.main import app
from apps.execution.trial_lock import TrialLockManager

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")


def get_auth_headers(
    user_id="test_user",
    roles="admin",
    change_reason="system_operation",
    action=None,
    sig_token_custom=None,
    payload=None,
):
    """Generate Gateway signature-compliant authentication headers."""
    import json

    from jose import jwt

    timestamp = str(time.time())
    header_payload = {
        "change_reason": change_reason,
        "roles": roles,
        "timestamp": timestamp,
        "user_id": user_id,
    }
    serialized = json.dumps(header_payload, sort_keys=True, separators=(",", ":"))
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
        if payload and "batch-sign-off" in action:
            norm_study = str(payload.get("study_id")).strip()
            norm_type = str(payload.get("target_type")).strip().upper()
            sorted_ids = sorted(
                [str(tid).strip() for tid in payload.get("target_ids", [])]
            )
            norm_ids = ",".join(sorted_ids)
            norm_reason = str(payload.get("signing_reason")).strip()
            binding_str = f"{norm_study}:{norm_type}:{norm_ids}:{norm_reason}"
            batch_id = hashlib.sha256(binding_str.encode("utf-8")).hexdigest()
            sig_payload["batch_id"] = batch_id
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
    """
    # @req:Trace-14
    # @req:Trace-15
    Test successful batch sign-off using FORM target resolution.
    """
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
            headers=get_auth_headers(roles="pi", action=action_path, payload=payload),
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

            # Assert the new 21 CFR §11.50 compliant signature_manifestation structure
            assert "signature_manifestation" in m1
            manifestation = m1["signature_manifestation"]
            assert manifestation["signer_username"] == "test_user"
            assert manifestation["signer_full_name"] == "Test User"
            assert manifestation["signing_timestamp_utc"].endswith("Z")
            assert manifestation["signing_reason_code"] == "PI_APPROVAL"
            assert (
                manifestation["signing_reason_text"]
                == "I approve this clinical record and confirm medical responsibility."
            )
            assert manifestation["record_id"] == id1
            assert manifestation["record_version"] == 2
            assert (
                manifestation["signature_hash_sha256"] == m1["canonical_signature_hash"]
            )


@pytest.mark.asyncio
async def test_batch_sign_off_visit_resolution() -> None:
    """
    # @req:Trace-14
    Test successful batch sign-off using VISIT target resolution.
    """
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
                roles="principal investigator", action=action_path, payload=payload
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
    """
    # @req:Trace-14
    Test successful batch sign-off using SUBJECT target resolution.
    """
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
            headers=get_auth_headers(roles="pi", action=action_path, payload=payload),
        )
        assert res.status_code == 200
        res_data = res.json()
        assert id1 in res_data["approved_submission_ids"]
        assert id2 not in res_data["approved_submission_ids"]


@pytest.mark.asyncio
async def test_batch_sign_off_pi_only() -> None:
    """
    # @req:Trace-14
    Test that unauthorized non-PI roles are rejected with HTTP 403.
    """
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
                headers=get_auth_headers(
                    roles=bad_role, action=action_path, payload=payload
                ),
            )
            assert res.status_code == 403
            assert "Only a Principal Investigator" in res.json()["detail"]


@pytest.mark.asyncio
async def test_batch_sign_off_token_replay() -> None:
    """
    # @req:Trace-15
    Test that signature token can be used exactly once and replay returns HTTP 401.
    """
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

        # Compute valid batch_id for the token
        norm_study = "STUDY-001"
        norm_type = "FORM"
        norm_ids = ""
        norm_reason = "PI approval and sign-off."
        binding_str = f"{norm_study}:{norm_type}:{norm_ids}:{norm_reason}"
        import hashlib

        batch_id = hashlib.sha256(binding_str.encode("utf-8")).hexdigest()

        sig_payload = {
            "sub": "test_user",
            "username": "test_user",
            "action": action_path,
            "roles": ["pi"],
            "iat": time.time(),
            "exp": time.time() + 300.0,
            "jti": "unique-replay-token-123",
            "batch_id": batch_id,
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
    """
    # @req:Trace-14
    Test that site/visit locks reject sign-off write and roll back everything atomically (no partial approvals).
    """
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
                headers=get_auth_headers(
                    roles="pi", action=action_path, payload=payload
                ),
            )

        # Verify that sub1 was NOT approved (proper rollback occurred!)
        async with db_manager.get_session_maker()() as session:
            stmt = select(FormSubmission).where(FormSubmission.id.in_([id1, id2]))
            res_db = await session.execute(stmt)
            subs = {s.id: s for s in res_db.scalars().all()}
            assert subs[id1].status == "COMPLETED"
            assert subs[id2].status == "COMPLETED"


@pytest.mark.asyncio
async def test_batch_sign_off_mismatched_bindings_and_no_write() -> None:
    """
    # @req:Trace-14
    # @req:Trace-15
    Test that a batch sign-off with mismatched binding (study, type, targets, reason) is rejected and does not write/modify any data.
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Pre-populate a completed form submission
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

        # Correct payload
        correct_payload = {
            "study_id": "STUDY-001",
            "target_type": "FORM",
            "target_ids": [sub_id],
            "signing_reason": "PI approval and sign-off.",
        }

        # Case A: Token generated with a different study_id
        payload_wrong_study = dict(correct_payload, study_id="STUDY-WRONG")
        headers_wrong_study = get_auth_headers(
            roles="pi", action=action_path, payload=payload_wrong_study
        )
        res_a = await client.post(
            action_path, json=correct_payload, headers=headers_wrong_study
        )
        assert res_a.status_code == 401
        assert "mismatch" in res_a.json()["message"]

        # Case B: Token generated with a different target_type
        payload_wrong_type = dict(correct_payload, target_type="VISIT")
        headers_wrong_type = get_auth_headers(
            roles="pi", action=action_path, payload=payload_wrong_type
        )
        res_b = await client.post(
            action_path, json=correct_payload, headers=headers_wrong_type
        )
        assert res_b.status_code == 401

        # Case C: Token generated with different target_ids
        payload_wrong_ids = dict(correct_payload, target_ids=["different-id"])
        headers_wrong_ids = get_auth_headers(
            roles="pi", action=action_path, payload=payload_wrong_ids
        )
        res_c = await client.post(
            action_path, json=correct_payload, headers=headers_wrong_ids
        )
        assert res_c.status_code == 401

        # Case D: Token generated with a different signing_reason
        payload_wrong_reason = dict(
            correct_payload, signing_reason="Review and confirmation."
        )
        headers_wrong_reason = get_auth_headers(
            roles="pi", action=action_path, payload=payload_wrong_reason
        )
        res_d = await client.post(
            action_path, json=correct_payload, headers=headers_wrong_reason
        )
        assert res_d.status_code == 401

        # Case E: Token not bound to a batch
        headers_no_batch = get_auth_headers(
            roles="pi", action=action_path
        )  # no payload => no batch_id
        res_e = await client.post(
            action_path, json=correct_payload, headers=headers_no_batch
        )
        assert res_e.status_code == 401

        # Verify that the form submission remains unchanged in the database (NO WRITE/MUTATION)
        async with db_manager.get_session_maker()() as session:
            stmt = select(FormSubmission).where(FormSubmission.id == sub_id)
            res_db = await session.execute(stmt)
            sub_db = res_db.scalar_one()
            assert sub_db.status == "COMPLETED"
            assert sub_db.signature_manifest is None


@pytest.mark.asyncio
async def test_batch_sign_off_all_locks() -> None:
    """
    # @req:Trace-14
    Test that different lock levels (trial, visit, subject, form) reject sign-off write and roll back atomically.
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Trial Lock
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

        TrialLockManager.lock_trial("Security breach simulation")
        try:
            with pytest.raises(PermissionError, match="Trial is currently locked"):
                await client.post(
                    action_path,
                    json=payload,
                    headers=get_auth_headers(
                        roles="pi", action=action_path, payload=payload
                    ),
                )
        finally:
            TrialLockManager.reset()

        # 2. Visit Lock
        TrialLockManager.lock_visit("VISIT-001")
        try:
            with pytest.raises(
                PermissionError, match="Visit VISIT-001 is currently locked"
            ):
                await client.post(
                    action_path,
                    json=payload,
                    headers=get_auth_headers(
                        roles="pi", action=action_path, payload=payload
                    ),
                )
        finally:
            TrialLockManager.reset()

        # 3. Subject Lock
        TrialLockManager.lock_subject("SUBJ-001")
        try:
            with pytest.raises(
                PermissionError, match="Subject SUBJ-001 is currently locked"
            ):
                await client.post(
                    action_path,
                    json=payload,
                    headers=get_auth_headers(
                        roles="pi", action=action_path, payload=payload
                    ),
                )
        finally:
            TrialLockManager.reset()

        # 4. Form Lock
        TrialLockManager.lock_form("FORM-001")
        try:
            with pytest.raises(
                PermissionError, match="Form FORM-001 is currently locked"
            ):
                await client.post(
                    action_path,
                    json=payload,
                    headers=get_auth_headers(
                        roles="pi", action=action_path, payload=payload
                    ),
                )
        finally:
            TrialLockManager.reset()

        # Verify that sub was NOT approved and proper rollback occurred across all tests
        async with db_manager.get_session_maker()() as session:
            stmt = select(FormSubmission).where(FormSubmission.id == sub_id)
            res_db = await session.execute(stmt)
            sub_db = res_db.scalar_one()
            assert sub_db.status == "COMPLETED"


@pytest.mark.asyncio
async def test_batch_sign_off_non_lock_rollback(monkeypatch) -> None:
    """
    # @req:Trace-14
    Test that a non-lock exception raised during processing rolls back changes to all processed submissions.
    """
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

        # Mock generate_canonical_signature to raise ValueError on the second submission
        call_count = 0
        from apps.execution.main import generate_canonical_signature as orig_gen_sig

        def mock_gen_sig(binding, secret):
            nonlocal call_count
            call_count += 1
            if call_count == 2:
                raise ValueError("Simulated signature failure")
            return orig_gen_sig(binding, secret)

        monkeypatch.setattr(
            "apps.execution.main.generate_canonical_signature", mock_gen_sig
        )

        with pytest.raises(ValueError, match="Simulated signature failure"):
            await client.post(
                action_path,
                json=payload,
                headers=get_auth_headers(
                    roles="pi", action=action_path, payload=payload
                ),
            )

        # Verify that both remain COMPLETED
        async with db_manager.get_session_maker()() as session:
            stmt = select(FormSubmission).where(FormSubmission.id.in_([id1, id2]))
            res_db = await session.execute(stmt)
            subs = {s.id: s for s in res_db.scalars().all()}
            assert subs[id1].status == "COMPLETED"
            assert subs[id2].status == "COMPLETED"


@pytest.mark.asyncio
async def test_batch_sign_off_audit_manifestation_capture() -> None:
    """
    # @req:Trace-14
    Test that batch sign-off captures signature manifestation and version details in database AuditLog records.
    """
    from apps.execution.database.models import AuditLog

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
                version=1,
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
            headers=get_auth_headers(
                user_id="pi_user", roles="pi", action=action_path, payload=payload
            ),
        )
        assert res.status_code == 200

        async with db_manager.get_session_maker()() as session:
            stmt = select(AuditLog).where(
                AuditLog.table_name == "form_submissions",
                AuditLog.record_id == str(sub_id),
                AuditLog.action == "UPDATE",
            )
            res_db = await session.execute(stmt)
            logs = res_db.scalars().all()

            assert len(logs) == 1
            audit_entry = logs[0]
            assert audit_entry.user_id == "pi_user"
            assert audit_entry.version_index == 2

            new_vals = audit_entry.new_values
            assert new_vals["status"] == "APPROVED"
            assert "signature_manifest" in new_vals

            manifest = new_vals["signature_manifest"]
            assert manifest["signer_id"] == "pi_user"
            assert manifest["signed_version"] == 2

            assert "signature_manifestation" in manifest
            manifestation = manifest["signature_manifestation"]
            assert manifestation["signer_username"] == "pi_user"
            assert manifestation["signing_reason_code"] == "PI_APPROVAL"
            assert manifestation["record_id"] == sub_id
            assert manifestation["record_version"] == 2
