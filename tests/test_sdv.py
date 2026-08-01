import hashlib
import hmac
import json
import time
from datetime import UTC, datetime
from unittest.mock import MagicMock, patch

import httpx
import pytest
from sqlalchemy import select, text

from apps.execution.database.core import db_manager
from apps.execution.database.migrate import deploy_database_triggers
from apps.execution.database.models import (
    AuditLog,
    Base,
    ClinicalObservation,
    ClinicalQuery,
    ClinicalSubject,
    ClinicalVisit,
    SDVSignOff,
)
from apps.execution.main import app
from apps.execution.trial_lock import TrialLockManager
from packages.security.context import audit_context

GATEWAY_SECRET = "internal-gateway-secret-12345"  # pragma: allowlist secret


def get_v2_auth_headers(
    user_id: str = "test_user",
    roles: str = "CRA",
    change_reason: str = "test operation",
) -> dict[str, str]:
    """Generate Gateway signature version 2 authentication headers."""
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


def get_bulk_sdv_auth_headers(
    user_id: str = "test_user",
    roles: str = "CRA",
    change_reason: str = "test operation",
    payload: dict = None,
    token_payload: dict = None,
    omit_batch_id: bool = False,
) -> dict[str, str]:
    """Generate Gateway authentication headers and an X-Sig-Token bound to a bulk SDV request."""
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

    sig_payload = {
        "sub": user_id,
        "username": user_id,
        "action": "/api/v1/execution/sdv/bulk-sign-off",
        "semantic_action": "execution.sdv.bulk_signoff",
        "roles": [roles],
        "iat": time.time(),
        "exp": time.time() + 300.0,
    }

    p = token_payload if token_payload is not None else payload
    if p is not None and not omit_batch_id:
        study_id = (
            str(p.get("study_id")).strip() if p.get("study_id") is not None else ""
        )
        scope = str(p.get("scope")).strip() if p.get("scope") is not None else ""
        target_ids = p.get("target_ids", [])
        sorted_ids = sorted([str(tid).strip() for tid in target_ids])
        reason = (
            str(p.get("reason_for_change")).strip()
            if p.get("reason_for_change") is not None
            else ""
        )
        binding_str = f"{study_id}:{scope}:{sorted_ids}:{reason}"
        batch_id = hashlib.sha256(binding_str.encode("utf-8")).hexdigest()
        sig_payload["batch_id"] = batch_id

    sig_token = jwt.encode(sig_payload, GATEWAY_SECRET, algorithm="HS256")
    headers["X-Sig-Token"] = sig_token
    return headers


def get_auth_headers(
    user_id: str = "test_user",
    roles: str = "CRA",
    change_reason: str = "test operation",
) -> dict[str, str]:
    """Alias for get_v2_auth_headers to match the standard signature helper naming."""
    return get_v2_auth_headers(
        user_id=user_id, roles=roles, change_reason=change_reason
    )


@pytest.fixture(autouse=True)
async def setup_db():
    TrialLockManager.reset()
    db_manager.init_db(
        "sqlite+aiosqlite:///:memory:",
        echo=False,
    )
    async with db_manager.engine.begin() as conn:
        if db_manager.engine.dialect.name == "postgresql":
            await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))
        await conn.run_sync(Base.metadata.create_all)
        await deploy_database_triggers(conn, db_manager.engine.dialect.name)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()
    TrialLockManager.reset()


@pytest.mark.asyncio
async def test_sdv_signoff_endpoints_rbac_and_target_validation():
    # @Req:PRD-QRY-005
    # @req:PRD-QRY-005
    """
    Test CRA/monitor role-based access control, invalid/missing targets,
    consistent study/subject combinations, field verification metadata populating,
    deterministic repeat sign-off behavior, and correct persistence of PAGE/VISIT SDVSignOff records.
    """
    # 1. Populate DB with test subject, visit, and observations
    async with db_manager.get_session_maker()() as session, session.begin():
        await session.execute(
            text("SELECT set_config('cadence.app_writing', 'true', 1);")
        )
        subj = ClinicalSubject(
            subject_id="SUBJ-SDV-1", study_id="STUDY-SDV-TEST", site_id="SITE-SDV-1"
        )
        session.add(subj)

        visit = ClinicalVisit(
            id="VISIT-SDV-1",
            subject_id="SUBJ-SDV-1",
            study_id="STUDY-SDV-TEST",
            visit_name="Screening",
        )
        session.add(visit)

        obs = ClinicalObservation(
            id="OBS-SDV-1",
            subject_id="SUBJ-SDV-1",
            study_id="STUDY-SDV-TEST",
            visit_id="VISIT-SDV-1",
            page_id="PAGE-SDV-1",
            domain="VS",
            test_code="SYSBP",
            test_name="Systolic Blood Pressure",
            value=120.0,
        )
        session.add(obs)

    # 2. RBAC checks: Only allowed roles can sign off (CRA, monitor). Site Investigator should be forbidden (403)
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        resp = await client.post(
            "/api/v1/execution/sdv/signoff",
            json={
                "scope": "FIELD",
                "target_id": "OBS-SDV-1",
                "subject_id": "SUBJ-SDV-1",
                "study_id": "STUDY-SDV-TEST",
            },
            headers=get_v2_auth_headers(roles="Site Investigator"),
        )
        assert resp.status_code == 403

        resp = await client.post(
            "/api/v1/execution/sdv/signoff",
            json={
                "scope": "FIELD",
                "target_id": "OBS-SDV-1",
                "subject_id": "SUBJ-SDV-1",
                "study_id": "STUDY-SDV-TEST",
            },
            headers=get_v2_auth_headers(roles="Data Manager"),
        )
        assert resp.status_code == 403

    # 3. Target consistency: Invalid subject or inconsistent study/subject combinations -> 404
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Inconsistent subject/study
        resp = await client.post(
            "/api/v1/execution/sdv/signoff",
            json={
                "scope": "FIELD",
                "target_id": "OBS-SDV-1",
                "subject_id": "SUBJ-SDV-1",
                "study_id": "STUDY-SDV-WRONG",
            },
            headers=get_v2_auth_headers(roles="CRA"),
        )
        assert resp.status_code == 404

        # Nonexistent observation id for FIELD scope
        resp = await client.post(
            "/api/v1/execution/sdv/signoff",
            json={
                "scope": "FIELD",
                "target_id": "OBS-NONEXISTENT",
                "subject_id": "SUBJ-SDV-1",
                "study_id": "STUDY-SDV-TEST",
            },
            headers=get_v2_auth_headers(roles="CRA"),
        )
        assert resp.status_code == 404

        # Nonexistent visit id for VISIT scope
        resp = await client.post(
            "/api/v1/execution/sdv/signoff",
            json={
                "scope": "VISIT",
                "target_id": "VISIT-NONEXISTENT",
                "subject_id": "SUBJ-SDV-1",
                "study_id": "STUDY-SDV-TEST",
            },
            headers=get_v2_auth_headers(roles="CRA"),
        )
        assert resp.status_code == 404

        # Nonexistent page id for PAGE scope
        resp = await client.post(
            "/api/v1/execution/sdv/signoff",
            json={
                "scope": "PAGE",
                "target_id": "PAGE-NONEXISTENT",
                "subject_id": "SUBJ-SDV-1",
                "study_id": "STUDY-SDV-TEST",
            },
            headers=get_v2_auth_headers(roles="CRA"),
        )
        assert resp.status_code == 404

    # 4. Successful FIELD sign-off with verifier and timestamp
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        resp = await client.post(
            "/api/v1/execution/sdv/signoff",
            json={
                "scope": "FIELD",
                "target_id": "OBS-SDV-1",
                "subject_id": "SUBJ-SDV-1",
                "study_id": "STUDY-SDV-TEST",
            },
            headers=get_v2_auth_headers(user_id="CRA-A", roles="CRA"),
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["is_verified"] is True
        assert data["verified_by"] == "CRA-A"
        assert data["verified_at"] is not None

    async with db_manager.get_session_maker()() as session:
        res = await session.execute(
            select(ClinicalObservation).where(ClinicalObservation.id == "OBS-SDV-1")
        )
        obs_db = res.scalar_one()
        assert obs_db.is_sdv_verified is True
        assert obs_db.sdv_verified_by == "CRA-A"
        assert obs_db.sdv_verified_at is not None

    # 5. Deterministic repeat sign-off (idempotency/updating)
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        resp = await client.post(
            "/api/v1/execution/sdv/signoff",
            json={
                "scope": "FIELD",
                "target_id": "OBS-SDV-1",
                "subject_id": "SUBJ-SDV-1",
                "study_id": "STUDY-SDV-TEST",
            },
            headers=get_v2_auth_headers(user_id="CRA-B", roles="monitor"),
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["verified_by"] == "CRA-B"

    # Ensure duplicate records were not created in the database
    async with db_manager.get_session_maker()() as session:
        res = await session.execute(
            select(SDVSignOff).where(
                SDVSignOff.scope == "FIELD", SDVSignOff.target_id == "OBS-SDV-1"
            )
        )
        signoffs = res.scalars().all()
        assert len(signoffs) == 1
        assert signoffs[0].verified_by == "CRA-B"

    # 6. Correct persistence and querying of page and visit SDVSignOff records
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Sign off visit
        resp_visit = await client.post(
            "/api/v1/execution/sdv/signoff",
            json={
                "scope": "VISIT",
                "target_id": "VISIT-SDV-1",
                "subject_id": "SUBJ-SDV-1",
                "study_id": "STUDY-SDV-TEST",
            },
            headers=get_v2_auth_headers(user_id="CRA-V"),
        )
        assert resp_visit.status_code == 200

        # Sign off page
        resp_page = await client.post(
            "/api/v1/execution/sdv/signoff",
            json={
                "scope": "PAGE",
                "target_id": "PAGE-SDV-1",
                "subject_id": "SUBJ-SDV-1",
                "study_id": "STUDY-SDV-TEST",
            },
            headers=get_v2_auth_headers(user_id="CRA-P"),
        )
        assert resp_page.status_code == 200

    async with db_manager.get_session_maker()() as session:
        # Query visit sign-off
        res_v = await session.execute(
            select(SDVSignOff).where(
                SDVSignOff.scope == "VISIT", SDVSignOff.target_id == "VISIT-SDV-1"
            )
        )
        so_visit = res_v.scalar_one()
        assert so_visit.is_verified is True
        assert so_visit.verified_by == "CRA-V"

        # Query page sign-off
        res_p = await session.execute(
            select(SDVSignOff).where(
                SDVSignOff.scope == "PAGE", SDVSignOff.target_id == "PAGE-SDV-1"
            )
        )
        so_page = res_p.scalar_one()
        assert so_page.is_verified is True
        assert so_page.verified_by == "CRA-P"


@pytest.mark.asyncio
async def test_sdv_automatic_verification_drop_compliance():
    # @Req:PRD-QRY-006
    # @req:PRD-QRY-006
    """
    Test that editing verified clinical value representations (value, value_string, normalized_value)
    automatically drops verification state, clears verifier metadata, drops matching field-level
    sign-off with dropped details, fails without a GxP change reason, triggers audit evidence on success,
    does not drop on metadata-only changes, and sends expected mockable dashboard notifications.
    """
    # 1. Populate DB with a verified observation and a matching field-level sign-off
    async with db_manager.get_session_maker()() as session, session.begin():
        await session.execute(
            text("SELECT set_config('cadence.app_writing', 'true', 1);")
        )
        subj = ClinicalSubject(
            subject_id="SUBJ-DROP-1",
            study_id="STUDY-DROP-TEST",
            site_id="SITE-DROP-1",
        )
        session.add(subj)

        obs = ClinicalObservation(
            id="OBS-DROP-1",
            subject_id="SUBJ-DROP-1",
            study_id="STUDY-DROP-TEST",
            visit_id="VISIT-DROP-1",
            page_id="PAGE-DROP-1",
            domain="LB",
            test_code="WBC",
            test_name="White Blood Cells",
            value=6.5,
            value_string="6.5",
            normalized_value="6.5",
            is_sdv_verified=True,
            sdv_verified_by="CRA-VERIFIER",
            sdv_verified_at=datetime.now(UTC),
        )
        session.add(obs)

        signoff = SDVSignOff(
            scope="FIELD",
            target_id="OBS-DROP-1",
            subject_id="SUBJ-DROP-1",
            study_id="STUDY-DROP-TEST",
            is_verified=True,
            verified_by="CRA-VERIFIER",
            verified_at=datetime.now(UTC),
        )
        session.add(signoff)

    # 2. Editing without a GxP change reason (or with empty/default reasons) must fail
    for bad_reason in ["", "system_operation", "default_reason", "   "]:
        with pytest.raises(ValueError, match="GxP change reason is required"):
            with audit_context(user_id="editor-user", change_reason=bad_reason):
                async with db_manager.get_session_maker()() as session:
                    res = await session.execute(
                        select(ClinicalObservation).where(
                            ClinicalObservation.id == "OBS-DROP-1"
                        )
                    )
                    obs_edit = res.scalar_one()
                    obs_edit.value = 7.0
                    await session.commit()

    # 3. Metadata-only changes (like page_id) should NOT trigger a verification drop
    with audit_context(user_id="editor-user", change_reason="Change form page layout"):
        async with db_manager.get_session_maker()() as session:
            res = await session.execute(
                select(ClinicalObservation).where(
                    ClinicalObservation.id == "OBS-DROP-1"
                )
            )
            obs_edit = res.scalar_one()
            obs_edit.page_id = "PAGE-NEW-1"
            await session.commit()

    # Verify that observation is still verified
    async with db_manager.get_session_maker()() as session:
        res = await session.execute(
            select(ClinicalObservation).where(ClinicalObservation.id == "OBS-DROP-1")
        )
        obs_db = res.scalar_one()
        assert obs_db.is_sdv_verified is True
        assert obs_db.sdv_verified_by == "CRA-VERIFIER"
        assert obs_db.page_id == "PAGE-NEW-1"

    # 4. Editing 'value' triggers the drop, clears metadata, drops sign-off, records audit log and notifications
    mock_notify = MagicMock()
    with (
        patch(
            "apps.execution.trial_lock.NotificationRouter.send_dashboard_notification",
            mock_notify,
        ),
        audit_context(
            user_id="editor-user",
            change_reason="Clinical value updated by lab coordinator",
        ),
    ):
        async with db_manager.get_session_maker()() as session:
            res = await session.execute(
                select(ClinicalObservation).where(
                    ClinicalObservation.id == "OBS-DROP-1"
                )
            )
            obs_edit = res.scalar_one()
            obs_edit.value = 8.8
            await session.commit()

    # Verify drop results for 'value' modification
    async with db_manager.get_session_maker()() as session:
        res_obs = await session.execute(
            select(ClinicalObservation).where(ClinicalObservation.id == "OBS-DROP-1")
        )
        obs_final = res_obs.scalar_one()
        assert obs_final.is_sdv_verified is False
        assert obs_final.sdv_verified_by is None
        assert obs_final.sdv_verified_at is None

        # Sign-off dropped details
        res_so = await session.execute(
            select(SDVSignOff).where(SDVSignOff.target_id == "OBS-DROP-1")
        )
        so_final = res_so.scalar_one()
        assert so_final.is_verified is False
        assert so_final.dropped_reason == "Clinical value updated by lab coordinator"
        assert so_final.dropped_at is not None

        # Audit log entries
        res_audit = await session.execute(
            select(AuditLog).where(
                AuditLog.table_name == "sdv_sign_offs", AuditLog.action == "UPDATE"
            )
        )
        audit_recs = res_audit.scalars().all()
        assert len(audit_recs) >= 1
        assert audit_recs[-1].new_values["is_verified"] is False
        assert (
            audit_recs[-1].change_reason == "Clinical value updated by lab coordinator"
        )

    # Dashboard notification assertion
    mock_notify.assert_called_once()
    recipients, payload = mock_notify.call_args[0]
    assert recipients == ["CRA-VERIFIER"]
    assert (
        "Previously verified field modified on Subject SUBJ-DROP-1 - Visit VISIT-DROP-1"
        in payload["message"]
    )
    assert payload["study_id"] == "STUDY-DROP-TEST"
    assert payload["subject_id"] == "SUBJ-DROP-1"
    assert payload["visit_id"] == "VISIT-DROP-1"
    assert payload["observation_id"] == "OBS-DROP-1"
    assert payload["editor"] == "editor-user"
    assert payload["change_reason"] == "Clinical value updated by lab coordinator"

    # Reset verification status for next checks (value_string and normalized_value edits)
    async with db_manager.get_session_maker()() as session, session.begin():
        await session.execute(
            text("SELECT set_config('cadence.app_writing', 'true', 1);")
        )
        res_obs = await session.execute(
            select(ClinicalObservation).where(ClinicalObservation.id == "OBS-DROP-1")
        )
        o = res_obs.scalar_one()
        o.is_sdv_verified = True
        o.sdv_verified_by = "CRA-VERIFIER"
        o.sdv_verified_at = datetime.now(UTC)

        res_so = await session.execute(
            select(SDVSignOff).where(SDVSignOff.target_id == "OBS-DROP-1")
        )
        so = res_so.scalar_one()
        so.is_verified = True
        so.dropped_reason = None
        so.dropped_at = None

    # 5. Editing 'value_string' triggers the drop
    with audit_context(
        user_id="editor-user", change_reason="Change string representation"
    ):
        async with db_manager.get_session_maker()() as session:
            res = await session.execute(
                select(ClinicalObservation).where(
                    ClinicalObservation.id == "OBS-DROP-1"
                )
            )
            obs_edit = res.scalar_one()
            obs_edit.value_string = "8.8 mmol/L"
            await session.commit()

    async with db_manager.get_session_maker()() as session:
        res_obs = await session.execute(
            select(ClinicalObservation).where(ClinicalObservation.id == "OBS-DROP-1")
        )
        assert res_obs.scalar_one().is_sdv_verified is False

    # Reset verification status for the last check
    async with db_manager.get_session_maker()() as session, session.begin():
        await session.execute(
            text("SELECT set_config('cadence.app_writing', 'true', 1);")
        )
        res_obs = await session.execute(
            select(ClinicalObservation).where(ClinicalObservation.id == "OBS-DROP-1")
        )
        o = res_obs.scalar_one()
        o.is_sdv_verified = True
        o.sdv_verified_by = "CRA-VERIFIER"
        o.sdv_verified_at = datetime.now(UTC)


@pytest.mark.asyncio
async def test_bulk_sdv_signoff_happy_path():
    """
    # @Req:PRD-SYS-001
    # @req:PRD-SYS-001
    Verify that a valid bulk sign-off request upserts the expected records and creates audit logs.
    """
    # 1. Populate DB with test subject, visit, and observations
    async with db_manager.get_session_maker()() as session, session.begin():
        await session.execute(
            text("SELECT set_config('cadence.app_writing', 'true', 1);")
        )
        subj = ClinicalSubject(
            subject_id="SUBJ-BULK-1",
            study_id="STUDY-BULK-TEST",
            site_id="SITE-BULK-1",
        )
        session.add(subj)

        visit = ClinicalVisit(
            id="VISIT-BULK-1",
            subject_id="SUBJ-BULK-1",
            study_id="STUDY-BULK-TEST",
            visit_name="Baseline",
        )
        session.add(visit)

        obs1 = ClinicalObservation(
            id="OBS-BULK-1",
            subject_id="SUBJ-BULK-1",
            study_id="STUDY-BULK-TEST",
            visit_id="VISIT-BULK-1",
            page_id="PAGE-BULK-1",
            domain="VS",
            test_code="SYSBP",
            test_name="Systolic Blood Pressure",
            value=120.0,
        )
        obs2 = ClinicalObservation(
            id="OBS-BULK-2",
            subject_id="SUBJ-BULK-1",
            study_id="STUDY-BULK-TEST",
            visit_id="VISIT-BULK-1",
            page_id="PAGE-BULK-1",
            domain="VS",
            test_code="DIABP",
            test_name="Diastolic Blood Pressure",
            value=80.0,
        )
        session.add_all([obs1, obs2])

    payload = {
        "study_id": "STUDY-BULK-TEST",
        "subject_id": "SUBJ-BULK-1",
        "scope": "FIELD",
        "target_ids": ["OBS-BULK-1", "OBS-BULK-2"],
        "reason_for_change": "Verification of vital signs.",
        "site_id": "SITE-BULK-1",
    }

    # 2. Send valid request with get_bulk_sdv_auth_headers and a matching X-Sig-Token
    headers = get_bulk_sdv_auth_headers(
        user_id="CRA-USER-1",
        roles="CRA",
        change_reason="Bulk verification test",
        payload=payload,
    )

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        resp = await client.post(
            "/api/v1/execution/sdv/bulk-sign-off",
            json=payload,
            headers=headers,
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["signed_count"] == 2
        assert set(data["signed_target_ids"]) == {"OBS-BULK-1", "OBS-BULK-2"}
        assert data["content_digest"] is not None

    # 3. Assert that multiple SDVSignOff rows are upserted with is_verified=True
    async with db_manager.get_session_maker()() as session:
        res = await session.execute(
            select(SDVSignOff).where(
                SDVSignOff.scope == "FIELD",
                SDVSignOff.subject_id == "SUBJ-BULK-1",
                SDVSignOff.study_id == "STUDY-BULK-TEST",
            )
        )
        signoffs = res.scalars().all()
        assert len(signoffs) == 2
        for so in signoffs:
            assert so.is_verified is True
            assert so.verified_by == "CRA-USER-1"

        # 4. For FIELD scope, assert that the matching ClinicalObservation rows set is_sdv_verified, sdv_verified_by, and sdv_verified_at
        res_obs = await session.execute(
            select(ClinicalObservation).where(
                ClinicalObservation.id.in_(["OBS-BULK-1", "OBS-BULK-2"])
            )
        )
        observations = res_obs.scalars().all()
        assert len(observations) == 2
        for obs in observations:
            assert obs.is_sdv_verified is True
            assert obs.sdv_verified_by == "CRA-USER-1"
            assert obs.sdv_verified_at is not None

        # 5. Assert that AuditLog rows exist for the affected sdv_sign_offs records
        res_audit = await session.execute(
            select(AuditLog).where(
                AuditLog.table_name == "sdv_sign_offs", AuditLog.action == "INSERT"
            )
        )
        audit_records = res_audit.scalars().all()
        assert len(audit_records) >= 2
        targets_logged = {rec.new_values.get("target_id") for rec in audit_records}
        assert "OBS-BULK-1" in targets_logged
        assert "OBS-BULK-2" in targets_logged


@pytest.mark.asyncio
async def test_bulk_sdv_signoff_rbac_and_idempotency():
    """
    # @Req:PRD-SYS-001
    # @req:PRD-SYS-001
    Verify role gating and safe re-submission.
    """
    # Populate DB with test subject and multiple observations for isolation
    async with db_manager.get_session_maker()() as session, session.begin():
        await session.execute(
            text("SELECT set_config('cadence.app_writing', 'true', 1);")
        )
        subj = ClinicalSubject(
            subject_id="SUBJ-RBAC-1",
            study_id="STUDY-RBAC-TEST",
            site_id="SITE-RBAC-1",
        )
        session.add(subj)

        obs_cra = ClinicalObservation(
            id="OBS-RBAC-CRA",
            subject_id="SUBJ-RBAC-1",
            study_id="STUDY-RBAC-TEST",
            visit_id="VISIT-RBAC-1",
            page_id="PAGE-RBAC-1",
            domain="VS",
            test_code="SYSBP",
            test_name="Systolic Blood Pressure",
            value=120.0,
        )
        obs_mon = ClinicalObservation(
            id="OBS-RBAC-MON",
            subject_id="SUBJ-RBAC-1",
            study_id="STUDY-RBAC-TEST",
            visit_id="VISIT-RBAC-1",
            page_id="PAGE-RBAC-1",
            domain="VS",
            test_code="SYSBP",
            test_name="Systolic Blood Pressure",
            value=120.0,
        )
        obs_pi = ClinicalObservation(
            id="OBS-RBAC-PI",
            subject_id="SUBJ-RBAC-1",
            study_id="STUDY-RBAC-TEST",
            visit_id="VISIT-RBAC-1",
            page_id="PAGE-RBAC-1",
            domain="VS",
            test_code="SYSBP",
            test_name="Systolic Blood Pressure",
            value=120.0,
        )
        obs_dm = ClinicalObservation(
            id="OBS-RBAC-DM",
            subject_id="SUBJ-RBAC-1",
            study_id="STUDY-RBAC-TEST",
            visit_id="VISIT-RBAC-1",
            page_id="PAGE-RBAC-1",
            domain="VS",
            test_code="SYSBP",
            test_name="Systolic Blood Pressure",
            value=120.0,
        )
        obs_idem = ClinicalObservation(
            id="OBS-RBAC-IDEM",
            subject_id="SUBJ-RBAC-1",
            study_id="STUDY-RBAC-TEST",
            visit_id="VISIT-RBAC-1",
            page_id="PAGE-RBAC-1",
            domain="VS",
            test_code="SYSBP",
            test_name="Systolic Blood Pressure",
            value=120.0,
        )
        session.add_all([obs_cra, obs_mon, obs_pi, obs_dm, obs_idem])

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Assert that a request with role CRA succeeds
        payload_cra = {
            "study_id": "STUDY-RBAC-TEST",
            "subject_id": "SUBJ-RBAC-1",
            "scope": "FIELD",
            "target_ids": ["OBS-RBAC-CRA"],
            "reason_for_change": "RBAC CRA verification check",
            "site_id": "SITE-RBAC-1",
        }
        headers_cra = get_bulk_sdv_auth_headers(roles="CRA", payload=payload_cra)
        resp = await client.post(
            "/api/v1/execution/sdv/bulk-sign-off",
            json=payload_cra,
            headers=headers_cra,
        )
        assert resp.status_code == 200

        # 2. Assert that a request with role monitor (raw string) succeeds
        payload_mon = {
            "study_id": "STUDY-RBAC-TEST",
            "subject_id": "SUBJ-RBAC-1",
            "scope": "FIELD",
            "target_ids": ["OBS-RBAC-MON"],
            "reason_for_change": "RBAC Monitor verification check",
            "site_id": "SITE-RBAC-1",
        }
        headers_monitor = get_bulk_sdv_auth_headers(
            roles="monitor", payload=payload_mon
        )
        resp = await client.post(
            "/api/v1/execution/sdv/bulk-sign-off",
            json=payload_mon,
            headers=headers_monitor,
        )
        assert resp.status_code == 200

        # 3. Assert that a request with role Site Investigator returns 403
        payload_pi = {
            "study_id": "STUDY-RBAC-TEST",
            "subject_id": "SUBJ-RBAC-1",
            "scope": "FIELD",
            "target_ids": ["OBS-RBAC-PI"],
            "reason_for_change": "RBAC PI check",
            "site_id": "SITE-RBAC-1",
        }
        headers_pi = get_bulk_sdv_auth_headers(
            roles="Site Investigator", payload=payload_pi
        )
        resp = await client.post(
            "/api/v1/execution/sdv/bulk-sign-off",
            json=payload_pi,
            headers=headers_pi,
        )
        assert resp.status_code == 403

        # 4. Assert that a request with role Data Manager returns 403
        payload_dm = {
            "study_id": "STUDY-RBAC-TEST",
            "subject_id": "SUBJ-RBAC-1",
            "scope": "FIELD",
            "target_ids": ["OBS-RBAC-DM"],
            "reason_for_change": "RBAC DM check",
            "site_id": "SITE-RBAC-1",
        }
        headers_dm = get_bulk_sdv_auth_headers(roles="Data Manager", payload=payload_dm)
        resp = await client.post(
            "/api/v1/execution/sdv/bulk-sign-off",
            json=payload_dm,
            headers=headers_dm,
        )
        assert resp.status_code == 403

        # 5. Send the same valid request twice and assert that no duplicate SDVSignOff rows are created
        payload_idem = {
            "study_id": "STUDY-RBAC-TEST",
            "subject_id": "SUBJ-RBAC-1",
            "scope": "FIELD",
            "target_ids": ["OBS-RBAC-IDEM"],
            "reason_for_change": "Idempotency check",
            "site_id": "SITE-RBAC-1",
        }

        # Re-verify OBS-RBAC-IDEM with CRA role (first time)
        headers_cra_1 = get_bulk_sdv_auth_headers(
            user_id="CRA-1", roles="CRA", payload=payload_idem
        )
        resp1 = await client.post(
            "/api/v1/execution/sdv/bulk-sign-off",
            json=payload_idem,
            headers=headers_cra_1,
        )
        assert resp1.status_code == 200
        assert resp1.json()["signed_count"] == 1

        # Re-send the exact same valid request (second time)
        headers_cra_2 = get_bulk_sdv_auth_headers(
            user_id="CRA-1", roles="CRA", payload=payload_idem
        )
        resp2 = await client.post(
            "/api/v1/execution/sdv/bulk-sign-off",
            json=payload_idem,
            headers=headers_cra_2,
        )
        assert resp2.status_code == 200
        assert resp2.json()["signed_count"] == 0
        assert resp2.json()["skipped_target_ids"] == ["OBS-RBAC-IDEM"]

        # Ensure no duplicates in the database
        async with db_manager.get_session_maker()() as session:
            res_so = await session.execute(
                select(SDVSignOff).where(
                    SDVSignOff.target_id == "OBS-RBAC-IDEM", SDVSignOff.scope == "FIELD"
                )
            )
            signoffs = res_so.scalars().all()
            assert len(signoffs) == 1


@pytest.mark.asyncio
async def test_bulk_sdv_signoff_batch_binding_mismatch():
    """
    # @Req:PRD-SYS-001
    # @req:PRD-SYS-001
    Verify that a mismatched or missing token blocks the request and mutates nothing.
    """
    # Populate DB with test subject and observation
    async with db_manager.get_session_maker()() as session, session.begin():
        await session.execute(
            text("SELECT set_config('cadence.app_writing', 'true', 1);")
        )
        subj = ClinicalSubject(
            subject_id="SUBJ-MIS-1", study_id="STUDY-MIS-TEST", site_id="SITE-MIS-1"
        )
        session.add(subj)

        obs = ClinicalObservation(
            id="OBS-MIS-1",
            subject_id="SUBJ-MIS-1",
            study_id="STUDY-MIS-TEST",
            visit_id="VISIT-MIS-1",
            page_id="PAGE-MIS-1",
            domain="VS",
            test_code="SYSBP",
            test_name="Systolic Blood Pressure",
            value=120.0,
        )
        session.add(obs)

    correct_payload = {
        "study_id": "STUDY-MIS-TEST",
        "subject_id": "SUBJ-MIS-1",
        "scope": "FIELD",
        "target_ids": ["OBS-MIS-1"],
        "reason_for_change": "Mismatched validation check",
        "site_id": "SITE-MIS-1",
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Build one case per mismatch: wrong study_id, wrong scope, wrong target_ids, wrong reason_for_change, and missing batch_id.

        # Case 1: Wrong study_id
        mismatched_study = dict(correct_payload, study_id="STUDY-WRONG")
        headers = get_bulk_sdv_auth_headers(
            roles="CRA", payload=correct_payload, token_payload=mismatched_study
        )
        resp = await client.post(
            "/api/v1/execution/sdv/bulk-sign-off", json=correct_payload, headers=headers
        )
        assert resp.status_code == 401

        # Case 2: Wrong scope
        mismatched_scope = dict(correct_payload, scope="PAGE")
        headers = get_bulk_sdv_auth_headers(
            roles="CRA", payload=correct_payload, token_payload=mismatched_scope
        )
        resp = await client.post(
            "/api/v1/execution/sdv/bulk-sign-off", json=correct_payload, headers=headers
        )
        assert resp.status_code == 401

        # Case 3: Wrong target_ids
        mismatched_targets = dict(correct_payload, target_ids=["OBS-WRONG"])
        headers = get_bulk_sdv_auth_headers(
            roles="CRA", payload=correct_payload, token_payload=mismatched_targets
        )
        resp = await client.post(
            "/api/v1/execution/sdv/bulk-sign-off", json=correct_payload, headers=headers
        )
        assert resp.status_code == 401

        # Case 4: Wrong reason_for_change
        mismatched_reason = dict(
            correct_payload, reason_for_change="Different reason entirely"
        )
        headers = get_bulk_sdv_auth_headers(
            roles="CRA", payload=correct_payload, token_payload=mismatched_reason
        )
        resp = await client.post(
            "/api/v1/execution/sdv/bulk-sign-off", json=correct_payload, headers=headers
        )
        assert resp.status_code == 401

        # Case 5: Missing batch_id
        headers = get_bulk_sdv_auth_headers(
            roles="CRA", payload=correct_payload, omit_batch_id=True
        )
        resp = await client.post(
            "/api/v1/execution/sdv/bulk-sign-off", json=correct_payload, headers=headers
        )
        assert resp.status_code == 401

        # After each case, re-query the database and assert that no SDVSignOff row was created or modified.
        async with db_manager.get_session_maker()() as session:
            res_so = await session.execute(
                select(SDVSignOff).where(SDVSignOff.target_id == "OBS-MIS-1")
            )
            assert len(res_so.scalars().all()) == 0


@pytest.mark.asyncio
async def test_bulk_sdv_signoff_input_validation():
    """
    # @Req:PRD-SYS-001
    # @req:PRD-SYS-001
    Verify request-body validation for reason and target list.
    """
    # Populate DB with test subject
    async with db_manager.get_session_maker()() as session, session.begin():
        await session.execute(
            text("SELECT set_config('cadence.app_writing', 'true', 1);")
        )
        subj = ClinicalSubject(
            subject_id="SUBJ-VAL-1", study_id="STUDY-VAL-TEST", site_id="SITE-VAL-1"
        )
        session.add(subj)

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Case 1: Blank reason_for_change
        payload_blank_reason = {
            "study_id": "STUDY-VAL-TEST",
            "subject_id": "SUBJ-VAL-1",
            "scope": "FIELD",
            "target_ids": ["OBS-VAL-1"],
            "reason_for_change": "   ",
            "site_id": "SITE-VAL-1",
        }
        headers = get_bulk_sdv_auth_headers(roles="CRA", payload=payload_blank_reason)
        resp = await client.post(
            "/api/v1/execution/sdv/bulk-sign-off",
            json=payload_blank_reason,
            headers=headers,
        )
        assert resp.status_code == 400


@pytest.mark.asyncio
async def test_bulk_query_generation_happy_path(signed_headers):
    """
    Verify that a valid bulk query generation request creates queries,
    respects GxP Part 11 changes, and dispatches notifications.
    """
    # 1. Populate DB with test subject, visit, and observations
    async with db_manager.get_session_maker()() as session, session.begin():
        await session.execute(
            text("SELECT set_config('cadence.app_writing', 'true', 1);")
        )
        subj = ClinicalSubject(
            subject_id="SUBJ-QGEN-1",
            study_id="STUDY-QGEN-TEST",
            site_id="SITE-QGEN-1",
        )
        session.add(subj)

    payload = {
        "study_id": "STUDY-QGEN-TEST",
        "reason_for_change": "Discrepancy review.",
        "targets": [
            {
                "study_id": "STUDY-QGEN-TEST",
                "subject_id": "SUBJ-QGEN-1",
                "visit_id": "VISIT-1",
                "domain": "VS",
                "test_code": "SYSBP",
                "explanation": "Please verify high systolic BP value.",
            },
            {
                "study_id": "STUDY-QGEN-TEST",
                "subject_id": "SUBJ-QGEN-1",
                "visit_id": "VISIT-1",
                "domain": "VS",
                "test_code": "DIABP",
                "explanation": "Please verify low diastolic BP value.",
            },
        ],
    }

    from unittest.mock import AsyncMock

    mock_notify = AsyncMock()
    with (
        patch(
            "apps.execution.notifications_client.publish_notification",
            mock_notify,
        ),
    ):
        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app), base_url="http://test"
        ) as client:
            headers = signed_headers(
                user_id="CRA-USER-QGEN",
                roles="CRA",
                change_reason="Discrepancy review.",
                site_id="SITE-QGEN-1",
                study_id="STUDY-QGEN-TEST",
            )
            # Send bulk query generation request
            resp = await client.post(
                "/api/v1/execution/queries/generate",
                json=payload,
                headers=headers,
            )
            print("STATUS CODE:", resp.status_code)
            print("BODY:", resp.text)
            assert resp.status_code == 200
            data = resp.json()
            assert data["generated_count"] == 2
            assert len(data["generated_query_ids"]) == 2
            assert len(data["skipped_targets"]) == 0

    # Assert that queries are saved in DB
    async with db_manager.get_session_maker()() as session:
        res = await session.execute(
            select(ClinicalQuery).where(
                ClinicalQuery.subject_id == "SUBJ-QGEN-1",
                ClinicalQuery.study_id == "STUDY-QGEN-TEST",
            )
        )
        queries = res.scalars().all()
        assert len(queries) == 2
        for q in queries:
            assert q.status == "OPEN"
            assert q.origin == "manual"
            assert q.created_by == "CRA-USER-QGEN"

    # Assert that notifications were triggered
    assert mock_notify.call_count == 2


@pytest.mark.asyncio
async def test_bulk_query_generation_deduplication(signed_headers):
    """
    Verify that bulk query generation skips targets that already have an active query on the coordinates.
    """
    async with db_manager.get_session_maker()() as session, session.begin():
        await session.execute(
            text("SELECT set_config('cadence.app_writing', 'true', 1);")
        )
        subj = ClinicalSubject(
            subject_id="SUBJ-QDUP-1",
            study_id="STUDY-QDUP-TEST",
            site_id="SITE-QDUP-1",
        )
        session.add(subj)

        # Pre-existing active query on SYSBP
        existing_q = ClinicalQuery(
            id="qry_existing_1",
            study_id="STUDY-QDUP-TEST",
            site_id="SITE-QDUP-1",
            subject_id="SUBJ-QDUP-1",
            visit_id="VISIT-1",
            domain="VS",
            test_code="SYSBP",
            status="OPEN",
            origin="manual",
            explanation="Existing issue.",
            created_by="someone",
        )
        session.add(existing_q)

    payload = {
        "study_id": "STUDY-QDUP-TEST",
        "reason_for_change": "Discrepancy review.",
        "targets": [
            {
                "study_id": "STUDY-QDUP-TEST",
                "subject_id": "SUBJ-QDUP-1",
                "visit_id": "VISIT-1",
                "domain": "VS",
                "test_code": "SYSBP",
                "explanation": "Existing coordinate, should be skipped.",
            },
            {
                "study_id": "STUDY-QDUP-TEST",
                "subject_id": "SUBJ-QDUP-1",
                "visit_id": "VISIT-1",
                "domain": "VS",
                "test_code": "DIABP",
                "explanation": "New coordinate, should be generated.",
            },
        ],
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = signed_headers(
            user_id="CRA-USER-QDUP",
            roles="CRA",
            change_reason="Discrepancy review.",
            site_id="SITE-QDUP-1",
            study_id="STUDY-QDUP-TEST",
        )
        resp = await client.post(
            "/api/v1/execution/queries/generate",
            json=payload,
            headers=headers,
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["generated_count"] == 1
        assert len(data["skipped_targets"]) == 1
        assert data["skipped_targets"][0]["test_code"] == "SYSBP"


@pytest.mark.asyncio
async def test_bulk_query_generation_rbac_gating(signed_headers):
    """
    Verify role-based access control for bulk query generation.
    """
    payload = {
        "study_id": "STUDY-QRBAC-TEST",
        "reason_for_change": "Discrepancy review.",
        "targets": [
            {
                "study_id": "STUDY-QRBAC-TEST",
                "subject_id": "SUBJ-QRBAC-1",
                "visit_id": "VISIT-1",
                "domain": "VS",
                "test_code": "SYSBP",
                "explanation": "Will fail auth.",
            }
        ],
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Investigator (forbidden)
        headers_inv = signed_headers(
            user_id="INV-USER-QRBAC",
            roles="Site Investigator",
            change_reason="Discrepancy review.",
            site_id="SITE-QRBAC-1",
            study_id="STUDY-QRBAC-TEST",
        )
        resp = await client.post(
            "/api/v1/execution/queries/generate",
            json=payload,
            headers=headers_inv,
        )
        assert resp.status_code == 403

        # CRC (forbidden)
        headers_crc = signed_headers(
            user_id="CRC-USER-QRBAC",
            roles="CRC",
            change_reason="Discrepancy review.",
            site_id="SITE-QRBAC-1",
            study_id="STUDY-QRBAC-TEST",
        )
        resp = await client.post(
            "/api/v1/execution/queries/generate",
            json=payload,
            headers=headers_crc,
        )
        assert resp.status_code == 403


@pytest.mark.asyncio
async def test_bulk_query_generation_input_validation(signed_headers):
    """
    Verify input validation (empty targets, blank reason) for query generation.
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Blank reason
        payload_blank_reason = {
            "study_id": "STUDY-QVAL-TEST",
            "reason_for_change": "   ",
            "targets": [
                {
                    "study_id": "STUDY-QVAL-TEST",
                    "subject_id": "SUBJ-QVAL-1",
                    "visit_id": "VISIT-1",
                    "domain": "VS",
                    "test_code": "SYSBP",
                    "explanation": "Will fail.",
                }
            ],
        }
        headers_blank_reason = signed_headers(
            user_id="CRA-USER-QVAL",
            roles="CRA",
            change_reason="Valid reason.",
            site_id="SITE-QVAL-1",
            study_id="STUDY-QVAL-TEST",
        )
        # Note: X-Change-Reason in gateway signatures is validated before reaching route schemas,
        # but the JSON body reason_for_change is what gets blank here.
        resp = await client.post(
            "/api/v1/execution/queries/generate",
            json=payload_blank_reason,
            headers=headers_blank_reason,
        )
        assert resp.status_code == 400

        # 2. Empty targets list
        payload_empty_targets = {
            "study_id": "STUDY-QVAL-TEST",
            "reason_for_change": "Valid reason.",
            "targets": [],
        }
        resp = await client.post(
            "/api/v1/execution/queries/generate",
            json=payload_empty_targets,
            headers=headers_blank_reason,
        )
        assert resp.status_code == 400

        # Case 2: Empty target_ids list
        payload_empty_targets = {
            "study_id": "STUDY-VAL-TEST",
            "subject_id": "SUBJ-VAL-1",
            "scope": "FIELD",
            "target_ids": [],
            "reason_for_change": "Validation check",
            "site_id": "SITE-VAL-1",
        }
        headers = get_bulk_sdv_auth_headers(roles="CRA", payload=payload_empty_targets)
        resp = await client.post(
            "/api/v1/execution/sdv/bulk-sign-off",
            json=payload_empty_targets,
            headers=headers,
        )
        assert resp.status_code == 400
