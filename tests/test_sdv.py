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


@pytest.fixture(autouse=True)
async def setup_test_db():
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
    # @req:PRD-QRY-005
    """
    Test CRA/monitor role-based access control, invalid/missing targets,
    consistent study/subject combinations, field verification metadata populating,
    deterministic repeat sign-off behavior, and correct persistence of PAGE/VISIT SDVSignOff records.
    """
    # 1. Populate DB with test subject, visit, and observations
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
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
    # @req:PRD-QRY-006
    """
    Test that editing verified clinical value representations (value, value_string, normalized_value)
    automatically drops verification state, clears verifier metadata, drops matching field-level
    sign-off with dropped details, fails without a GxP change reason, triggers audit evidence on success,
    does not drop on metadata-only changes, and sends expected mockable dashboard notifications.
    """
    # 1. Populate DB with a verified observation and a matching field-level sign-off
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
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
    with patch(
        "apps.execution.trial_lock.NotificationRouter.send_dashboard_notification",
        mock_notify,
    ):
        with audit_context(
            user_id="editor-user",
            change_reason="Clinical value updated by lab coordinator",
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
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', 1);")
            )
            res_obs = await session.execute(
                select(ClinicalObservation).where(
                    ClinicalObservation.id == "OBS-DROP-1"
                )
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
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', 1);")
            )
            res_obs = await session.execute(
                select(ClinicalObservation).where(
                    ClinicalObservation.id == "OBS-DROP-1"
                )
            )
            o = res_obs.scalar_one()
            o.is_sdv_verified = True
            o.sdv_verified_by = "CRA-VERIFIER"
            o.sdv_verified_at = datetime.now(UTC)

    # 6. Editing 'normalized_value' triggers the drop
    with audit_context(
        user_id="editor-user",
        change_reason="Change normalized clinical value representation",
    ):
        async with db_manager.get_session_maker()() as session:
            res = await session.execute(
                select(ClinicalObservation).where(
                    ClinicalObservation.id == "OBS-DROP-1"
                )
            )
            obs_edit = res.scalar_one()
            obs_edit.normalized_value = "8.80"
            await session.commit()

    async with db_manager.get_session_maker()() as session:
        res_obs = await session.execute(
            select(ClinicalObservation).where(ClinicalObservation.id == "OBS-DROP-1")
        )
        assert res_obs.scalar_one().is_sdv_verified is False
