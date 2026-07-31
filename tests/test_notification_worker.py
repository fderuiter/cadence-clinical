import asyncio
from datetime import datetime, timezone
from unittest.mock import AsyncMock, MagicMock, patch

import pytest
import pytest_asyncio
from notifications.event_models import SystemDomainEvent
from sqlalchemy import select

from apps.notifications.database import db_manager as notifications_db_manager
from apps.notifications.models import (
    Base as NotificationsBase,
)
from apps.notifications.models import (
    Notification,
    NotificationDelivery,
)
from apps.notifications.workers.notification_worker import (
    NotificationWorker,
    publish_domain_event,
    start_notification_worker,
    stop_notification_worker,
)
from apps.org.database import db_manager as org_db_manager
from apps.org.models import (
    Base as OrgBase,
)
from apps.org.models import (
    Organization,
    Personnel,
    PersonnelAssignment,
    Site,
)


@pytest_asyncio.fixture(autouse=True)
async def setup_test_databases():
    """
    Autouse fixture to spin up separate in-memory sqlite databases for
    both the Notifications and Org microservices, preventing test-to-test pollution.
    Also ensures any background worker task is cleanly terminated and reset.
    """
    import apps.notifications.workers.notification_worker as nw

    nw._should_run = False
    if nw._worker_task:
        nw._worker_task.cancel()
        try:
            await nw._worker_task
        except Exception:
            pass
        nw._worker_task = None

    # Clear mock queue
    while not nw._mock_queue.empty():
        try:
            nw._mock_queue.get_nowait()
        except asyncio.QueueEmpty:
            break

    # Initialize Notifications Relational DB
    notifications_db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with notifications_db_manager.engine.begin() as conn:
        await conn.run_sync(NotificationsBase.metadata.create_all)

    # Initialize Org Directory Relational DB
    org_db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with org_db_manager.engine.begin() as conn:
        await conn.run_sync(OrgBase.metadata.create_all)

    yield

    # Clean up worker task
    nw._should_run = False
    if nw._worker_task:
        nw._worker_task.cancel()
        try:
            await nw._worker_task
        except Exception:
            pass
        nw._worker_task = None

    while not nw._mock_queue.empty():
        try:
            nw._mock_queue.get_nowait()
        except asyncio.QueueEmpty:
            break

    # Clean up both DB sessions & engines
    async with notifications_db_manager.engine.begin() as conn:
        await conn.run_sync(NotificationsBase.metadata.drop_all)
    await notifications_db_manager.close()

    async with org_db_manager.engine.begin() as conn:
        await conn.run_sync(OrgBase.metadata.drop_all)
    await org_db_manager.close()


@pytest.mark.asyncio
async def test_worker_resolves_crc_for_edc_query():
    """
    Verify that an EDC_QUERY_RAISED event resolves the site's active CRC staff,
    writes GxP-compliant notifications, and enqueues in-app and email delivery.

    Requirements: PRD-SYS-001
    """
    # 1. Seed Org DB with site and personnel assignments
    async with org_db_manager.get_session_maker()() as session:
        org = Organization(
            name="St. Jude Research Org",
            org_type="site",
            created_by="test_setup",
            reason_for_change="Seed org for test",
        )
        session.add(org)
        await session.flush()

        site = Site(
            site_id="SITE-01",
            name="Boston Oncology",
            organization_id=org.id,
            study_id="STUDY-100",
            created_by="test_setup",
            reason_for_change="Seed site for test",
        )
        session.add(site)
        await session.flush()

        # Target CRC personnel
        person_crc = Personnel(
            keycloak_user_id="kc-crc-01",
            first_name="Jane",
            last_name="Coordinator",
            email="jane.coordinator@stjude.org",
            role="crc",  # Match required role
            organization_id=org.id,
            site_id="SITE-01",
            study_id="STUDY-100",
            created_by="test_setup",
            reason_for_change="Onboard test CRC",
        )
        # Non-target personnel (different role)
        person_other = Personnel(
            keycloak_user_id="kc-investigator-01",
            first_name="Arthur",
            last_name="Investigator",
            email="arthur.pi@stjude.org",
            role="investigator",
            organization_id=org.id,
            site_id="SITE-01",
            study_id="STUDY-100",
            created_by="test_setup",
            reason_for_change="Onboard test Investigator",
        )
        session.add_all([person_crc, person_other])
        await session.flush()

        assign_crc = PersonnelAssignment(
            personnel_id=person_crc.id,
            site_id="SITE-01",
            study_id="STUDY-100",
            is_active=True,
            created_by="test_setup",
            reason_for_change="Assign CRC to Boston",
        )
        assign_other = PersonnelAssignment(
            personnel_id=person_other.id,
            site_id="SITE-01",
            study_id="STUDY-100",
            is_active=True,
            created_by="test_setup",
            reason_for_change="Assign investigator to Boston",
        )
        session.add_all([assign_crc, assign_other])
        await session.commit()

    # 2. Process event
    worker = NotificationWorker()
    event = SystemDomainEvent(
        event_id="evt-query-001",
        event_type="EDC_QUERY_RAISED",
        source_service="edc",
        study_id="STUDY-100",
        payload={
            "site_id": "SITE-01",
            "subject_id": "SUBJ-901",
            "form_name": "Demographics",
            "field_name": "Age",
            "query_message": "Age field is blank. Please verify.",
        },
        timestamp_utc=datetime.now(timezone.utc).isoformat(),
    )

    dispatched = await worker.process_domain_event(event)
    assert dispatched == 1

    # 3. Verify in Notifications DB
    async with notifications_db_manager.get_session_maker()() as session:
        # Check parent notification record
        stmt = select(Notification).where(Notification.recipient_user_id == "kc-crc-01")
        res = await session.execute(stmt)
        notifs = res.scalars().all()
        assert len(notifs) == 1
        notif = notifs[0]
        assert notif.category == "ACTION_ITEMS"
        assert notif.priority == "HIGH"
        assert "New clinical query raised" in notif.message_content

        # Check associated delivery rows
        stmt_del = select(NotificationDelivery).where(
            NotificationDelivery.notification_id == notif.id
        )
        res_del = await session.execute(stmt_del)
        deliveries = res_del.scalars().all()
        assert len(deliveries) == 2
        channels = [d.channel for d in deliveries]
        assert "IN_APP" in channels
        assert "EMAIL" in channels


@pytest.mark.asyncio
async def test_worker_resolves_cra_for_document_expiry():
    """
    Verify that an ETMF_DOCUMENT_EXPIRING event resolves the active CRA assigned to the study/site,
    persists a GxP alert record, and sets up appropriate notifications.

    Requirements: PRD-SYS-001
    """
    # 1. Seed Org DB with CRA personnel
    async with org_db_manager.get_session_maker()() as session:
        org = Organization(
            name="Boston CRO",
            org_type="CRO",
            created_by="test_setup",
            reason_for_change="Seed CRO org",
        )
        session.add(org)
        await session.flush()

        person_cra = Personnel(
            keycloak_user_id="kc-cra-01",
            first_name="Steve",
            last_name="Monitor",
            email="steve.monitor@cro.org",
            role="cra",
            organization_id=org.id,
            site_id="SITE-02",
            study_id="STUDY-200",
            created_by="test_setup",
            reason_for_change="Onboard CRA",
        )
        session.add(person_cra)
        await session.flush()

        assign_cra = PersonnelAssignment(
            personnel_id=person_cra.id,
            site_id="SITE-02",
            study_id="STUDY-200",
            is_active=True,
            created_by="test_setup",
            reason_for_change="Assign CRA",
        )
        session.add(assign_cra)
        await session.commit()

    # 2. Process Document Expiring event
    worker = NotificationWorker()
    event = SystemDomainEvent(
        event_id="evt-expiry-002",
        event_type="ETMF_DOCUMENT_EXPIRING",
        source_service="etmf",
        study_id="STUDY-200",
        payload={
            "site_id": "SITE-02",
            "document_name": "Informed Consent Template",
            "artifact_code": "05.02.05",
            "expiration_date": "2026-12-31",
        },
        timestamp_utc=datetime.now(timezone.utc).isoformat(),
    )

    dispatched = await worker.process_domain_event(event)
    assert dispatched == 1

    # 3. Verify in Notifications DB
    async with notifications_db_manager.get_session_maker()() as session:
        stmt = select(Notification).where(Notification.recipient_user_id == "kc-cra-01")
        res = await session.execute(stmt)
        notifs = res.scalars().all()
        assert len(notifs) == 1
        notif = notifs[0]
        assert notif.category == "ALERTS"
        assert notif.priority == "HIGH"
        assert "Informed Consent Template" in notif.message_content


@pytest.mark.asyncio
async def test_worker_resolves_safety_officer_for_sae_flag():
    """
    Verify that an SAE_RECONCILIATION_FLAG event resolves the Safety Officer / Medical Monitor,
    and dispatches a critical priority alert.

    Requirements: PRD-SYS-001
    """
    # 1. Seed Org DB with Safety Officer
    async with org_db_manager.get_session_maker()() as session:
        org = Organization(
            name="Global Safety Sponsor",
            org_type="sponsor",
            created_by="test_setup",
            reason_for_change="Seed sponsor org",
        )
        session.add(org)
        await session.flush()

        person_safety = Personnel(
            keycloak_user_id="kc-safety-01",
            first_name="Diana",
            last_name="Safety",
            email="diana.safety@sponsor.com",
            role="sponsor_mm",
            organization_id=org.id,
            site_id="SITE-03",
            study_id="STUDY-300",
            created_by="test_setup",
            reason_for_change="Onboard Safety Officer",
        )
        session.add(person_safety)
        await session.flush()

        assign_safety = PersonnelAssignment(
            personnel_id=person_safety.id,
            site_id="SITE-03",
            study_id="STUDY-300",
            is_active=True,
            created_by="test_setup",
            reason_for_change="Assign safety role",
        )
        session.add(assign_safety)
        await session.commit()

    # 2. Process SAE flag event
    worker = NotificationWorker()
    event = SystemDomainEvent(
        event_id="evt-sae-003",
        event_type="SAE_RECONCILIATION_FLAG",
        source_service="safety",
        study_id="STUDY-300",
        payload={
            "site_id": "SITE-03",
            "subject_id": "SUBJ-301",
            "adverse_event_description": "Anaphylaxis",
            "flag_reason": "Sponsor database AE severity grade mismatch",
            "discrepancy_source": "Dataset-JSON VS EDC Form AE",
        },
        timestamp_utc=datetime.now(timezone.utc).isoformat(),
    )

    dispatched = await worker.process_domain_event(event)
    assert dispatched == 1

    # 3. Verify in Notifications DB
    async with notifications_db_manager.get_session_maker()() as session:
        stmt = select(Notification).where(
            Notification.recipient_user_id == "kc-safety-01"
        )
        res = await session.execute(stmt)
        notifs = res.scalars().all()
        assert len(notifs) == 1
        notif = notifs[0]
        assert notif.category == "ALERTS"
        assert notif.priority == "CRITICAL"
        assert "Urgent SAE reconciliation mismatch" in notif.message_content


@pytest.mark.asyncio
async def test_worker_gxp_exponential_retry_and_dlq():
    """
    Verify GxP compliance of retry mechanics: if a processing failure occurs,
    the background worker applies exponential backoff, retries up to max allowed,
    and then logs the failure to the Dead-Letter Queue (DLQ).

    Requirements: PRD-SYS-001
    """
    event = SystemDomainEvent(
        event_id="evt-faulty-01",
        event_type="EDC_QUERY_RAISED",
        source_service="edc",
        study_id="STUDY-100",
        payload={
            "site_id": "SITE-01",
            "query_message": "Faulty event",
        },
        timestamp_utc="2026-10-10T12:00:00Z",
    )

    # Mock the NotificationWorker to raise an exception
    mock_process = AsyncMock(side_effect=Exception("Database connection timed out"))

    # Create local mock logger
    mock_logger = MagicMock()

    with (
        patch(
            "apps.notifications.workers.notification_worker.NotificationWorker.process_domain_event",
            mock_process,
        ),
        patch(
            "apps.notifications.workers.notification_worker.logger",
            mock_logger,
        ),
    ):
        # Start background worker and publish fault event
        await start_notification_worker()
        await publish_domain_event(event)

        # Wait robustly in a small loop until DLQ error is logged
        for _ in range(100):
            await asyncio.sleep(0.01)
            # Find any call containing [DLQ]
            dlq_logged = False
            for call in mock_logger.error.call_args_list:
                if call[0] and "[DLQ]" in str(call[0][0]):
                    dlq_logged = True
                    break
            if dlq_logged:
                break

        await stop_notification_worker()

        # Verify DLQ logging happened
        dlq_calls = [
            call
            for call in mock_logger.error.call_args_list
            if call[0] and "[DLQ]" in str(call[0][0])
        ]
        assert len(dlq_calls) == 1
        assert "Database connection timed out" in str(dlq_calls[0][0])


@pytest.mark.asyncio
async def test_start_stop_notification_worker_integration():
    """
    Verify that starting and stopping the notification background consumer worker
    manages the task lifespan and processes events successfully.

    Requirements: PRD-SYS-001
    """
    event = SystemDomainEvent(
        event_id="evt-integration-99",
        event_type="PROTOCOL_AMENDMENT_SUBMITTED",
        source_service="designer",
        study_id="STUDY-99",
        payload={
            "amendment_tag": "Amendment-02",
            "version_index": 2,
            "change_reason": "Adding mandatory site assessment section",
            "submitted_by": "sponsor_admin_01",
        },
        timestamp_utc=datetime.now(timezone.utc).isoformat(),
    )

    # Start the background worker loop
    await start_notification_worker()

    # Publish an event to the queue
    await publish_domain_event(event)

    # Give a tiny slice of time for the async background worker task to pick up and process
    await asyncio.sleep(0.2)

    # Stop the worker cleanly
    await stop_notification_worker()

    # Check that a notification record was created in the Notifications database
    async with notifications_db_manager.get_session_maker()() as session:
        stmt = select(Notification).where(
            Notification.related_entity_id == "evt-integration-99"
        )
        res = await session.execute(stmt)
        notifs = res.scalars().all()
        assert len(notifs) >= 1
        assert notifs[0].category == "SYSTEM"
        assert notifs[0].priority == "LOW"
        assert "Protocol amendment submitted" in notifs[0].message_content
