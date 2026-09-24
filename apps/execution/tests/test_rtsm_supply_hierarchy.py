"""Unit and integration tests for Multi-Echelon Supply Hierarchy.

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
    AuditLog,
    Base,
    SupplyNode,
    SupplyNodeInventory,
)
from apps.execution.main import app
from apps.execution.rtsm_supply import (
    InvalidHierarchyError,
    build_supply_hierarchy_tree,
)
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
    user_id="test_user",
    roles="sponsor_admin",
    change_reason="Supply chain configuration",
    site_id=None,
) -> dict:
    """Generate signed gateway V2 headers for test requests."""
    timestamp = str(time.time())
    sig = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=b"internal-gateway-secret-12345",  # pragma: allowlist secret
        change_reason=change_reason,
        site_id=site_id,
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
    return headers


def test_build_supply_hierarchy_tree_pure():
    """Test pure-Python construction of multi-tier supply hierarchy tree and rollups.

    @req:PRD-SYS-184
    @req:PRD-SCM-001
    """
    mfg = SupplyNode(
        study_id="STUDY-001",
        node_id="MFG-01",
        node_type="PRIMARY_MANUFACTURER",
        name="Global Pharma Plant",
        country="USA",
        storage_capacity=50000,
        current_utilization=12000,
        lead_time_days=10,
    )
    central = SupplyNode(
        study_id="STUDY-001",
        node_id="DEPOT-CENTRAL",
        node_type="CENTRAL_DEPOT",
        name="Central Global Depot",
        country="USA",
        parent_node_id="MFG-01",
        storage_capacity=20000,
        current_utilization=8000,
        lead_time_days=5,
    )
    regional_eu = SupplyNode(
        study_id="STUDY-001",
        node_id="DEPOT-EU",
        node_type="REGIONAL_DEPOT",
        name="EU Regional Depot",
        country="BEL",
        parent_node_id="DEPOT-CENTRAL",
        storage_capacity=5000,
        current_utilization=1500,
        lead_time_days=3,
    )
    site_101 = SupplyNode(
        study_id="STUDY-001",
        node_id="SITE-101",
        node_type="CLINICAL_SITE",
        name="Brussels University Hospital",
        country="BEL",
        parent_node_id="DEPOT-EU",
        storage_capacity=200,
        current_utilization=40,
        lead_time_days=1,
    )

    inv_mfg = SupplyNodeInventory(
        study_id="STUDY-001",
        node_id="MFG-01",
        kit_type="KIT-A",
        on_hand_qty=12000,
        reserved_qty=2000,
        in_transit_qty=0,
    )
    inv_central = SupplyNodeInventory(
        study_id="STUDY-001",
        node_id="DEPOT-CENTRAL",
        kit_type="KIT-A",
        on_hand_qty=8000,
        reserved_qty=1000,
        in_transit_qty=2000,
    )
    inv_eu = SupplyNodeInventory(
        study_id="STUDY-001",
        node_id="DEPOT-EU",
        kit_type="KIT-A",
        on_hand_qty=1500,
        reserved_qty=200,
        in_transit_qty=500,
    )
    inv_site = SupplyNodeInventory(
        study_id="STUDY-001",
        node_id="SITE-101",
        kit_type="KIT-A",
        on_hand_qty=40,
        reserved_qty=10,
        in_transit_qty=50,
    )

    rollup = build_supply_hierarchy_tree(
        nodes=[mfg, central, regional_eu, site_101],
        inventories=[inv_mfg, inv_central, inv_eu, inv_site],
    )

    assert rollup.study_id == "STUDY-001"
    assert rollup.total_nodes_count == 4
    assert rollup.manufacturers_count == 1
    assert rollup.central_depots_count == 1
    assert rollup.regional_depots_count == 1
    assert rollup.clinical_sites_count == 1

    assert rollup.total_global_on_hand_stock == 12000 + 8000 + 1500 + 40
    assert rollup.total_global_in_transit_stock == 0 + 2000 + 500 + 50
    assert rollup.total_global_reserved_stock == 2000 + 1000 + 200 + 10
    assert rollup.total_global_storage_capacity == 50000 + 20000 + 5000 + 200

    # Verify tree hierarchy structure
    assert len(rollup.hierarchy_tree) == 1
    root = rollup.hierarchy_tree[0]
    assert root.node_id == "MFG-01"
    assert len(root.children) == 1

    central_child = root.children[0]
    assert central_child.node_id == "DEPOT-CENTRAL"
    assert len(central_child.children) == 1

    eu_child = central_child.children[0]
    assert eu_child.node_id == "DEPOT-EU"
    assert len(eu_child.children) == 1

    site_child = eu_child.children[0]
    assert site_child.node_id == "SITE-101"
    assert len(site_child.children) == 0


def test_hierarchy_circular_dependency_rejection():
    """Verify that circular parent-child links in supply hierarchy raise InvalidHierarchyError.

    @req:PRD-SYS-184
    @req:PRD-SCM-001
    """
    node_a = SupplyNode(
        study_id="STUDY-001",
        node_id="NODE-A",
        node_type="CENTRAL_DEPOT",
        name="Depot A",
        country="USA",
        parent_node_id="NODE-B",
        storage_capacity=1000,
    )
    node_b = SupplyNode(
        study_id="STUDY-001",
        node_id="NODE-B",
        node_type="REGIONAL_DEPOT",
        name="Depot B",
        country="USA",
        parent_node_id="NODE-A",
        storage_capacity=1000,
    )

    with pytest.raises(InvalidHierarchyError, match="Circular dependency detected"):
        build_supply_hierarchy_tree(nodes=[node_a, node_b])


@pytest.mark.asyncio
async def test_supply_node_endpoints_and_audit():
    """Verify REST API registration of supply nodes, retrieval of hierarchy, and GxP audit trail.

    @req:PRD-SYS-184
    @req:PRD-SCM-001
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_gateway_headers(roles="sponsor_admin")

        # 1. Register Manufacturer
        mfg_payload = {
            "study_id": "STUDY-SCM",
            "node_id": "MFG-01",
            "node_type": "PRIMARY_MANUFACTURER",
            "name": "Global Manufacturer Facility",
            "country": "USA",
            "storage_capacity": 50000,
            "lead_time_days": 7,
            "temperature_conditions": ["CONTROLLED_ROOM_TEMP", "REFRIGERATED_2_TO_8"],
        }
        res1 = await client.post(
            "/api/v1/execution/rtsm/nodes", json=mfg_payload, headers=headers
        )
        assert res1.status_code == 201
        data1 = res1.json()
        assert data1["node_id"] == "MFG-01"
        assert data1["node_type"] == "PRIMARY_MANUFACTURER"

        # 2. Register Site attached to Manufacturer
        site_payload = {
            "study_id": "STUDY-SCM",
            "node_id": "SITE-201",
            "node_type": "CLINICAL_SITE",
            "name": "Site 201 Oncology Clinic",
            "country": "USA",
            "parent_node_id": "MFG-01",
            "storage_capacity": 300,
            "lead_time_days": 2,
            "temperature_conditions": ["REFRIGERATED_2_TO_8"],
        }
        res2 = await client.post(
            "/api/v1/execution/rtsm/nodes", json=site_payload, headers=headers
        )
        assert res2.status_code == 201

        # 3. Adjust Inventory for Site
        inv_payload = {
            "study_id": "STUDY-SCM",
            "node_id": "SITE-201",
            "kit_type": "IP-DRUG-BLINDED",
            "on_hand_qty": 25,
            "safety_stock_threshold": 8,
            "reorder_point": 15,
            "max_capacity": 300,
        }
        res3 = await client.post(
            "/api/v1/execution/rtsm/nodes/SITE-201/inventory/adjust",
            json=inv_payload,
            headers=headers,
        )
        assert res3.status_code == 200
        inv_data = res3.json()
        assert inv_data["on_hand_qty"] == 25
        assert inv_data["resupply_signal"] is False

        # 4. Get Hierarchy
        res_hier = await client.get(
            "/api/v1/execution/rtsm/hierarchy?study_id=STUDY-SCM", headers=headers
        )
        assert res_hier.status_code == 200
        hier_data = res_hier.json()
        assert hier_data["total_nodes_count"] == 2
        assert hier_data["total_global_on_hand_stock"] == 25
        assert len(hier_data["hierarchy_tree"]) == 1
        assert hier_data["hierarchy_tree"][0]["node_id"] == "MFG-01"
        assert hier_data["hierarchy_tree"][0]["children"][0]["node_id"] == "SITE-201"

    # Verify GxP audit log creation
    async with db_manager.get_session_maker()() as session:
        stmt_audit = select(AuditLog).where(AuditLog.table_name == "supply_nodes")
        res_audit = await session.execute(stmt_audit)
        audit_logs = res_audit.scalars().all()
        assert len(audit_logs) >= 2
        for log in audit_logs:
            assert log.action in ("INSERT", "UPDATE")
            assert "treatment_arm" not in (log.new_values or {})
