import pytest
from fastapi import HTTPException, Request
from fastapi.testclient import TestClient

from packages.security.rbac import (
    Principal,
    can_access_study,
    get_principal,
    require_study_scope,
    StudyScopeChecker,
)
from packages.security.permissions import (
    PermissionEnum,
    ROLE_PERMISSIONS_MAP,
    RoleEnum,
)
from packages.security.middleware import GatewayAuthMiddleware


def test_permission_enum_export_sdtm():
    """Confirm the PermissionEnum value and mapping of EXPORT_SDTM."""
    # Ensure PermissionEnum.EXPORT_SDTM has the correct value
    assert PermissionEnum.EXPORT_SDTM == "export:sdtm"

    # Ensure EXPORT_SDTM is mapped to the roles allowed to export (SponsorAdmin and DataManager)
    assert PermissionEnum.EXPORT_SDTM in ROLE_PERMISSIONS_MAP[RoleEnum.SPONSOR_ADMIN.value]
    assert PermissionEnum.EXPORT_SDTM in ROLE_PERMISSIONS_MAP[RoleEnum.DATA_MANAGER.value]


def test_can_access_study_fail_open():
    """Confirm can_access_study fails open when principal.assigned_studies is empty for non-external_monitor roles."""
    # Scenario 1: Non-external monitor role with empty assigned_studies -> fails open (returns True)
    p_empty = Principal(
        user_id="user1",
        roles=["sponsor_dm"],
        assigned_studies=[],
    )
    assert can_access_study(p_empty, "study_abc") is True

    # Scenario 2: External monitor role with empty assigned_studies -> fails closed (returns False)
    p_ext = Principal(
        user_id="user2",
        roles=["external_monitor"],
        assigned_studies=[],
    )
    assert can_access_study(p_ext, "study_abc") is False

    # Scenario 3: Non-external monitor role with assigned_studies mismatch -> fails closed (returns False)
    p_restricted = Principal(
        user_id="user3",
        roles=["sponsor_dm"],
        assigned_studies=["study_xyz"],
    )
    assert can_access_study(p_restricted, "study_abc") is False

    # Scenario 4: Non-external monitor role with assigned_studies match -> success (returns True)
    p_restricted_match = Principal(
        user_id="user4",
        roles=["sponsor_dm"],
        assigned_studies=["study_abc"],
    )
    assert can_access_study(p_restricted_match, "study_abc") is True


@pytest.mark.asyncio
async def test_require_study_scope_resolution_order():
    """Confirm study-scope guard resolves study_id from different sources and enforces access control."""
    checker = require_study_scope()
    assert isinstance(checker, StudyScopeChecker)

    # 1. Resolve from query parameters
    class MockRequestQuery:
        def __init__(self, query_params=None, path_params=None, headers=None):
            self.query_params = query_params or {}
            self.path_params = path_params or {}
            self.headers = headers or {}
            self.method = "GET"

    principal = Principal(user_id="u1", roles=["sponsor_dm"], assigned_studies=["study1"])

    # Match query parameter
    req = MockRequestQuery(query_params={"study_id": "study1"})
    res = await checker(req, principal)
    assert res == principal  # Returns principal on success for chaining

    # Mismatch query parameter raises 403 Forbidden with exact detail
    req_mismatch = MockRequestQuery(query_params={"study_id": "study2"})
    with pytest.raises(HTTPException) as exc_info:
        await checker(req_mismatch, principal)
    assert exc_info.value.status_code == 403
    assert exc_info.value.detail == "Forbidden: Insufficient scope access for this study."

    # 2. Resolve from path parameters
    req_path = MockRequestQuery(path_params={"study_id": "study1"}, query_params={})
    res_path = await checker(req_path, principal)
    assert res_path == principal

    # 3. Resolve from X-Study-Id header (case-insensitive)
    req_header_1 = MockRequestQuery(headers={"X-Study-Id": "study1"})
    res_header_1 = await checker(req_header_1, principal)
    assert res_header_1 == principal

    req_header_2 = MockRequestQuery(headers={"x-study-id": "study1"})
    res_header_2 = await checker(req_header_2, principal)
    assert res_header_2 == principal


@pytest.mark.asyncio
async def test_gateway_auth_middleware_tenant_fallback():
    """Confirm GatewayAuthMiddleware dispatch handles X-Tenant-Id fallback correctly."""
    from fastapi import FastAPI
    from fastapi.responses import PlainTextResponse
    import hashlib
    import hmac
    import time

    app = FastAPI()
    app.add_middleware(GatewayAuthMiddleware)

    @app.get("/test-tenant")
    async def endpoint(request: Request):
        return PlainTextResponse(getattr(request.state, "tenant_id", "none"))

    client = TestClient(app)

    # GatewayAuthMiddleware expects gateway signature headers
    # Let's mock a valid signature token
    gateway_secret = "internal-gateway-secret-12345"
    user_id = "test_user"
    roles = "sponsor_dm"
    timestamp = str(time.time())
    change_reason = "justification"

    serialized = f"{user_id}:{roles}:{timestamp}"
    signature = hmac.new(
        gateway_secret.encode(), serialized.encode(), hashlib.sha256
    ).hexdigest()

    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "1",
        "X-Change-Reason": change_reason,
    }

    # Case 1: X-Tenant-Id is missing -> fall back to tenant_default
    response = client.get("/test-tenant", headers=headers)
    assert response.status_code == 200
    assert response.text == "tenant_default"

    # Case 2: X-Tenant-Id is present -> use it
    headers_with_tenant = headers.copy()
    headers_with_tenant["X-Tenant-Id"] = "my_custom_tenant"
    response2 = client.get("/test-tenant", headers=headers_with_tenant)
    assert response2.status_code == 200
    assert response2.text == "my_custom_tenant"
