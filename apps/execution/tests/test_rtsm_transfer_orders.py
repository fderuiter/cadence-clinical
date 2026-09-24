"""Integration tests for Multi-Echelon Transfer Orders & GxP Packing Manifest Lifecycle.

Requirements: PRD-SCM-001, GMP Annex 13
"""

import os
import time

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    Base,
    IntegrationOutbox,
    SupplyNode,
    SupplyNodeInventory,
)
from apps.execution.main import app
from apps.execution.trial_lock import TrialLockManager
from packages.security.signing import generate_gateway_signature


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """Initializes clean test database."""
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


def get_gateway_headers(
    user_id="test_supply_mgr",
    roles="sponsor_admin",
    change_reason="Replenish site stock",
    site_id=None,
    include_sig_token=True,
) -> dict:
    """Generate signed gateway V2 headers for test requests."""
    from jose import jwt

    timestamp = str(time.time())
    sig_token = None
    if include_sig_token:
        sig_payload = {
            "sub": user_id,
            "username": user_id,
            "roles": [roles],
            "iat": time.time(),
            "exp": time.time() + 300.0,
        }
        sig_token = jwt.encode(
            sig_payload, b"internal-gateway-secret-12345", algorithm="HS256"
        )

    sig = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=b"internal-gateway-secret-12345",  # pragma: allowlist secret
        change_reason=change_reason,
        site_id=site_id,
        sig_token=sig_token,
    )
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }
    if site_id:
        headers["X-Site-Id"] = site_id
    if sig_token:
        headers["X-Sig-Token"] = sig_token
    return headers


@pytest.mark.asyncio
async def test_full_transfer_order_and_manifest_lifecycle():
    """Verify complete lifecycle: Create -> Approve -> Manifest -> Ship -> Deliver.

    @req:PRD-SYS-184
    @req:PRD-SCM-001
    """
    study_id = "STUDY-TO-100"
    source_depot_id = "DEPOT-CENTRAL"
    destination_site_id = "SITE-301"

    # 1. Setup Source Depot and Destination Site with Inventory
    async with db_manager.get_session_maker()() as session:
        depot = SupplyNode(
            study_id=study_id,
            node_id=source_depot_id,
            node_type="CENTRAL_DEPOT",
            name="Main Central Depot",
            country="USA",
            storage_capacity=5000,
            current_utilization=1000,
        )
        site = SupplyNode(
            study_id=study_id,
            node_id=destination_site_id,
            node_type="CLINICAL_SITE",
            name="Site 301",
            country="USA",
            parent_node_id=source_depot_id,
            storage_capacity=200,
            current_utilization=10,
        )
        depot_inv = SupplyNodeInventory(
            study_id=study_id,
            node_id=source_depot_id,
            kit_type="KIT-TYPE-1",
            on_hand_qty=500,
            reserved_qty=0,
            in_transit_qty=0,
        )
        site_inv = SupplyNodeInventory(
            study_id=study_id,
            node_id=destination_site_id,
            kit_type="KIT-TYPE-1",
            on_hand_qty=10,
            reserved_qty=0,
            in_transit_qty=0,
            safety_stock_threshold=5,
            reorder_point=15,
            resupply_signal=True,
        )
        session.add_all([depot, site, depot_inv, site_inv])
        await session.commit()

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_gateway_headers(roles="sponsor_admin")

        # Step 1: Create Transfer Order
        create_payload = {
            "study_id": study_id,
            "source_node_id": source_depot_id,
            "destination_node_id": destination_site_id,
            "kit_type": "KIT-TYPE-1",
            "quantity": 25,
            "priority": "ROUTINE",
            "temperature_condition": "REFRIGERATED_2_TO_8",
            "carrier": "World Courier",
        }
        res_create = await client.post(
            "/api/v1/execution/rtsm/transfer-orders",
            json=create_payload,
            headers=headers,
        )
        assert res_create.status_code == 201
        order_data = res_create.json()
        order_id = order_data["id"]
        assert order_data["status"] == "REQUESTED"
        assert order_data["quantity"] == 25

        # Verify source stock was reserved
        async with db_manager.get_session_maker()() as session:
            stmt = select(SupplyNodeInventory).where(
                SupplyNodeInventory.node_id == source_depot_id
            )
            res = await session.execute(stmt)
            inv = res.scalars().one()
            assert inv.reserved_qty == 25

        # Step 2: Approve Transfer Order
        approve_payload = {
            "change_justification": "Approved replenishment based on forecast"
        }
        res_app = await client.post(
            f"/api/v1/execution/rtsm/transfer-orders/{order_id}/approve",
            json=approve_payload,
            headers=headers,
        )
        assert res_app.status_code == 200
        assert res_app.json()["status"] == "APPROVED"

        # Step 3: Generate Packing Manifest
        kit_serials = [f"KIT-SER-{i:04d}" for i in range(1, 26)]
        manifest_payload = {
            "manifest_number": "PM-2026-00101",
            "serialized_kit_numbers": kit_serials,
            "lot_number": "LOT-2026-X1",
            "data_logger_id": "TL-998877",
            "tamper_seal_id": "SEAL-5544",
            "container_serial_number": "BOX-012",
        }
        res_man = await client.post(
            f"/api/v1/execution/rtsm/transfer-orders/{order_id}/manifest",
            json=manifest_payload,
            headers=headers,
        )
        assert res_man.status_code == 201
        manifest_data = res_man.json()
        assert manifest_data["manifest_number"] == "PM-2026-00101"
        assert len(manifest_data["serialized_kit_numbers"]) == 25
        assert manifest_data["data_logger_id"] == "TL-998877"

        # Step 4: Ship Transfer Order
        ship_payload = {
            "tracking_number": "TRK-987654321",
            "carrier": "World Courier",
            "change_justification": "Carrier pickup complete",
        }
        res_ship = await client.post(
            f"/api/v1/execution/rtsm/transfer-orders/{order_id}/ship",
            json=ship_payload,
            headers=headers,
        )
        assert res_ship.status_code == 200
        assert res_ship.json()["status"] == "SHIPPED"

        # Verify inventories post-shipment
        async with db_manager.get_session_maker()() as session:
            # Source depot: on_hand reduced from 500 to 475, reserved reduced from 25 to 0
            stmt_src = select(SupplyNodeInventory).where(
                SupplyNodeInventory.node_id == source_depot_id
            )
            src_inv_db = (await session.execute(stmt_src)).scalars().one()
            assert src_inv_db.on_hand_qty == 475
            assert src_inv_db.reserved_qty == 0

            # Destination site: in_transit increased from 0 to 25
            stmt_dst = select(SupplyNodeInventory).where(
                SupplyNodeInventory.node_id == destination_site_id
            )
            dst_inv_db = (await session.execute(stmt_dst)).scalars().one()
            assert dst_inv_db.in_transit_qty == 25
            assert dst_inv_db.on_hand_qty == 10

            # Outbox event emitted
            stmt_outbox = select(IntegrationOutbox).where(
                IntegrationOutbox.event_type == "RTSM_SHIPMENT_DISPATCHED"
            )
            outbox_event = (await session.execute(stmt_outbox)).scalars().first()
            assert outbox_event is not None
            assert outbox_event.payload["tracking_number"] == "TRK-987654321"

        # Step 5: Receive / Deliver Transfer Order at Destination
        receive_payload = {
            "change_justification": "Site confirmed shipment delivery and inspected cold-chain logger"
        }
        res_recv = await client.post(
            f"/api/v1/execution/rtsm/transfer-orders/{order_id}/receive",
            json=receive_payload,
            headers=headers,
        )
        assert res_recv.status_code == 200
        assert res_recv.json()["status"] == "DELIVERED"

        # Verify destination stock updated: in_transit=0, on_hand=35 (10 + 25)
        async with db_manager.get_session_maker()() as session:
            stmt_dst = select(SupplyNodeInventory).where(
                SupplyNodeInventory.node_id == destination_site_id
            )
            dst_inv_db = (await session.execute(stmt_dst)).scalars().one()
            assert dst_inv_db.in_transit_qty == 0
            assert dst_inv_db.on_hand_qty == 35
            assert dst_inv_db.resupply_signal is False  # 35 > 15 reorder point

            # Destination site utilization updated
            stmt_node = select(SupplyNode).where(
                SupplyNode.node_id == destination_site_id
            )
            dst_node_db = (await session.execute(stmt_node)).scalars().one()
            assert dst_node_db.current_utilization == 35


@pytest.mark.asyncio
async def test_locked_destination_blocks_transfer_order():
    """Verify that locked destination site blocks transfer order creation with HTTP 423.

    @req:PRD-SYS-184
    @req:PRD-SCM-001
    """
    study_id = "STUDY-LOCK"
    source_depot_id = "DEPOT-1"
    destination_site_id = "SITE-LOCKED-99"

    async with db_manager.get_session_maker()() as session:
        depot = SupplyNode(
            study_id=study_id,
            node_id=source_depot_id,
            node_type="CENTRAL_DEPOT",
            name="Depot 1",
            country="USA",
        )
        site = SupplyNode(
            study_id=study_id,
            node_id=destination_site_id,
            node_type="CLINICAL_SITE",
            name="Locked Site",
            country="USA",
        )
        depot_inv = SupplyNodeInventory(
            study_id=study_id,
            node_id=source_depot_id,
            kit_type="KIT-1",
            on_hand_qty=100,
        )
        session.add_all([depot, site, depot_inv])
        await session.commit()

    TrialLockManager.lock_site(destination_site_id)
    try:
        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app), base_url="http://test"
        ) as client:
            headers = get_gateway_headers(roles="sponsor_admin")
            payload = {
                "study_id": study_id,
                "source_node_id": source_depot_id,
                "destination_node_id": destination_site_id,
                "kit_type": "KIT-1",
                "quantity": 10,
            }
            res = await client.post(
                "/api/v1/execution/rtsm/transfer-orders",
                json=payload,
                headers=headers,
            )
            assert res.status_code == 423
            assert "locked" in res.json()["detail"]
    finally:
        TrialLockManager.unlock_site(destination_site_id)


@pytest.mark.asyncio
async def test_storage_capacity_exceeded_rejection():
    """Verify that orders exceeding destination storage capacity are rejected with HTTP 400.

    @req:PRD-SYS-184
    @req:PRD-SCM-001
    """
    study_id = "STUDY-CAP"
    source_depot_id = "DEPOT-CAP"
    destination_site_id = "SITE-SMALL"

    async with db_manager.get_session_maker()() as session:
        depot = SupplyNode(
            study_id=study_id,
            node_id=source_depot_id,
            node_type="CENTRAL_DEPOT",
            name="Depot",
            country="USA",
        )
        site = SupplyNode(
            study_id=study_id,
            node_id=destination_site_id,
            node_type="CLINICAL_SITE",
            name="Small Site",
            country="USA",
            storage_capacity=50,
            current_utilization=40,
        )
        depot_inv = SupplyNodeInventory(
            study_id=study_id,
            node_id=source_depot_id,
            kit_type="KIT-1",
            on_hand_qty=200,
        )
        session.add_all([depot, site, depot_inv])
        await session.commit()

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_gateway_headers(roles="sponsor_admin")
        payload = {
            "study_id": study_id,
            "source_node_id": source_depot_id,
            "destination_node_id": destination_site_id,
            "kit_type": "KIT-1",
            "quantity": 25,  # 40 current + 25 = 65 > 50 capacity
        }
        res = await client.post(
            "/api/v1/execution/rtsm/transfer-orders",
            json=payload,
            headers=headers,
        )
        assert res.status_code == 400
        assert "storage capacity" in res.json()["detail"].lower()
