"""Integration test suite for Gateway CDISC REST API router.

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
        "sub": "user_12345",
        "email": "cdm@cadenceclinical.org",
        "roles": ["study_designer", "data_manager"],
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


def test_gateway_cdisc_products_authenticated(
    client_authenticated: TestClient,
) -> None:
    """Validate GET /api/v1/cdisc/products returns 200 OK with product catalog.

    Requirements: PRD-SYS-001
    """
    response = client_authenticated.get("/api/v1/cdisc/products")
    assert response.status_code == 200
    data = response.json()
    assert isinstance(data, list)
    assert len(data) >= 3


def test_gateway_cdisc_cdash_domain_authenticated(
    client_authenticated: TestClient,
) -> None:
    """Validate GET /api/v1/cdisc/cdash/DM returns CDASH domain definition.

    Requirements: PRD-SYS-001
    """
    response = client_authenticated.get("/api/v1/cdisc/cdash/DM")
    assert response.status_code == 200
    data = response.json()
    assert data["domain_code"] == "DM"
    assert data["version"] == "2.3"


def test_gateway_cdisc_sdtm_domain_authenticated(
    client_authenticated: TestClient,
) -> None:
    """Validate GET /api/v1/cdisc/sdtm/AE returns SDTM domain definition.

    Requirements: PRD-SYS-001
    """
    response = client_authenticated.get("/api/v1/cdisc/sdtm/AE")
    assert response.status_code == 200
    data = response.json()
    assert data["domain_code"] == "AE"
    assert data["version"] == "3.4"


def test_gateway_cdisc_codelist_authenticated(
    client_authenticated: TestClient,
) -> None:
    """Validate GET /api/v1/cdisc/codelists/C66742 returns codelist details.

    Requirements: PRD-SYS-001
    """
    response = client_authenticated.get(
        "/api/v1/cdisc/codelists/C66742?package=cdashct-2024-09-27"
    )
    assert response.status_code == 200
    data = response.json()
    assert data["codelist_code"] == "C66742"


def test_gateway_cdisc_unauthenticated_returns_401(
    client_unauthenticated: TestClient,
) -> None:
    """Validate requests without authentication header return 401 Unauthorized.

    Requirements: PRD-SYS-001
    """
    response = client_unauthenticated.get("/api/v1/cdisc/products")
    assert response.status_code == 401
