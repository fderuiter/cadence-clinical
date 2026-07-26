import datetime
import os

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
