import asyncio
import os
from datetime import datetime, timedelta, timezone
from unittest.mock import MagicMock

import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.etmf.database import db_manager
from apps.etmf.expiration_scanner import (
    determine_warning_window,
    execute_expiration_scan_cycle,
    start_background_etmf_expiration_scanner,
    stop_background_etmf_expiration_scanner,
)
from apps.etmf.models import Base, DocumentExpirationAlertState, TMFDocument


@pytest_asyncio.fixture(autouse=True)
async def setup_test_db():
    """Setup in-memory SQLite database before each test and clear down after."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)

    yield

    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_determine_warning_window():
    """Test the threshold helper determine_warning_window on standard thresholds."""
    now = datetime.now(timezone.utc)

    # 1. Past expiration
    assert determine_warning_window(now - timedelta(days=1), now) == "EXPIRED"
    assert determine_warning_window(now, now) == "EXPIRED"

    # 2. Within 7 days
    # exactly 7 days
    assert determine_warning_window(now + timedelta(days=7), now) == "7"
    # just inside 7 days (6.9 days)
    assert determine_warning_window(now + timedelta(days=6.9), now) == "7"
    # just outside 7 days (7.1 days) -> falls into 30
    assert determine_warning_window(now + timedelta(days=7.1), now) == "30"

    # 3. Within 30 days
    assert determine_warning_window(now + timedelta(days=30), now) == "30"
    assert determine_warning_window(now + timedelta(days=29.9), now) == "30"
    assert determine_warning_window(now + timedelta(days=30.1), now) == "90"

    # 4. Within 90 days
    assert determine_warning_window(now + timedelta(days=90), now) == "90"
    assert determine_warning_window(now + timedelta(days=89.9), now) == "90"
    # far outside 90 days
    assert determine_warning_window(now + timedelta(days=91), now) is None


@pytest.mark.asyncio
async def test_execute_expiration_scan_cycle_thresholds():
    """Test that execute_expiration_scan_cycle queries correctly, identifies states, and records alerts."""
    session_maker = db_manager.get_session_maker()
    now = datetime.now(timezone.utc)

    async with session_maker() as session:
        # Create documents with various expiration dates
        # d_expired: past expiration
        # d_7: 5 days remaining -> "7"
        # d_30: 20 days remaining -> "30"
        # d_90: 80 days remaining -> "90"
        # d_none: 100 days remaining -> no alert
        # d_no_exp: null expiration -> no alert
        d_expired = TMFDocument(
            study_id="study_1",
            zone=1,
            section="01",
            artifact_type="Protocol",
            filename="d_expired.pdf",
            content="expired",
            mime_type="application/pdf",
            created_by="test_user",
            expiration_date=now - timedelta(days=2),
        )
        d_7 = TMFDocument(
            study_id="study_1",
            zone=1,
            section="01",
            artifact_type="Protocol",
            filename="d_7.pdf",
            content="7",
            mime_type="application/pdf",
            created_by="test_user",
            expiration_date=now + timedelta(days=5),
        )
        d_30 = TMFDocument(
            study_id="study_1",
            zone=1,
            section="01",
            artifact_type="Protocol",
            filename="d_30.pdf",
            content="30",
            mime_type="application/pdf",
            created_by="test_user",
            expiration_date=now + timedelta(days=20),
        )
        d_90 = TMFDocument(
            study_id="study_1",
            zone=1,
            section="01",
            artifact_type="Protocol",
            filename="d_90.pdf",
            content="90",
            mime_type="application/pdf",
            created_by="test_user",
            expiration_date=now + timedelta(days=80),
        )
        d_none = TMFDocument(
            study_id="study_1",
            zone=1,
            section="01",
            artifact_type="Protocol",
            filename="d_none.pdf",
            content="none",
            mime_type="application/pdf",
            created_by="test_user",
            expiration_date=now + timedelta(days=100),
        )
        d_no_exp = TMFDocument(
            study_id="study_1",
            zone=1,
            section="01",
            artifact_type="Protocol",
            filename="d_no_exp.pdf",
            content="no_exp",
            mime_type="application/pdf",
            created_by="test_user",
            expiration_date=None,
        )
        session.add_all([d_expired, d_7, d_30, d_90, d_none, d_no_exp])
        await session.commit()

    # Run scanner cycle
    await execute_expiration_scan_cycle(session_maker)

    # Verify persistent state
    async with session_maker() as session:
        # Check expired
        res = await session.execute(
            select(DocumentExpirationAlertState)
            .join(TMFDocument)
            .where(TMFDocument.filename == "d_expired.pdf")
        )
        alerts = res.scalars().all()
        assert len(alerts) == 1
        assert alerts[0].warning_window == "EXPIRED"

        # Check 7
        res = await session.execute(
            select(DocumentExpirationAlertState)
            .join(TMFDocument)
            .where(TMFDocument.filename == "d_7.pdf")
        )
        alerts = res.scalars().all()
        assert len(alerts) == 1
        assert alerts[0].warning_window == "7"

        # Check 30
        res = await session.execute(
            select(DocumentExpirationAlertState)
            .join(TMFDocument)
            .where(TMFDocument.filename == "d_30.pdf")
        )
        alerts = res.scalars().all()
        assert len(alerts) == 1
        assert alerts[0].warning_window == "30"

        # Check 90
        res = await session.execute(
            select(DocumentExpirationAlertState)
            .join(TMFDocument)
            .where(TMFDocument.filename == "d_90.pdf")
        )
        alerts = res.scalars().all()
        assert len(alerts) == 1
        assert alerts[0].warning_window == "90"

        # Check d_none
        res = await session.execute(
            select(DocumentExpirationAlertState)
            .join(TMFDocument)
            .where(TMFDocument.filename == "d_none.pdf")
        )
        assert len(res.scalars().all()) == 0


@pytest.mark.asyncio
async def test_scanner_idempotency_restart_and_rearming():
    """Test scanner idempotency across runs, restart behavior, and explicit re-arming."""
    session_maker = db_manager.get_session_maker()
    now = datetime.now(timezone.utc)

    async with session_maker() as session:
        doc = TMFDocument(
            study_id="study_1",
            zone=1,
            section="01",
            artifact_type="Protocol",
            filename="idempotent_doc.pdf",
            content="content",
            mime_type="application/pdf",
            created_by="test_user",
            expiration_date=now + timedelta(days=5),
        )
        session.add(doc)
        await session.commit()
        doc_id = doc.id

    # 1. Run cycle first time -> creates 1 alert state row
    await execute_expiration_scan_cycle(session_maker)

    async with session_maker() as session:
        res = await session.execute(
            select(DocumentExpirationAlertState).where(
                DocumentExpirationAlertState.document_id == doc_id
            )
        )
        alerts = res.scalars().all()
        assert len(alerts) == 1
        assert alerts[0].warning_window == "7"

    # 2. Run cycle second time -> still only 1 alert state row (no duplicates)
    await execute_expiration_scan_cycle(session_maker)

    async with session_maker() as session:
        res = await session.execute(
            select(DocumentExpirationAlertState).where(
                DocumentExpirationAlertState.document_id == doc_id
            )
        )
        alerts = res.scalars().all()
        assert len(alerts) == 1

    # 3. Fresh session pointed at the same DB still recognizes prior dedup state
    async with session_maker() as fresh_session:
        res = await fresh_session.execute(
            select(DocumentExpirationAlertState).where(
                DocumentExpirationAlertState.document_id == doc_id
            )
        )
        assert len(res.scalars().all()) == 1

    # 4. Explicitly remove/delete the row (re-arm) and rerun -> creates a fresh alert row
    async with session_maker() as session:
        res = await session.execute(
            select(DocumentExpirationAlertState).where(
                DocumentExpirationAlertState.document_id == doc_id
            )
        )
        alert = res.scalars().first()
        await session.delete(alert)
        await session.commit()

    # Rerun scanner -> should generate a new alert for "7"
    await execute_expiration_scan_cycle(session_maker)

    async with session_maker() as session:
        res = await session.execute(
            select(DocumentExpirationAlertState).where(
                DocumentExpirationAlertState.document_id == doc_id
            )
        )
        alerts = res.scalars().all()
        assert len(alerts) == 1
        assert alerts[0].warning_window == "7"


@pytest.mark.asyncio
async def test_failure_isolation_and_resilience():
    """Test that a loop iteration exception is isolated and the loop keeps running."""
    session_maker = MagicMock()
    # Force the scanner's cycle to raise an exception
    session_maker.side_effect = Exception("Transient DB connectivity loss")

    # Start loop with very short interval
    os.environ["ETMF_EXPIRATION_SCANNER_INTERVAL_SECONDS"] = "0.1"
    await start_background_etmf_expiration_scanner(session_maker, interval=0.1)

    import apps.etmf.expiration_scanner as es

    assert es._scanner_task is not None
    assert es._should_run is True

    # Let it run for a short duration to verify it isolated the exception and did not crash/die
    await asyncio.sleep(0.3)

    assert es._scanner_task is not None
    assert es._should_run is True

    # Stop loop cleanly
    await stop_background_etmf_expiration_scanner()
    assert es._scanner_task is None
    assert es._should_run is False


@pytest.mark.asyncio
async def test_scanner_shutdown_cancellation():
    """Test that background task shuts down cleanly with no leaked background task."""
    session_maker = MagicMock()
    await start_background_etmf_expiration_scanner(session_maker, interval=0.1)

    import apps.etmf.expiration_scanner as es

    assert es._scanner_task is not None
    assert es._should_run is True

    await stop_background_etmf_expiration_scanner()
    assert es._scanner_task is None
    assert es._should_run is False


@pytest.mark.asyncio
async def test_audit_attribution():
    """Verify that alert-state rows created carry the explicit scanner service identity in created_by."""
    session_maker = db_manager.get_session_maker()
    now = datetime.now(timezone.utc)

    async with session_maker() as session:
        doc = TMFDocument(
            study_id="study_1",
            zone=1,
            section="01",
            artifact_type="Protocol",
            filename="audit_attributed.pdf",
            content="content",
            mime_type="application/pdf",
            created_by="test_user",
            expiration_date=now - timedelta(days=1),
        )
        session.add(doc)
        await session.commit()
        doc_id = doc.id

    # Run cycle
    await execute_expiration_scan_cycle(session_maker)

    async with session_maker() as session:
        res = await session.execute(
            select(DocumentExpirationAlertState).where(
                DocumentExpirationAlertState.document_id == doc_id
            )
        )
        alert = res.scalars().one()
        assert alert.created_by == "expiration_scanner"
        assert alert.reason_for_change == "System-initiated expiration alert generation"


@pytest.mark.asyncio
async def test_dispatch_successful_owner_routing():
    """Verify document owner routing and correct signature header properties on success."""
    from unittest.mock import AsyncMock, MagicMock, patch

    session_maker = db_manager.get_session_maker()
    now = datetime.now(timezone.utc)

    # 1. Create a document with document_owner_id
    async with session_maker() as session:
        doc = TMFDocument(
            study_id="study_abc",
            zone=5,
            section="02",
            artifact_type="Informed Consent Form",
            filename="owner_consent.pdf",
            content="owner content",
            mime_type="application/pdf",
            created_by="test_user",
            expiration_date=now - timedelta(days=2),
            document_owner_id="owner_usr_123",
        )
        session.add(doc)
        await session.commit()
        doc_id = doc.id

    # 2. Mock httpx POST call to return 201 (success)
    mock_response = MagicMock()
    mock_response.status_code = 201
    mock_response.json.return_value = {"id": "notif-abc-123"}

    with patch(
        "httpx.AsyncClient.post", new_callable=AsyncMock, return_value=mock_response
    ) as mock_post:
        # Run scan cycle
        await execute_expiration_scan_cycle(session_maker)

        # 3. Assert notification creation request was sent
        assert mock_post.call_count == 1
        call_args, call_kwargs = mock_post.call_args

        # Check payload/JSON content
        json_data = call_kwargs["json"]
        assert json_data["recipient_user_id"] == "owner_usr_123"
        assert json_data["recipient_role"] is None
        assert json_data["category"] == "ALERTS"
        assert json_data["priority"] == "HIGH"
        assert json_data["related_entity_type"] == "tmf_document_expiration"
        assert f"{doc_id}:EXPIRED" in json_data["related_entity_id"]

        # 4. Check gateway signature headers
        headers = call_kwargs["headers"]
        assert headers["X-User-Id"] == "etmf-service"
        assert headers["X-User-Roles"] == "admin"
        assert "X-Gateway-Signature" in headers

        # Verify gateway signature
        from packages.security.signing import verify_gateway_signature

        gateway_secret = os.getenv(
            "GATEWAY_SECRET", "internal-gateway-secret-12345"
        ).encode("utf-8")
        is_valid = verify_gateway_signature(
            user_id=headers["X-User-Id"],
            roles=headers["X-User-Roles"],
            timestamp=headers["X-Gateway-Timestamp"],
            signature=headers["X-Gateway-Signature"],
            secret=gateway_secret,
            change_reason=headers["X-Change-Reason"],
        )
        assert is_valid is True

    # 5. Check persistent database state
    async with session_maker() as session:
        res = await session.execute(
            select(DocumentExpirationAlertState).where(
                DocumentExpirationAlertState.document_id == doc_id
            )
        )
        alert = res.scalars().one()
        assert alert.dispatched is True
        assert alert.notification_id == "notif-abc-123"
        assert alert.attempts == 1
        assert alert.last_error is None

        # Check TMFAuditLog
        from apps.etmf.models import TMFAuditLog

        res_audit = await session.execute(
            select(TMFAuditLog).where(TMFAuditLog.action == "EXPIRATION_ALERT_DISPATCH")
        )
        audits = res_audit.scalars().all()
        assert len(audits) == 1
        assert (
            f"Successfully dispatched expiration alert for document ID '{doc_id}'"
            in audits[0].details
        )


@pytest.mark.asyncio
async def test_dispatch_fallback_cra_routing():
    """Verify fallback role routing to 'CRA' when document_owner_id is missing."""
    from unittest.mock import AsyncMock, MagicMock, patch

    session_maker = db_manager.get_session_maker()
    now = datetime.now(timezone.utc)

    # Create document without owner
    async with session_maker() as session:
        doc = TMFDocument(
            study_id="study_abc",
            zone=5,
            section="02",
            artifact_type="Informed Consent Form",
            filename="fallback_consent.pdf",
            content="fallback content",
            mime_type="application/pdf",
            created_by="test_user",
            expiration_date=now - timedelta(days=2),
            document_owner_id=None,
        )
        session.add(doc)
        await session.commit()

    mock_response = MagicMock()
    mock_response.status_code = 201
    mock_response.json.return_value = {"id": "notif-fallback-456"}

    with patch(
        "httpx.AsyncClient.post", new_callable=AsyncMock, return_value=mock_response
    ) as mock_post:
        await execute_expiration_scan_cycle(session_maker)

        assert mock_post.call_count == 1
        json_data = mock_post.call_args[1]["json"]
        assert json_data["recipient_user_id"] is None
        assert json_data["recipient_role"] == "CRA"


@pytest.mark.asyncio
async def test_dispatch_failure_and_retryability():
    """Verify failed dispatch handling, retry states, and audit log writing on failure."""
    from unittest.mock import AsyncMock, MagicMock, patch

    session_maker = db_manager.get_session_maker()
    now = datetime.now(timezone.utc)

    async with session_maker() as session:
        doc = TMFDocument(
            study_id="study_abc",
            zone=5,
            section="02",
            artifact_type="Informed Consent Form",
            filename="retry_consent.pdf",
            content="retry content",
            mime_type="application/pdf",
            created_by="test_user",
            expiration_date=now - timedelta(days=2),
        )
        session.add(doc)
        await session.commit()
        doc_id = doc.id

    # 1. First run with failure response (HTTP 500)
    mock_failed_resp = MagicMock()
    mock_failed_resp.status_code = 500
    mock_failed_resp.text = "Internal Server Error"

    with patch(
        "httpx.AsyncClient.post", new_callable=AsyncMock, return_value=mock_failed_resp
    ) as mock_post:
        await execute_expiration_scan_cycle(session_maker)

        assert mock_post.call_count == 1

    # Verify state remains undispatched, with recorded failure and attempt increment
    async with session_maker() as session:
        res = await session.execute(
            select(DocumentExpirationAlertState).where(
                DocumentExpirationAlertState.document_id == doc_id
            )
        )
        alert = res.scalars().one()
        assert alert.dispatched is False
        assert alert.attempts == 1
        assert "HTTP 500" in alert.last_error

        # Check TMFAuditLog for failed dispatch
        from apps.etmf.models import TMFAuditLog

        res_audit = await session.execute(
            select(TMFAuditLog).where(
                TMFAuditLog.action == "EXPIRATION_ALERT_DISPATCH_FAILED"
            )
        )
        audits = res_audit.scalars().all()
        assert len(audits) == 1
        assert "Failed to dispatch" in audits[0].details

    # 2. Second run with success response (HTTP 201)
    mock_success_resp = MagicMock()
    mock_success_resp.status_code = 201
    mock_success_resp.json.return_value = {"id": "notif-success-789"}

    with patch(
        "httpx.AsyncClient.post", new_callable=AsyncMock, return_value=mock_success_resp
    ) as mock_post:
        await execute_expiration_scan_cycle(session_maker)

        assert mock_post.call_count == 1

    # Verify state transitions to dispatched, attempts=2, last_error is None
    async with session_maker() as session:
        res = await session.execute(
            select(DocumentExpirationAlertState).where(
                DocumentExpirationAlertState.document_id == doc_id
            )
        )
        alert = res.scalars().one()
        assert alert.dispatched is True
        assert alert.notification_id == "notif-success-789"
        assert alert.attempts == 2
        assert alert.last_error is None

        # Check TMFAuditLog for successful dispatch
        from apps.etmf.models import TMFAuditLog

        res_audit = await session.execute(
            select(TMFAuditLog).where(TMFAuditLog.action == "EXPIRATION_ALERT_DISPATCH")
        )
        audits = res_audit.scalars().all()
        assert len(audits) == 1


@pytest.mark.asyncio
async def test_dispatch_idempotency_limit():
    """Verify that once dispatched, no further authenticated notification requests are generated."""
    from unittest.mock import AsyncMock, MagicMock, patch

    session_maker = db_manager.get_session_maker()
    now = datetime.now(timezone.utc)

    async with session_maker() as session:
        doc = TMFDocument(
            study_id="study_abc",
            zone=5,
            section="02",
            artifact_type="Informed Consent Form",
            filename="idempotency_consent.pdf",
            content="idempotency content",
            mime_type="application/pdf",
            created_by="test_user",
            expiration_date=now - timedelta(days=2),
        )
        session.add(doc)
        await session.commit()

    mock_response = MagicMock()
    mock_response.status_code = 201
    mock_response.json.return_value = {"id": "notif-idem-999"}

    # 1. First run: should dispatch successfully
    with patch(
        "httpx.AsyncClient.post", new_callable=AsyncMock, return_value=mock_response
    ) as mock_post:
        await execute_expiration_scan_cycle(session_maker)
        assert mock_post.call_count == 1

    # 2. Second run: already marked dispatched, should NOT call POST again
    with patch(
        "httpx.AsyncClient.post", new_callable=AsyncMock, return_value=mock_response
    ) as mock_post:
        await execute_expiration_scan_cycle(session_maker)
        assert mock_post.call_count == 0
