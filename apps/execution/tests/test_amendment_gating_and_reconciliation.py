"""Integration tests for execution amendment gating, consent, and non-destructive observation reconciliation.

Requirements: PRD-SUB-007, PRD-SYS-001
"""

import hashlib
import hmac
import json
import time

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select, text

from apps.execution.database.core import db_manager as exec_db_manager
from apps.execution.database.models import (
    Base as ExecBase,
)
from apps.execution.database.models import (
    ClinicalObservation,
    MigrationRule,
    SubjectConsent,
)
from apps.execution.main import app as exec_app
from apps.execution.migration_rules import reconcile_observations

GATEWAY_SECRET = "internal-gateway-secret-12345"  # pragma: allowlist secret


def get_exec_auth_headers(
    user_id="test_user", roles="admin", change_reason="system_operation"
):
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


async def record_subject_consent(
    subject_id: str,
    study_id: str,
    version_tag: str,
    version_index: int,
    icf_signed: bool = True,
    requires_reconsent: bool = False,
):
    async with exec_db_manager.get_session_maker()() as session:
        stmt = select(SubjectConsent).where(
            SubjectConsent.subject_id == subject_id,
            SubjectConsent.study_id == study_id,
            SubjectConsent.version_index == version_index,
        )
        existing = (await session.execute(stmt)).scalars().first()
        if existing:
            existing.version_tag = version_tag
            existing.icf_signed = icf_signed
            existing.requires_reconsent = requires_reconsent
        else:
            consent = SubjectConsent(
                subject_id=subject_id,
                study_id=study_id,
                version_tag=version_tag,
                version_index=version_index,
                icf_signed=icf_signed,
                requires_reconsent=requires_reconsent,
            )
            session.add(consent)
        await session.commit()


@pytest_asyncio.fixture(autouse=True)
async def setup_exec_db():
    exec_db_manager.init_db("sqlite+aiosqlite:///:memory:")
    async with exec_db_manager.engine.begin() as conn:
        if exec_db_manager.engine.dialect.name == "postgresql":
            await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))
        await conn.run_sync(ExecBase.metadata.create_all)
    yield
    async with exec_db_manager.engine.begin() as conn:
        await conn.run_sync(ExecBase.metadata.drop_all)
    await exec_db_manager.close()


@pytest.mark.asyncio
async def test_exact_version_consent_and_reconsent_gating():
    """Validate exact-version re-consent gating."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=exec_app), base_url="http://test"
    ) as client:
        subject_payload = {
            "subject_id": "SUBJ-GATE-Y",
            "study_id": "STUDY-GATE",
            "demographics": {
                "name": "Alice Smith",
                "birthdate": "1995-05-15",
                "gender": "F",
                "race": "Asian",
            },
        }
        res_subj = await client.post(
            "/api/v1/execution/subjects",
            json=subject_payload,
            headers=get_exec_auth_headers(),
        )
        assert res_subj.status_code == 200

        await record_subject_consent(
            subject_id="SUBJ-GATE-Y",
            study_id="STUDY-GATE",
            version_tag="1.0",
            version_index=1,
            icf_signed=True,
            requires_reconsent=False,
        )

        await record_subject_consent(
            subject_id="SUBJ-GATE-Y",
            study_id="STUDY-GATE",
            version_tag="2.0",
            version_index=2,
            icf_signed=False,
            requires_reconsent=True,
        )

        obs_payload = {
            "subject_id": "SUBJ-GATE-Y",
            "study_id": "STUDY-GATE",
            "protocol_version_tag": "2.0",
            "domain": "VS",
            "test_code": "SYSBP",
            "test_name": "Systolic Blood Pressure",
            "value": 120.0,
            "unit": "mmHg",
        }
        with pytest.raises(PermissionError) as exc_info:
            await client.post(
                "/api/v1/execution/observations",
                json=obs_payload,
                headers=get_exec_auth_headers(),
            )
        assert "Re-Consent Required - Demographics & Visit Forms Locked" in str(
            exc_info.value
        )

        await record_subject_consent(
            subject_id="SUBJ-GATE-Y",
            study_id="STUDY-GATE",
            version_tag="2.0",
            version_index=2,
            icf_signed=True,
            requires_reconsent=False,
        )

        res_allowed = await client.post(
            "/api/v1/execution/observations",
            json=obs_payload,
            headers=get_exec_auth_headers(),
        )
        assert res_allowed.status_code == 200


@pytest.mark.asyncio
async def test_clinical_capture_provenance_and_version_stamping():
    """Validate clinical observation version stamping and provenance capture."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=exec_app), base_url="http://test"
    ) as client:
        subj_payload = {
            "subject_id": "SUBJ-PROV-100",
            "study_id": "STUDY-PROV",
            "demographics": {"name": "Bob Jones"},
        }
        res_subj = await client.post(
            "/api/v1/execution/subjects",
            json=subj_payload,
            headers=get_exec_auth_headers(),
        )
        assert res_subj.status_code == 200

        await record_subject_consent(
            subject_id="SUBJ-PROV-100",
            study_id="STUDY-PROV",
            version_tag="1.5",
            version_index=1,
            icf_signed=True,
            requires_reconsent=False,
        )

        obs_payload = {
            "subject_id": "SUBJ-PROV-100",
            "study_id": "STUDY-PROV",
            "protocol_version_tag": "1.5",
            "domain": "LB",
            "test_code": "GLUC",
            "test_name": "Glucose",
            "value": 95.0,
            "unit": "mg/dL",
        }
        res_obs = await client.post(
            "/api/v1/execution/observations",
            json=obs_payload,
            headers=get_exec_auth_headers(),
        )
        assert res_obs.status_code == 200
        obs_id = res_obs.json()["id"]

        async with exec_db_manager.get_session_maker()() as session:
            stmt = select(ClinicalObservation).where(ClinicalObservation.id == obs_id)
            obs = (await session.execute(stmt)).scalars().first()
            assert obs is not None
            assert obs.protocol_version_tag == "1.5"


@pytest.mark.asyncio
async def test_non_destructive_reconciliation_and_multi_hop():
    """Validate non-destructive observation reconciliation across version hops."""
    async with exec_db_manager.get_session_maker()() as session:
        obs1 = ClinicalObservation(
            subject_id="SUBJ-MIG",
            study_id="STUDY-MIG",
            protocol_version_tag="1.0",
            domain="VS",
            test_code="VSSBP",
            test_name="Vital Signs SBP",
            value=115.0,
            unit="mmHg",
        )
        rule_v1_to_v2 = MigrationRule(
            study_id="STUDY-MIG",
            source_version="1.0",
            target_version="2.0",
            rule_type="rename",
            source_field="VSSBP",
            target_field="SYSBP",
        )
        rule_v2_to_v3 = MigrationRule(
            study_id="STUDY-MIG",
            source_version="2.0",
            target_version="3.0",
            rule_type="rename",
            source_field="SYSBP",
            target_field="SYSBP_V3",
        )
        session.add_all([obs1, rule_v1_to_v2, rule_v2_to_v3])
        await session.commit()

        reconciled = await reconcile_observations(
            session=session,
            observations=[obs1],
            target_version="3.0",
        )

        assert len(reconciled) == 1
        sysbp = reconciled[0]
        assert sysbp.test_code == "SYSBP_V3"
        assert sysbp.value == 115.0
        assert sysbp.protocol_version_tag == "3.0"

        session.expire_all()
        stmt_pristine = select(ClinicalObservation).where(
            ClinicalObservation.subject_id == "SUBJ-MIG"
        )
        pristine_obs = list((await session.execute(stmt_pristine)).scalars().all())
        assert len(pristine_obs) == 1
        assert pristine_obs[0].test_code == "VSSBP"
        assert pristine_obs[0].protocol_version_tag == "1.0"
        assert pristine_obs[0].value == 115.0
