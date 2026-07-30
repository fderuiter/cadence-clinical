"""
Comprehensive end-to-end integration and compliance test suite for the Organization Directory,
API Gateway, and eISF/eTMF archival handoff workflows.
"""

import json
import time
import uuid
from datetime import datetime, timezone
from unittest.mock import AsyncMock, MagicMock

import httpx
import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from jose import jwt

from apps.eisf.database import db_manager as eisf_db_manager
from apps.eisf.main import app as eisf_app
from apps.eisf.models import Base as EisfBase
from apps.gateway.main import SERVICES
from apps.gateway.main import app as gateway_app
from apps.org.database import db_manager as org_db_manager
from apps.org.main import GATEWAY_SECRET
from apps.org.main import app as org_app
from apps.org.models import Base as OrgBase
from packages.security.signing import (
    generate_canonical_signature,
    generate_gateway_signature,
)


@pytest_asyncio.fixture(autouse=True)
async def setup_databases():
    """
    Setup in-memory databases for org and eisf microservices.
    """
    org_db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with org_db_manager.engine.begin() as conn:
        await conn.run_sync(OrgBase.metadata.create_all)

    eisf_db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with eisf_db_manager.engine.begin() as conn:
        await conn.run_sync(EisfBase.metadata.create_all)

    yield

    async with org_db_manager.engine.begin() as conn:
        await conn.run_sync(OrgBase.metadata.drop_all)
    await org_db_manager.close()

    async with eisf_db_manager.engine.begin() as conn:
        await conn.run_sync(EisfBase.metadata.drop_all)
    await eisf_db_manager.close()


def get_mock_jwt_token(user_id: str = "test_user", roles: list = None) -> str:
    """
    Generate a mock JWT token signed with JWT_TEST_SECRET.
    """
    roles = roles or ["admin"]
    secret = "test_secret"  # pragma: allowlist secret
    payload = {
        "sub": user_id,
        "realm_access": {"roles": roles},
        "exp": time.time() + 3600,
    }
    return jwt.encode(payload, secret, algorithm="HS256")


def get_gateway_auth_headers(user_id: str = "test_user", roles: list = None) -> dict:
    """
    Get mock headers for gateway.
    """
    token = get_mock_jwt_token(user_id, roles)
    return {"Authorization": f"Bearer {token}"}


def get_gxp_auth_headers(
    user_id: str,
    roles: str,
    site_id: str = None,
    sponsor_id: str = None,
    change_reason: str = "Valid Change Reason",
) -> dict:
    """
    Generates valid gateway V2 signed headers for downstream microservice authentication.
    """
    timestamp = str(time.time())
    secret = b"internal-gateway-secret-12345"
    sig = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=secret,
        change_reason=change_reason,
        site_id=site_id,
        sponsor_id=sponsor_id,
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
    if sponsor_id:
        headers["X-Sponsor-Id"] = sponsor_id
    return headers


def generate_sig_token(user_id: str, action: str) -> str:
    """
    Generates a mock Part 11 step-up re-authentication token (X-Sig-Token).
    """
    secret = "internal-gateway-secret-12345"  # pragma: allowlist secret
    payload = {
        "sub": user_id,
        "action": action,
        "exp": time.time() + 3600,
        "jti": str(uuid.uuid4()),
    }
    return jwt.encode(payload, secret, algorithm="HS256")


# =====================================================================
# 1. Gateway Routing and Proxying Tests
# =====================================================================


def test_gateway_org_proxy_routing(monkeypatch: pytest.MonkeyPatch) -> None:
    """
    Verify that requests to 'org/' and 'api/v1/org' are correctly proxied to the ORG service.
    """
    monkeypatch.setenv("JWT_TEST_SECRET", "test_secret")
    token = get_mock_jwt_token("pi-user", ["Principal Investigator"])

    mock_send = AsyncMock()
    mock_resp = MagicMock()
    mock_resp.status_code = 200
    mock_resp.content = b'{"status": "ok", "service": "org"}'
    mock_resp.headers = {"content-type": "application/json"}
    mock_send.return_value = mock_resp
    monkeypatch.setattr(httpx.AsyncClient, "send", mock_send)

    with TestClient(gateway_app) as client:
        # Test routing for '/org/health'
        res1 = client.get("/org/health", headers={"Authorization": f"Bearer {token}"})
        assert res1.status_code == 200
        sent_req1 = mock_send.call_args.args[0]
        # Should drop prefix and point to localhost:8010/health
        assert str(sent_req1.url) == "http://localhost:8010/health"

        # Test routing for '/api/v1/org/organizations'
        res2 = client.get(
            "/api/v1/org/organizations", headers={"Authorization": f"Bearer {token}"}
        )
        assert res2.status_code == 200
        sent_req2 = mock_send.call_args.args[0]
        # Should keep path and point to localhost:8010/api/v1/org/organizations
        assert str(sent_req2.url) == "http://localhost:8010/api/v1/org/organizations"


# =====================================================================
# 2. Gateway OpenAPI Aggregation Tests
# =====================================================================


@pytest.mark.asyncio
async def test_gateway_openapi_aggregation_with_org(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """
    Verify that gateway aggregates OpenAPI from org service and prefixes component namespace.
    """
    monkeypatch.setenv("JWT_TEST_SECRET", "test_secret")

    class MockResponse:
        status_code = 200

        def __init__(self, service_name: str) -> None:
            self.service_name = service_name

        def json(self):
            return {
                "openapi": "3.1.0",
                "paths": {
                    "/test-endpoint": {
                        "get": {"responses": {"200": {"description": "Success"}}}
                    }
                },
                "components": {
                    "schemas": {
                        "TestModel": {
                            "type": "object",
                            "properties": {"id": {"type": "string"}},
                        }
                    }
                },
            }

    async def mock_get(*args, **kwargs) -> MockResponse:
        url = args[1] if len(args) > 1 else kwargs.get("url", "")
        service_name = "unknown"
        for name, base_url in SERVICES.items():
            if base_url in url:
                service_name = name
                break
        return MockResponse(service_name)

    monkeypatch.setattr(httpx.AsyncClient, "get", mock_get)

    with TestClient(gateway_app) as client:
        response = client.get("/openapi.json")
        assert response.status_code == 200
        data = response.json()

        # Schema component rewrite checks
        assert "Org_TestModel" in data["components"]["schemas"]
        # Path rewrite checks (route prefixing)
        assert "/org/test-endpoint" in data["paths"]


# =====================================================================
# 3. Finalization-to-Archive Handoff Tests
# =====================================================================


@pytest.mark.asyncio
async def test_doa_signoff_automatic_archival_handoff(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """
    E2E integration: Verify that successfully signing off a Delegation of Authority
    triggers an automatic, authenticated, durable handoff call to eISF ingestion.
    """
    # 1. Setup mock eISF client to capture the handoff payload
    captured_payloads = []

    async def mock_eisf_ingest(*args, **kwargs):
        # args[0] is self, args[1] is url
        url = args[1] if len(args) > 1 else kwargs.get("url", "")
        json_data = kwargs.get("json")
        headers_data = kwargs.get("headers")
        captured_payloads.append((url, json_data, headers_data))
        # Return mock response representing successful ingestion
        resp = MagicMock()
        resp.status_code = 201
        resp.text = '{"status": "success"}'
        return resp

    # Intercept httpx AsyncClient post call in apps/org/main.py
    monkeypatch.setattr(httpx.AsyncClient, "post", mock_eisf_ingest)

    org_client = TestClient(org_app)

    # 2. Setup mock data: Site, PI, CRC, and Delegation in org database
    admin_headers = get_gxp_auth_headers("admin-1", "admin")

    # Create Site
    site_payload = {
        "site_id": "site_boston_01",
        "name": "Boston Medical",
        "organization_id": "org_1",
        "study_id": "study_alpha",
        "reason_for_change": "Initial site setup",
    }
    site_resp = org_client.post(
        "/api/v1/org/sites", json=site_payload, headers=admin_headers
    )
    assert site_resp.status_code == 201

    # Create PI Personnel
    pi_payload = {
        "keycloak_user_id": "kc-pi-boston",
        "first_name": "Arthur",
        "last_name": "Pendragon",
        "email": "pi.arthur@camelot.org",
        "role": "Principal Investigator",
        "site_id": "site_boston_01",
        "study_id": "study_alpha",
        "reason_for_change": "Initial PI setup",
    }
    pi_resp = org_client.post(
        "/api/v1/org/personnel", json=pi_payload, headers=admin_headers
    )
    assert pi_resp.status_code == 201
    pi_id = pi_resp.json()["id"]

    # Create CRC Personnel
    crc_payload = {
        "keycloak_user_id": "kc-crc-boston",
        "first_name": "Gwen",
        "last_name": "Guinevere",
        "email": "crc.gwen@camelot.org",
        "role": "CRC",
        "site_id": "site_boston_01",
        "study_id": "study_alpha",
        "reason_for_change": "Initial CRC setup",
    }
    crc_resp = org_client.post(
        "/api/v1/org/personnel", json=crc_payload, headers=admin_headers
    )
    assert crc_resp.status_code == 201
    crc_id = crc_resp.json()["id"]

    # Grant Delegation
    grant_headers = get_gxp_auth_headers(
        user_id="kc-pi-boston",
        roles="Principal Investigator",
        site_id="site_boston_01",
        change_reason="Granting coordinator entry rights",
    )
    grant_payload = {
        "delegator_id": pi_id,
        "delegatee_id": crc_id,
        "site_id": "site_boston_01",
        "study_id": "study_alpha",
        "duties": ["Informed Consent", "CRF Data Entry"],
        "start_date": datetime.now(timezone.utc).isoformat(),
        "reason_for_change": "Onboarding coordinator",
    }

    grant_resp = org_client.post(
        "/api/v1/org/delegations", json=grant_payload, headers=grant_headers
    )
    assert grant_resp.status_code == 201
    doa_data = grant_resp.json()
    doa_id = doa_data["id"]

    # 3. Sign the delegation of authority
    canonical_payload = {
        "id": doa_id,
        "delegator_id": pi_id,
        "delegatee_id": crc_id,
        "site_id": "site_boston_01",
        "study_id": "study_alpha",
        "duties": ["Informed Consent", "CRF Data Entry"],
        "start_date": doa_data["start_date"],
    }
    valid_sig = generate_canonical_signature(canonical_payload, GATEWAY_SECRET)

    sign_headers = grant_headers.copy()
    sign_headers["X-Sig-Token"] = generate_sig_token(
        "kc-pi-boston", f"/api/v1/org/delegations/{doa_id}/sign-off"
    )

    sign_payload = {
        "payload": canonical_payload,
        "signature": valid_sig,
        "reason_for_change": "I verify and execute this delegation",
    }

    sign_resp = org_client.post(
        f"/api/v1/org/delegations/{doa_id}/sign-off",
        json=sign_payload,
        headers=sign_headers,
    )
    assert sign_resp.status_code == 200

    # 4. Assert that handoff POST was triggered and contains valid data
    assert len(captured_payloads) == 1
    url, captured_json, headers = captured_payloads[0]

    assert "/api/v1/eisf/ingest" in url
    assert captured_json["study_id"] == "study_alpha"
    assert captured_json["site_id"] == "site_boston_01"
    assert captured_json["binder_classification"] == "Delegation of Authority Log"
    assert captured_json["filename"] == f"signed_doa_{doa_id}.json"
    assert captured_json["source_system"] == "Organization Directory"
    assert captured_json["metadata_json"]["artifact_code"] == "05.02.04"

    # Assert content preserves all signed and metadata properties
    content_data = json.loads(captured_json["content"])
    assert content_data["doa_id"] == doa_id
    assert content_data["signature"] == valid_sig
    assert content_data["signed_by"] == "kc-pi-boston"
    assert content_data["delegated_duties"] == ["Informed Consent", "CRF Data Entry"]

    # Assert Gateway V2 signature headers were injected for service authentication
    assert headers["X-User-Id"] == "org_directory_service"
    assert headers["X-User-Roles"] == "admin"
    assert headers["X-Signature-Version"] == "2"
    assert "X-Gateway-Signature" in headers


# =====================================================================
# 4. eISF Completeness Integration Tests
# =====================================================================


def test_eisf_completeness_participation() -> None:
    """
    Verify that archived Delegation of Authority Log (05.02.04) correctly participates
    in the site-level eISF completeness checks in apps/eisf/main.py.
    """
    eisf_client = TestClient(eisf_app)

    # Headers for boston investigator
    headers = get_gxp_auth_headers(
        user_id="pi-boston",
        roles="site investigator",
        site_id="site-boston-01",
    )

    # 1. Fetch completeness initially - should be incomplete and Delegation of Authority Log is missing
    comp_resp1 = eisf_client.get(
        "/api/v1/eisf/completeness?study_id=study-101&site_id=site-boston-01",
        headers=headers,
    )
    assert comp_resp1.status_code == 200
    comp_data1 = comp_resp1.json()
    assert comp_data1["is_complete"] is False

    # Find Investigator & Staff section and assert Delegation of Authority Log is missing
    staff_section = next(
        s for s in comp_data1["sections"] if s["section_name"] == "Investigator & Staff"
    )
    assert "Delegation of Authority Log" in staff_section["missing"]
    assert "Delegation of Authority Log" not in staff_section["present"]

    # 2. Ingest/File the Delegation of Authority Log into eISF
    ingest_payload = {
        "study_id": "study-101",
        "site_id": "site-boston-01",
        "binder_classification": "Delegation of Authority Log",
        "filename": "signed_doa_123.json",
        "content": "{}",
        "mime_type": "application/json",
        "reason_for_change": "Archiving signed DOA log",
    }
    ingest_resp = eisf_client.post(
        "/api/v1/eisf/ingest",
        json=ingest_payload,
        headers=headers,
    )
    assert ingest_resp.status_code == 201

    # 3. Check completeness again - Delegation of Authority Log should now be present
    comp_resp2 = eisf_client.get(
        "/api/v1/eisf/completeness?study_id=study-101&site_id=site-boston-01",
        headers=headers,
    )
    assert comp_resp2.status_code == 200
    comp_data2 = comp_resp2.json()

    staff_section_after = next(
        s for s in comp_data2["sections"] if s["section_name"] == "Investigator & Staff"
    )
    assert "Delegation of Authority Log" not in staff_section_after["missing"]
    assert "Delegation of Authority Log" in staff_section_after["present"]


# =====================================================================
# 5. Rejection and Error Paths
# =====================================================================


def test_doa_signoff_tampered_payload_rejected() -> None:
    """
    Verify that signing off a DOA with mismatched or tampered payload returns 400 Bad Request.
    """
    org_client = TestClient(org_app)

    # Pre-populate Site, PI, CRC, and Delegation (un-signed)
    admin_headers = get_gxp_auth_headers("admin-1", "admin")

    # Create Site
    site_payload = {
        "site_id": "site_boston_01",
        "name": "Boston Medical",
        "organization_id": "org_1",
        "study_id": "study_alpha",
        "reason_for_change": "Initial site setup",
    }
    org_client.post("/api/v1/org/sites", json=site_payload, headers=admin_headers)

    # Create PI Personnel
    pi_payload = {
        "keycloak_user_id": "kc-pi-boston",
        "first_name": "Arthur",
        "last_name": "Pendragon",
        "email": "pi.arthur@camelot.org",
        "role": "Principal Investigator",
        "site_id": "site_boston_01",
        "study_id": "study_alpha",
        "reason_for_change": "Initial PI setup",
    }
    pi_resp = org_client.post(
        "/api/v1/org/personnel", json=pi_payload, headers=admin_headers
    )
    pi_id = pi_resp.json()["id"]

    # Create CRC Personnel
    crc_payload = {
        "keycloak_user_id": "kc-crc-boston",
        "first_name": "Gwen",
        "last_name": "Guinevere",
        "email": "crc.gwen@camelot.org",
        "role": "CRC",
        "site_id": "site_boston_01",
        "study_id": "study_alpha",
        "reason_for_change": "Initial CRC setup",
    }
    crc_resp = org_client.post(
        "/api/v1/org/personnel", json=crc_payload, headers=admin_headers
    )
    crc_id = crc_resp.json()["id"]

    # Grant Delegation
    grant_headers = get_gxp_auth_headers(
        user_id="kc-pi-boston",
        roles="Principal Investigator",
        site_id="site_boston_01",
        change_reason="Granting coordinator entry rights",
    )
    grant_payload = {
        "delegator_id": pi_id,
        "delegatee_id": crc_id,
        "site_id": "site_boston_01",
        "study_id": "study_alpha",
        "duties": ["Informed Consent", "CRF Data Entry"],
        "start_date": datetime.now(timezone.utc).isoformat(),
        "reason_for_change": "Onboarding coordinator",
    }
    grant_resp = org_client.post(
        "/api/v1/org/delegations", json=grant_payload, headers=grant_headers
    )
    doa_data = grant_resp.json()
    doa_id = doa_data["id"]

    # Tamper with duties inside payload
    tampered_payload = {
        "id": doa_id,
        "delegator_id": pi_id,
        "delegatee_id": crc_id,
        "site_id": "site_boston_01",
        "study_id": "study_alpha",
        "duties": [
            "Informed Consent",
            "CRF Data Entry",
            "Unauthorized Drug Dispensation",
        ],
        "start_date": doa_data["start_date"],
    }
    # Compute signature over correct/original payload
    original_payload = tampered_payload.copy()
    original_payload["duties"] = ["Informed Consent", "CRF Data Entry"]
    valid_sig = generate_canonical_signature(original_payload, GATEWAY_SECRET)

    sign_headers = grant_headers.copy()
    sign_headers["X-Sig-Token"] = generate_sig_token(
        "kc-pi-boston", f"/api/v1/org/delegations/{doa_id}/sign-off"
    )

    sign_payload = {
        "payload": tampered_payload,
        "signature": valid_sig,
        "reason_for_change": "I verify and execute this delegation",
    }

    # Sign-off attempt with tampered duties in payload
    sign_resp = org_client.post(
        f"/api/v1/org/delegations/{doa_id}/sign-off",
        json=sign_payload,
        headers=sign_headers,
    )
    assert sign_resp.status_code == 400
