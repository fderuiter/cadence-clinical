import asyncio
import time
from datetime import datetime, timedelta
from unittest.mock import AsyncMock, patch

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.gateway.main import generate_signature
from apps.notifications.database import db_manager
from apps.notifications.main import app, poll_and_dispatch
from apps.notifications.models import (
    Base,
    Notification,
    NotificationAuditLog,
    NotificationCategory,
    NotificationDelivery,
    NotificationPriority,
)


@pytest_asyncio.fixture(autouse=True)
async def setup_notifications_db():
    """
    Setup in-memory Notifications database for unit and integration testing.
    """
    from apps.notifications.main import active_deliveries, active_tasks

    active_deliveries.clear()
    active_tasks.clear()
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    # Clean up outstanding background tasks robustly
    for task in list(active_tasks):
        if not task.done():
            task.cancel()
    if active_tasks:
        await asyncio.gather(*active_tasks, return_exceptions=True)
    active_tasks.clear()

    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()
    active_deliveries.clear()


def get_auth_headers(
    user_id: str = "notifications_test_user",
    roles: str = "admin",
    change_reason: str = "",
) -> dict:
    """
    Helper to generate valid gateway V2 signed headers for testing.
    """
    timestamp = str(time.time())
    sig = generate_signature(
        user_id, roles, timestamp, version="2", change_reason=change_reason
    )
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
    }
    if change_reason:
        headers["X-Change-Reason"] = change_reason
    return headers


def test_notifications_health_check():
    """
    Verify health check of independent Notifications service.
    """
    client = TestClient(app)
    response = client.get("/health")
    assert response.status_code == 200
    data = response.json()
    assert data["status"] == "ok"
    assert data["service"] == "notifications"


@pytest.mark.asyncio
async def test_notifications_database_schema_creation():
    """
    Verify that notification tables can be created and queried successfully.
    """
    async with db_manager.get_session_maker()() as session:
        notifications = await session.execute(select(Notification))
        logs = await session.execute(select(NotificationAuditLog))
        deliveries = await session.execute(select(NotificationDelivery))

        assert notifications.scalars().all() == []
        assert logs.scalars().all() == []
        assert deliveries.scalars().all() == []


@pytest.mark.asyncio
async def test_notification_creation_and_auditing():
    """
    Verify that creating a notification persists all fields, sets state to PENDING,
    and transitions to DELIVERED after dispatcher execution.
    """
    client = TestClient(app)
    headers = get_auth_headers(
        user_id="user_creator",
        roles="Grants Manager",
        change_reason="Notify principal investigator",
    )

    payload = {
        "recipient_user_id": "pi_john",
        "recipient_role": "investigator",
        "category": "ACTION_ITEMS",
        "priority": "HIGH",
        "channels": "IN_APP,EMAIL",
        "message_content": "Please sign off on the pending visit log.",
        "related_entity_id": "visit_abc_123",
        "related_entity_type": "VISIT",
    }

    # Create the notification - initially starts as PENDING due to IN_APP channel
    response = client.post("/api/v1/notifications", json=payload, headers=headers)
    assert response.status_code == 201
    data = response.json()
    assert data["id"] is not None
    assert data["recipient_user_id"] == "pi_john"
    assert data["recipient_role"] == "investigator"
    assert data["category"] == "ACTION_ITEMS"
    assert data["priority"] == "HIGH"
    assert data["status"] == "OPEN"
    assert data["delivery_state"] == "PENDING"
    assert data["version_index"] == 1

    notification_id = data["id"]

    # Verify that associated NotificationDelivery rows were created in PENDING status
    async with db_manager.get_session_maker()() as session:
        stmt = select(NotificationDelivery).where(
            NotificationDelivery.notification_id == notification_id
        )
        res = await session.execute(stmt)
        deliveries = res.scalars().all()
        assert len(deliveries) == 2
        channels_found = [d.channel for d in deliveries]
        assert "IN_APP" in channels_found
        assert "EMAIL" in channels_found
        for d in deliveries:
            assert d.status == "PENDING"

    # Execute a poller and dispatcher tick to deliver IN_APP (and update delivery_state to DELIVERED)
    # We will mock send_email_notification to prevent external network traffic/SMTP calls
    with patch(
        "apps.notifications.delivery.send_email_notification", new_callable=AsyncMock
    ):
        await poll_and_dispatch()
        # Allow async task processing robustly
        for _ in range(160):
            await asyncio.sleep(0.05)
            headers_recipient = get_auth_headers(
                user_id="pi_john",
                roles="investigator",
            )
            response_detail = client.get(
                f"/api/v1/notifications/{notification_id}", headers=headers_recipient
            )
            if (
                response_detail.status_code == 200
                and response_detail.json()["delivery_state"] == "DELIVERED"
            ):
                break

    # Re-fetch notification detail as the target recipient to satisfy visibility checks
    headers_recipient = get_auth_headers(
        user_id="pi_john",
        roles="investigator",
    )
    response_detail = client.get(
        f"/api/v1/notifications/{notification_id}", headers=headers_recipient
    )
    assert response_detail.status_code == 200
    detail_data = response_detail.json()
    assert detail_data["delivery_state"] == "DELIVERED"

    # Verify audit log was written
    async with db_manager.get_session_maker()() as session:
        stmt = select(NotificationAuditLog).where(
            NotificationAuditLog.action == "NOTIFICATION_CREATE"
        )
        res = await session.execute(stmt)
        audit = res.scalars().first()
        assert audit is not None
        assert audit.user_id == "user_creator"
        assert "pi_john" in audit.details


@pytest.mark.asyncio
async def test_notification_creation_with_new_columns():
    """
    Verify that creating a notification with new optional columns (subject, subject_id, visit_id, query_id, site_id)
    persists all fields correctly and returns them in the response.
    """
    client = TestClient(app)
    headers = get_auth_headers(
        user_id="user_creator",
        roles="Grants Manager",
        change_reason="Notify with explicit fields",
    )

    payload = {
        "recipient_user_id": "pi_john",
        "recipient_role": "investigator",
        "category": "ACTION_ITEMS",
        "priority": "HIGH",
        "channels": "IN_APP",
        "message_content": "Please sign off on the pending visit log.",
        "related_entity_id": "visit_abc_123",
        "related_entity_type": "VISIT",
        "subject": "New Trial Notification",
        "subject_id": "sub_999",
        "visit_id": "visit_001",
        "query_id": "query_555",
        "site_id": "site_777",
    }

    # Create the notification
    response = client.post("/api/v1/notifications", json=payload, headers=headers)
    assert response.status_code == 201
    data = response.json()
    assert data["id"] is not None
    assert data["recipient_user_id"] == "pi_john"
    assert data["recipient_role"] == "investigator"
    assert data["subject"] == "New Trial Notification"
    assert data["subject_id"] == "sub_999"
    assert data["visit_id"] == "visit_001"
    assert data["query_id"] == "query_555"
    assert data["site_id"] == "site_777"

    notification_id = data["id"]

    # Verify that the database contains the correct values
    async with db_manager.get_session_maker()() as session:
        stmt = select(Notification).where(Notification.id == notification_id)
        res = await session.execute(stmt)
        notif = res.scalars().first()
        assert notif is not None
        assert notif.subject == "New Trial Notification"
        assert notif.subject_id == "sub_999"
        assert notif.visit_id == "visit_001"
        assert notif.query_id == "query_555"
        assert notif.site_id == "site_777"


@pytest.mark.asyncio
async def test_notification_list_visibility_and_filtering():
    """
    Verify target-based visibility: users can only fetch notifications for their user ID or roles, or global.
    """
    # Pre-seed notifications (explicitly marked as DELIVERED to be visible to the dashboard)
    async with db_manager.get_session_maker()() as session:
        notif_user = Notification(
            recipient_user_id="alice",
            recipient_role="admin",
            category=NotificationCategory.ALERTS,
            priority=NotificationPriority.CRITICAL,
            message_content="For Alice specifically",
            delivery_state="DELIVERED",
            created_by="system",
            reason_for_change="Initial seed",
        )
        notif_role = Notification(
            recipient_user_id="bob",
            recipient_role="investigator",
            category=NotificationCategory.SYSTEM,
            priority=NotificationPriority.MEDIUM,
            message_content="For Investigators",
            delivery_state="DELIVERED",
            created_by="system",
            reason_for_change="Initial seed",
        )
        notif_other = Notification(
            recipient_user_id="charlie",
            recipient_role="monitor",
            category=NotificationCategory.ACTION_ITEMS,
            priority=NotificationPriority.LOW,
            message_content="For Charlie/Monitor",
            delivery_state="DELIVERED",
            created_by="system",
            reason_for_change="Initial seed",
        )
        notif_global = Notification(
            recipient_user_id=None,
            recipient_role=None,
            category=NotificationCategory.SYSTEM,
            priority=NotificationPriority.LOW,
            message_content="Global broadcast",
            delivery_state="DELIVERED",
            created_by="system",
            reason_for_change="Initial seed",
        )
        session.add_all([notif_user, notif_role, notif_other, notif_global])
        await session.commit()

    client = TestClient(app)

    # 1. Fetch as Alice (roles: admin). Should see "For Alice specifically" and "Global broadcast".
    headers_alice = get_auth_headers(user_id="alice", roles="admin")
    response = client.get("/api/v1/notifications", headers=headers_alice)
    assert response.status_code == 200
    data = response.json()
    assert len(data) == 2
    contents = [n["message_content"] for n in data]
    assert "For Alice specifically" in contents
    assert "Global broadcast" in contents

    # 2. Fetch as Dave (roles: investigator).
    headers_dave = get_auth_headers(user_id="dave", roles="investigator")
    response = client.get("/api/v1/notifications", headers=headers_dave)
    assert response.status_code == 200
    data = response.json()
    assert len(data) == 2
    contents = [n["message_content"] for n in data]
    assert "For Investigators" in contents
    assert "Global broadcast" in contents

    # 3. List with priority filter
    response = client.get(
        "/api/v1/notifications?priority=CRITICAL", headers=headers_alice
    )
    assert response.status_code == 200
    data = response.json()
    assert len(data) == 1
    assert data[0]["message_content"] == "For Alice specifically"


@pytest.mark.asyncio
async def test_notification_detail_visibility():
    """
    Verify detail endpoint visibility restrictions.
    """
    async with db_manager.get_session_maker()() as session:
        notif = Notification(
            recipient_user_id="alice",
            recipient_role="admin",
            category=NotificationCategory.ALERTS,
            priority=NotificationPriority.CRITICAL,
            message_content="Super secret alert for Alice",
            delivery_state="DELIVERED",
            created_by="system",
            reason_for_change="Initial seed",
        )
        session.add(notif)
        await session.commit()
        notif_id = notif.id

    client = TestClient(app)

    # Alice can view her notification
    headers_alice = get_auth_headers(user_id="alice", roles="admin")
    res1 = client.get(f"/api/v1/notifications/{notif_id}", headers=headers_alice)
    assert res1.status_code == 200
    assert res1.json()["message_content"] == "Super secret alert for Alice"

    # Bob (roles: investigator) cannot view Alice's notification -> 403 Forbidden
    headers_bob = get_auth_headers(user_id="bob", roles="investigator")
    res2 = client.get(f"/api/v1/notifications/{notif_id}", headers=headers_bob)
    assert res2.status_code == 403
    assert "Recipient target boundary violation" in res2.json()["detail"]


@pytest.mark.asyncio
async def test_lifecycle_transitions_and_justifications():
    """
    Verify state transitions OPEN -> ACKNOWLEDGED -> RESOLVED, requiring change reasons.
    """
    async with db_manager.get_session_maker()() as session:
        notif = Notification(
            recipient_user_id="alice",
            recipient_role="admin",
            category=NotificationCategory.ALERTS,
            priority=NotificationPriority.CRITICAL,
            message_content="Action required",
            delivery_state="DELIVERED",
            created_by="system",
            reason_for_change="Initial seed",
        )
        session.add(notif)
        await session.commit()
        notif_id = notif.id

    client = TestClient(app)

    # 1. Transition: Acknowledge. Must supply change reason.
    headers_no_reason = get_auth_headers(user_id="alice", roles="admin")
    res = client.post(
        f"/api/v1/notifications/{notif_id}/acknowledge", headers=headers_no_reason
    )
    assert res.status_code == 403

    headers_ack = get_auth_headers(
        user_id="alice", roles="admin", change_reason="I have read this notification"
    )
    res_ack = client.post(
        f"/api/v1/notifications/{notif_id}/acknowledge", headers=headers_ack
    )
    assert res_ack.status_code == 200
    assert res_ack.json()["status"] == "ACKNOWLEDGED"
    assert res_ack.json()["version_index"] == 2
    assert res_ack.json()["reason_for_change"] == "I have read this notification"

    # 2. Cannot acknowledge again
    res_ack_double = client.post(
        f"/api/v1/notifications/{notif_id}/acknowledge", headers=headers_ack
    )
    assert res_ack_double.status_code == 422
    assert "Invalid transition" in res_ack_double.json()["detail"]

    # 3. Transition: Resolve. Must supply change reason.
    headers_res = get_auth_headers(
        user_id="alice", roles="admin", change_reason="Issue resolved in clinical DB"
    )
    res_resolve = client.post(
        f"/api/v1/notifications/{notif_id}/resolve", headers=headers_res
    )
    assert res_resolve.status_code == 200
    assert res_resolve.json()["status"] == "RESOLVED"
    assert res_resolve.json()["version_index"] == 3

    # 4. Cannot resolve once already resolved
    res_resolve_double = client.post(
        f"/api/v1/notifications/{notif_id}/resolve", headers=headers_res
    )
    assert res_resolve_double.status_code == 422

    # 5. Non-existent notification checks
    res_not_found = client.get(
        "/api/v1/notifications/non-existent-id", headers=headers_ack
    )
    assert res_not_found.status_code == 404

    res_ack_not_found = client.post(
        "/api/v1/notifications/non-existent-id/acknowledge", headers=headers_ack
    )
    assert res_ack_not_found.status_code == 404

    res_res_not_found = client.post(
        "/api/v1/notifications/non-existent-id/resolve", headers=headers_ack
    )
    assert res_res_not_found.status_code == 404


@pytest.mark.asyncio
async def test_direct_transition_open_to_resolved():
    """
    Verify that an OPEN notification can directly transition to RESOLVED.
    """
    async with db_manager.get_session_maker()() as session:
        notif = Notification(
            recipient_user_id="alice",
            recipient_role="admin",
            category=NotificationCategory.ALERTS,
            priority=NotificationPriority.CRITICAL,
            message_content="Direct resolve test",
            delivery_state="DELIVERED",
            created_by="system",
            reason_for_change="Initial seed",
        )
        session.add(notif)
        await session.commit()
        notif_id = notif.id

    client = TestClient(app)

    headers_res = get_auth_headers(
        user_id="alice",
        roles="admin",
        change_reason="Direct resolution from open status",
    )
    res_resolve = client.post(
        f"/api/v1/notifications/{notif_id}/resolve", headers=headers_res
    )
    assert res_resolve.status_code == 200
    assert res_resolve.json()["status"] == "RESOLVED"
    assert res_resolve.json()["version_index"] == 2


@pytest.mark.asyncio
async def test_email_delivery_channel_success():
    """
    Verify that an EMAIL channel delivery attempt initializes SMTP and succeeds.
    """
    async with db_manager.get_session_maker()() as session:
        notif = Notification(
            recipient_user_id="doc_brown",
            recipient_role="investigator",
            category=NotificationCategory.ALERTS,
            priority=NotificationPriority.HIGH,
            message_content="Time travel completed.",
            delivery_state="PENDING",
            created_by="system",
            reason_for_change="Initial",
        )
        session.add(notif)
        await session.flush()

        delivery = NotificationDelivery(
            notification_id=notif.id,
            channel="EMAIL",
            status="PENDING",
        )
        session.add(delivery)
        await session.commit()
        delivery_id = delivery.id

    mock_smtp_client = AsyncMock()
    with patch("aiosmtplib.SMTP", return_value=mock_smtp_client):
        await poll_and_dispatch()
        # Wait up to 8 seconds for the background task to update the status to SUCCESS
        for _ in range(160):
            await asyncio.sleep(0.05)
            async with db_manager.get_session_maker()() as session:
                stmt = select(NotificationDelivery).where(
                    NotificationDelivery.id == delivery_id
                )
                res = await session.execute(stmt)
                updated_delivery = res.scalars().first()
                if updated_delivery.status == "SUCCESS":
                    break

    # Assert SMTP interaction
    assert mock_smtp_client.connect.called
    assert mock_smtp_client.send_message.called
    assert mock_smtp_client.quit.called

    # Assert persistence
    async with db_manager.get_session_maker()() as session:
        stmt = select(NotificationDelivery).where(
            NotificationDelivery.id == delivery_id
        )
        res = await session.execute(stmt)
        updated_delivery = res.scalars().first()
        assert updated_delivery.status == "SUCCESS"
        assert updated_delivery.attempts == 1
        assert updated_delivery.completed_at is not None


@pytest.mark.asyncio
async def test_webhook_delivery_channel_success():
    """
    Verify that a WEBHOOK channel delivery calculates correct deterministic HMAC-SHA256 and succeeds.
    """
    async with db_manager.get_session_maker()() as session:
        notif = Notification(
            recipient_user_id="webhook_tester",
            category=NotificationCategory.SYSTEM,
            priority=NotificationPriority.MEDIUM,
            message_content="Event payload",
            delivery_state="PENDING",
            created_by="system",
            reason_for_change="Initial",
        )
        session.add(notif)
        await session.flush()

        delivery = NotificationDelivery(
            notification_id=notif.id,
            channel="WEBHOOK",
            status="PENDING",
        )
        session.add(delivery)
        await session.commit()
        delivery_id = delivery.id

    from unittest.mock import MagicMock

    mock_response = AsyncMock()
    mock_response.status_code = 200
    mock_response.raise_for_status = MagicMock()
    mock_client = AsyncMock()
    mock_client.post.return_value = mock_response

    mock_context = AsyncMock()
    mock_context.__aenter__.return_value = mock_client

    with patch("httpx.AsyncClient", return_value=mock_context):
        await poll_and_dispatch()
        # Wait up to 8 seconds for the background task to update the status to SUCCESS
        for _ in range(160):
            await asyncio.sleep(0.05)
            async with db_manager.get_session_maker()() as session:
                stmt = select(NotificationDelivery).where(
                    NotificationDelivery.id == delivery_id
                )
                res = await session.execute(stmt)
                updated_delivery = res.scalars().first()
                if updated_delivery.status == "SUCCESS":
                    break

    # Assert httpx interaction
    assert mock_client.post.called
    called_args, called_kwargs = mock_client.post.call_args
    assert "Content-Type" in called_kwargs["headers"]
    assert "X-Cadence-Signature" in called_kwargs["headers"]

    # Assert persistence
    async with db_manager.get_session_maker()() as session:
        stmt = select(NotificationDelivery).where(
            NotificationDelivery.id == delivery_id
        )
        res = await session.execute(stmt)
        updated_delivery = res.scalars().first()
        assert updated_delivery.status == "SUCCESS"
        assert updated_delivery.attempts == 1


@pytest.mark.asyncio
async def test_webhook_delivery_channel_failure_and_retry_backoff():
    """
    Verify that when webhook delivery fails, it is recorded, schedules backoff,
    and increments attempts until capped, disabling retry eligibility.
    """
    async with db_manager.get_session_maker()() as session:
        notif = Notification(
            recipient_user_id="webhook_failer",
            category=NotificationCategory.SYSTEM,
            priority=NotificationPriority.MEDIUM,
            message_content="Will fail.",
            delivery_state="PENDING",
            created_by="system",
            reason_for_change="Initial",
        )
        session.add(notif)
        await session.flush()

        delivery = NotificationDelivery(
            notification_id=notif.id,
            channel="WEBHOOK",
            status="PENDING",
        )
        session.add(delivery)
        await session.commit()
        delivery_id = delivery.id

    # 1st Attempt: Fails with a network/timeout exception
    with patch("httpx.AsyncClient", side_effect=Exception("Connection timed out")):
        await poll_and_dispatch()
        for _ in range(160):
            await asyncio.sleep(0.05)
            async with db_manager.get_session_maker()() as session:
                res = await session.execute(
                    select(NotificationDelivery).where(
                        NotificationDelivery.id == delivery_id
                    )
                )
                delivery_1 = res.scalars().first()
                if delivery_1.status == "FAILED":
                    break

    async with db_manager.get_session_maker()() as session:
        stmt = select(NotificationDelivery).where(
            NotificationDelivery.id == delivery_id
        )
        res = await session.execute(stmt)
        delivery_1 = res.scalars().first()
        assert delivery_1.status == "FAILED"
        assert delivery_1.attempts == 1
        assert "Connection timed out" in delivery_1.last_error
        assert delivery_1.retry_eligible is True
        assert delivery_1.next_retry_at is not None

        # Manually alter next_retry_at to past to simulate time travel
        delivery_1.next_retry_at = datetime.utcnow() - timedelta(seconds=1)
        await session.commit()

    # 2nd Attempt: Fails again
    with patch("httpx.AsyncClient", side_effect=Exception("Temporary server error")):
        await poll_and_dispatch()
        for _ in range(160):
            await asyncio.sleep(0.05)
            async with db_manager.get_session_maker()() as session:
                res = await session.execute(
                    select(NotificationDelivery).where(
                        NotificationDelivery.id == delivery_id
                    )
                )
                delivery_2 = res.scalars().first()
                if delivery_2.status == "FAILED" and delivery_2.attempts == 2:
                    break

    async with db_manager.get_session_maker()() as session:
        stmt = select(NotificationDelivery).where(
            NotificationDelivery.id == delivery_id
        )
        res = await session.execute(stmt)
        delivery_2 = res.scalars().first()
        assert delivery_2.status == "FAILED"
        assert delivery_2.attempts == 2
        assert "Temporary server error" in delivery_2.last_error
        assert delivery_2.retry_eligible is True

        # Manually max out attempts (e.g. set attempts to 4, limit is 5)
        delivery_2.attempts = 4
        delivery_2.next_retry_at = datetime.utcnow() - timedelta(seconds=1)
        await session.commit()

    # 3rd Attempt: Reaches maximum allowed attempts
    with patch("httpx.AsyncClient", side_effect=Exception("Terminal failure")):
        await poll_and_dispatch()
        for _ in range(160):
            await asyncio.sleep(0.05)
            async with db_manager.get_session_maker()() as session:
                res = await session.execute(
                    select(NotificationDelivery).where(
                        NotificationDelivery.id == delivery_id
                    )
                )
                delivery_3 = res.scalars().first()
                if delivery_3.status == "FAILED" and delivery_3.attempts == 5:
                    break

    async with db_manager.get_session_maker()() as session:
        stmt = select(NotificationDelivery).where(
            NotificationDelivery.id == delivery_id
        )
        res = await session.execute(stmt)
        delivery_3 = res.scalars().first()
        assert delivery_3.status == "FAILED"
        assert delivery_3.attempts == 5
        assert delivery_3.retry_eligible is False


@pytest.mark.asyncio
async def test_email_delivery_channel_failure_and_exhaustion():
    """
    # @req:PRD-SYS-003
    Verify that when EMAIL delivery fails, it is recorded, schedules backoff,
    and increments attempts until capped, writing NOTIFICATION_DELIVERY_EXHAUSTED to NotificationAuditLog.
    """
    async with db_manager.get_session_maker()() as session:
        notif = Notification(
            recipient_user_id="email_failer",
            category=NotificationCategory.SYSTEM,
            priority=NotificationPriority.MEDIUM,
            message_content="SMTP will fail.",
            delivery_state="PENDING",
            created_by="system",
            reason_for_change="Initial",
        )
        session.add(notif)
        await session.flush()

        delivery = NotificationDelivery(
            notification_id=notif.id,
            channel="EMAIL",
            status="PENDING",
        )
        session.add(delivery)
        await session.commit()
        delivery_id = delivery.id

    # Mock SMTP to fail
    mock_smtp_client = AsyncMock()
    mock_smtp_client.connect.side_effect = Exception("SMTP connect error")
    with patch("aiosmtplib.SMTP", return_value=mock_smtp_client):
        # 1st attempt
        await poll_and_dispatch()

        # Robustly wait for first attempt to complete
        for _ in range(160):
            await asyncio.sleep(0.05)
            async with db_manager.get_session_maker()() as session:
                res = await session.execute(
                    select(NotificationDelivery).where(
                        NotificationDelivery.id == delivery_id
                    )
                )
                d = res.scalars().first()
                if d.status == "FAILED":
                    break

        # Confirm failure
        async with db_manager.get_session_maker()() as session:
            res = await session.execute(
                select(NotificationDelivery).where(
                    NotificationDelivery.id == delivery_id
                )
            )
            d = res.scalars().first()
            assert d.status == "FAILED"
            assert d.attempts == 1
            assert d.retry_eligible is True

            # Advance next_retry_at and max attempts close to cap (4)
            d.attempts = 4
            d.next_retry_at = datetime.utcnow() - timedelta(seconds=1)
            await session.commit()

        # Final attempt -> Exhaustion
        await poll_and_dispatch()

        # Robustly wait for final attempt to complete
        for _ in range(160):
            await asyncio.sleep(0.05)
            async with db_manager.get_session_maker()() as session:
                res = await session.execute(
                    select(NotificationDelivery).where(
                        NotificationDelivery.id == delivery_id
                    )
                )
                d = res.scalars().first()
                if d.attempts == 5:
                    break

    # Check exhaustion audit log
    async with db_manager.get_session_maker()() as session:
        res = await session.execute(
            select(NotificationDelivery).where(NotificationDelivery.id == delivery_id)
        )
        d = res.scalars().first()
        assert d.attempts == 5
        assert d.retry_eligible is False

        # Assert NOTIFICATION_DELIVERY_EXHAUSTED audit log was written
        res_audit = await session.execute(
            select(NotificationAuditLog).where(
                NotificationAuditLog.action == "NOTIFICATION_DELIVERY_EXHAUSTED"
            )
        )
        audit_entry = res_audit.scalars().first()
        assert audit_entry is not None
        assert "SMTP connect error" in audit_entry.details
        assert "EMAIL" in audit_entry.details


@pytest.mark.asyncio
async def test_multi_channel_edge_case_in_app_succeeds_email_exhausts():
    """
    # @req:PRD-SYS-003
    Add a multi-channel edge-case test verifying the parent Notification state
    and that the failed channel's exhaustion remains observable via the audit entry.
    """
    async with db_manager.get_session_maker()() as session:
        notif = Notification(
            recipient_user_id="multi_tester",
            category=NotificationCategory.SYSTEM,
            priority=NotificationPriority.MEDIUM,
            message_content="Multi-channel test.",
            delivery_state="PENDING",
            created_by="system",
            reason_for_change="Initial",
        )
        session.add(notif)
        await session.flush()

        delivery_in_app = NotificationDelivery(
            notification_id=notif.id,
            channel="IN_APP",
            status="PENDING",
        )
        delivery_email = NotificationDelivery(
            notification_id=notif.id,
            channel="EMAIL",
            status="PENDING",
        )
        session.add_all([delivery_in_app, delivery_email])
        await session.commit()
        delivery_in_app_id = delivery_in_app.id
        delivery_email_id = delivery_email.id
        notif_id = notif.id

    # Mock SMTP to fail, run dispatcher
    mock_smtp_client = AsyncMock()
    mock_smtp_client.connect.side_effect = Exception("SMTP connection failure")
    with patch("aiosmtplib.SMTP", return_value=mock_smtp_client):
        # 1st dispatch tick -> IN_APP succeeds immediately, EMAIL fails once
        await poll_and_dispatch()

        # Robustly wait for the first dispatch to complete
        for _ in range(160):
            await asyncio.sleep(0.05)
            async with db_manager.get_session_maker()() as session:
                res_email = await session.execute(
                    select(NotificationDelivery).where(
                        NotificationDelivery.id == delivery_email_id
                    )
                )
                d_email = res_email.scalars().first()
                if d_email.attempts == 1:
                    break

        # Advance EMAIL to 4 attempts and past next_retry_at
        async with db_manager.get_session_maker()() as session:
            res_email = await session.execute(
                select(NotificationDelivery).where(
                    NotificationDelivery.id == delivery_email_id
                )
            )
            d_email = res_email.scalars().first()
            assert d_email.attempts == 1
            d_email.attempts = 4
            d_email.next_retry_at = datetime.utcnow() - timedelta(seconds=1)
            await session.commit()

        # 2nd dispatch tick -> EMAIL exhausts (reaches 5 attempts)
        await poll_and_dispatch()

        # Robustly wait for the second dispatch to complete
        for _ in range(160):
            await asyncio.sleep(0.05)
            async with db_manager.get_session_maker()() as session:
                res_email = await session.execute(
                    select(NotificationDelivery).where(
                        NotificationDelivery.id == delivery_email_id
                    )
                )
                d_email = res_email.scalars().first()
                if d_email.attempts == 5:
                    break

    # Verify both states
    async with db_manager.get_session_maker()() as session:
        # Check IN_APP is successful
        res_in_app = await session.execute(
            select(NotificationDelivery).where(
                NotificationDelivery.id == delivery_in_app_id
            )
        )
        d_in_app = res_in_app.scalars().first()
        assert d_in_app.status == "SUCCESS"

        # Check EMAIL is exhausted
        res_email = await session.execute(
            select(NotificationDelivery).where(
                NotificationDelivery.id == delivery_email_id
            )
        )
        d_email = res_email.scalars().first()
        assert d_email.status == "FAILED"
        assert d_email.retry_eligible is False

        # Assert parent notification is updated
        res_notif = await session.execute(
            select(Notification).where(Notification.id == notif_id)
        )
        n = res_notif.scalars().first()
        assert (
            n.delivery_state == "DELIVERED"
        )  # Because IN_APP completed, marking delivery_state = "DELIVERED"
        assert n.retries == 5

        # Check NOTIFICATION_DELIVERY_EXHAUSTED audit log is present
        res_audit = await session.execute(
            select(NotificationAuditLog).where(
                NotificationAuditLog.action == "NOTIFICATION_DELIVERY_EXHAUSTED"
            )
        )
        audit_records = res_audit.scalars().all()
        assert len(audit_records) > 0
        email_exhaust_audit = [a for a in audit_records if "EMAIL" in a.details]
        assert len(email_exhaust_audit) == 1


def test_notifications_negative_security_paths():
    """
    # @req:PRD-SYS-004
    Verify negative-path security checks at the microservice boundary.
    Covers absent/malformed/expired/unsupported version/spoofed headers.
    """
    client = TestClient(app)

    # Scenario 1: Absent signature/identity headers (expect 401 on GET, 403 on POST)
    resp = client.get("/api/v1/notifications")
    assert resp.status_code == 401
    assert "Missing gateway authentication headers" in resp.json()["detail"]

    resp_post = client.post("/api/v1/notifications", json={})
    assert resp_post.status_code == 403
    assert "Missing gateway authentication headers" in resp_post.json()["detail"]

    # Scenario 2: Malformed/incorrect X-Gateway-Signature
    timestamp = str(time.time())
    headers_bad_sig = {
        "X-User-Id": "test_user",
        "X-User-Roles": "admin",
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": "invalid_signature_hex_code",
        "X-Signature-Version": "2",
    }
    resp = client.get("/api/v1/notifications", headers=headers_bad_sig)
    assert resp.status_code == 401
    assert "Invalid gateway signature" in resp.json()["detail"]

    # Scenario 3: Expired/stale X-Gateway-Timestamp (>300 seconds)
    stale_timestamp = str(time.time() - 301)
    sig_stale = generate_signature("test_user", "admin", stale_timestamp, version="2")
    headers_stale = {
        "X-User-Id": "test_user",
        "X-User-Roles": "admin",
        "X-Gateway-Timestamp": stale_timestamp,
        "X-Gateway-Signature": sig_stale,
        "X-Signature-Version": "2",
    }
    resp = client.get("/api/v1/notifications", headers=headers_stale)
    assert resp.status_code == 401
    assert "Gateway signature expired" in resp.json()["detail"]

    # Scenario 4: Missing/unsupported X-Signature-Version
    headers_no_ver = get_auth_headers(user_id="test_user", roles="admin")
    headers_no_ver.pop("X-Signature-Version")
    resp = client.get("/api/v1/notifications", headers=headers_no_ver)
    assert resp.status_code == 401
    assert "Missing or obsolete signature format" in resp.json()["detail"]

    headers_bad_ver = get_auth_headers(user_id="test_user", roles="admin")
    headers_bad_ver["X-Signature-Version"] = "3"
    resp = client.get("/api/v1/notifications", headers=headers_bad_ver)
    assert resp.status_code == 401
    assert "Missing or obsolete signature format" in resp.json()["detail"]

    # Scenario 5: Spoofed identity where signed reason/tenant mismatches headers
    # Generate signature with tenant_id="tenant_A" but pass "tenant_B" in headers
    sig_tenant = generate_signature(
        user_id="test_user",
        roles="admin",
        timestamp=timestamp,
        version="2",
        change_reason="Reason",
        tenant_id="tenant_A",
    )
    headers_spoof = {
        "X-User-Id": "test_user",
        "X-User-Roles": "admin",
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig_tenant,
        "X-Signature-Version": "2",
        "X-Change-Reason": "Reason",
        "X-Tenant-Id": "tenant_B",  # Mismatch / Spoof!
    }
    resp = client.get("/api/v1/notifications", headers=headers_spoof)
    assert resp.status_code == 401
    assert "Invalid gateway signature" in resp.json()["detail"]
