import asyncio
import time
from datetime import UTC, datetime, timedelta
from unittest.mock import AsyncMock, patch

import httpx
import pytest
import pytest_asyncio
from jose import jwt
from sqlalchemy import select, text

# Import both db_managers and bases
from apps.execution.database.core import db_manager as exec_db_manager
from apps.execution.database.models import (
    Base as ExecBase,
)
from apps.execution.database.models import (
    ClinicalObservation,
    ClinicalQuery,
    ClinicalSubject,
    ClinicalVisit,
    SubjectRandomization,
)
from apps.execution.main import app as execution_app
from apps.execution.queries_escalation import execute_query_escalation_cycle
from apps.execution.trial_lock import TrialLockManager
from apps.notifications.database import db_manager as notif_db_manager
from apps.notifications.main import app as notifications_app
from apps.notifications.models import Base as NotifBase
from apps.notifications.models import Notification, NotificationAuditLog
from packages.security.context import audit_context
from packages.security.signing import generate_gateway_signature

GATEWAY_SECRET = "internal-gateway-secret-12345"  # pragma: allowlist secret


def get_v2_auth_headers(
    user_id: str = "test_user",
    roles: str = "admin",
    change_reason: str = "test operation",
    unblinded_access: bool = False,
) -> dict[str, str]:
    """Generate Gateway signature version 2 authentication headers."""
    timestamp = str(time.time())
    sig = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=GATEWAY_SECRET.encode(),
        change_reason=change_reason,
        unblinded_access=unblinded_access,
    )
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }
    if unblinded_access:
        headers["X-Unblinded-Access"] = "true"
    return headers


def get_sig_token(
    user_id="test_inv", roles="principal_investigator", action="unblind"
) -> str:
    payload = {
        "sub": user_id,
        "username": user_id,
        "action": action,
        "roles": [roles],
        "iat": time.time(),
        "exp": time.time() + 60.0,
    }
    return jwt.encode(payload, "internal-gateway-secret-12345", algorithm="HS256")  # pragma: allowlist secret


@pytest_asyncio.fixture(autouse=True)
async def setup_dual_dbs(monkeypatch):
    """
    Setup in-memory SQLite databases for both Clinical Execution and Notifications services,
    and monkeypatch outbound HTTP calls from execution to route in-process to notifications.
    """
    # 1. Reset and initialize notifications database
    from apps.notifications.main import active_deliveries

    active_deliveries.clear()
    notif_db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with notif_db_manager.engine.begin() as conn:
        await conn.run_sync(NotifBase.metadata.create_all)

    # 2. Reset and initialize execution database
    TrialLockManager.reset()
    exec_db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with exec_db_manager.engine.begin() as conn:
        await conn.run_sync(ExecBase.metadata.create_all)

    # 3. Monkeypatch httpx.AsyncClient in packages.security.gateway_client to route to notifications_app
    original_async_client = httpx.AsyncClient

    def mock_async_client(*args, **kwargs):
        if "transport" in kwargs:
            return original_async_client(*args, **kwargs)
        kwargs["transport"] = httpx.ASGITransport(app=notifications_app)
        return original_async_client(*args, **kwargs)

    monkeypatch.setattr(
        "packages.security.gateway_client.httpx.AsyncClient", mock_async_client
    )

    yield

    # Cleanup
    async with notif_db_manager.engine.begin() as conn:
        await conn.run_sync(NotifBase.metadata.drop_all)
    await notif_db_manager.close()
    active_deliveries.clear()

    async with exec_db_manager.engine.begin() as conn:
        await conn.run_sync(ExecBase.metadata.drop_all)
    await exec_db_manager.close()
    TrialLockManager.reset()


@pytest.mark.asyncio
async def test_trial_lock_generates_notification():
    """
    # @req:PRD-SYS-003
    # @req:PRD-SYS-004
    Verify that triggering a trial-wide lock generates and persists a real notification
    and audit log in the notifications service.
    """
    # Trigger trial lock
    with audit_context(
        user_id="security_lead", change_reason="Intrusion attempt detected"
    ):
        TrialLockManager.lock_trial("Unauthorized schema access detected")

    # Give async tasks a brief moment to process
    await asyncio.sleep(0.2)

    # Query notifications database and verify record exists
    async with notif_db_manager.get_session_maker()() as session:
        stmt = select(Notification).where(Notification.category == "ALERTS")
        res = await session.execute(stmt)
        notifs = res.scalars().all()

        assert len(notifs) > 0
        critical_alert = [
            n for n in notifs if "URGENT: Trial locked" in n.message_content
        ]
        assert len(critical_alert) > 0
        assert critical_alert[0].priority == "CRITICAL"
        assert critical_alert[0].related_entity_type == "trial-lock"

        # Verify NotificationAuditLog entry
        stmt_audit = select(NotificationAuditLog).where(
            NotificationAuditLog.action == "NOTIFICATION_CREATE"
        )
        res_audit = await session.execute(stmt_audit)
        audit_logs = res_audit.scalars().all()
        assert len(audit_logs) > 0


@pytest.mark.asyncio
async def test_query_aging_generates_notification():
    """
    # @req:PRD-QRY-002
    Verify that clinical query aging digest generates and persists real notifications.
    """
    cutoff_date = datetime.now() - timedelta(days=15)
    async with exec_db_manager.get_session_maker()() as session:
        async with session.begin():
            # Seed clinical subject and visit
            subj = ClinicalSubject(
                subject_id="SUBJ-AGING-99",
                study_id="STUDY-AGING-99",
                site_id="SITE-AGING-99",
            )
            session.add(subj)

            visit = ClinicalVisit(
                id="VISIT-AGING-99",
                subject_id="SUBJ-AGING-99",
                study_id="STUDY-AGING-99",
                visit_name="Baseline",
            )
            session.add(visit)

            # Seed aging query
            q = ClinicalQuery(
                study_id="STUDY-AGING-99",
                site_id="SITE-AGING-99",
                subject_id="SUBJ-AGING-99",
                visit_id="VISIT-AGING-99",
                test_code="ALT",
                explanation="Value exceeds protocol limits",
                status="OPEN",
                is_deleted=False,
                created_at=cutoff_date,
            )
            session.add(q)

    # Trigger query escalation/aging cycle
    with audit_context(
        user_id="system_cron", change_reason="Daily clinical query aging scan"
    ):
        await execute_query_escalation_cycle(exec_db_manager.get_session_maker())

    # Wait for background publish
    await asyncio.sleep(0.2)

    # Verify notifications DB
    async with notif_db_manager.get_session_maker()() as session:
        stmt = select(Notification).where(Notification.category == "ACTION_ITEMS")
        res = await session.execute(stmt)
        notifs = res.scalars().all()

        assert len(notifs) > 0
        digest_notif = [
            n
            for n in notifs
            if "Daily Clinical Query Aging Digest" in n.message_content
        ]
        assert len(digest_notif) > 0
        assert digest_notif[0].priority == "HIGH"
        assert digest_notif[0].related_entity_type == "study-site"
        assert "STUDY-AGING-99:SITE-AGING-99" in digest_notif[0].related_entity_id


@pytest.mark.asyncio
async def test_sdv_drop_generates_notification():
    """
    # @req:PRD-QRY-006
    Verify that an automatic verification drop triggers a real notification to the original verifier.
    """
    # 1. Populate DB with a verified observation
    async with exec_db_manager.get_session_maker()() as session:
        async with session.begin():
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', 1);")
            )
            subj = ClinicalSubject(
                subject_id="SUBJ-DROP-99",
                study_id="STUDY-DROP-99",
                site_id="SITE-DROP-99",
            )
            session.add(subj)

            obs = ClinicalObservation(
                id="OBS-DROP-99",
                subject_id="SUBJ-DROP-99",
                study_id="STUDY-DROP-99",
                visit_id="VISIT-DROP-99",
                page_id="PAGE-DROP-99",
                domain="VS",
                test_code="SYSBP",
                test_name="Systolic Blood Pressure",
                value=120.0,
                value_string="120",
                normalized_value="120",
                is_sdv_verified=True,
                sdv_verified_by="verifier_cra_99",
                sdv_verified_at=datetime.now(UTC).replace(tzinfo=None),
            )
            session.add(obs)

    # 2. Modify value of verified observation to trigger SDV-drop
    with audit_context(
        user_id="editor-user-99",
        change_reason="Typo correction in systolic blood pressure",
    ):
        async with exec_db_manager.get_session_maker()() as session:
            res = await session.execute(
                select(ClinicalObservation).where(
                    ClinicalObservation.id == "OBS-DROP-99"
                )
            )
            obs_edit = res.scalar_one()
            obs_edit.value = 125.0
            await session.commit()

    # Wait for background publish
    await asyncio.sleep(0.2)

    # Verify notifications DB
    async with notif_db_manager.get_session_maker()() as session:
        stmt = select(Notification).where(Notification.category == "ALERTS")
        res = await session.execute(stmt)
        notifs = res.scalars().all()

        assert len(notifs) > 0
        drop_notif = [n for n in notifs if "verifier_cra_99" in n.recipient_user_id]
        assert len(drop_notif) > 0
        assert "Previously verified field modified" in drop_notif[0].message_content
        assert drop_notif[0].priority == "HIGH"
        assert drop_notif[0].related_entity_type == "observation"
        assert drop_notif[0].related_entity_id == "OBS-DROP-99"


@pytest.mark.asyncio
async def test_emergency_unblinding_generates_notification():
    """
    # @req:PRD-SUB-006
    Verify that executing an emergency unblinding generates real notifications targeting multiple roles.
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=execution_app), base_url="http://test"
    ) as client:
        # Create randomized subject in execution DB
        async with exec_db_manager.get_session_maker()() as session:
            subj = ClinicalSubject(
                subject_id="SUBJ-UNBLIND-99",
                study_id="STUDY-UNBLIND-99",
                kit_reference="KIT-9999",
            )
            session.add(subj)
            await session.flush()
            subj.status = "ENROLLED"
            await session.flush()
            subj.status = "RANDOMIZED"

            from apps.execution.cryptography import AllocationKeyManager

            key_mgr = AllocationKeyManager()
            encrypted_alloc = key_mgr.encrypt({"allocation": "Arm A Active"})

            rand = SubjectRandomization(
                study_id="STUDY-UNBLIND-99",
                subject_id="SUBJ-UNBLIND-99",
                encrypted_allocation=encrypted_alloc,
                kit_reference="KIT-9999",
            )
            session.add(rand)
            await session.commit()

        # Submit unblind request
        headers = get_v2_auth_headers(
            user_id="pi_doctor",
            roles="principal_investigator",
            change_reason="Emergency unblinding: patient in critical state",
            unblinded_access=True,
        )
        headers["X-Sig-Token"] = get_sig_token(  # pragma: allowlist secret
            user_id="pi_doctor", roles="principal_investigator"
        )

        unblind_payload = {
            "reason_code": "SAE-Life-Threatening-Event",
            "justification": "Patient non-responsive after drug administration, immediate medical intervention required.",
            "shares": [
                {
                    "custodian": "Lead Unblinded Statistician",
                    "version": 1,
                    "x": 1,
                    "y": 42,
                },
                {"custodian": "IDMC", "version": 1, "x": 2, "y": 87},
            ],
        }

        with (
            patch(
                "apps.execution.cryptography.AllocationKeyManager.load_from_db",
                new_callable=AsyncMock,
            ),
            patch(
                "apps.execution.cryptography.AllocationKeyManager.decrypt_with_shares",
                return_value={"allocation": "Arm A Active"},
            ),
        ):
            res = await client.post(
                "/api/v1/execution/subjects/SUBJ-UNBLIND-99/unblind",  # pragma: allowlist secret
                headers=headers,
                json=unblind_payload,
            )
        assert res.status_code == 200

    # Wait for background publish
    await asyncio.sleep(0.2)

    # Verify notifications DB
    async with notif_db_manager.get_session_maker()() as session:
        stmt = select(Notification).where(Notification.category == "ALERTS")
        res = await session.execute(stmt)
        notifs = res.scalars().all()

        assert len(notifs) > 0
        unblind_notifs = [
            n
            for n in notifs
            if "Emergency unblinding alert for Subject SUBJ-UNBLIND-99"
            in n.message_content
        ]
        assert (
            len(unblind_notifs) == 3
        )  # Targeting roles: "Sponsor Safety Lead", "Lead CRA", "IDMC"

        target_roles = [n.recipient_role for n in unblind_notifs]
        assert "Sponsor Safety Lead" in target_roles
        assert "Lead CRA" in target_roles
        assert "IDMC" in target_roles

        for n in unblind_notifs:
            assert n.priority == "CRITICAL"
            assert n.related_entity_type == "subject"
            assert n.related_entity_id == "SUBJ-UNBLIND-99"
