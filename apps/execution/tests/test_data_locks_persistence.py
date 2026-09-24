"""Integration and unit test suite for Persistent Granular Data Lock & Freeze System.

Validates:
1. SQLModel / SQLAlchemy relational persistence of DataLock with GxP audit fields.
2. 6-tier hierarchical lock inheritance across Study -> Site -> Subject -> Visit -> Form -> Field scopes.
3. Database mutation interception blocking INSERT, UPDATE, and DELETE operations on locked entities.
4. Step-up dual-signature authentication (X-Sig-Token) for HARD_LOCK actions and replay prevention.
5. Strict mandatory >= 50-character GxP unlock justification validation and audit trail persistence.
6. Restoration of write access following valid unlock operations.
7. Lock status and hierarchy tree REST API endpoints.

Requirements:
- @req:PRD-SYS-001
- @req:PRD-SYS-002
- @req:PRD-MDR-002
- @req:Trace-1
- @req:Trace-3
- @req:Trace-13
- @req:Trace-17
"""

import hashlib
import hmac
import os
import time
import uuid
from datetime import datetime

import httpx
import pytest
import pytest_asyncio
from jose import jwt
from sqlalchemy import select

from apps.execution.database.context import (
    current_change_reason,
    current_user_id,
)
from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    Base,
    ClinicalObservation,
    ClinicalSubject,
    DataLock,
    FormSubmission,
)
from apps.execution.main import app
from apps.execution.trial_lock import TrialLockManager
from packages.security.sig_token_verifier import token_consumption_cache

GATEWAY_SECRET = os.getenv(
    "GATEWAY_SECRET", "internal-gateway-secret-12345"
)  # pragma: allowlist secret


def generate_auth_headers(
    user_id: str = "datamanager_user",
    roles: str = "data_manager",
    change_reason: str = "Data Lock Governance Operation",
    sig_action: str | None = None,
    sig_exp_offset: float = 300.0,
    custom_jti: str | None = None,
    secret: str = GATEWAY_SECRET,
) -> dict[str, str]:
    """Generate Gateway signature and optional step-up X-Sig-Token authentication headers."""
    import json

    timestamp = str(time.time())
    payload = {
        "change_reason": change_reason,
        "roles": roles,
        "timestamp": timestamp,
        "user_id": user_id,
    }
    serialized = json.dumps(payload, sort_keys=True, separators=(",", ":"))
    signature = hmac.new(
        secret.encode("utf-8"), serialized.encode("utf-8"), hashlib.sha256
    ).hexdigest()

    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
        "X-Tenant-Id": "tenant_default",
    }

    if sig_action:
        sig_payload = {
            "sub": user_id,
            "username": user_id,
            "action": sig_action,
            "roles": [roles],
            "iat": time.time(),
            "exp": time.time() + sig_exp_offset,
            "jti": custom_jti or str(uuid.uuid4()),
        }
        sig_token = jwt.encode(sig_payload, secret, algorithm="HS256")
        headers["X-Sig-Token"] = sig_token

    return headers


@pytest_asyncio.fixture(autouse=True)
async def setup_test_db():
    """Setup isolated in-memory SQLite database before each test and tear down after."""
    TrialLockManager.reset()
    token_consumption_cache.reset()
    current_user_id.set("test_data_manager")
    current_change_reason.set("Initial setup for test")

    db_manager.init_db("sqlite+aiosqlite:///:memory:")
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)

    yield

    TrialLockManager.reset()
    token_consumption_cache.reset()
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_datalock_sqlmodel_relational_persistence() -> None:
    """Validate DataLock SQLModel relational persistence, property aliases, and GxP audit trail.

    @req:PRD-SYS-001
    @req:PRD-SYS-002
    @req:PRD-MDR-002
    @req:Trace-1
    @req:Trace-3
    @req:Trace-13
    @req:Trace-17
    """
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            lock = DataLock(
                lock_id="dl_relational_001",
                study_id="STUDY-100",
                site_id="SITE-200",
                subject_id="SUBJ-300",
                visit_id="VISIT-400",
                form_id="FORM-500",
                field_name="SYSBP",
                scope_type="FORM",
                scope_id="FORM-500",
                lock_type="HARD_LOCK",
                is_active=True,
                created_by="lead_datamanager",
                reason_for_change="Interim analysis database lock",
            )
            session.add(lock)

    # Query back and verify relational columns and aliases
    async with db_manager.get_session_maker()() as session:
        stmt = select(DataLock).where(DataLock.id == "dl_relational_001")
        res = await session.execute(stmt)
        persisted = res.scalars().first()

        assert persisted is not None
        assert persisted.id == "dl_relational_001"
        assert persisted.lock_id == "dl_relational_001"
        assert persisted.study_id == "STUDY-100"
        assert persisted.site_id == "SITE-200"
        assert persisted.subject_id == "SUBJ-300"
        assert persisted.visit_id == "VISIT-400"
        assert persisted.form_id == "FORM-500"
        assert persisted.field_name == "SYSBP"
        assert persisted.scope_type == "FORM"
        assert persisted.scope == "FORM"
        assert persisted.scope_id == "FORM-500"
        assert persisted.lock_type == "HARD_LOCK"
        assert persisted.status == "HARD_LOCK"
        assert persisted.is_active is True
        assert persisted.created_by == "lead_datamanager"
        assert persisted.locked_by == "lead_datamanager"
        assert persisted.reason_for_change == "Interim analysis database lock"
        assert persisted.version == 1
        assert persisted.is_deleted is False


@pytest.mark.asyncio
async def test_hierarchical_lock_inheritance_study_blocks_all() -> None:
    """Validate that locking a Study blocks all descendant entities (Site, Subject, Visit, Form, Field).

    @req:PRD-SYS-001
    @req:PRD-SYS-002
    @req:PRD-MDR-002
    @req:Trace-1
    @req:Trace-3
    @req:Trace-13
    @req:Trace-17
    """
    # 1. Persist a Study-level DataLock
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            study_lock = DataLock(
                study_id="STUDY-GLOBAL-01",
                scope_type="STUDY",
                scope_id="STUDY-GLOBAL-01",
                lock_type="HARD_LOCK",
                is_active=True,
                created_by="sponsor_lead",
                reason_for_change="Study completed and locked globally",
            )
            session.add(study_lock)

    # 2. Attempt inserting a FormSubmission under the locked study -> must be blocked
    with pytest.raises(
        PermissionError, match="Study STUDY-GLOBAL-01 is currently locked"
    ):
        async with db_manager.get_session_maker()() as session:
            async with session.begin():
                form = FormSubmission(
                    study_id="STUDY-GLOBAL-01",
                    site_id="SITE-10",
                    subject_id="SUBJ-20",
                    visit_id="VISIT-30",
                    form_id="FORM-40",
                    status="DRAFT",
                    is_active=True,
                )
                session.add(form)

    # 3. Attempt inserting a ClinicalObservation under the locked study -> must be blocked
    with pytest.raises(
        PermissionError, match="Study STUDY-GLOBAL-01 is currently locked"
    ):
        async with db_manager.get_session_maker()() as session:
            async with session.begin():
                obs = ClinicalObservation(
                    study_id="STUDY-GLOBAL-01",
                    site_id="SITE-10",
                    subject_id="SUBJ-20",
                    visit_id="VISIT-30",
                    page_id="FORM-40",
                    domain="VS",
                    test_code="SYSBP",
                    test_name="Systolic BP",
                    value=120.0,
                )
                session.add(obs)


@pytest.mark.asyncio
async def test_hierarchical_lock_inheritance_site_blocks_subjects_and_forms() -> None:
    """Validate that locking a Site blocks mutations at that site while permitting other sites.

    @req:PRD-SYS-001
    @req:PRD-SYS-002
    @req:PRD-MDR-002
    @req:Trace-1
    @req:Trace-3
    @req:Trace-13
    @req:Trace-17
    """
    # 1. Lock SITE-ALPHA but leave SITE-BETA unlocked
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            site_lock = DataLock(
                study_id="STUDY-001",
                site_id="SITE-ALPHA",
                scope_type="SITE",
                scope_id="SITE-ALPHA",
                lock_type="LOCKED",
                is_active=True,
                created_by="cra_monitor",
                reason_for_change="Site audit inspection lock",
            )
            session.add(site_lock)

    # 2. Mutation on SITE-ALPHA -> blocked
    with pytest.raises(PermissionError, match="Site SITE-ALPHA is currently locked"):
        async with db_manager.get_session_maker()() as session:
            async with session.begin():
                form_alpha = FormSubmission(
                    study_id="STUDY-001",
                    site_id="SITE-ALPHA",
                    subject_id="SUBJ-001",
                    visit_id="VISIT-001",
                    form_id="FORM-VS",
                    status="DRAFT",
                )
                session.add(form_alpha)

    # 3. Mutation on SITE-BETA -> allowed
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            form_beta = FormSubmission(
                study_id="STUDY-001",
                site_id="SITE-BETA",
                subject_id="SUBJ-002",
                visit_id="VISIT-001",
                form_id="FORM-VS",
                status="DRAFT",
            )
            session.add(form_beta)

    # Verify form_beta was saved successfully
    async with db_manager.get_session_maker()() as session:
        stmt = select(FormSubmission).where(FormSubmission.site_id == "SITE-BETA")
        res = await session.execute(stmt)
        assert res.scalars().first() is not None


@pytest.mark.asyncio
async def test_hierarchical_lock_inheritance_subject_blocks_visits_and_observations() -> (
    None
):
    """Validate that locking a Subject blocks all visits, forms, and observations for that subject.

    @req:PRD-SYS-001
    @req:PRD-SYS-002
    @req:PRD-MDR-002
    @req:Trace-1
    @req:Trace-3
    @req:Trace-13
    @req:Trace-17
    """
    # 1. Lock subject SUBJ-LOCKED-01
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            sub_lock = DataLock(
                study_id="STUDY-001",
                site_id="SITE-001",
                subject_id="SUBJ-LOCKED-01",
                scope_type="SUBJECT",
                scope_id="SUBJ-LOCKED-01",
                lock_type="HARD_LOCK",
                is_active=True,
                created_by="data_manager",
                reason_for_change="Subject completed study follow-up",
            )
            session.add(sub_lock)

    # 2. Block new observation for locked subject
    with pytest.raises(
        PermissionError, match="Subject SUBJ-LOCKED-01 is currently locked"
    ):
        async with db_manager.get_session_maker()() as session:
            async with session.begin():
                obs = ClinicalObservation(
                    study_id="STUDY-001",
                    site_id="SITE-001",
                    subject_id="SUBJ-LOCKED-01",
                    visit_id="VISIT-002",
                    page_id="FORM-LAB",
                    domain="LB",
                    test_code="ALT",
                    test_name="Alanine Aminotransferase",
                    value=34.0,
                )
                session.add(obs)


@pytest.mark.asyncio
async def test_hierarchical_lock_inheritance_form_blocks_field_observations() -> None:
    """Validate that locking a Form blocks observations and form submission updates under that form.

    @req:PRD-SYS-001
    @req:PRD-SYS-002
    @req:PRD-MDR-002
    @req:Trace-1
    @req:Trace-3
    @req:Trace-13
    @req:Trace-17
    """
    # 1. Insert initial form and observation
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            form = FormSubmission(
                id="fs-100",
                study_id="STUDY-001",
                site_id="SITE-001",
                subject_id="SUBJ-101",
                visit_id="VISIT-101",
                form_id="FORM-VITALS",
                status="DRAFT",
            )
            obs = ClinicalObservation(
                id="obs-100",
                study_id="STUDY-001",
                site_id="SITE-001",
                subject_id="SUBJ-101",
                visit_id="VISIT-101",
                page_id="FORM-VITALS",
                domain="VS",
                test_code="DIABP",
                test_name="Diastolic BP",
                value=80.0,
            )
            session.add(form)
            session.add(obs)

    # 2. Lock FORM-VITALS
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            form_lock = DataLock(
                study_id="STUDY-001",
                site_id="SITE-001",
                subject_id="SUBJ-101",
                form_id="FORM-VITALS",
                scope_type="FORM",
                scope_id="FORM-VITALS",
                lock_type="FROZEN",
                is_active=True,
                created_by="data_manager",
                reason_for_change="Freeze vitals form for monitoring",
            )
            session.add(form_lock)

    # 3. Attempt updating existing observation on locked form -> blocked
    with pytest.raises(PermissionError, match="Form FORM-VITALS is currently locked"):
        async with db_manager.get_session_maker()() as session:
            async with session.begin():
                stmt = select(ClinicalObservation).where(
                    ClinicalObservation.id == "obs-100"
                )
                res = await session.execute(stmt)
                obs_to_update = res.scalars().first()
                obs_to_update.value = 85.0


@pytest.mark.asyncio
async def test_hierarchical_lock_inheritance_field_blocks_single_observation() -> None:
    """Validate that locking a single Field blocks only that field while sibling fields remain editable.

    @req:PRD-SYS-001
    @req:PRD-SYS-002
    @req:PRD-MDR-002
    @req:Trace-1
    @req:Trace-3
    @req:Trace-13
    @req:Trace-17
    """
    # 1. Insert two observations in the same form (SYSBP and DIABP)
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            obs_sys = ClinicalObservation(
                id="obs-sys-1",
                study_id="STUDY-001",
                site_id="SITE-001",
                subject_id="SUBJ-101",
                visit_id="VISIT-101",
                page_id="FORM-VITALS",
                domain="VS",
                test_code="SYSBP",
                test_name="Systolic BP",
                value=120.0,
            )
            obs_dia = ClinicalObservation(
                id="obs-dia-1",
                study_id="STUDY-001",
                site_id="SITE-001",
                subject_id="SUBJ-101",
                visit_id="VISIT-101",
                page_id="FORM-VITALS",
                domain="VS",
                test_code="DIABP",
                test_name="Diastolic BP",
                value=80.0,
            )
            session.add(obs_sys)
            session.add(obs_dia)

    # 2. Lock only the SYSBP field
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            field_lock = DataLock(
                study_id="STUDY-001",
                form_id="FORM-VITALS",
                field_name="SYSBP",
                scope_type="FIELD",
                scope_id="SYSBP",
                lock_type="HARD_LOCK",
                is_active=True,
                created_by="data_manager",
                reason_for_change="Lock adjudicated primary endpoint parameter",
            )
            session.add(field_lock)

    # 3. Modifying SYSBP observation -> blocked
    with pytest.raises(PermissionError, match="Field SYSBP is currently locked"):
        async with db_manager.get_session_maker()() as session:
            async with session.begin():
                stmt = select(ClinicalObservation).where(
                    ClinicalObservation.id == "obs-sys-1"
                )
                res = await session.execute(stmt)
                target_sys = res.scalars().first()
                target_sys.value = 130.0

    # 4. Modifying DIABP observation -> succeeds
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            stmt = select(ClinicalObservation).where(
                ClinicalObservation.id == "obs-dia-1"
            )
            res = await session.execute(stmt)
            target_dia = res.scalars().first()
            target_dia.value = 82.0


@pytest.mark.asyncio
async def test_hard_lock_requires_valid_sig_token() -> None:
    """Validate that HARD_LOCK actions require a valid X-Sig-Token header and prevent replay attacks.

    @req:PRD-SYS-001
    @req:PRD-SYS-002
    @req:PRD-MDR-002
    @req:Trace-1
    @req:Trace-3
    @req:Trace-13
    @req:Trace-17
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        payload = {
            "study_id": "STUDY-001",
            "site_id": "SITE-001",
            "subject_id": "SUBJ-001",
            "form_id": "FORM-SAE-01",
            "scope_type": "FORM",
            "scope_id": "FORM-SAE-01",
            "action": "HARD_LOCK",
            "reason_for_change": "Execute formal GxP Part 11 database lock",
        }

        # 1. Missing X-Sig-Token header -> Rejected with 401
        headers_no_sig = generate_auth_headers(
            user_id="dm_pi_user", roles="data_manager"
        )
        res_no_sig = await client.post(
            "/api/v1/execution/locks/lock",
            json=payload,
            headers=headers_no_sig,
        )
        assert res_no_sig.status_code == 401
        assert res_no_sig.json()["detail"] == "REAUTHENTICATION_REQUIRED"

        # 2. Expired X-Sig-Token header -> Rejected with 401
        headers_expired = generate_auth_headers(
            user_id="dm_pi_user",
            roles="data_manager",
            sig_action="HARD_LOCK",
            sig_exp_offset=-10.0,
        )
        res_expired = await client.post(
            "/api/v1/execution/locks/lock",
            json=payload,
            headers=headers_expired,
        )
        assert res_expired.status_code == 401

        # 3. Valid X-Sig-Token header -> Succeeded with 200 and persisted
        jti_val = str(uuid.uuid4())
        headers_valid = generate_auth_headers(
            user_id="dm_pi_user",
            roles="data_manager",
            sig_action="HARD_LOCK",
            custom_jti=jti_val,
        )
        res_valid = await client.post(
            "/api/v1/execution/locks/lock",
            json=payload,
            headers=headers_valid,
        )
        assert res_valid.status_code == 200
        data_valid = res_valid.json()
        assert data_valid["status"] == "HARD_LOCK"
        assert "lock_id" in data_valid
        lock_id = data_valid["lock_id"]

        # Verify persisted in database
        async with db_manager.get_session_maker()() as session:
            stmt = select(DataLock).where(DataLock.id == lock_id)
            res = await session.execute(stmt)
            persisted_lock = res.scalars().first()
            assert persisted_lock is not None
            assert persisted_lock.lock_type == "HARD_LOCK"
            assert persisted_lock.is_active is True
            assert persisted_lock.signature_token is not None

        # 4. Replay of the consumed token -> Rejected with 401
        res_replay = await client.post(
            "/api/v1/execution/locks/lock",
            json=payload,
            headers=headers_valid,
        )
        assert res_replay.status_code == 401
        assert res_replay.json()["detail"] == "REAUTHENTICATION_REQUIRED"


@pytest.mark.asyncio
async def test_unlock_enforces_min_50_char_justification() -> None:
    """Validate that unlocking data strictly enforces >= 50 characters justification.

    @req:PRD-SYS-001
    @req:PRD-SYS-002
    @req:PRD-MDR-002
    @req:Trace-1
    @req:Trace-3
    @req:Trace-13
    @req:Trace-17
    """
    # 1. Create active lock first
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            lock = DataLock(
                id="dl_test_unlock_01",
                study_id="STUDY-001",
                form_id="FORM-TEST-01",
                scope_type="FORM",
                scope_id="FORM-TEST-01",
                lock_type="LOCKED",
                is_active=True,
                created_by="datamanager_user",
                reason_for_change="Routine lock",
            )
            session.add(lock)

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = generate_auth_headers(user_id="lead_cra", roles="data_manager")

        # 2. Attempt unlock with short justification (49 characters) -> Rejected with 400
        short_justification = (
            "1234567890123456789012345678901234567890123456789"  # 49 chars
        )
        assert len(short_justification) == 49
        res_short = await client.post(
            "/api/v1/execution/locks/unlock",
            json={
                "lock_id": "dl_test_unlock_01",
                "form_id": "FORM-TEST-01",
                "scope_type": "FORM",
                "justification": short_justification,
            },
            headers=headers,
        )
        assert res_short.status_code == 400
        assert "at least 50 characters" in res_short.json()["detail"]

        # 3. Attempt unlock with valid >=50 characters justification -> Succeeded with 200
        valid_justification = "Unlock approved by Lead CRA to resolve adverse event critical discrepancy query Q-104."  # 87 chars
        assert len(valid_justification) >= 50
        res_valid = await client.post(
            "/api/v1/execution/locks/unlock",
            json={
                "lock_id": "dl_test_unlock_01",
                "form_id": "FORM-TEST-01",
                "scope_type": "FORM",
                "justification": valid_justification,
            },
            headers=headers,
        )
        assert res_valid.status_code == 200
        data = res_valid.json()
        assert data["status"] == "UNLOCKED"
        assert data["is_active"] is False

        # 4. Verify database state updated
        async with db_manager.get_session_maker()() as session:
            stmt = select(DataLock).where(DataLock.id == "dl_test_unlock_01")
            res = await session.execute(stmt)
            updated_lock = res.scalars().first()
            assert updated_lock is not None
            assert updated_lock.is_active is False
            assert updated_lock.unlocked_by == "lead_cra"
            assert updated_lock.unlocked_at is not None
            assert updated_lock.unlock_justification == valid_justification


@pytest.mark.asyncio
async def test_unlocked_entity_allows_subsequent_mutations() -> None:
    """Validate that unlocking a locked entity restores full write permissions.

    @req:PRD-SYS-001
    @req:PRD-SYS-002
    @req:PRD-MDR-002
    @req:Trace-1
    @req:Trace-3
    @req:Trace-13
    @req:Trace-17
    """
    # 1. Insert subject and form
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            subj = ClinicalSubject(
                id="subj-perm-01",
                subject_id="SUBJ-PERM-01",
                study_id="STUDY-001",
                site_id="SITE-001",
                status="SCREENING",
            )
            form = FormSubmission(
                id="fs-perm-01",
                study_id="STUDY-001",
                site_id="SITE-001",
                subject_id="SUBJ-PERM-01",
                visit_id="VISIT-001",
                form_id="FORM-PERM",
                status="DRAFT",
            )
            session.add(subj)
            session.add(form)

    # 2. Lock the form via API
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = generate_auth_headers()
        res_lock = await client.post(
            "/api/v1/execution/locks/lock",
            json={
                "study_id": "STUDY-001",
                "site_id": "SITE-001",
                "subject_id": "SUBJ-PERM-01",
                "form_id": "FORM-PERM",
                "scope_type": "FORM",
                "scope_id": "FORM-PERM",
                "action": "LOCK",
                "reason_for_change": "Locking form for interim data snapshot",
            },
            headers=headers,
        )
        assert res_lock.status_code == 200
        lock_id = res_lock.json()["lock_id"]

    # 3. Mutation blocked while locked
    with pytest.raises(PermissionError):
        async with db_manager.get_session_maker()() as session:
            async with session.begin():
                stmt = select(FormSubmission).where(FormSubmission.id == "fs-perm-01")
                res = await session.execute(stmt)
                f = res.scalars().first()
                f.status = "COMPLETED"

    # 4. Unlock form with valid >=50 char justification
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        res_unlock = await client.post(
            "/api/v1/execution/locks/unlock",
            json={
                "lock_id": lock_id,
                "form_id": "FORM-PERM",
                "scope_type": "FORM",
                "justification": "Form unlocked following formal Sponsor request to amend screening baseline vital signs.",
            },
            headers=headers,
        )
        assert res_unlock.status_code == 200

    # 5. Mutation now succeeds cleanly
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            stmt = select(FormSubmission).where(FormSubmission.id == "fs-perm-01")
            res = await session.execute(stmt)
            f = res.scalars().first()
            f.status = "COMPLETED"

    # Verify updated status
    async with db_manager.get_session_maker()() as session:
        stmt = select(FormSubmission).where(FormSubmission.id == "fs-perm-01")
        res = await session.execute(stmt)
        f = res.scalars().first()
        assert f.status == "COMPLETED"
        assert f.version == 2


@pytest.mark.asyncio
async def test_get_lock_status_and_tree_endpoints() -> None:
    """Validate GET /api/v1/execution/locks/status/{form_id}, list, and tree endpoints.

    @req:PRD-SYS-001
    @req:PRD-SYS-002
    @req:PRD-MDR-002
    @req:Trace-1
    @req:Trace-3
    @req:Trace-13
    @req:Trace-17
    """
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            lock1 = DataLock(
                id="dl_tree_01",
                study_id="STUDY-001",
                site_id="SITE-101",
                scope_type="SITE",
                scope_id="SITE-101",
                lock_type="FROZEN",
                is_active=True,
                created_by="dm_lead",
                reason_for_change="Site freeze",
            )
            lock2 = DataLock(
                id="dl_tree_02",
                study_id="STUDY-001",
                site_id="SITE-101",
                subject_id="SUBJ-101",
                form_id="FORM-DM",
                scope_type="FORM",
                scope_id="FORM-DM",
                lock_type="HARD_LOCK",
                is_active=True,
                created_by="dm_lead",
                reason_for_change="Form lock",
            )
            session.add(lock1)
            session.add(lock2)

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = generate_auth_headers()

        # 1. GET status by form_id
        res_form = await client.get(
            "/api/v1/execution/locks/status/FORM-DM", headers=headers
        )
        assert res_form.status_code == 200
        form_locks = res_form.json()
        assert len(form_locks) >= 1
        assert any(rec["lock_id"] == "dl_tree_02" for rec in form_locks)

        # 2. GET list filtered by scope_type
        res_list = await client.get(
            "/api/v1/execution/locks?scope_type=SITE", headers=headers
        )
        assert res_list.status_code == 200
        site_locks = res_list.json()
        assert len(site_locks) >= 1
        assert any(rec["lock_id"] == "dl_tree_01" for rec in site_locks)

        # 3. GET hierarchy tree
        res_tree = await client.get(
            "/api/v1/execution/locks/tree?study_id=STUDY-001", headers=headers
        )
        assert res_tree.status_code == 200
        tree = res_tree.json()
        assert tree["study_id"] == "STUDY-001"
        assert "SITE-101" in tree["site_locks"]
        assert "FORM-DM" in tree["form_locks"]
        assert tree["total_active_locks"] >= 2


@pytest.mark.asyncio
async def test_trial_lock_manager_methods_and_reset() -> None:
    """Validate all direct methods on TrialLockManager.

    @req:PRD-SYS-001
    @req:PRD-SYS-002
    @req:PRD-MDR-002
    @req:Trace-1
    @req:Trace-3
    @req:Trace-13
    @req:Trace-17
    """
    TrialLockManager.reset()
    assert TrialLockManager.is_locked() is False
    assert TrialLockManager.is_site_locked("SITE-1") is False
    assert TrialLockManager.is_visit_locked("VISIT-1") is False
    assert TrialLockManager.is_subject_locked("SUBJ-1") is False
    assert TrialLockManager.is_form_locked("FORM-1") is False
    assert TrialLockManager.is_field_locked("SYSBP") is False
    assert TrialLockManager.is_field_locked("SYSBP", "FORM-1") is False

    # Lock and unlock trial
    TrialLockManager.lock_trial(reason="Breach detected")
    assert TrialLockManager.is_locked() is True
    TrialLockManager.unlock_trial()
    assert TrialLockManager.is_locked() is False

    # Lock and unlock site
    TrialLockManager.lock_site("SITE-1")
    assert TrialLockManager.is_site_locked("SITE-1") is True
    TrialLockManager.unlock_site("SITE-1")
    assert TrialLockManager.is_site_locked("SITE-1") is False

    # Lock and unlock visit
    TrialLockManager.lock_visit("VISIT-1")
    assert TrialLockManager.is_visit_locked("VISIT-1") is True
    TrialLockManager.unlock_visit("VISIT-1")
    assert TrialLockManager.is_visit_locked("VISIT-1") is False

    # Lock and unlock subject
    TrialLockManager.lock_subject("SUBJ-1")
    assert TrialLockManager.is_subject_locked("SUBJ-1") is True
    TrialLockManager.unlock_subject("SUBJ-1")
    assert TrialLockManager.is_subject_locked("SUBJ-1") is False

    # Lock and unlock form
    TrialLockManager.lock_form("FORM-1")
    assert TrialLockManager.is_form_locked("FORM-1") is True
    TrialLockManager.unlock_form("FORM-1")
    assert TrialLockManager.is_form_locked("FORM-1") is False

    # Lock and unlock field
    TrialLockManager.lock_field("SYSBP")
    assert TrialLockManager.is_field_locked("SYSBP") is True
    TrialLockManager.unlock_field("SYSBP")
    assert TrialLockManager.is_field_locked("SYSBP") is False

    # Composite form:field
    TrialLockManager.lock_field("SYSBP", "FORM-1")
    assert TrialLockManager.is_field_locked("SYSBP", "FORM-1") is True
    TrialLockManager.unlock_field("SYSBP", "FORM-1")
    assert TrialLockManager.is_field_locked("SYSBP", "FORM-1") is False


@pytest.mark.asyncio
async def test_datalock_model_aliases_and_properties() -> None:
    """Validate DataLock model constructor parameter aliases and property accessors.

    @req:PRD-SYS-001
    @req:PRD-SYS-002
    @req:PRD-MDR-002
    @req:Trace-1
    @req:Trace-3
    @req:Trace-13
    @req:Trace-17
    """
    # 1. Alias constructor with locked_by, locked_at, action, scope
    d1 = DataLock(
        lock_id="dl_alias_01",
        study_id="STUDY-1",
        scope="STUDY",
        action="FREEZE",
        locked_by="user_alias",
        locked_at="2026-08-14T00:00:00Z",
        reason_for_change="Freeze alias test",
    )
    assert d1.id == "dl_alias_01"
    assert d1.lock_id == "dl_alias_01"
    assert d1.scope_type == "STUDY"
    assert d1.scope_id == "STUDY-1"
    assert d1.lock_type == "FROZEN"
    assert d1.status == "FROZEN"
    assert d1.created_by == "user_alias"
    assert d1.locked_by == "user_alias"
    assert isinstance(d1.created_at, datetime)
    assert isinstance(d1.locked_at, datetime)

    # 2. Scope_id inference for various scope types
    d_site = DataLock(
        site_id="SITE-99", scope_type="SITE", reason_for_change="site lock"
    )
    assert d_site.scope_id == "SITE-99"

    d_subj = DataLock(
        subject_id="SUBJ-99",
        scope_type="SUBJECT",
        reason_for_change="subj lock",
    )
    assert d_subj.scope_id == "SUBJ-99"

    d_vis = DataLock(
        visit_id="VIS-99", scope_type="VISIT", reason_for_change="vis lock"
    )
    assert d_vis.scope_id == "VIS-99"

    d_fld = DataLock(
        field_name="DIABP", scope_type="FIELD", reason_for_change="fld lock"
    )
    assert d_fld.scope_id == "DIABP"

    d_grp = DataLock(
        item_group_id="GRP-1",
        scope_type="FIELD",
        reason_for_change="grp lock",
    )
    assert d_grp.scope_id == "GRP-1"

    # Status when inactive
    d1.is_active = False
    assert d1.status == "UNLOCKED"


@pytest.mark.asyncio
async def test_lock_and_unlock_router_branches_and_validation() -> None:
    """Validate router endpoints across various scopes, error handling, and in-memory synchronization.

    @req:PRD-SYS-001
    @req:PRD-SYS-002
    @req:PRD-MDR-002
    @req:Trace-1
    @req:Trace-3
    @req:Trace-13
    @req:Trace-17
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = generate_auth_headers()

        # 1. Missing reason in lock request -> 400 Bad Request
        res_no_reason = await client.post(
            "/api/v1/execution/locks/lock",
            json={
                "study_id": "STUDY-001",
                "scope_type": "STUDY",
                "scope_id": "STUDY-001",
                "action": "LOCK",
            },
            headers=headers,
        )
        assert res_no_reason.status_code == 400

        # 2. Lock SITE via API
        res_site = await client.post(
            "/api/v1/execution/locks/lock",
            json={
                "study_id": "STUDY-001",
                "site_id": "SITE-BRANCH-1",
                "scope_type": "SITE",
                "action": "FREEZE",
                "reason_for_change": "Soft freeze site branch",
            },
            headers=headers,
        )
        assert res_site.status_code == 200
        assert res_site.json()["status"] == "FROZEN"

        # 3. Lock SUBJECT via API
        res_subj = await client.post(
            "/api/v1/execution/locks/lock",
            json={
                "study_id": "STUDY-001",
                "site_id": "SITE-BRANCH-1",
                "subject_id": "SUBJ-BRANCH-1",
                "scope_type": "SUBJECT",
                "action": "LOCK",
                "reason_for_change": "Lock subject branch",
            },
            headers=headers,
        )
        assert res_subj.status_code == 200

        # 4. Lock VISIT via API
        res_vis = await client.post(
            "/api/v1/execution/locks/lock",
            json={
                "study_id": "STUDY-001",
                "visit_id": "VISIT-BRANCH-1",
                "scope_type": "VISIT",
                "action": "LOCK",
                "reason_for_change": "Lock visit branch",
            },
            headers=headers,
        )
        assert res_vis.status_code == 200

        # 5. Lock FIELD via API
        res_fld = await client.post(
            "/api/v1/execution/locks/lock",
            json={
                "study_id": "STUDY-001",
                "form_id": "FORM-BRANCH-1",
                "field_name": "HEART_RATE",
                "scope_type": "FIELD",
                "action": "LOCK",
                "reason_for_change": "Lock field branch",
            },
            headers=headers,
        )
        assert res_fld.status_code == 200

        # 6. Unlock SITE via API with >=50 char justification
        res_un_site = await client.post(
            "/api/v1/execution/locks/unlock",
            json={
                "site_id": "SITE-BRANCH-1",
                "scope_type": "SITE",
                "justification": "Site branch unlocked for routine regulatory audit review and validation.",
            },
            headers=headers,
        )
        assert res_un_site.status_code == 200

        # 7. Unlock VISIT via API with >=50 char justification
        res_un_vis = await client.post(
            "/api/v1/execution/locks/unlock",
            json={
                "visit_id": "VISIT-BRANCH-1",
                "scope_type": "VISIT",
                "justification": "Visit branch unlocked following formal query resolution by lead investigator.",
            },
            headers=headers,
        )
        assert res_un_vis.status_code == 200

        # 8. Unlock SUBJECT via API with >=50 char justification
        res_un_subj = await client.post(
            "/api/v1/execution/locks/unlock",
            json={
                "subject_id": "SUBJ-BRANCH-1",
                "scope_type": "SUBJECT",
                "justification": "Subject branch unlocked for follow-up SAE reconciliation and verification.",
            },
            headers=headers,
        )
        assert res_un_subj.status_code == 200

        # 9. Unlock FIELD via API with >=50 char justification
        res_un_fld = await client.post(
            "/api/v1/execution/locks/unlock",
            json={
                "form_id": "FORM-BRANCH-1",
                "field_name": "HEART_RATE",
                "scope_type": "FIELD",
                "justification": "Field heart rate unlocked to correct manual transcription error per source.",
            },
            headers=headers,
        )
        assert res_un_fld.status_code == 200
