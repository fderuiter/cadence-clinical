"""
Tests for Clinical KPI and KRI analytics calculations in apps/tickets.
"""

import time

import pytest
import pytest_asyncio
from httpx import ASGITransport, AsyncClient

from apps.tickets.adapters.database import db_manager
from apps.tickets.adapters.models import Base
from apps.tickets.main import app
from packages.testing.security import generate_signature


@pytest_asyncio.fixture(autouse=True)
async def setup_tickets_db():
    """Setup in-memory Tickets database."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    if db_manager.engine is not None:
        async with db_manager.engine.begin() as conn:
            await conn.run_sync(Base.metadata.drop_all)
        await db_manager.close()


def get_auth_headers(
    roles: str = "sponsor_admin,data_manager",
    change_reason: str = "Analytics test verification",
    user_id: str = "analytics_lead",
) -> dict:
    timestamp = str(time.time())
    sig = generate_signature(
        user_id,
        roles,
        timestamp,
        version="2",
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
async def test_kpi_and_kri_metrics_computation():
    """
    Validate real-time clinical KPI / KRI calculations:
    active tickets, critical deviation count, resolution velocity, and distributions.
    """
    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        headers = get_auth_headers()

        # 1. Create a Critical Protocol Deviation
        await client.post(
            "/api/v1/tickets",
            json={
                "title": "Critical Protocol Deviation",
                "description": "Patient enrolled without meeting inclusion criterion #3.",
                "category": "PROTOCOL_DEVIATION",
                "priority": "CRITICAL",
                "gxp_severity": "CRITICAL",
                "site_id": "SITE-101",
                "study_id": "STUDY-001",
            },
            headers=headers,
        )

        # 2. Create a Standard Data Query
        await client.post(
            "/api/v1/tickets",
            json={
                "title": "Missing Vital Signs Time",
                "description": "Blood pressure recorded without timestamp.",
                "category": "DATA_QUERY",
                "priority": "LOW",
                "gxp_severity": "MINOR",
                "site_id": "SITE-102",
                "study_id": "STUDY-001",
            },
            headers=headers,
        )

        # 3. Query KPI Summary
        res_kpi = await client.get(
            "/api/v1/tickets/analytics/kpi",
            headers=headers,
        )
        assert res_kpi.status_code == 200
        data = res_kpi.json()

        assert data["total_tickets"] == 2
        assert data["active_tickets"] == 2
        assert data["critical_deviations"] == 1
        assert data["category_distribution"]["PROTOCOL_DEVIATION"] == 1
        assert data["category_distribution"]["DATA_QUERY"] == 1
        assert data["severity_distribution"]["CRITICAL"] == 1
        assert data["severity_distribution"]["MINOR"] == 1
        assert "SITE-101" in data["site_distribution"]
        assert "SITE-102" in data["site_distribution"]
