import asyncio
import datetime
import os

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.database.context import (
    current_change_reason,
    current_session,
    current_user_id,
)
from apps.execution.database.core import db_manager
from apps.execution.database.decorators import transactional
from apps.execution.database.models import (
    AuditLog,
    Base,
    IPKit,
    KitDispensation,
    ResupplyEvent,
    SiteInventory,
)
from apps.execution.main import app
from apps.execution.rtsm_supply import (
    evaluate_resupply,
)
from apps.execution.trial_lock import TrialLockManager


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """Initializes a clean, fully-triggered SQLite/Postgres test database before each test."""
    from apps.execution.database.migrate import deploy_database_triggers

    TrialLockManager.reset()
    db_manager.init_db(
        os.getenv("TEST_DATABASE_URL", "sqlite+aiosqlite:///:memory:"),
        echo=False,
    )
    async with db_manager.engine.begin() as conn:
        from sqlalchemy import text

        if db_manager.engine.dialect.name == "postgresql":
            await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))
        await conn.run_sync(Base.metadata.create_all)
        await deploy_database_triggers(conn, db_manager.engine.dialect.name)
    yield
    TrialLockManager.reset()
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_supply_entities_audit_trail_and_soft_delete():
    """Verify that all new supply entities participate in GxP auditing and support soft deletes."""
    current_user_id.set("user_supply_mgr")
    current_change_reason.set("Configure initial supply-domain catalog and inventory")

    # 1. Insert IPKit catalog item
    @transactional(lambda: db_manager.get_session_maker()())
    async def configure_catalog():
        session = current_session.get()
        kit = IPKit(
            study_id="STUDY_XYZ",
            kit_number="KIT-1001",
            kit_type="Type A (Blinded)",
            description="Active drug or placebo kit, strictly blinded",
        )
        session.add(kit)
        await session.flush()
        return kit.id

    kit_id = await configure_catalog()

    # Verify audit log for the INSERT operation
    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(AuditLog).where(AuditLog.record_id == kit_id)
        )
        kit_log = result.scalars().first()
        assert kit_log is not None
        assert kit_log.action == "INSERT"
        assert kit_log.table_name == "ip_kits"
        assert kit_log.user_id == "user_supply_mgr"
        assert (
            kit_log.change_reason
            == "Configure initial supply-domain catalog and inventory"
        )
        assert kit_log.version_index == 1
        assert kit_log.new_values["kit_number"] == "KIT-1001"
        assert (
            "treatment_arm" not in kit_log.new_values
        )  # Preserve blinding: no unblinded fields

    # 2. Insert SiteInventory item
    @transactional(lambda: db_manager.get_session_maker()())
    async def configure_inventory():
        session = current_session.get()
        inventory = SiteInventory(
            study_id="STUDY_XYZ",
            site_id="SITE-001",
            kit_id="KIT-1001",
            on_hand_qty=10,
            reorder_threshold=3,
            resupply_signal=False,
        )
        session.add(inventory)
        await session.flush()
        return inventory.id

    inv_id = await configure_inventory()

    # Verify audit log for the inventory INSERT operation
    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(AuditLog).where(AuditLog.record_id == inv_id)
        )
        inv_log = result.scalars().first()
        assert inv_log is not None
        assert inv_log.action == "INSERT"
        assert inv_log.table_name == "site_inventories"
        assert inv_log.version_index == 1
        assert inv_log.new_values["on_hand_qty"] == 10

    # 3. Update SiteInventory (e.g. reduce stock and trigger resupply)
    current_user_id.set("user_supply_mgr")
    current_change_reason.set("Adjust inventory level due to dispensing")

    @transactional(lambda: db_manager.get_session_maker()())
    async def update_inventory():
        session = current_session.get()
        result = await session.execute(
            select(SiteInventory).where(SiteInventory.id == inv_id)
        )
        inv = result.scalars().one()
        inv.on_hand_qty = 2
        inv.resupply_signal = True
        await session.flush()
        return inv.version

    new_version = await update_inventory()
    assert new_version == 2

    # Verify audit log for the UPDATE operation
    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(AuditLog)
            .where(AuditLog.record_id == inv_id)
            .order_by(AuditLog.version_index)
        )
        inv_logs = result.scalars().all()
        assert len(inv_logs) == 2
        update_log = inv_logs[1]
        assert update_log.action == "UPDATE"
        assert update_log.old_values["on_hand_qty"] == 10
        assert update_log.new_values["on_hand_qty"] == 2
        assert update_log.version_index == 2

    # 4. Insert KitDispensation
    current_user_id.set("user_investigator")
    current_change_reason.set("Dispense kit at Visit 1")

    @transactional(lambda: db_manager.get_session_maker()())
    async def dispense_kit():
        session = current_session.get()
        dispensation = KitDispensation(
            study_id="STUDY_XYZ",
            subject_id="SUBJ-001",
            kit_id="KIT-1001",
            site_id="SITE-001",
            visit_id="VISIT-001",
            quantity=1,
        )
        session.add(dispensation)
        await session.flush()
        return dispensation.id

    disp_id = await dispense_kit()

    # Verify dispensation details and audit trail
    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(KitDispensation).where(KitDispensation.id == disp_id)
        )
        disp = result.scalars().one()
        assert disp.subject_id == "SUBJ-001"
        assert disp.kit_id == "KIT-1001"
        assert disp.site_id == "SITE-001"
        assert disp.visit_id == "VISIT-001"
        assert disp.quantity == 1
        assert isinstance(disp.timestamp, datetime.datetime)

        result_log = await session.execute(
            select(AuditLog).where(AuditLog.record_id == disp_id)
        )
        disp_log = result_log.scalars().one()
        assert disp_log.action == "INSERT"
        assert disp_log.table_name == "kit_dispensations"

    # 5. Insert ResupplyEvent
    current_user_id.set("system_automation")
    current_change_reason.set("Automatic reorder threshold reached")

    @transactional(lambda: db_manager.get_session_maker()())
    async def request_resupply():
        session = current_session.get()
        event = ResupplyEvent(
            study_id="STUDY_XYZ",
            site_id="SITE-001",
            kit_id="KIT-1001",
            requested_qty=20,
            status="PENDING",
        )
        session.add(event)
        await session.flush()
        return event.id

    event_id = await request_resupply()

    # Verify resupply event and auditing without writing directly to audit ledger
    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(ResupplyEvent).where(ResupplyEvent.id == event_id)
        )
        event = result.scalars().one()
        assert event.site_id == "SITE-001"
        assert event.requested_qty == 20
        assert event.status == "PENDING"

        result_log = await session.execute(
            select(AuditLog).where(AuditLog.record_id == event_id)
        )
        event_log = result_log.scalars().one()
        assert event_log.action == "INSERT"
        assert event_log.table_name == "resupply_events"


# --- Newly Added RTSM Supply Workflow Tests ---


def test_evaluate_resupply_boundaries():
    """Verify that evaluate_resupply correctly signals at, below, and above threshold."""
    # At threshold
    assert evaluate_resupply(5, 5) is True
    assert evaluate_resupply(0, 0) is True
    # Below threshold
    assert evaluate_resupply(3, 5) is True
    # Above threshold
    assert evaluate_resupply(6, 5) is False


def get_gateway_headers(
    user_id="test_crc",
    roles="crc",
    change_reason="Dispensation justification",
) -> dict:
    """Generate Gateway signature V2 headers for test requests."""
    import time

    from packages.security.signing import generate_gateway_signature

    timestamp = str(time.time())
    sig = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret="internal-gateway-secret-12345".encode(),  # pragma: allowlist secret
        change_reason=change_reason,
    )
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }


@pytest.mark.asyncio
async def test_successful_dispensation_endpoint():
    """Verify successful kit dispensation decrements inventory, creates a KitDispensation, and commits atomically."""
    # Setup initial inventory Catalog
    async with db_manager.get_session_maker()() as session:
        kit = IPKit(
            study_id="STUDY_123", kit_number="KIT-101", kit_type="A", description="desc"
        )
        session.add(kit)
        inv = SiteInventory(
            study_id="STUDY_123",
            site_id="SITE-A",
            kit_id="KIT-101",
            on_hand_qty=10,
            reorder_threshold=2,
            resupply_signal=False,
        )
        session.add(inv)
        await session.commit()

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_gateway_headers(roles="crc")
        payload = {
            "study_id": "STUDY_123",
            "site_id": "SITE-A",
            "subject_id": "SUBJ-001",
            "visit_id": "VISIT-1",
            "kit_id": "KIT-101",
            "quantity": 3,
        }
        response = await client.post(
            "/api/v1/execution/rtsm/dispense", json=payload, headers=headers
        )
        assert response.status_code == 201
        data = response.json()
        assert data["status"] == "success"
        assert data["resupply_triggered"] is False

    # Verify inventory was decremented and dispensation was committed
    async with db_manager.get_session_maker()() as session:
        # Check stock
        res_inv = await session.execute(
            select(SiteInventory).where(SiteInventory.kit_id == "KIT-101")
        )
        inv_db = res_inv.scalars().one()
        assert inv_db.on_hand_qty == 7
        assert inv_db.resupply_signal is False

        # Check dispensation
        res_disp = await session.execute(
            select(KitDispensation).where(KitDispensation.subject_id == "SUBJ-001")
        )
        disp_db = res_disp.scalars().one()
        assert disp_db.quantity == 3
        assert disp_db.site_id == "SITE-A"


@pytest.mark.asyncio
async def test_insufficient_stock_rejection_and_rollback():
    """Verify that insufficient stock is rejected, inventory remains unchanged, and transaction rolls back."""
    # Setup initial inventory Catalog
    async with db_manager.get_session_maker()() as session:
        kit = IPKit(
            study_id="STUDY_123", kit_number="KIT-102", kit_type="A", description="desc"
        )
        session.add(kit)
        inv = SiteInventory(
            study_id="STUDY_123",
            site_id="SITE-A",
            kit_id="KIT-102",
            on_hand_qty=2,
            reorder_threshold=1,
            resupply_signal=False,
        )
        session.add(inv)
        await session.commit()

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_gateway_headers(roles="site investigator")
        payload = {
            "study_id": "STUDY_123",
            "site_id": "SITE-A",
            "subject_id": "SUBJ-001",
            "visit_id": "VISIT-1",
            "kit_id": "KIT-102",
            "quantity": 5,  # Exceeds on-hand quantity of 2
        }
        response = await client.post(
            "/api/v1/execution/rtsm/dispense", json=payload, headers=headers
        )
        assert response.status_code == 400
        assert "Insufficient stock" in response.json()["detail"]

    # Verify inventory was NOT decremented and no dispensation was committed
    async with db_manager.get_session_maker()() as session:
        res_inv = await session.execute(
            select(SiteInventory).where(SiteInventory.kit_id == "KIT-102")
        )
        inv_db = res_inv.scalars().one()
        assert inv_db.on_hand_qty == 2

        res_disp = await session.execute(
            select(KitDispensation).where(KitDispensation.kit_id == "KIT-102")
        )
        assert res_disp.scalars().first() is None


@pytest.mark.asyncio
async def test_invalid_site_kit_relationship_rejection():
    """Verify that dispensation fails with 404 when no such site/kit relationship exists in inventory."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_gateway_headers(roles="cra")
        payload = {
            "study_id": "STUDY_123",
            "site_id": "SITE-B",  # Does not exist in SiteInventory
            "subject_id": "SUBJ-001",
            "visit_id": "VISIT-1",
            "kit_id": "KIT-999",  # Does not exist
            "quantity": 1,
        }
        response = await client.post(
            "/api/v1/execution/rtsm/dispense", json=payload, headers=headers
        )
        assert response.status_code == 404
        assert "No inventory record found" in response.json()["detail"]


@pytest.mark.asyncio
async def test_resupply_threshold_breach_and_deduplication():
    """Verify that crossing threshold creates ResupplyEvent and notification, while sub-threshold writes dedup."""
    # Setup initial inventory
    async with db_manager.get_session_maker()() as session:
        kit = IPKit(study_id="STUDY_XYZ", kit_number="KIT-200", kit_type="A")
        session.add(kit)
        inv = SiteInventory(
            study_id="STUDY_XYZ",
            site_id="SITE-C",
            kit_id="KIT-200",
            on_hand_qty=6,
            reorder_threshold=3,
            resupply_signal=False,
        )
        session.add(inv)
        await session.commit()

    # We mock send_dashboard_notification to verify it is called exactly once when first triggered
    from unittest.mock import patch

    with patch(
        "apps.execution.trial_lock.NotificationRouter.send_dashboard_notification"
    ) as mock_notif:
        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app), base_url="http://test"
        ) as client:
            headers = get_gateway_headers(roles="crc")
            payload = {
                "study_id": "STUDY_XYZ",
                "site_id": "SITE-C",
                "subject_id": "SUBJ-001",
                "visit_id": "VISIT-1",
                "kit_id": "KIT-200",
                "quantity": 4,  # Decrements from 6 to 2, which is <= 3 threshold -> triggers resupply
            }
            # 1. First trigger: triggers resupply event + notification
            response1 = await client.post(
                "/api/v1/execution/rtsm/dispense", json=payload, headers=headers
            )
            assert response1.status_code == 201
            assert response1.json()["resupply_triggered"] is True

            # Wait briefly to let async background tasks finish if any
            await asyncio.sleep(0.1)
            mock_notif.assert_called_once()

            # 2. Second trigger: already below threshold, but should NOT create a duplicate PENDING event
            payload2 = {
                "study_id": "STUDY_XYZ",
                "site_id": "SITE-C",
                "subject_id": "SUBJ-001",
                "visit_id": "VISIT-2",
                "kit_id": "KIT-200",
                "quantity": 1,  # Decrements from 2 to 1 -> still below threshold but dedups PENDING
            }
            response2 = await client.post(
                "/api/v1/execution/rtsm/dispense", json=payload2, headers=headers
            )
            assert response2.status_code == 201
            assert response2.json()["resupply_triggered"] is False

            # Wait briefly and assert call count is still 1 (deduped)
            await asyncio.sleep(0.1)
            assert mock_notif.call_count == 1

    # Verify database state: one ResupplyEvent and audited entries are preserved
    async with db_manager.get_session_maker()() as session:
        # Check ResupplyEvent
        stmt_event = select(ResupplyEvent).where(ResupplyEvent.kit_id == "KIT-200")
        res_events = await session.execute(stmt_event)
        events = res_events.scalars().all()
        assert len(events) == 1
        event = events[0]
        assert event.status == "PENDING"
        assert event.site_id == "SITE-C"

        # Assert AuditLog entry was created for ResupplyEvent insert
        stmt_audit = select(AuditLog).where(AuditLog.table_name == "resupply_events")
        res_audit = await session.execute(stmt_audit)
        audit_logs = res_audit.scalars().all()
        assert len(audit_logs) >= 1
        # Check that blinding is preserved (no unblinded fields in audit new_values)
        for log in audit_logs:
            assert "treatment_arm" not in (log.new_values or {})
            assert "treatment_arm_id" not in (log.new_values or {})


@pytest.mark.asyncio
async def test_locked_site_rejection():
    """Verify that locked site raises HTTP 423 and blocks any supply mutations."""
    # Setup initial inventory
    async with db_manager.get_session_maker()() as session:
        kit = IPKit(study_id="STUDY_XYZ", kit_number="KIT-300", kit_type="A")
        session.add(kit)
        inv = SiteInventory(
            study_id="STUDY_XYZ",
            site_id="SITE-LOCKED",
            kit_id="KIT-300",
            on_hand_qty=10,
            reorder_threshold=3,
            resupply_signal=False,
        )
        session.add(inv)
        await session.commit()

    # Lock site
    TrialLockManager.lock_site("SITE-LOCKED")

    try:
        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app), base_url="http://test"
        ) as client:
            headers = get_gateway_headers(roles="crc")
            payload = {
                "study_id": "STUDY_XYZ",
                "site_id": "SITE-LOCKED",
                "subject_id": "SUBJ-001",
                "visit_id": "VISIT-1",
                "kit_id": "KIT-300",
                "quantity": 1,
            }
            response = await client.post(
                "/api/v1/execution/rtsm/dispense", json=payload, headers=headers
            )
            # Early proactive check should reject with 423
            assert response.status_code == 423
            assert "locked" in response.json()["detail"]

        # Verify DB inventory did not change
        async with db_manager.get_session_maker()() as session:
            res_inv = await session.execute(
                select(SiteInventory).where(SiteInventory.kit_id == "KIT-300")
            )
            inv_db = res_inv.scalars().one()
            assert inv_db.on_hand_qty == 10
    finally:
        # Unlock site
        TrialLockManager.unlock_site("SITE-LOCKED")


@pytest.mark.asyncio
async def test_site_inventory_unique_constraint():
    """Verify that inventory is uniquely identified for a relevant site and kit."""

    @transactional(lambda: db_manager.get_session_maker()())
    async def configure_inventories():
        session = current_session.get()
        inv1 = SiteInventory(
            study_id="STUDY_XYZ",
            site_id="SITE-001",
            kit_id="KIT-1001",
            on_hand_qty=10,
        )
        session.add(inv1)
        await session.flush()

        # Attempting to insert a duplicate inventory for the same site and kit should raise unique constraint exception
        inv2 = SiteInventory(
            study_id="STUDY_XYZ",
            site_id="SITE-001",
            kit_id="KIT-1001",
            on_hand_qty=5,
        )
        session.add(inv2)
        await session.flush()

    with pytest.raises(Exception):
        await configure_inventories()


@pytest.mark.asyncio
async def test_hard_delete_prevented_for_supply_entities():
    """Verify that hard deletions are strictly prevented on all new supply entities."""

    # Setup record first
    @transactional(lambda: db_manager.get_session_maker()())
    async def setup_kit():
        session = current_session.get()
        kit = IPKit(study_id="STUDY_XYZ", kit_number="KIT-9999", kit_type="Type Z")
        session.add(kit)
        await session.flush()
        return kit.id

    kit_id = await setup_kit()

    @transactional(lambda: db_manager.get_session_maker()())
    async def hard_delete_kit():
        session = current_session.get()
        result = await session.execute(select(IPKit).where(IPKit.id == kit_id))
        kit = result.scalars().one()
        await session.delete(kit)
        await session.flush()

    with pytest.raises(ValueError, match="Hard deletion .* is forbidden"):
        await hard_delete_kit()


@pytest.mark.asyncio
async def test_trial_locking_conformity():
    """Verify that all new supply entities conform to global, site, visit, and subject locks."""
    # 1. Trial lock
    TrialLockManager.lock_trial()

    @transactional(lambda: db_manager.get_session_maker()())
    async def create_kit_locked():
        session = current_session.get()
        kit = IPKit(study_id="STUDY_XYZ", kit_number="KIT-8888", kit_type="Type B")
        session.add(kit)
        await session.flush()

    with pytest.raises(PermissionError, match="Trial is currently locked"):
        await create_kit_locked()

    TrialLockManager.unlock_trial()

    # 2. Site lock
    TrialLockManager.lock_site("SITE-001")

    @transactional(lambda: db_manager.get_session_maker()())
    async def create_inventory_site_locked():
        session = current_session.get()
        inv = SiteInventory(study_id="S", site_id="SITE-001", kit_id="K-1")
        session.add(inv)
        await session.flush()

    with pytest.raises(PermissionError, match="Site SITE-001 is currently locked"):
        await create_inventory_site_locked()

    TrialLockManager.unlock_site("SITE-001")

    # 3. Visit lock
    TrialLockManager.lock_visit("VISIT-001")

    @transactional(lambda: db_manager.get_session_maker()())
    async def dispense_visit_locked():
        session = current_session.get()
        disp = KitDispensation(
            study_id="S",
            subject_id="SUBJ-1",
            kit_id="K-1",
            site_id="SITE-2",
            visit_id="VISIT-001",
        )
        session.add(disp)
        await session.flush()

    with pytest.raises(PermissionError, match="Visit VISIT-001 is currently locked"):
        await dispense_visit_locked()

    TrialLockManager.unlock_visit("VISIT-001")

    # 4. Subject lock
    TrialLockManager.lock_subject("SUBJ-001")

    @transactional(lambda: db_manager.get_session_maker()())
    async def dispense_subject_locked():
        session = current_session.get()
        disp = KitDispensation(
            study_id="S",
            subject_id="SUBJ-001",
            kit_id="K-1",
            site_id="SITE-2",
            visit_id="VISIT-2",
        )
        session.add(disp)
        await session.flush()

    with pytest.raises(PermissionError, match="Subject SUBJ-001 is currently locked"):
        await dispense_subject_locked()

    TrialLockManager.unlock_subject("SUBJ-001")
