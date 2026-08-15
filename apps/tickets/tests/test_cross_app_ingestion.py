"""
Tests for automated Cross-App ticket ingestion in apps/tickets.
"""

import time

import pytest
import pytest_asyncio
from httpx import ASGITransport, AsyncClient
from sqlalchemy import select

from apps.tickets.adapters.database import db_manager
from apps.tickets.adapters.models import (
    Base,
    TicketAuditLog,
)
from apps.tickets.main import app
from packages.testing.security import generate_signature


@pytest_asyncio.fixture(autouse=True)
async def setup_tickets_db():
    """Setup in-memory Tickets database for unit testing."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    if db_manager.engine is not None:
        async with db_manager.engine.begin() as conn:
            await conn.run_sync(Base.metadata.drop_all)
        await db_manager.close()


def get_auth_headers(
    roles: str = "admin",
    change_reason: str = "Automated cross-service ingestion",
    site_id: str | None = None,
    user_id: str = "service_execution",
) -> dict:
    timestamp = str(time.time())
    sig = generate_signature(
        user_id,
        roles,
        timestamp,
        version="2",
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


@pytest.mark.asyncio
async def test_cross_app_ticket_ingestion_from_execution_edc():
    """
    Validate that sibling microservices (e.g. apps/execution) can trigger automated
    cross-app ticket creation for clinical data discrepancies.

    @req:PRD-SYS-042
    @req:PRD-TCK-003
    """
    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        headers = get_auth_headers(
            roles="data_manager,admin",
            change_reason="Automated rule firing detected abnormal lab value outside range",
            site_id="SITE-101",
            user_id="service_execution",
        )
        payload = {
            "event_type": "DATA_DISCREPANCY",
            "title": "Lab Discrepancy: Potassium Value Out of Range",
            "description": "Subject SUBJ-101 Visit V1 potassium value 6.8 mmol/L exceeds critical threshold.",
            "category": "DATA_QUERY",
            "priority": "HIGH",
            "gxp_severity": "MAJOR",
            "source_service": "execution",
            "study_id": "STUDY-001",
            "site_id": "SITE-101",
            "related_entity_type": "ClinicalObservation",
            "related_entity_id": "OBS-9942",
            "context_payload": {
                "subject_id": "SUBJ-101",
                "visit_id": "V1",
                "form_id": "LAB_CHEM_01",
                "parameter": "K+",
                "value": 6.8,
                "units": "mmol/L",
            },
        }

        response = await client.post(
            "/api/v1/tickets/cross-app/events",
            json=payload,
            headers=headers,
        )

        assert response.status_code == 201
        data = response.json()
        assert data["reference"].startswith("TKT-")
        assert data["title"] == "Lab Discrepancy: Potassium Value Out of Range"
        assert data["category"] == "DATA_QUERY"
        assert data["gxp_severity"] == "MAJOR"
        assert data["related_entity_type"] == "ClinicalObservation"
        assert data["related_entity_id"] == "OBS-9942"
        assert data["assignee_role"] == "execution_lead"
        assert data["sla_target_at"] is not None

        # Verify Part 11 audit trail record
        async with db_manager.get_session_maker()() as session:
            stmt = select(TicketAuditLog).where(TicketAuditLog.ticket_id == data["id"])
            res = await session.execute(stmt)
            logs = res.scalars().all()
            assert len(logs) >= 1
            assert logs[0].action == "TICKET_CROSS_APP_CREATE"
            assert "OBS-9942" in logs[0].details
