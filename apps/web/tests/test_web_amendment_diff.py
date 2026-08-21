"""Web contract and integration test suite for Semantic Protocol Amendment Diffing & USDM Branching.

Validates the full API communication lifecycle consumed by apps/web/src/views/AmendmentDiffView.vue.
Requirements: PRD-SYS-001, PRD-SUB-007
"""

import hashlib
import hmac
import json
import time
from collections.abc import AsyncGenerator

import httpx
import pytest
import pytest_asyncio

from apps.designer.db import (
    MOCK_STUDIES,
    MOCK_STUDY_VERSIONS,
)
from apps.designer.main import app as designer_app
from apps.execution.database.core import db_manager
from apps.execution.database.models import Base
from apps.execution.main import app as execution_app

GATEWAY_SECRET = "internal-gateway-secret-12345"  # pragma: allowlist secret


@pytest_asyncio.fixture(autouse=True)
async def setup_test_db() -> AsyncGenerator[None]:
    """Setup in-memory SQLite database before each test."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:")
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_auth_headers(
    user_id: str = "sponsor_designer_01",
    roles: str = "STUDY_DESIGNER",
    change_reason: str = "Semantic Diff Verification for Web View",
) -> dict[str, str]:
    timestamp = str(time.time())
    payload = {
        "change_reason": change_reason,
        "roles": roles,
        "timestamp": timestamp,
        "user_id": user_id,
    }
    serialized = json.dumps(payload, sort_keys=True, separators=(",", ":"))
    signature = hmac.new(
        GATEWAY_SECRET.encode(), serialized.encode(), hashlib.sha256
    ).hexdigest()
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }


@pytest.mark.asyncio
async def test_web_amendment_branching_and_diff_contracts() -> None:
    """Validate web client interaction with Designer API for branching and multi-layer diffing.

    @req:PRD-SYS-001, PRD-SUB-007
    """
    study_id = "CADENCE-101"
    MOCK_STUDY_VERSIONS[study_id] = [
        {
            "id": f"{study_id}_1.0.0",
            "version_tag": "1.0.0",
            "status": "APPROVED",
            "version_index": 1,
            "created_by": "sponsor_user",
        }
    ]
    MOCK_STUDIES[study_id] = {
        "study_id": study_id,
        "title": "CADENCE-101 Phase 2 Protocol",
        "current_version": "1.0.0",
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=designer_app), base_url="http://test"
    ) as client:
        # 1. Branch an amendment
        branch_res = await client.post(
            "/api/v1/designer/amendments/branch",
            json={
                "study_id": study_id,
                "base_version_tag": "1.0.0",
                "amendment_type": "major",
                "requires_reconsent": True,
                "change_reason": "CADENCE-101 Amendment 1 adding optional biomarker encounter",
                "branch_name": "amendment-v2.0.0-draft",
            },
            headers=get_auth_headers(),
        )
        assert branch_res.status_code == 201
        branch_data = branch_res.json()
        assert branch_data["new_version_tag"] == "2.0.0"
        assert branch_data["status"] == "DRAFT_AMENDMENT"
        assert branch_data["requires_reconsent"] is True

        # 2. Fetch Multi-Layer Semantic Diff
        diff_res = await client.post(
            "/api/v1/designer/amendments/diff",
            json={
                "study_id": study_id,
                "base_version_tag": "1.0.0",
                "amended_version_tag": "2.0.0",
            },
            headers=get_auth_headers(),
        )
        assert diff_res.status_code == 200
        diff_data = diff_res.json()

        # Validate SoA diff has Added, Modified, and Preserved tokens
        soa_diffs = diff_data["soa_matrix_diffs"]
        change_types = {d["change_type"] for d in soa_diffs}
        assert "ADDED" in change_types
        assert "MODIFIED" in change_types
        assert "PRESERVED" in change_types

        # Verify added encounter has visual delta note
        added_enc = next(
            d
            for d in soa_diffs
            if d["change_type"] == "ADDED" and d["entity_type"] == "Encounter"
        )
        assert "Visit 3.5" in added_enc["name"]
        assert added_enc["delta_note"] is not None

        # Verify Impact Summary
        impact = diff_data["impact_summary"]
        assert impact["burden_delta"] > 0.0
        assert impact["affected_visits_count"] > 0
        assert impact["is_substantial"] is True
        assert impact["requires_reconsent"] is True
        assert impact["schema_revisions"]["encounters"]["added"] > 0


@pytest.mark.asyncio
async def test_web_execution_reconsent_gating_and_subject_impact() -> None:
    """Validate downstream execution subject impact and re-consent gating for AmendmentDiffView.

    @req:PRD-SYS-001, PRD-SUB-007
    """
    study_id = "CADENCE-101"

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=execution_app), base_url="http://test"
    ) as client:
        # Publish amendment to execution service
        pub_res = await client.post(
            "/api/v1/execution/amendments/publish",
            json={
                "study_id": study_id,
                "version_number": "2.0.0",
                "description": "Publishing CADENCE-101 Amendment 2.0 with re-consent gating",
                "baseline_snapshot": {"version": "1.0.0", "activities": []},
                "amended_snapshot": {
                    "version": "2.0.0",
                    "activities": [{"id": "act_pk", "name": "PK Blood Draw"}],
                },
            },
            headers=get_auth_headers(roles="SYSTEM_ADMIN"),
        )
        assert pub_res.status_code == 200
        pub_data = pub_res.json()
        assert pub_data["version_number"] == "2.0.0"
        assert pub_data["added_activities_count"] == 1

        # Query subject impact analysis
        impact_res = await client.get(
            f"/api/v1/execution/amendments/{study_id}/subject-impact?target_version=2.0.0",
            headers=get_auth_headers(roles="STUDY_DESIGNER"),
        )
        assert impact_res.status_code == 200
        impact_data = impact_res.json()
        assert "categories" in impact_data
        assert "migrated_and_reconsented" in impact_data["categories"]
        assert "pending_reconsent" in impact_data["categories"]
        assert "completed_under_previous_version" in impact_data["categories"]
