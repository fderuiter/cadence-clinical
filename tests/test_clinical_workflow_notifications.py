import asyncio
import os
import time
from unittest.mock import AsyncMock, MagicMock, patch

import httpx
import pytest
import pytest_asyncio
from jose import jwt

from apps.execution.database.core import db_manager
from apps.execution.database.models import Base, ClinicalSubject
from apps.execution.main import app
from apps.execution.notifications_client import publish_notification
from apps.execution.trial_lock import NotificationRouter
from packages.security.signing import generate_gateway_signature

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")


def get_auth_headers(
    user_id="test_inv",
    roles="site investigator",
    change_reason="Emergency unblinding requested",
    unblinded_access=True,
) -> dict:
    """Generate Gateway signature-compliant authentication headers."""
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
    user_id="test_inv", roles="site investigator", action="unblind"
) -> str:
    """Generate a 21 CFR Part 11 compliant re-authentication token."""
    payload = {
        "sub": user_id,
        "username": user_id,
        "action": action,
        "roles": [roles],
        "iat": time.time(),
        "exp": time.time() + 60.0,
    }
    return jwt.encode(payload, "internal-gateway-secret-12345", algorithm="HS256")


@pytest_asyncio.fixture(autouse=True)
async def setup_test_db():
    """Setup in-memory SQLite database before each test and drop tables after."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:")
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_publish_notification_success() -> None:
    """Verifies that publish_notification correctly formats headers, posts to the endpoint, and returns True on success."""
    notif_payload = {
        "recipient_user_id": "test-user",
        "category": "ALERTS",
        "priority": "HIGH",
        "channels": "IN_APP",
        "message_content": "Test Message",
    }

    mock_resp = MagicMock()
    mock_resp.status_code = 201

    with patch("httpx.AsyncClient.post", new_callable=AsyncMock) as mock_post:
        mock_post.return_value = mock_resp
        success = await publish_notification(notif_payload)

        assert success is True
        mock_post.assert_called_once()
        args, kwargs = mock_post.call_args
        assert "headers" in kwargs
        headers = kwargs["headers"]
        assert headers["X-User-Id"] == "execution-service"
        assert headers["X-Signature-Version"] == "2"
        assert kwargs["json"] == notif_payload


@pytest.mark.asyncio
async def test_publish_notification_failure_swallowed() -> None:
    """Verifies that transport/HTTP errors are swallowed and return False instead of raising."""
    notif_payload = {"message_content": "Failure test"}

    # Mock raise error
    with patch(
        "httpx.AsyncClient.post",
        side_effect=httpx.RequestError("Connection refused"),
    ):
        success = await publish_notification(notif_payload)
        assert success is False

    # Mock non-201 response status
    mock_resp = MagicMock()
    mock_resp.status_code = 500
    mock_resp.text = "Internal Server Error"
    with patch("httpx.AsyncClient.post", new_callable=AsyncMock) as mock_post:
        mock_post.return_value = mock_resp
        success = await publish_notification(notif_payload)
        assert success is False


@pytest.mark.asyncio
async def test_router_send_email_mapping() -> None:
    """Tests email mapping for Trial-lock, Query-aging, and general emails."""
    router = NotificationRouter()

    mock_resp = MagicMock()
    mock_resp.status_code = 201

    with patch("httpx.AsyncClient.post", new_callable=AsyncMock) as mock_post:
        mock_post.return_value = mock_resp

        # 1. Trial-lock email
        router.send_email(
            ["security@cadence.clinical"],
            "URGENT: Trial locked. Reason: Simulated Data Tampering",
        )
        assert mock_post.call_count == 1
        args, kwargs = mock_post.call_args
        payload = kwargs["json"]
        assert payload["category"] == "ALERTS"
        assert payload["priority"] == "CRITICAL"
        assert payload["channels"] == "EMAIL"
        assert payload["related_entity_type"] == "trial-lock"
        assert payload["recipient_user_id"] == "security@cadence.clinical"

        mock_post.reset_mock()

        # 2. Query-aging digest email
        digest_msg = "Daily Clinical Query Aging Digest\nStudy: study_123\nSite: site_abc\nThe following queries..."
        router.send_email(["cra@cadence.clinical"], digest_msg)
        assert mock_post.call_count == 1
        args, kwargs = mock_post.call_args
        payload = kwargs["json"]
        assert payload["category"] == "ACTION_ITEMS"
        assert payload["priority"] == "HIGH"
        assert payload["channels"] == "EMAIL"
        assert payload["related_entity_type"] == "study-site"
        assert payload["related_entity_id"] == "study_123:site_abc"


@pytest.mark.asyncio
async def test_router_send_sms_mapping() -> None:
    """Tests sms maps to IN_APP channel."""
    router = NotificationRouter()

    mock_resp = MagicMock()
    mock_resp.status_code = 201

    with patch("httpx.AsyncClient.post", new_callable=AsyncMock) as mock_post:
        mock_post.return_value = mock_resp

        router.send_sms(["+1234567890"], "Trial locked")
        assert mock_post.call_count == 1
        args, kwargs = mock_post.call_args
        payload = kwargs["json"]
        assert payload["channels"] == "IN_APP"
        assert payload["category"] == "ALERTS"
        assert payload["priority"] == "CRITICAL"


@pytest.mark.asyncio
async def test_router_send_webhook_mapping() -> None:
    """Tests webhook mapping rules."""
    router = NotificationRouter()

    mock_resp = MagicMock()
    mock_resp.status_code = 201

    with patch("httpx.AsyncClient.post", new_callable=AsyncMock) as mock_post:
        mock_post.return_value = mock_resp

        router.send_webhook(
            "https://hooks.cadence.clinical/alerts",
            {"text": "General webhook data"},
        )
        assert mock_post.call_count == 1
        args, kwargs = mock_post.call_args
        payload = kwargs["json"]
        assert payload["channels"] == "WEBHOOK"
        assert payload["category"] == "SYSTEM"
        assert payload["priority"] == "MEDIUM"
        assert payload["related_entity_type"] == "webhook-url"
        assert payload["related_entity_id"] == "https://hooks.cadence.clinical/alerts"


@pytest.mark.asyncio
async def test_router_send_dashboard_notification_sdv_drop() -> None:
    """Tests dashboard notifications for SDV drop."""
    router = NotificationRouter()

    mock_resp = MagicMock()
    mock_resp.status_code = 201

    with patch("httpx.AsyncClient.post", new_callable=AsyncMock) as mock_post:
        mock_post.return_value = mock_resp

        sdv_payload = {
            "message": "Previously verified field modified on Subject SUBJ-001",
            "observation_id": "obs_12345",
        }
        router.send_dashboard_notification(["verifier_cra"], sdv_payload)
        assert mock_post.call_count == 1
        args, kwargs = mock_post.call_args
        payload = kwargs["json"]
        assert payload["channels"] == "IN_APP"
        assert payload["category"] == "ALERTS"
        assert payload["priority"] == "HIGH"
        assert payload["related_entity_type"] == "observation"
        assert payload["related_entity_id"] == "obs_12345"
        assert (
            payload["message_content"]
            == "Previously verified field modified on Subject SUBJ-001"
        )


@pytest.mark.asyncio
async def test_unblind_emergency_unblinding_alert_integration() -> None:
    """Verifies unblind_subject publishes post-commit alert targeting Sponsor Safety Lead, Lead CRA, and IDMC roles."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Create a randomized subject
        async with db_manager.get_session_maker()() as session:
            subj = ClinicalSubject(
                subject_id="SUBJ-005",
                study_id="STUDY-1",
                kit_reference="KIT-555",
            )
            session.add(subj)
            await session.flush()
            subj.status = "ENROLLED"
            await session.flush()
            subj.status = "RANDOMIZED"
            await session.commit()

        headers = get_auth_headers(roles="site investigator", unblinded_access=True)
        headers["X-Sig-Token"] = get_sig_token()

        with patch(
            "apps.execution.trial_lock.publish_notification",
            new_callable=AsyncMock,
        ) as mock_pub:
            mock_pub.return_value = True

            res = await client.post(
                "/api/v1/execution/subjects/SUBJ-005/unblind", headers=headers
            )
            assert res.status_code == 200

            # Allow background tasks to run
            await asyncio.sleep(0.1)

            # Three roles should be alerted: "Sponsor Safety Lead", "Lead CRA", "IDMC"
            assert mock_pub.call_count == 3

            roles_notified = []
            for call in mock_pub.call_args_list:
                payload = call[0][0]
                assert payload["category"] == "ALERTS"
                assert payload["priority"] == "CRITICAL"
                assert payload["related_entity_type"] == "subject"
                assert payload["related_entity_id"] == "SUBJ-005"
                assert "Sponsor lock" not in payload["message_content"]
                # Must exclude sensitive values:
                assert "Active Treatment" not in payload["message_content"]
                assert "KIT-555" not in payload["message_content"]

                roles_notified.append(payload.get("recipient_role"))

            assert "Sponsor Safety Lead" in roles_notified
            assert "Lead CRA" in roles_notified
            assert "IDMC" in roles_notified
