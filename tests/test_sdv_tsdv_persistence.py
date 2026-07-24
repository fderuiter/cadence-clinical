import hashlib
import hmac
import json
import time
from datetime import datetime

import httpx
import pytest
from sqlalchemy import select, text

from apps.execution.database.core import db_manager
from apps.execution.database.migrate import deploy_database_triggers
from apps.execution.database.models import (
    AuditLog,
    Base,
    ClinicalObservation,
    SDVSignOff,
    TSDVConfig,
)
from apps.execution.main import app
from apps.execution.trial_lock import TrialLockManager

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
async def test_clinical_observation_sdv_defaults():
    # @req:PRD-QRY-005
    # @req:PRD-QRY-007
    """
    Verify that ClinicalObservation supports field-level SDV state columns and optional page grouping,
    and defaults correctly for backwards compatibility.
    """
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            # Silence DB triggers to let SQLAlchemy listener record the audit log
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', 1);")
            )
            obs = ClinicalObservation(
                subject_id="SUBJ-001",
                study_id="STUDY-XYZ",
                domain="VS",
                test_code="SYSBP",
                test_name="Systolic Blood Pressure",
                value=120.0,
            )
            session.add(obs)

    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(ClinicalObservation).where(
                ClinicalObservation.subject_id == "SUBJ-001"
            )
        )
        saved_obs = result.scalar_one()

        # Assert default behavior and nullability allows existing observations to remain valid
        assert saved_obs.is_sdv_verified is False
        assert saved_obs.sdv_verified_by is None
        assert saved_obs.sdv_verified_at is None
        assert saved_obs.page_id is None

        # Modify values and verify persistence
        await session.execute(
            text("SELECT set_config('cadence.app_writing', 'true', 1);")
        )
        saved_obs.is_sdv_verified = True
        saved_obs.sdv_verified_by = "CRA-007"
        saved_obs.sdv_verified_at = datetime(2026, 7, 28, 12, 0, 0)
        saved_obs.page_id = "FORM-VITAL-01"
        await session.commit()

    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(ClinicalObservation).where(
                ClinicalObservation.subject_id == "SUBJ-001"
            )
        )
        updated_obs = result.scalar_one()
        assert updated_obs.is_sdv_verified is True
        assert updated_obs.sdv_verified_by == "CRA-007"
        assert updated_obs.sdv_verified_at == datetime(2026, 7, 28, 12, 0, 0)
        assert updated_obs.page_id == "FORM-VITAL-01"


@pytest.mark.asyncio
async def test_sdv_sign_off_persistence_and_audit():
    # @req:PRD-QRY-005
    """
    Verify that SDVSignOff records aggregate sign-offs, respects defaults,
    inherits from AuditedModel, and registers triggers for audit trail capture.
    """
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            # Silence DB triggers to let SQLAlchemy listener record the audit log
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', 1);")
            )
            sign_off = SDVSignOff(
                scope="PAGE",
                target_id="PAGE-01",
                subject_id="SUBJ-001",
                study_id="STUDY-XYZ",
                is_verified=True,
                verified_by="CRA-123",
                verified_at=datetime(2026, 7, 28, 14, 0, 0),
            )
            session.add(sign_off)

    # Verify audit trail triggered on insert
    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(AuditLog).where(AuditLog.table_name == "sdv_sign_offs")
        )
        logs = result.scalars().all()
        assert len(logs) == 1
        insert_log = logs[0]
        assert insert_log.action == "INSERT"
        assert insert_log.new_values["scope"] == "PAGE"
        assert insert_log.new_values["target_id"] == "PAGE-01"
        assert insert_log.new_values["is_verified"] is True

    # Drop verification and check update auditing
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', 1);")
            )
            result = await session.execute(
                select(SDVSignOff).where(SDVSignOff.target_id == "PAGE-01")
            )
            sign_off = result.scalar_one()
            sign_off.is_verified = False
            sign_off.dropped_reason = "Data updated in source"
            sign_off.dropped_at = datetime(2026, 7, 28, 15, 0, 0)

    # Verify update auditing
    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(AuditLog)
            .where(AuditLog.table_name == "sdv_sign_offs")
            .order_by(AuditLog.timestamp)
        )
        logs = result.scalars().all()
        assert len(logs) == 2
        update_log = logs[1]
        assert update_log.action == "UPDATE"
        assert update_log.new_values["is_verified"] is False
        assert update_log.new_values["dropped_reason"] == "Data updated in source"

    # Verify hard deletion prevention
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            with pytest.raises(
                Exception, match="Hard deletions are strictly forbidden"
            ):
                await session.execute(
                    text("DELETE FROM sdv_sign_offs WHERE target_id = 'PAGE-01';")
                )


@pytest.mark.asyncio
async def test_tsdv_config_persistence():
    # @req:PRD-QRY-007
    """
    Verify TSDVConfig stores study-specific configuration settings, JSON lists,
    enforces a unique study_id, and registers audit logs.
    """
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', 1);")
            )
            cfg = TSDVConfig(
                study_id="STUDY-SAMPLING",
                sampling_model="FIELD_BASED",
                initial_full_sdv_subject_count=5,
                random_sample_percentage=25.5,
                full_sdv_domains=["VS", "EG"],
                safety_endpoints=["AE", "SAE"],
                zero_sdv_domains=["DM"],
                trial_random_seed=42,
            )
            session.add(cfg)

    # Retrieve and verify types (including JSON columns list mapping)
    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(TSDVConfig).where(TSDVConfig.study_id == "STUDY-SAMPLING")
        )
        saved_cfg = result.scalar_one()
        assert saved_cfg.sampling_model == "FIELD_BASED"
        assert saved_cfg.initial_full_sdv_subject_count == 5
        assert saved_cfg.random_sample_percentage == 25.5
        assert saved_cfg.full_sdv_domains == ["VS", "EG"]
        assert saved_cfg.safety_endpoints == ["AE", "SAE"]
        assert saved_cfg.zero_sdv_domains == ["DM"]
        assert saved_cfg.trial_random_seed == 42

    # Verify unique constraint on study_id
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            duplicate_cfg = TSDVConfig(
                study_id="STUDY-SAMPLING",
                sampling_model="SUBJECT_BASED",
            )
            session.add(duplicate_cfg)
            with pytest.raises(Exception):
                await session.flush()


@pytest.mark.asyncio
async def test_sdv_signoff_endpoint_and_idempotency():
    # @req:PRD-QRY-005
    # @req:PRD-QRY-006
    """Test that CRA/monitor roles can successfully sign off FIELD, PAGE, and VISIT targets,

    role-based restrictions are enforced, and re-signing is idempotent.
    """
    from apps.execution.database.models import (
        ClinicalObservation,
        ClinicalSubject,
        ClinicalVisit,
    )

    # 1. Populate DB with test subject, visit, and observations
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', 1);")
            )
            subj = ClinicalSubject(
                subject_id="SUBJ-A", study_id="STUDY-1", site_id="SITE-1"
            )
            session.add(subj)

            visit = ClinicalVisit(
                id="VISIT-A",
                subject_id="SUBJ-A",
                study_id="STUDY-1",
                visit_name="Screening",
            )
            session.add(visit)

            obs = ClinicalObservation(
                id="OBS-A",
                subject_id="SUBJ-A",
                study_id="STUDY-1",
                visit_id="VISIT-A",
                page_id="PAGE-A",
                domain="VS",
                test_code="SYSBP",
                test_name="Systolic Blood Pressure",
                value=120.0,
            )
            session.add(obs)

    # 2. Test role-based HTTP 403 enforcement
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        resp = await client.post(
            "/api/v1/execution/sdv/signoff",
            json={
                "scope": "FIELD",
                "target_id": "OBS-A",
                "subject_id": "SUBJ-A",
                "study_id": "STUDY-1",
            },
            headers=get_v2_auth_headers(roles="Site Investigator"),
        )
        assert resp.status_code == 403

    # 3. Test successful FIELD sign-off with CRA role
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        resp = await client.post(
            "/api/v1/execution/sdv/signoff",
            json={
                "scope": "FIELD",
                "target_id": "OBS-A",
                "subject_id": "SUBJ-A",
                "study_id": "STUDY-1",
            },
            headers=get_v2_auth_headers(user_id="CRA-01", roles="CRA"),
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["is_verified"] is True
        assert data["verified_by"] == "CRA-01"
        assert data["scope"] == "FIELD"

    # 4. Verify ClinicalObservation was updated with verifier and timestamp
    async with db_manager.get_session_maker()() as session:
        res = await session.execute(
            select(ClinicalObservation).where(ClinicalObservation.id == "OBS-A")
        )
        obs_db = res.scalar_one()
        assert obs_db.is_sdv_verified is True
        assert obs_db.sdv_verified_by == "CRA-01"
        assert obs_db.sdv_verified_at is not None

    # 5. Verify sign-off is idempotent (repeated call updates instead of duplicate)
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        resp = await client.post(
            "/api/v1/execution/sdv/signoff",
            json={
                "scope": "FIELD",
                "target_id": "OBS-A",
                "subject_id": "SUBJ-A",
                "study_id": "STUDY-1",
            },
            headers=get_v2_auth_headers(user_id="CRA-02", roles="monitor"),
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["verified_by"] == "CRA-02"

    async with db_manager.get_session_maker()() as session:
        res = await session.execute(
            select(SDVSignOff).where(SDVSignOff.target_id == "OBS-A")
        )
        signoffs = res.scalars().all()
        assert len(signoffs) == 1
        assert signoffs[0].verified_by == "CRA-02"


@pytest.mark.asyncio
async def test_sdv_signoff_page_visit_scopes():
    # @req:PRD-QRY-005
    # @req:PRD-QRY-006
    """Test page and visit level sign-off scopes including consistency checks and invalid target 404 errors."""
    from apps.execution.database.models import (
        ClinicalObservation,
        ClinicalSubject,
        ClinicalVisit,
    )

    # 1. Populate DB with test subject, visit, and observations
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', 1);")
            )
            subj = ClinicalSubject(
                subject_id="SUBJ-B", study_id="STUDY-1", site_id="SITE-1"
            )
            session.add(subj)
            visit = ClinicalVisit(
                id="VISIT-B",
                subject_id="SUBJ-B",
                study_id="STUDY-1",
                visit_name="Baseline",
            )
            session.add(visit)
            obs = ClinicalObservation(
                id="OBS-B",
                subject_id="SUBJ-B",
                study_id="STUDY-1",
                visit_id="VISIT-B",
                page_id="PAGE-B",
                domain="LB",
                test_code="ALT",
                test_name="Alanine Aminotransferase",
                value=35.0,
            )
            session.add(obs)

    # 2. Test sign-off of invalid target (404)
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Invalid visit target
        resp = await client.post(
            "/api/v1/execution/sdv/signoff",
            json={
                "scope": "VISIT",
                "target_id": "INVALID-VISIT",
                "subject_id": "SUBJ-B",
                "study_id": "STUDY-1",
            },
            headers=get_v2_auth_headers(),
        )
        assert resp.status_code == 404

        # Invalid page target
        resp = await client.post(
            "/api/v1/execution/sdv/signoff",
            json={
                "scope": "PAGE",
                "target_id": "INVALID-PAGE",
                "subject_id": "SUBJ-B",
                "study_id": "STUDY-1",
            },
            headers=get_v2_auth_headers(),
        )
        assert resp.status_code == 404

    # 3. Test successful VISIT sign-off
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        resp = await client.post(
            "/api/v1/execution/sdv/signoff",
            json={
                "scope": "VISIT",
                "target_id": "VISIT-B",
                "subject_id": "SUBJ-B",
                "study_id": "STUDY-1",
            },
            headers=get_v2_auth_headers(user_id="CRA-99"),
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["scope"] == "VISIT"
        assert data["is_verified"] is True
        assert data["verified_by"] == "CRA-99"

    # 4. Test successful PAGE sign-off
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        resp = await client.post(
            "/api/v1/execution/sdv/signoff",
            json={
                "scope": "PAGE",
                "target_id": "PAGE-B",
                "subject_id": "SUBJ-B",
                "study_id": "STUDY-1",
            },
            headers=get_v2_auth_headers(user_id="CRA-99"),
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["scope"] == "PAGE"
        assert data["is_verified"] is True
        assert data["verified_by"] == "CRA-99"


@pytest.mark.asyncio
async def test_sdv_automatic_verification_drop():
    # @req:PRD-QRY-005
    # @req:PRD-QRY-006
    """Verify automatic SDV drop centrally when verified clinical value is modified.

    - Fails without mandatory GxP change reason.
    - Drops field-level verification and sets dropped fields on SDVSignOff.
    - Sends dashboard notification with correct user message and payload.
    - Metadata-only updates do not drop verification.
    """
    from unittest.mock import MagicMock, patch

    from apps.execution.database.models import (
        ClinicalObservation,
        ClinicalSubject,
        SDVSignOff,
    )
    from packages.security.context import audit_context

    # 1. Seed DB with subject, verified observation, and matching field-level SDVSignOff
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', 1);")
            )
            subj = ClinicalSubject(
                subject_id="SUBJ-C", study_id="STUDY-2", site_id="SITE-2"
            )
            session.add(subj)

            obs = ClinicalObservation(
                id="OBS-C",
                subject_id="SUBJ-C",
                study_id="STUDY-2",
                visit_id="VISIT-C",
                page_id="PAGE-C",
                domain="LB",
                test_code="WBC",
                test_name="White Blood Cells",
                value=6.5,
                is_sdv_verified=True,
                sdv_verified_by="CRA-VERIFIER",
                sdv_verified_at=datetime.utcnow(),
            )
            session.add(obs)

            signoff = SDVSignOff(
                scope="FIELD",
                target_id="OBS-C",
                subject_id="SUBJ-C",
                study_id="STUDY-2",
                is_verified=True,
                verified_by="CRA-VERIFIER",
                verified_at=datetime.utcnow(),
            )
            session.add(signoff)

    # 2. Try to update clinical value WITHOUT a change reason (or default reason)
    with pytest.raises(ValueError, match="GxP change reason is required"):
        with audit_context(user_id="editor-user", change_reason="system_operation"):
            async with db_manager.get_session_maker()() as session:
                res = await session.execute(
                    select(ClinicalObservation).where(ClinicalObservation.id == "OBS-C")
                )
                obs_to_edit = res.scalar_one()
                obs_to_edit.value = 8.2
                await session.commit()

    # 3. Perform a metadata-only update (e.g. page_id) with a valid change reason
    with audit_context(user_id="editor-user", change_reason="Form layout adjustments"):
        async with db_manager.get_session_maker()() as session:
            res = await session.execute(
                select(ClinicalObservation).where(ClinicalObservation.id == "OBS-C")
            )
            obs_to_edit = res.scalar_one()
            obs_to_edit.page_id = "NEW-PAGE-ID"
            await session.commit()

    # Verify observation is STILL verified
    async with db_manager.get_session_maker()() as session:
        res = await session.execute(
            select(ClinicalObservation).where(ClinicalObservation.id == "OBS-C")
        )
        obs_db = res.scalar_one()
        assert obs_db.is_sdv_verified is True
        assert obs_db.sdv_verified_by == "CRA-VERIFIER"

    # 4. Update the clinical value with a valid GxP change reason.
    mock_notify = MagicMock()
    with patch(
        "apps.execution.trial_lock.NotificationRouter.send_dashboard_notification",
        mock_notify,
    ):
        with audit_context(
            user_id="editor-user", change_reason="Corrected typo in lab results"
        ):
            async with db_manager.get_session_maker()() as session:
                res = await session.execute(
                    select(ClinicalObservation).where(ClinicalObservation.id == "OBS-C")
                )
                obs_to_edit = res.scalar_one()
                obs_to_edit.value = 7.5
                await session.commit()

    # 5. Verify the updates
    async with db_manager.get_session_maker()() as session:
        # Check ClinicalObservation verification cleared
        res_obs = await session.execute(
            select(ClinicalObservation).where(ClinicalObservation.id == "OBS-C")
        )
        obs_final = res_obs.scalar_one()
        assert obs_final.is_sdv_verified is False
        assert obs_final.sdv_verified_by is None
        assert obs_final.sdv_verified_at is None

        # Check SDVSignOff marked as not verified and dropped details saved
        res_so = await session.execute(
            select(SDVSignOff).where(SDVSignOff.target_id == "OBS-C")
        )
        so_final = res_so.scalar_one()
        assert so_final.is_verified is False
        assert so_final.dropped_reason == "Corrected typo in lab results"
        assert so_final.dropped_at is not None

        # Verify Audit Log capture for the drop transition
        res_audit = await session.execute(
            select(AuditLog).where(
                AuditLog.table_name == "sdv_sign_offs",
                AuditLog.action == "UPDATE",
            )
        )
        audit_records = res_audit.scalars().all()
        assert len(audit_records) >= 1
        assert audit_records[-1].new_values["is_verified"] is False
        assert (
            audit_records[-1].new_values["dropped_reason"]
            == "Corrected typo in lab results"
        )

    # 6. Verify Dashboard notification sent correctly
    mock_notify.assert_called_once()
    recipients, payload = mock_notify.call_args[0]
    assert recipients == ["CRA-VERIFIER"]
    assert (
        "Previously verified field modified on Subject SUBJ-C - Visit VISIT-C"
        in payload["message"]
    )
    assert payload["study_id"] == "STUDY-2"
    assert payload["subject_id"] == "SUBJ-C"
    assert payload["visit_id"] == "VISIT-C"
    assert payload["observation_id"] == "OBS-C"
    assert payload["editor"] == "editor-user"
    assert payload["change_reason"] == "Corrected typo in lab results"
