"""Unit and integration tests for Predictive Resupply Demand Forecasting.

Requirements: PRD-SCM-001, GMP Annex 13
"""

import os
import time
from datetime import UTC, datetime

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    Base,
    ClinicalSubject,
    PredictiveResupplyForecast,
    SupplyNode,
    SupplyNodeInventory,
)
from apps.execution.domain.rtsm_models import (
    PredictiveForecastInput,
    StockoutRiskLevel,
)
from apps.execution.main import app
from apps.execution.rtsm_supply import (
    calculate_predictive_forecast,
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
    user_id="test_crc",
    roles="crc",
    change_reason="Predictive forecasting request",
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


def test_predictive_forecast_pure_calculation():
    """Test pure-Python simulation of visit demand, screening conversion, and lead time buffer.

    @req:PRD-SCM-001
    """
    input_params = PredictiveForecastInput(
        study_id="STUDY-001",
        site_id="SITE-101",
        kit_type="KIT-A",
        horizon_days=60,
        enrollment_velocity_weekly=1.4,  # ~0.2 subjects/day -> ~12 new subjects in 60d
        screening_pipeline_count=4,  # 4 in screening, 75% conversion -> 3 converted subjects
        screening_conversion_rate=0.75,
        lead_time_days=4,
        buffer_days=3,
        safety_stock_threshold=6,
        storage_capacity=200,
    )

    # 3 active enrolled subjects with various enrollment durations
    active_subjects = [
        {
            "subject_id": "SUBJ-01",
            "days_enrolled": 10,
        },  # Visit at day 4 (14 - 10), day 18 (28 - 10)...
        {
            "subject_id": "SUBJ-02",
            "days_enrolled": 25,
        },  # Visit at day 3 (28 - 25), day 31 (56 - 25)...
        {
            "subject_id": "SUBJ-03",
            "days_enrolled": 50,
        },  # Visit at day 6 (56 - 50), day 34 (84 - 50)...
    ]

    now = datetime(2026, 9, 1, 12, 0, 0, tzinfo=UTC)

    # Scenario A: Low stock -> Critical Risk
    res_critical = calculate_predictive_forecast(
        input_params=input_params,
        active_subjects=active_subjects,
        current_on_hand_qty=2,
        current_reserved_qty=0,
        current_in_transit_qty=0,
        start_date=now,
    )
    assert res_critical.stockout_risk_level == StockoutRiskLevel.CRITICAL
    assert res_critical.reorder_recommended is True
    assert res_critical.recommended_order_qty > 0
    assert len(res_critical.timeline) == 60

    # Scenario B: High stock -> Low Risk
    res_low = calculate_predictive_forecast(
        input_params=input_params,
        active_subjects=active_subjects,
        current_on_hand_qty=80,
        current_reserved_qty=5,
        current_in_transit_qty=20,
        start_date=now,
    )
    assert res_low.stockout_risk_level == StockoutRiskLevel.LOW
    assert res_low.recommended_order_qty == 0


def test_forecast_storage_capacity_bounding():
    """Verify that recommended order quantities are strictly bounded by site storage capacity.

    @req:PRD-SCM-001
    """
    input_params = PredictiveForecastInput(
        study_id="STUDY-001",
        site_id="SITE-TINY",
        kit_type="KIT-A",
        horizon_days=30,
        enrollment_velocity_weekly=10.0,  # massive enrollment surge
        screening_pipeline_count=20,
        lead_time_days=3,
        buffer_days=3,
        safety_stock_threshold=10,
        storage_capacity=30,  # Small storage capacity of 30 kits
    )

    res = calculate_predictive_forecast(
        input_params=input_params,
        active_subjects=[],
        current_on_hand_qty=10,
        current_reserved_qty=0,
        current_in_transit_qty=0,
    )

    # Allowable order cannot exceed storage capacity (30 - 10 on-hand = max 20)
    assert res.recommended_order_qty <= 20


@pytest.mark.asyncio
async def test_predictive_forecast_endpoint_and_persistence():
    """Verify predictive forecast endpoint queries active subjects, computes timeline, and saves record.

    @req:PRD-SCM-001
    """
    # Seed site node and active subjects
    async with db_manager.get_session_maker()() as session:
        site = SupplyNode(
            study_id="STUDY-FC",
            node_id="SITE-FC-1",
            node_type="CLINICAL_SITE",
            name="Boston Research Site",
            country="USA",
            storage_capacity=400,
            lead_time_days=3,
        )
        inv = SupplyNodeInventory(
            study_id="STUDY-FC",
            node_id="SITE-FC-1",
            kit_type="KIT-1",
            on_hand_qty=3,
            safety_stock_threshold=5,
            reorder_point=12,
            max_capacity=400,
        )
        sub1 = ClinicalSubject(
            study_id="STUDY-FC",
            site_id="SITE-FC-1",
            subject_id="SUBJ-FC-01",
            status="SCREENING",
        )
        sub1.status = "ENROLLED"
        sub1.status = "RANDOMIZED"
        sub1.status = "ACTIVE"
        sub2 = ClinicalSubject(
            study_id="STUDY-FC",
            site_id="SITE-FC-1",
            subject_id="SUBJ-FC-02",
            status="SCREENING",
        )
        session.add_all([site, inv, sub1, sub2])
        await session.commit()

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_gateway_headers(roles="cra", site_id="SITE-FC-1")

        req_payload = {
            "study_id": "STUDY-FC",
            "site_id": "SITE-FC-1",
            "kit_type": "KIT-1",
            "horizon_days": 45,
            "enrollment_velocity_weekly": 0.7,
            "screening_pipeline_count": 2,
            "screening_conversion_rate": 0.8,
            "lead_time_days": 3,
            "buffer_days": 2,
            "safety_stock_threshold": 5,
            "storage_capacity": 400,
        }

        response = await client.post(
            "/api/v1/execution/rtsm/forecast/site", json=req_payload, headers=headers
        )
        if response.status_code != 200:
            print("ERROR RESPONSE:", response.status_code, response.text)
        assert response.status_code == 200
        data = response.json()
        assert data["study_id"] == "STUDY-FC"
        assert data["site_id"] == "SITE-FC-1"
        assert data["active_subjects_count"] == 1
        assert data["screening_pipeline_count"] == 2
        assert len(data["timeline"]) == 45
        assert data["reorder_recommended"] is True

    # Verify forecast record persisted in database
    async with db_manager.get_session_maker()() as session:
        stmt = select(PredictiveResupplyForecast).where(
            PredictiveResupplyForecast.study_id == "STUDY-FC",
            PredictiveResupplyForecast.site_id == "SITE-FC-1",
        )
        res = await session.execute(stmt)
        persisted = res.scalars().first()
        assert persisted is not None
        assert persisted.horizon_days == 45
        assert persisted.projected_demand_qty > 0
        assert persisted.forecast_details is not None
