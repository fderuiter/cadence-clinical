import time
from datetime import UTC, datetime

import httpx
import pytest
from sqlalchemy import select, text

from apps.execution.database.core import db_manager
from apps.execution.database.migrate import deploy_database_triggers
from apps.execution.database.models import (
    Base,
    ClinicalObservation,
    ClinicalSubject,
    SDVSignOff,
)
from apps.execution.main import app
from apps.execution.trial_lock import TrialLockManager
from packages.security.context import audit_context
from packages.security.signing import generate_gateway_signature

GATEWAY_SECRET = "internal-gateway-secret-12345"  # pragma: allowlist secret


def get_v2_auth_headers(
    user_id: str = "test_user",
    roles: str = "CRA",
    change_reason: str = "test operation",
    site_id: str = "SITE-FLAG-1",
) -> dict[str, str]:
    """Generate Gateway signature version 2 authentication headers."""
    timestamp = str(time.time())
    signature = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=GATEWAY_SECRET.encode(),
        change_reason=change_reason,
        site_id=site_id,
        tenant_id="tenant_default",
    )
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
        "X-Site-Id": site_id,
        "X-User-Site": site_id,
    }


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
async def test_sdv_flag_endpoint_cra_success():
    """Verify that a CRA can successfully flag a clinical observation."""
    # 1. Insert Subject and Observation
    async with db_manager.get_session_maker()() as session, session.begin():
        await session.execute(
            text("SELECT set_config('cadence.app_writing', 'true', 1);")
        )
        subj = ClinicalSubject(
            subject_id="SUBJ-FLAG-1",
            study_id="STUDY-FLAG-1",
            site_id="SITE-FLAG-1",
        )
        session.add(subj)

        obs = ClinicalObservation(
            id="OBS-FLAG-1",
            subject_id="SUBJ-FLAG-1",
            study_id="STUDY-FLAG-1",
            visit_id="VISIT-FLAG-1",
            page_id="PAGE-FLAG-1",
            domain="VS",
            test_code="SYSBP",
            test_name="Systolic Blood Pressure",
            value=120.0,
            value_string="120",
        )
        session.add(obs)

    # 2. Invoke /sdv/flag endpoint as CRA
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        resp = await client.post(
            "/api/v1/execution/sdv/flag",
            json={
                "study_id": "STUDY-FLAG-1",
                "subject_id": "SUBJ-FLAG-1",
                "scope": "FIELD",
                "targets": [
                    {
                        "target_id": "OBS-FLAG-1",
                        "flag_reason": "Out of expected range",
                        "flag_severity": "MAJOR",
                    }
                ],
                "reason_for_change": "Flagging suspicious value",
            },
            headers=get_v2_auth_headers(roles="CRA"),
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["flagged_count"] == 1
        assert data["flagged_target_ids"] == ["OBS-FLAG-1"]
        assert "flag_id" in data
        assert "content_digest" in data

    # 3. Verify DB state after flagging
    async with db_manager.get_session_maker()() as session:
        res_obs = await session.execute(
            select(ClinicalObservation).where(ClinicalObservation.id == "OBS-FLAG-1")
        )
        obs_db = res_obs.scalar_one()
        assert obs_db.is_sdv_flagged is True
        assert obs_db.sdv_flag_reason == "Out of expected range"

        res_so = await session.execute(
            select(SDVSignOff).where(
                SDVSignOff.scope == "FIELD",
                SDVSignOff.target_id == "OBS-FLAG-1",
                SDVSignOff.subject_id == "SUBJ-FLAG-1",
            )
        )
        so_db = res_so.scalar_one()
        assert so_db.status == "FLAGGED"
        assert so_db.flag_reason == "Out of expected range"
        assert so_db.flag_severity == "MAJOR"
        assert so_db.is_verified is False


@pytest.mark.asyncio
async def test_sdv_flag_endpoint_forbidden_for_other_roles():
    """Verify that only monitors/CRAs can flag, and other roles receive 403."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Site Investigator
        resp = await client.post(
            "/api/v1/execution/sdv/flag",
            json={
                "study_id": "STUDY-FLAG-1",
                "subject_id": "SUBJ-FLAG-1",
                "scope": "FIELD",
                "targets": [
                    {
                        "target_id": "OBS-FLAG-1",
                        "flag_reason": "Out of expected range",
                        "flag_severity": "MAJOR",
                    }
                ],
                "reason_for_change": "Flagging suspicious value",
            },
            headers=get_v2_auth_headers(roles="Site Investigator"),
        )
        assert resp.status_code == 403

        # Auditor
        resp2 = await client.post(
            "/api/v1/execution/sdv/flag",
            json={
                "study_id": "STUDY-FLAG-1",
                "subject_id": "SUBJ-FLAG-1",
                "scope": "FIELD",
                "targets": [
                    {
                        "target_id": "OBS-FLAG-1",
                        "flag_reason": "Out of expected range",
                        "flag_severity": "MAJOR",
                    }
                ],
                "reason_for_change": "Flagging suspicious value",
            },
            headers=get_v2_auth_headers(roles="Auditor"),
        )
        assert resp2.status_code == 403


@pytest.mark.asyncio
async def test_sdv_resolve_endpoint_cra_success():
    """Verify that a CRA can successfully resolve a flagged clinical observation."""
    # 1. Insert Subject and Observation
    async with db_manager.get_session_maker()() as session, session.begin():
        await session.execute(
            text("SELECT set_config('cadence.app_writing', 'true', 1);")
        )
        subj = ClinicalSubject(
            subject_id="SUBJ-FLAG-1",
            study_id="STUDY-FLAG-1",
            site_id="SITE-FLAG-1",
        )
        session.add(subj)

        obs = ClinicalObservation(
            id="OBS-FLAG-1",
            subject_id="SUBJ-FLAG-1",
            study_id="STUDY-FLAG-1",
            visit_id="VISIT-FLAG-1",
            page_id="PAGE-FLAG-1",
            domain="VS",
            test_code="SYSBP",
            test_name="Systolic Blood Pressure",
            value=120.0,
            value_string="120",
            is_sdv_flagged=True,
            sdv_flag_reason="Suspicious value",
        )
        session.add(obs)

        signoff = SDVSignOff(
            scope="FIELD",
            target_id="OBS-FLAG-1",
            subject_id="SUBJ-FLAG-1",
            study_id="STUDY-FLAG-1",
            site_id="SITE-FLAG-1",
            is_verified=False,
            status="FLAGGED",
            flag_reason="Suspicious value",
            flag_severity="MINOR",
        )
        session.add(signoff)

    # 2. Invoke /sdv/resolve endpoint as CRA
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        resp = await client.post(
            "/api/v1/execution/sdv/resolve",
            json={
                "study_id": "STUDY-FLAG-1",
                "subject_id": "SUBJ-FLAG-1",
                "scope": "FIELD",
                "target_ids": ["OBS-FLAG-1"],
                "reason_for_change": "Resolving flagged value",
            },
            headers=get_v2_auth_headers(roles="CRA"),
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["resolved_count"] == 1
        assert data["resolved_target_ids"] == ["OBS-FLAG-1"]
        assert "resolution_id" in data
        assert "content_digest" in data

    # 3. Verify DB state after resolution
    async with db_manager.get_session_maker()() as session:
        res_obs = await session.execute(
            select(ClinicalObservation).where(ClinicalObservation.id == "OBS-FLAG-1")
        )
        obs_db = res_obs.scalar_one()
        assert obs_db.is_sdv_flagged is False
        assert obs_db.sdv_flag_reason is None

        res_so = await session.execute(
            select(SDVSignOff).where(
                SDVSignOff.scope == "FIELD",
                SDVSignOff.target_id == "OBS-FLAG-1",
                SDVSignOff.subject_id == "SUBJ-FLAG-1",
            )
        )
        so_db = res_so.scalar_one()
        assert so_db.status == "RESOLVED"
        assert so_db.resolved_by == "test_user"
        assert so_db.resolved_at is not None


@pytest.mark.asyncio
async def test_sdv_resolve_endpoint_forbidden_for_other_roles():
    """Verify that only monitors/CRAs can resolve flags, and other roles receive 403."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        resp = await client.post(
            "/api/v1/execution/sdv/resolve",
            json={
                "study_id": "STUDY-FLAG-1",
                "subject_id": "SUBJ-FLAG-1",
                "scope": "FIELD",
                "target_ids": ["OBS-FLAG-1"],
                "reason_for_change": "Resolving flag",
            },
            headers=get_v2_auth_headers(roles="Site Investigator"),
        )
        assert resp.status_code == 403


@pytest.mark.asyncio
async def test_cascading_signature_deletion_on_observation_change():
    """Verify that modifying a verified clinical observation automatically removes its
    verification flag and deletes associated parent page and visit sign-off records in a single tx.
    """
    # 1. Set up subject, observation, and parent PAGE and VISIT sign-offs
    async with db_manager.get_session_maker()() as session, session.begin():
        await session.execute(
            text("SELECT set_config('cadence.app_writing', 'true', 1);")
        )
        subj = ClinicalSubject(
            subject_id="SUBJ-CASC-1",
            study_id="STUDY-CASC-1",
            site_id="SITE-CASC-1",
        )
        session.add(subj)

        obs = ClinicalObservation(
            id="OBS-CASC-1",
            subject_id="SUBJ-CASC-1",
            study_id="STUDY-CASC-1",
            visit_id="VISIT-CASC-1",
            page_id="PAGE-CASC-1",
            domain="VS",
            test_code="SYSBP",
            test_name="Systolic Blood Pressure",
            value=120.0,
            value_string="120",
            is_sdv_verified=True,
            sdv_verified_by="CRA-1",
            sdv_verified_at=datetime.now(UTC).replace(tzinfo=None),
        )
        session.add(obs)

        # FIELD signature
        so_field = SDVSignOff(
            scope="FIELD",
            target_id="OBS-CASC-1",
            subject_id="SUBJ-CASC-1",
            study_id="STUDY-CASC-1",
            site_id="SITE-CASC-1",
            is_verified=True,
            verified_by="CRA-1",
            verified_at=datetime.now(UTC).replace(tzinfo=None),
        )
        session.add(so_field)

        # PAGE signature
        so_page = SDVSignOff(
            scope="PAGE",
            target_id="PAGE-CASC-1",
            subject_id="SUBJ-CASC-1",
            study_id="STUDY-CASC-1",
            site_id="SITE-CASC-1",
            is_verified=True,
            verified_by="CRA-1",
            verified_at=datetime.now(UTC).replace(tzinfo=None),
        )
        session.add(so_page)

        # VISIT signature
        so_visit = SDVSignOff(
            scope="VISIT",
            target_id="VISIT-CASC-1",
            subject_id="SUBJ-CASC-1",
            study_id="STUDY-CASC-1",
            site_id="SITE-CASC-1",
            is_verified=True,
            verified_by="CRA-1",
            verified_at=datetime.now(UTC).replace(tzinfo=None),
        )
        session.add(so_visit)

    # 2. Update clinical value of the observation (e.g. correcting a typo)
    with audit_context(
        user_id="site-coord", change_reason="Typo corrected in observation"
    ):
        async with db_manager.get_session_maker()() as session:
            res = await session.execute(
                select(ClinicalObservation).where(
                    ClinicalObservation.id == "OBS-CASC-1"
                )
            )
            obs_edit = res.scalar_one()
            obs_edit.value = 115.0
            await session.commit()

    # 3. Verify that the field-level verification is dropped, and PAGE & VISIT signatures are deleted!
    async with db_manager.get_session_maker()() as session:
        # Check observation state
        res_obs = await session.execute(
            select(ClinicalObservation).where(ClinicalObservation.id == "OBS-CASC-1")
        )
        obs_final = res_obs.scalar_one()
        assert obs_final.is_sdv_verified is False
        assert obs_final.sdv_verified_by is None

        # Check FIELD signature state (should be dropped, but not deleted as it is field-level audit trail)
        res_field = await session.execute(
            select(SDVSignOff).where(
                SDVSignOff.scope == "FIELD",
                SDVSignOff.target_id == "OBS-CASC-1",
                SDVSignOff.subject_id == "SUBJ-CASC-1",
            )
        )
        so_field_final = res_field.scalar_one()
        assert so_field_final.is_verified is False
        assert so_field_final.dropped_reason == "Typo corrected in observation"

        # Check PAGE signature (must be COMPLETELY DELETED / soft-deleted)
        res_page = await session.execute(
            select(SDVSignOff).where(
                SDVSignOff.scope == "PAGE",
                SDVSignOff.target_id == "PAGE-CASC-1",
                SDVSignOff.subject_id == "SUBJ-CASC-1",
            )
        )
        so_page_final = res_page.scalar_one()
        assert so_page_final.is_deleted is True
        assert so_page_final.is_verified is False

        # Check VISIT signature (must be COMPLETELY DELETED / soft-deleted)
        res_visit = await session.execute(
            select(SDVSignOff).where(
                SDVSignOff.scope == "VISIT",
                SDVSignOff.target_id == "VISIT-CASC-1",
                SDVSignOff.subject_id == "SUBJ-CASC-1",
            )
        )
        so_visit_final = res_visit.scalar_one()
        assert so_visit_final.is_deleted is True
        assert so_visit_final.is_verified is False
