"""
Comprehensive suite of unit and integration tests for delegation and role authorization helpers.
"""

import pytest
from fastapi import Depends, FastAPI, HTTPException
from fastapi.testclient import TestClient
from organization_domain import ClinicalStaffRole

from packages.security.delegation import (
    normalize_and_validate_staff_role,
    require_delegation,
    validate_request_staff_roles,
)

# ==========================================
# Dummy FastAPI Application for Integration Tests
# ==========================================
app = FastAPI()


@app.post("/test-delegation")
async def delegation_endpoint(
    roles: list = Depends(require_delegation(enforce_pi=True)),
):
    """Endpoint requiring delegation authority from a Principal Investigator."""
    return {"status": "success", "roles": [r.value for r in roles]}


@app.post("/test-delegation-non-pi")
async def delegation_endpoint_non_pi(
    roles: list = Depends(require_delegation(enforce_pi=False)),
):
    """Endpoint requiring delegation authority, but allowing non-PI clinical staff."""
    return {"status": "success", "roles": [r.value for r in roles]}


client = TestClient(app)


# ==========================================
# Unit Tests
# ==========================================


def test_normalize_and_validate_staff_role_valid() -> None:
    """Verify that valid staff roles and their common synonyms resolve correctly."""
    # Principal Investigator aliases
    assert (
        normalize_and_validate_staff_role("Principal Investigator")
        == ClinicalStaffRole.PRINCIPAL_INVESTIGATOR
    )
    assert (
        normalize_and_validate_staff_role("pi")
        == ClinicalStaffRole.PRINCIPAL_INVESTIGATOR
    )
    assert (
        normalize_and_validate_staff_role("principal_investigator")
        == ClinicalStaffRole.PRINCIPAL_INVESTIGATOR
    )

    # Sub-Investigator aliases
    assert (
        normalize_and_validate_staff_role("Sub-Investigator")
        == ClinicalStaffRole.SUB_INVESTIGATOR
    )
    assert (
        normalize_and_validate_staff_role("sub investigator")
        == ClinicalStaffRole.SUB_INVESTIGATOR
    )

    # CRC aliases
    assert normalize_and_validate_staff_role("CRC") == ClinicalStaffRole.CRC
    assert (
        normalize_and_validate_staff_role("clinical research coordinator")
        == ClinicalStaffRole.CRC
    )

    # CRA/Monitor aliases
    assert (
        normalize_and_validate_staff_role("CRA/Monitor")
        == ClinicalStaffRole.CRA_MONITOR
    )
    assert (
        normalize_and_validate_staff_role("cra monitor")
        == ClinicalStaffRole.CRA_MONITOR
    )
    assert normalize_and_validate_staff_role("cra") == ClinicalStaffRole.CRA_MONITOR
    assert normalize_and_validate_staff_role("monitor") == ClinicalStaffRole.CRA_MONITOR


def test_normalize_and_validate_staff_role_invalid() -> None:
    """Verify that invalid or out-of-scope roles raise 400 Bad Request."""
    with pytest.raises(HTTPException) as exc_info:
        normalize_and_validate_staff_role("invalid-role")
    assert exc_info.value.status_code == 400
    assert "Invalid clinical staff role" in exc_info.value.detail

    with pytest.raises(HTTPException) as exc_info:
        normalize_and_validate_staff_role("auditor")  # Out-of-scope
    assert exc_info.value.status_code == 400


def test_validate_request_staff_roles_empty() -> None:
    """Verify that missing roles list in the request raises 403 Forbidden."""

    class MockRequest:
        def __init__(self):
            class State:
                roles = []

            self.state = State()
            self.headers = {}

    request = MockRequest()
    with pytest.raises(HTTPException) as exc_info:
        validate_request_staff_roles(request)
    assert exc_info.value.status_code == 403
    assert "Missing required clinical staff roles" in exc_info.value.detail


# ==========================================
# Integration & Flow Tests
# ==========================================


def test_delegation_successful_pi_matching_scope() -> None:
    """Verify successful delegation authorization when PI has matching site/sponsor scopes."""
    headers = {
        "X-User-Roles": "Principal Investigator",
        "X-Delegator-Site-Id": "site_ABC",
        "X-Delegator-Sponsor-Id": "sponsor_123",
    }
    # Pass target via query params
    response = client.post(
        "/test-delegation?target_site_id=site_ABC&target_sponsor_id=sponsor_123",
        headers=headers,
    )
    assert response.status_code == 200
    assert response.json()["status"] == "success"
    assert "Principal Investigator" in response.json()["roles"]


def test_delegation_denied_when_not_pi() -> None:
    """Verify that a non-PI (like CRC) is denied delegation when PI enforcement is enabled."""
    headers = {
        "X-User-Roles": "CRC",
        "X-Delegator-Site-Id": "site_ABC",
    }
    response = client.post("/test-delegation?target_site_id=site_ABC", headers=headers)
    assert response.status_code == 403
    assert "Only a Principal Investigator may delegate" in response.json()["detail"]


def test_delegation_allowed_non_pi_when_not_enforced() -> None:
    """Verify that a non-PI (like CRC) is authorized when PI enforcement is disabled."""
    headers = {
        "X-User-Roles": "CRC",
        "X-Delegator-Site-Id": "site_ABC",
    }
    response = client.post(
        "/test-delegation-non-pi?target_site_id=site_ABC", headers=headers
    )
    assert response.status_code == 200
    assert response.json()["status"] == "success"
    assert "CRC" in response.json()["roles"]


def test_delegation_denied_site_mismatch() -> None:
    """Verify that delegation is denied when there is a site_id scope mismatch."""
    headers = {
        "X-User-Roles": "Principal Investigator",
        "X-Delegator-Site-Id": "site_ABC",
    }
    response = client.post("/test-delegation?target_site_id=site_XYZ", headers=headers)
    assert response.status_code == 403
    assert "Delegation scope mismatch for site_id" in response.json()["detail"]


def test_delegation_denied_sponsor_mismatch() -> None:
    """Verify that delegation is denied when there is a sponsor_id scope mismatch."""
    headers = {
        "X-User-Roles": "Principal Investigator",
        "X-Delegator-Site-Id": "site_ABC",
        "X-Delegator-Sponsor-Id": "sponsor_123",
    }
    response = client.post(
        "/test-delegation?target_site_id=site_ABC&target_sponsor_id=sponsor_999",
        headers=headers,
    )
    assert response.status_code == 403
    assert "Delegation scope mismatch for sponsor_id" in response.json()["detail"]


def test_delegation_malformed_role() -> None:
    """Verify that malformed or unrecognized roles are rejected with 400 Bad Request."""
    headers = {
        "X-User-Roles": "super-user",  # Unrecognized role
        "X-Delegator-Site-Id": "site_ABC",
    }
    response = client.post("/test-delegation?target_site_id=site_ABC", headers=headers)
    assert response.status_code == 400
    assert "Invalid clinical staff role" in response.json()["detail"]


def test_delegation_missing_target_context() -> None:
    """Verify that missing target site/sponsor context raises a 400 Bad Request."""
    headers = {
        "X-User-Roles": "Principal Investigator",
        "X-Delegator-Site-Id": "site_ABC",
    }
    response = client.post("/test-delegation", headers=headers)
    assert response.status_code == 400
    assert (
        "Missing target site context for delegation scope check"
        in response.json()["detail"]
    )


def test_delegation_missing_delegator_context() -> None:
    """Verify that missing delegator site context raises a 400 Bad Request."""
    headers = {
        "X-User-Roles": "Principal Investigator",
    }
    response = client.post("/test-delegation?target_site_id=site_ABC", headers=headers)
    assert response.status_code == 400
    assert (
        "Missing delegator site context for delegation scope check"
        in response.json()["detail"]
    )


def test_delegation_target_from_body() -> None:
    """Verify that target_site_id can be extracted dynamically from the JSON request body."""
    headers = {
        "X-User-Roles": "Principal Investigator",
        "X-Delegator-Site-Id": "site_ABC",
        "X-Delegator-Sponsor-Id": "sponsor_123",
    }
    payload = {"target_site_id": "site_ABC", "target_sponsor_id": "sponsor_123"}
    response = client.post("/test-delegation", json=payload, headers=headers)
    assert response.status_code == 200
    assert response.json()["status"] == "success"
