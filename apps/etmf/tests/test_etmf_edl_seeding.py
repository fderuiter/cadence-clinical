"""Automated Test Suite for eTMF Expected Document List (EDL) Seeding.

Validates automated pre-seeding of mandatory DIA TMF Reference Model (Zones 1-11)
expected documents across trial lifecycle milestones:
- STUDY_INITIATION
- ETHICS_SUBMISSION
- SITE_ACTIVATION
- FSI
- INITIATION, CONDUCT, CLOSEOUT (compatibility)

Requirements: PRD-TMF-001, PRD-EDL-001, PRD-SYS-001, Trace-4
"""

from __future__ import annotations

from typing import Any

import pytest
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.etmf.adapters.database import db_manager
from apps.etmf.adapters.ingestion_service import (
    seed_etmf_expected_documents_for_study,
)
from apps.etmf.adapters.models import (
    Base,
    ExpectedDocument,
)
from apps.etmf.adapters.repositories import SQLETMFRepository
from apps.etmf.domain.tmf_reference_model import (
    normalize_milestone,
)
from apps.etmf.main import app
from apps.etmf.tests.test_etmf import get_auth_headers


@pytest.fixture(autouse=True)
def setup_db():
    """Setup isolated in-memory database for testing."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    import asyncio

    async def create_all():
        async with db_manager.engine.begin() as conn:
            await conn.run_sync(Base.metadata.create_all)

    asyncio.run(create_all())
    yield

    async def drop_all():
        async with db_manager.engine.begin() as conn:
            await conn.run_sync(Base.metadata.drop_all)

    asyncio.run(drop_all())
    asyncio.run(db_manager.close())


@pytest.mark.asyncio
async def test_offline_in_memory_edl_seeding_all_zones() -> None:
    """Validate offline in-memory EDL seeding populating DIA TMF Reference Model Zones 1-11.

    @req:PRD-TMF-001
    @req:PRD-EDL-001
    """
    study_id = "study_offline_edl_001"
    created_by = "lead_designer_001"
    reason = "Automated Zero-Click USDM Protocol Ingestion"

    seeded = await seed_etmf_expected_documents_for_study(
        study_id=study_id,
        db_session=None,
        created_by=created_by,
        reason_for_change=reason,
    )

    assert isinstance(seeded, list)
    assert len(seeded) > 0

    # Collect zones and milestones from seeded results
    zones_present = {doc["zone"] for doc in seeded if doc.get("zone") is not None}
    milestones_present = {doc["milestone"] for doc in seeded}

    # Verify milestones
    assert "STUDY_INITIATION" in milestones_present
    assert "ETHICS_SUBMISSION" in milestones_present
    assert "SITE_ACTIVATION" in milestones_present
    assert "FSI" in milestones_present

    # Verify DIA TMF Zones 1-11 are covered
    # Specifically Zones 1, 2, 4, 5, 6, 7, 8, 10, 11 (plus 3, 9)
    assert 1 in zones_present  # Trial Management
    assert 2 in zones_present  # Central Trial Documents
    assert 3 in zones_present  # Regulatory
    assert 4 in zones_present  # IRB/IEC & other Approvals
    assert 5 in zones_present  # Site Management
    assert 6 in zones_present  # IP & Trial Supplies
    assert 7 in zones_present  # Safety Reporting
    assert 8 in zones_present  # Centralized & Local Testing
    assert 9 in zones_present  # Third Parties
    assert 10 in zones_present  # Data Management
    assert 11 in zones_present  # Statistics

    # Validate GxP audit fields
    for doc in seeded:
        assert doc["study_id"] == study_id
        assert doc["created_by"] == created_by
        assert doc["reason_for_change"] == reason
        assert doc["version_index"] == 1
        assert "metadata_json" in doc
        assert doc["metadata_json"]["default_seeded"] is True


@pytest.mark.asyncio
async def test_live_async_session_edl_seeding_and_idempotency() -> None:
    """Validate persistence and strict idempotency under live SQLAlchemy AsyncSession.

    @req:PRD-TMF-001
    @req:Trace-4
    """
    study_id = "study_async_idempotent_001"
    session_maker = db_manager.get_session_maker()

    async with session_maker() as session:
        # First execution: seeds initial expected documents
        results_pass1 = await seed_etmf_expected_documents_for_study(
            study_id=study_id,
            db_session=session,
            created_by="system_admin",
            reason_for_change="Initial zero-click study setup",
        )
        await session.commit()

        count_pass1 = len(results_pass1)
        assert count_pass1 >= 20

        # Query database to confirm row count
        stmt = select(ExpectedDocument).where(ExpectedDocument.study_id == study_id)
        res = await session.execute(stmt)
        db_rows = res.scalars().all()
        assert len(db_rows) == count_pass1

        # Second execution: idempotency check (must not create duplicate rows)
        results_pass2 = await seed_etmf_expected_documents_for_study(
            study_id=study_id,
            db_session=session,
            created_by="system_admin",
            reason_for_change="Duplicate trigger attempt",
        )
        await session.commit()

        # Row count in DB must remain identical
        res2 = await session.execute(stmt)
        db_rows_after = res2.scalars().all()
        assert len(db_rows_after) == count_pass1

        # Results returned must match initial IDs
        assert len(results_pass2) == count_pass1
        pass1_ids = {d["id"] for d in results_pass1}
        pass2_ids = {d["id"] for d in results_pass2}
        assert pass1_ids == pass2_ids


@pytest.mark.asyncio
async def test_custom_milestones_and_backward_compatibility() -> None:
    """Validate seeding with specific requested milestones including legacy INITIATION/CONDUCT/CLOSEOUT.

    @req:PRD-TMF-001
    """
    study_id = "study_custom_milestones_001"

    # 1. Custom subset: STUDY_INITIATION only
    subset = await seed_etmf_expected_documents_for_study(
        study_id=study_id,
        db_session=None,
        milestones=["STUDY_INITIATION"],
    )
    assert len(subset) > 0
    assert all(d["milestone"] == "STUDY_INITIATION" for d in subset)

    # 2. Legacy milestones: INITIATION, CONDUCT, CLOSEOUT
    legacy = await seed_etmf_expected_documents_for_study(
        study_id=study_id,
        db_session=None,
        milestones=["INITIATION", "CONDUCT", "CLOSEOUT"],
    )
    legacy_milestones = {d["milestone"] for d in legacy}
    assert legacy_milestones == {"INITIATION", "CONDUCT", "CLOSEOUT"}

    # 3. Milestone normalization
    assert normalize_milestone("study initiation") == "STUDY_INITIATION"
    assert normalize_milestone("ethics-submission") == "ETHICS_SUBMISSION"
    assert normalize_milestone("first subject in") == "FSI"
    assert normalize_milestone("STUDY START") == "INITIATION"


@pytest.mark.asyncio
async def test_repository_integration_edl_seeding() -> None:
    """Validate EDL seeding when passing an SQLETMFRepository instance.

    @req:PRD-TMF-001
    """
    study_id = "study_repo_seed_001"
    session_maker = db_manager.get_session_maker()

    async with session_maker() as session:
        repo = SQLETMFRepository(session=session)
        seeded = await seed_etmf_expected_documents_for_study(
            study_id=study_id,
            db_session=repo,
            created_by="repo_tester",
            reason_for_change="Repository integration test",
        )
        await session.commit()

        assert len(seeded) > 0
        repo_docs = await repo.get_expected_documents_by_study(study_id)
        assert len(repo_docs) == len(seeded)


def test_seed_edl_rest_endpoint() -> None:
    """Validate POST /api/v1/etmf/studies/{study_id}/seed-edl REST API endpoint.

    @req:PRD-TMF-001
    @req:PRD-EDL-001
    """
    client = TestClient(app)
    study_id = "study_api_seed_001"
    admin_headers = get_auth_headers(
        roles="admin", change_reason="Zero-Click USDM Study Ingestion"
    )
    inspector_headers = get_auth_headers(
        roles="regulatory_inspector", change_reason="Attempt write"
    )

    # 1. Inspector forbidden (read-only)
    resp_inspector = client.post(
        f"/api/v1/etmf/studies/{study_id}/seed-edl",
        json={"reason_for_change": "Unauthorized seed attempt"},
        headers=inspector_headers,
    )
    assert resp_inspector.status_code == 403

    # 2. Admin success
    payload: dict[str, Any] = {
        "milestones": ["STUDY_INITIATION", "SITE_ACTIVATION"],
        "reason_for_change": "Zero-Click USDM Protocol Ingestion",
    }
    resp_admin = client.post(
        f"/api/v1/etmf/studies/{study_id}/seed-edl",
        json=payload,
        headers=admin_headers,
    )
    assert resp_admin.status_code == 201
    data = resp_admin.json()
    assert isinstance(data, list)
    assert len(data) > 0

    milestones_returned = {item["milestone"] for item in data}
    assert "STUDY_INITIATION" in milestones_returned
    assert "SITE_ACTIVATION" in milestones_returned

    # 3. Verify audit log entry
    audit_resp = client.get("/api/v1/etmf/audit-logs", headers=inspector_headers)
    assert audit_resp.status_code == 200
    logs = audit_resp.json()["items"]
    assert any(log["action"] == "EDL_UPDATE" for log in logs)
