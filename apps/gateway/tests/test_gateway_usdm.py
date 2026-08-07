"""Integration test suite for Gateway USDM REST API router.

Requirements: PRD-SYS-001
"""

import pytest
from fastapi.testclient import TestClient

import packages  # noqa: F401
from apps.gateway.main import app
from packages.security.middleware import get_current_user


def _mock_user_override() -> dict:
    """Mock user dependency override for authenticated tests."""
    return {
        "sub": "user_designer_100",
        "email": "designer@cadenceclinical.org",
        "roles": ["study_designer"],
        "tenant_id": "tenant_test",
    }


@pytest.fixture
def client_authenticated() -> TestClient:
    """Test client fixture with authenticated user override."""
    app.dependency_overrides[get_current_user] = _mock_user_override
    client = TestClient(app)
    yield client
    app.dependency_overrides.pop(get_current_user, None)


@pytest.fixture
def client_unauthenticated() -> TestClient:
    """Test client fixture without authentication overrides."""
    app.dependency_overrides.pop(get_current_user, None)
    return TestClient(app)


def test_gateway_usdm_import_authenticated(
    client_authenticated: TestClient,
) -> None:
    """Validate POST /api/v1/usdm/import returns 201 Created with import summary.

    Requirements: PRD-SYS-001
    """
    payload = {
        "raw_usdm_json": {
            "id": "study_usdm_gateway_01",
            "name": "GATEWAY-01",
            "protocolTitle": "Gateway Import Trial",
            "usdmVersion": "3.0",
            "studyDesigns": [
                {
                    "id": "sd_gw_1",
                    "name": "Design 1",
                    "arms": [
                        {
                            "id": "arm_gw_1",
                            "name": "Arm 1",
                            "armType": "Treatment",
                        }
                    ],
                }
            ],
        },
        "target_version": "v3.0",
        "reason_for_change": "Initial protocol creation",
    }

    response = client_authenticated.post("/api/v1/usdm/import", json=payload)
    assert response.status_code == 201
    data = response.json()
    assert data["study_id"] == "study_usdm_gateway_01"
    assert data["nodes_created"] >= 3
    assert data["relationships_created"] >= 2


def test_gateway_usdm_export_authenticated(
    client_authenticated: TestClient,
) -> None:
    """Validate GET /api/v1/usdm/export/{study_id} returns 200 OK with USDM spec.

    Requirements: PRD-SYS-001
    """
    response = client_authenticated.get("/api/v1/usdm/export/study_usdm_gateway_01")
    assert response.status_code == 200
    data = response.json()
    assert data["study_id"] == "study_usdm_gateway_01"
    assert "usdm_json" in data
    assert data["usdm_json"]["id"] == "study_usdm_gateway_01"


def test_gateway_usdm_unauthenticated_returns_401(
    client_unauthenticated: TestClient,
) -> None:
    """Validate unauthenticated USDM requests return 401 Unauthorized.

    Requirements: PRD-SYS-001
    """
    response = client_unauthenticated.get("/api/v1/usdm/export/study_123")
    assert response.status_code == 401
