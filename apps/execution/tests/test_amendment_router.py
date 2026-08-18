"""Integration test suite for protocol amendment publishing and Summary of Changes REST API.

Requirements: PRD-SYS-001
"""

from fastapi.testclient import TestClient

import packages  # noqa: F401
from apps.execution.main import app
from apps.execution.tests.test_lock_router import _make_auth_headers

client = TestClient(app)


def test_publish_amendment_post_endpoint() -> None:
    """Validate POST /api/v1/execution/amendments/publish publishes amendment and returns summary of changes.

    Requirements: PRD-SYS-001
    """
    headers = _make_auth_headers(
        user_id="designer_01",
        roles="study_designer",
        change_reason="Publish Protocol Amendment v2.0",
    )

    baseline = {
        "version": "1.0",
        "activities": [{"id": "act_01", "name": "Vital Signs"}],
    }
    amended = {
        "version": "2.0",
        "activities": [
            {"id": "act_01", "name": "Vital Signs"},
            {"id": "act_02", "name": "PK Draw"},
        ],
    }

    response = client.post(
        "/api/v1/execution/amendments/publish",
        json={
            "study_id": "study_pub_01",
            "version_number": "2.0",
            "description": "Added PK blood draw sampling at Visit 3",
            "baseline_snapshot": baseline,
            "amended_snapshot": amended,
        },
        headers=headers,
    )

    assert response.status_code == 200
    data = response.json()
    assert data["study_id"] == "study_pub_01"
    assert data["version_number"] == "2.0"
    assert data["added_activities_count"] == 1
    assert "amendment_id" in data


def test_get_amendment_summary_endpoint() -> None:
    """Validate GET /api/v1/execution/amendments/summary/{study_id}/{version} returns Summary of Changes.

    Requirements: PRD-SYS-001
    """
    headers = _make_auth_headers()

    response = client.get(
        "/api/v1/execution/amendments/summary/study_pub_01/2.0",
        headers=headers,
    )

    assert response.status_code == 200
    data = response.json()
    assert "summary_of_changes" in data
