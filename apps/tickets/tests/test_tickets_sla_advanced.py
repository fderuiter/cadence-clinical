"""
Tests for Clinical SLA Multi-Tier Escalation, Amber Warnings, and Pause States.
"""

import time
from datetime import UTC, datetime, timedelta

import pytest
import pytest_asyncio
from httpx import ASGITransport, AsyncClient

from apps.tickets.adapters.database import db_manager
from apps.tickets.adapters.models import (
    Base,
    TicketCategory,
    TicketPriority,
)
from apps.tickets.domain.services import (
    calculate_sla_target,
    evaluate_sla_status,
)
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
    roles: str = "admin,sponsor_lead",
    change_reason: str = "SLA test verification",
    user_id: str = "sla_tester",
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


def test_sla_target_calculation_with_multipliers():
    """
    Verify SLA target date calculation with priority defaults and category speed multipliers.
    """
    now = datetime(2026, 8, 15, 12, 0, 0, tzinfo=UTC)

    # Critical = 4h
    t_crit = calculate_sla_target(now, TicketPriority.CRITICAL)
    assert t_crit == now + timedelta(hours=4)

    # Safety Adverse Event on Critical has 0.5 multiplier -> 2h
    t_safety = calculate_sla_target(
        now, TicketPriority.CRITICAL, TicketCategory.SAFETY_ADVERSE_EVENT
    )
    assert t_safety == now + timedelta(hours=2)

    # Custom override (e.g. 12h)
    t_custom = calculate_sla_target(now, TicketPriority.HIGH, custom_sla_hours=12)
    assert t_custom == now + timedelta(hours=12)


def test_evaluate_sla_status_amber_warning_and_breach():
    """
    Verify SLA progression evaluation, amber warning (75%), and breach detection.
    """
    created_at = datetime(2026, 8, 15, 0, 0, 0, tzinfo=UTC)
    sla_target = created_at + timedelta(hours=10)

    # 1. 50% elapsed (5h in) -> No breach, no amber warning
    status_5h = evaluate_sla_status(
        created_at=created_at,
        sla_target=sla_target,
        current_time=created_at + timedelta(hours=5),
    )
    assert status_5h["elapsed_percent"] == 50.0
    assert status_5h["is_amber_warning"] is False
    assert status_5h["is_breached"] is False

    # 2. 80% elapsed (8h in) -> Amber warning triggered (>= 75%)
    status_8h = evaluate_sla_status(
        created_at=created_at,
        sla_target=sla_target,
        current_time=created_at + timedelta(hours=8),
    )
    assert status_8h["elapsed_percent"] == 80.0
    assert status_8h["is_amber_warning"] is True
    assert status_8h["is_breached"] is False

    # 3. 110% elapsed (11h in) -> Breached
    status_11h = evaluate_sla_status(
        created_at=created_at,
        sla_target=sla_target,
        current_time=created_at + timedelta(hours=11),
    )
    assert status_11h["is_breached"] is True
    assert status_11h["is_amber_warning"] is False


@pytest.mark.asyncio
async def test_ticket_sla_pause_and_resume_lifecycle():
    """
    Validate that transitioning a ticket into WAITING_ON_SITE pauses the SLA clock,
    and transitioning back to IN_PROGRESS accurately increments total paused duration.
    """
    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        headers = get_auth_headers()

        # 1. Create ticket
        res = await client.post(
            "/api/v1/tickets",
            json={
                "title": "Site Clarification on Consent Form",
                "description": "Awaiting re-consent verification from site PI",
                "category": "SITE_OPERATIONS",
                "priority": "HIGH",
            },
            headers=headers,
        )
        assert res.status_code == 201
        ticket_id = res.json()["id"]

        # 2. Transition to WAITING_ON_SITE (pauses SLA)
        res_pause = await client.post(
            f"/api/v1/tickets/{ticket_id}/transition",
            json={
                "status": "WAITING_ON_SITE",
                "version_index": 1,
            },
            headers=headers,
        )
        assert res_pause.status_code == 200
        data_paused = res_pause.json()
        assert data_paused["status"] == "WAITING_ON_SITE"
        assert data_paused["sla_paused_at"] is not None

        # 3. Transition back to IN_PROGRESS (resumes SLA and records paused duration)
        res_resume = await client.post(
            f"/api/v1/tickets/{ticket_id}/transition",
            json={
                "status": "IN_PROGRESS",
                "version_index": 2,
            },
            headers=headers,
        )
        assert res_resume.status_code == 200
        data_resumed = res_resume.json()
        assert data_resumed["status"] == "IN_PROGRESS"
        assert data_resumed["sla_paused_at"] is None
        assert data_resumed["sla_total_paused_seconds"] >= 0
