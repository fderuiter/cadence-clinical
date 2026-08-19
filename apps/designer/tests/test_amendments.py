"""Integration and unit tests for Designer Protocol Amendment Branching and Semantic Diffing.

Requirements: PRD-SYS-001, PRD-SUB-007
"""

import hashlib
import hmac
import json
import time

import httpx
import pytest

from apps.designer.db import (
    MOCK_STUDIES,
    MOCK_STUDY_VERSIONS,
)
from apps.designer.main import app as designer_app

GATEWAY_SECRET = "internal-gateway-secret-12345"  # pragma: allowlist secret


def get_auth_headers(
    user_id: str = "sponsor_designer_01",
    roles: str = "STUDY_DESIGNER",
    change_reason: str = "Protocol Amendment Authoring & Branching",
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
async def test_designer_amendment_branching_success() -> None:
    """Validate immutable protocol amendment branching from an approved baseline version.

    @req:PRD-SYS-001, PRD-SUB-007
    """
    study_id = "test_study_branch_001"
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
        "title": "CADENCE-101 Baseline Protocol",
        "current_version": "1.0.0",
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=designer_app), base_url="http://test"
    ) as client:
        # Branch a major amendment
        response = await client.post(
            "/api/v1/designer/amendments/branch",
            json={
                "study_id": study_id,
                "base_version_tag": "1.0.0",
                "amendment_type": "major",
                "requires_reconsent": True,
                "change_reason": "Introducing Phase 2 biomarker cohort and optional PK visit",
                "branch_name": "amendment-v2.0.0-draft",
            },
            headers=get_auth_headers(),
        )

        assert response.status_code == 201
        data = response.json()
        assert data["study_id"] == study_id
        assert data["base_version_tag"] == "1.0.0"
        assert data["new_version_tag"] == "2.0.0"
        assert data["status"] == "DRAFT_AMENDMENT"
        assert data["requires_reconsent"] is True
        assert data["branch_name"] == "amendment-v2.0.0-draft"
        assert "branch_id" in data
        assert "version_id" in data


@pytest.mark.asyncio
async def test_designer_amendment_branching_unapproved_baseline_rejection() -> None:
    """Validate that branching from a non-approved/draft study version is strictly rejected.

    @req:PRD-SYS-001
    """
    study_id = "test_study_unapproved_002"
    MOCK_STUDY_VERSIONS[study_id] = [
        {
            "id": f"{study_id}_1.0.0",
            "version_tag": "1.0.0",
            "status": "DRAFT",  # Not approved or locked
            "version_index": 1,
            "created_by": "sponsor_user",
        }
    ]

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=designer_app), base_url="http://test"
    ) as client:
        response = await client.post(
            "/api/v1/designer/amendments/branch",
            json={
                "study_id": study_id,
                "base_version_tag": "1.0.0",
                "amendment_type": "minor",
                "requires_reconsent": False,
                "change_reason": "Attempting branch from draft",
            },
            headers=get_auth_headers(),
        )

        assert response.status_code == 409
        assert "IMMUTABILITY_VIOLATION" in response.json()["detail"]


@pytest.mark.asyncio
async def test_designer_semantic_diff_and_impact_summary() -> None:
    """Validate multi-layer semantic diff across USDM graph, SoA matrix, eligibility, and eCRF forms.

    @req:PRD-SYS-001, PRD-SUB-007
    """
    study_id = "test_study_semantic_diff_003"

    base_payload = {
        "id": study_id,
        "name": "CADENCE-101 Baseline",
        "arms": [{"id": "arm_1", "name": "Arm A: Active Dose", "description": "100mg"}],
        "visits": [
            {"id": "v1", "name": "Visit 1: Screening", "schedule": "Day -7"},
            {"id": "v2", "name": "Visit 2: Baseline", "schedule": "Day 1"},
            {"id": "v3", "name": "Visit 3: Treatment Cycle 1", "schedule": "Day 14"},
        ],
        "activities": [
            {
                "id": "act_chem",
                "name": "Standard Safety Chemistry",
                "spec": "CBC + Chem Panel",
            }
        ],
        "eligibilityCriteria": [
            {"id": "crit_01", "text": "Age >= 18"},
            {"id": "crit_02", "text": "Solid tumor diagnosis"},
        ],
        "forms": [{"id": "f_demo", "form_key": "DEMO", "name": "Demographics"}],
    }

    amended_payload = {
        "id": study_id,
        "name": "CADENCE-101 Amendment 1",
        "arms": [{"id": "arm_1", "name": "Arm A: Active Dose", "description": "100mg"}],
        "visits": [
            {"id": "v1", "name": "Visit 1: Screening", "schedule": "Day -7"},
            {"id": "v2", "name": "Visit 2: Baseline", "schedule": "Day 1"},
            {
                "id": "v3",
                "name": "Visit 3: Treatment Cycle 1",
                "schedule": "Day 14",
                "delta_note": "Expanded PK blood draw",
            },  # Modified
            {
                "id": "v3_5",
                "name": "Visit 3.5: Interim PK Assessment",
                "schedule": "Day 21",
                "delta_note": "Added PK encounter",
            },  # Added
        ],
        "activities": [
            {
                "id": "act_chem",
                "name": "Standard Safety Chemistry",
                "spec": "CBC + Chem Panel + Biomarkers",
                "delta_note": "Added high-sensitivity troponin",
            },  # Modified
            {
                "id": "act_pk",
                "name": "PK Blood Draw",
                "spec": "Pharmacokinetics Assay",
            },  # Added
        ],
        "eligibilityCriteria": [
            {"id": "crit_01", "text": "Age >= 18 and Age <= 75"},  # Modified
            {"id": "crit_02", "text": "Solid tumor diagnosis"},
            {"id": "crit_03", "text": "Signed informed consent (v2.0)"},  # Added
        ],
        "forms": [
            {"id": "f_demo", "form_key": "DEMO", "name": "Demographics"},
            {"id": "f_pk", "form_key": "PK_ASSAY", "name": "Pharmacokinetics"},  # Added
        ],
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=designer_app), base_url="http://test"
    ) as client:
        response = await client.post(
            "/api/v1/designer/amendments/diff",
            json={
                "study_id": study_id,
                "base_version_tag": "1.0.0",
                "amended_version_tag": "2.0.0",
                "base_payload": base_payload,
                "draft_payload": amended_payload,
            },
            headers=get_auth_headers(),
        )

        assert response.status_code == 200
        diff_data = response.json()

        # Check SoA and graph diffs
        soa_diffs = diff_data["soa_matrix_diffs"]
        added_encounters = [
            d
            for d in soa_diffs
            if d["change_type"] == "ADDED" and d["entity_type"] == "Encounter"
        ]
        assert len(added_encounters) == 1
        assert added_encounters[0]["name"] == "Visit 3.5: Interim PK Assessment"

        modified_encounters = [
            d
            for d in soa_diffs
            if d["change_type"] == "MODIFIED" and d["entity_type"] == "Encounter"
        ]
        assert len(modified_encounters) == 1
        assert modified_encounters[0]["name"] == "Visit 3: Treatment Cycle 1"

        added_activities = [
            d
            for d in soa_diffs
            if d["change_type"] == "ADDED" and d["entity_type"] == "Activity"
        ]
        assert len(added_activities) == 1
        assert added_activities[0]["name"] == "PK Blood Draw"

        # Check eligibility diffs
        elig_diffs = diff_data["eligibility_diffs"]
        added_crit = [d for d in elig_diffs if d["change_type"] == "ADDED"]
        assert len(added_crit) == 1
        assert added_crit[0]["entity_id"] == "crit_03"

        # Check Impact Summary
        impact = diff_data["impact_summary"]
        assert impact["base_version"] == "1.0.0"
        assert impact["amended_version"] == "2.0.0"
        assert impact["burden_delta"] > 0.0
        assert impact["is_substantial"] is True
        assert impact["requires_reconsent"] is True
        assert "Visit 3.5: Interim PK Assessment" in impact["affected_visits"]
        assert "PK Blood Draw" in impact["affected_activities"]

        # Check Migration Directives
        directives = diff_data["migration_directives"]
        directive_actions = [d["action"] for d in directives]
        assert "RECONSENT_GATE" in directive_actions
        assert "SCHEMA_UPGRADE" in directive_actions
        assert "PRESERVE_HISTORICAL" in directive_actions


@pytest.mark.asyncio
async def test_designer_amendment_impact_endpoint_direct() -> None:
    """Validate standalone POST /api/v1/designer/amendments/impact endpoint.

    @req:PRD-SYS-001
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=designer_app), base_url="http://test"
    ) as client:
        response = await client.post(
            "/api/v1/designer/amendments/impact",
            json={
                "study_id": "CADENCE-101",
                "base_version_tag": "1.0.0",
                "amended_version_tag": "2.0.0",
            },
            headers=get_auth_headers(),
        )

        assert response.status_code == 200
        impact = response.json()
        assert impact["base_version"] == "1.0.0"
        assert impact["amended_version"] == "2.0.0"
        assert impact["affected_visits_count"] > 0
        assert impact["schema_revisions"]["encounters"]["added"] > 0
        assert impact["requires_reconsent"] is True
