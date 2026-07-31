"""API Gateway router for ecoa.

Requirements: PRD-SYS-001 | GxP 21 CFR Part 11 Regulated
"""

import time
from typing import Any, Dict
import httpx
from fastapi import APIRouter, Depends, HTTPException, Request, Response, status
from packages.security.permissions import get_permissions_for_roles
from packages.security.rbac import can_access_study, Principal
from packages.security.signing import normalize_scope_values

router = APIRouter(prefix="", tags=["eCOA"])


def get_gateway_user_and_headers(request: Request, payload: Dict[str, Any]) -> Dict[str, Any]:
    """
    Extract user, roles, permissions and construct the headers to forward downstream.
    """
    from apps.gateway.main import generate_signature

    user_id = payload.get("sub", "")
    roles_set = set()
    realm_access = payload.get("realm_access", {})
    if isinstance(realm_access, dict):
        for r in realm_access.get("roles", []):
            roles_set.add(str(r))
    else:
        roles_list = payload.get("roles", [])
        if isinstance(roles_list, list):
            for r in roles_list:
                roles_set.add(str(r))
        elif roles_list:
            roles_set.add(str(roles_list))

    resource_access = payload.get("resource_access", {})
    if isinstance(resource_access, dict):
        for client_id, client_data in resource_access.items():
            if isinstance(client_data, dict):
                c_roles = client_data.get("roles", [])
                if isinstance(c_roles, list):
                    for r in c_roles:
                        roles_set.add(str(r))

    roles = ",".join(sorted(list(roles_set)))
    user_roles_list = [r.strip().lower() for r in roles.split(",") if r.strip()]

    # Construct clean headers
    headers = dict(request.headers)
    headers.pop("host", None)

    # Clean up incoming spoofed headers
    for k in list(headers.keys()):
        k_lower = k.lower()
        if k_lower in (
            "x-user-id",
            "x-user-roles",
            "x-gateway-timestamp",
            "x-gateway-signature",
            "x-signature-version",
            "x-change-reason",
            "x-site-id",
            "x-sponsor-id",
            "x-unblinded-access",
            "x-tenant-id",
        ):
            headers.pop(k, None)

    change_reason = request.headers.get("x-change-reason") or "eCOA Operation"
    if len(change_reason) > 255:
        raise HTTPException(
            status_code=400,
            detail="Change reason exceeds 255 characters",
        )
    headers["X-Change-Reason"] = change_reason

    custom_attrs = payload.get("custom_attributes") or {}
    raw_site_id = payload.get("site_id")
    raw_sponsor_id = custom_attrs.get("sponsor_id") or payload.get("sponsor_id") or ""
    raw_unblinded_access = payload.get("unblinded_access", False)

    site_id_val, sponsor_id_val, unblinded_access_val = normalize_scope_values(
        raw_site_id, raw_sponsor_id, raw_unblinded_access
    )

    tenant_id_val = custom_attrs.get("tenant_id") or payload.get("tenant_id", "")
    if tenant_id_val is None or not str(tenant_id_val).strip():
        tenant_id_val = "tenant_default"
    else:
        tenant_id_val = str(tenant_id_val).strip()

    timestamp = str(time.time())
    signature = generate_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        version="2",
        change_reason=change_reason,
        site_id=site_id_val if site_id_val else None,
        sponsor_id=sponsor_id_val if sponsor_id_val else None,
        unblinded_access=unblinded_access_val,
        tenant_id=tenant_id_val,
    )

    headers["X-User-Id"] = user_id
    headers["X-User-Roles"] = roles
    headers["X-Gateway-Timestamp"] = timestamp
    headers["X-Gateway-Signature"] = signature
    headers["X-Signature-Version"] = "2"
    headers["X-Tenant-Id"] = tenant_id_val
    if site_id_val:
        headers["X-Site-Id"] = site_id_val
    if sponsor_id_val:
        headers["X-Sponsor-Id"] = sponsor_id_val
    if unblinded_access_val:
        headers["X-Unblinded-Access"] = "true"

    return {
        "user_id": user_id,
        "roles": user_roles_list,
        "headers": headers,
        "site_id": site_id_val,
        "sponsor_id": sponsor_id_val,
        "unblinded_access": unblinded_access_val,
    }


async def validate_gateway_session(request: Request) -> Dict[str, Any]:
    """
    Verifies token presence and validity.
    """
    from apps.gateway.main import verify_token

    auth_header = request.headers.get("Authorization")
    if not auth_header or not auth_header.startswith("Bearer "):
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Missing or invalid Authorization header",
        )
    token = auth_header.split(" ")[1]
    try:
        payload = verify_token(token)
    except HTTPException as e:
        raise HTTPException(status_code=e.status_code, detail=e.detail)
    return payload


async def enforce_permissions_and_scope(request: Request, payload: Dict[str, Any], required_permission: str) -> Dict[str, Any]:
    """
    Validates granular permissions and study/sponsor scopes.
    """
    info = get_gateway_user_and_headers(request, payload)

    # 1. Enforce permission Check
    granted_permissions = {p.value for p in get_permissions_for_roles(info["roles"])}
    if required_permission not in granted_permissions:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail=f"Forbidden: Missing required permission '{required_permission}'",
        )

    # 2. Enforce Study Scope Check
    study_id = (
        request.query_params.get("study_id")
        or request.headers.get("X-Study-Id")
        or request.headers.get("x-study-id")
    )
    if not study_id:
        try:
            body = await request.json()
            if isinstance(body, dict):
                study_id = body.get("study_id") or body.get("id")
                if not study_id and "submissions" in body:
                    subs = body["submissions"]
                    if isinstance(subs, list) and len(subs) > 0:
                        study_id = subs[0].get("study_id") or subs[0].get("diary_id")
        except Exception:
            pass

    if study_id:
        study_id = str(study_id).strip()
        # Build principal for can_access_study check
        principal = Principal(
            user_id=info["user_id"],
            roles=info["roles"],
            assigned_sites=[info["site_id"]] if info["site_id"] else [],
            sponsor_id=info["sponsor_id"],
            unblinded_access=info["unblinded_access"],
        )
        if not can_access_study(principal, study_id):
            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN,
                detail="Forbidden: Insufficient scope access for this study.",
            )

    return info


@router.post(
    "/api/v1/interop/epro/submit",
    status_code=status.HTTP_201_CREATED,
)
async def submit_epro_record(
    request: Request,
    payload: Dict[str, Any],
):
    """
    Gateway endpoint for ePRO submission.
    """
    from apps.gateway.main import SERVICES

    token_payload = await validate_gateway_session(request)
    info = await enforce_permissions_and_scope(request, token_payload, "form:write")

    interop_url = SERVICES.get("interop", "http://localhost:8004")
    target_url = f"{interop_url}/api/v1/interop/epro/submit"

    async with httpx.AsyncClient() as client:
        try:
            resp = await client.post(
                target_url,
                json=payload,
                headers=info["headers"],
                timeout=10.0,
            )
            return Response(
                content=resp.content,
                status_code=resp.status_code,
                headers=dict(resp.headers),
            )
        except httpx.RequestError as e:
            raise HTTPException(
                status_code=status.HTTP_502_BAD_GATEWAY,
                detail=f"Bad Gateway: {str(e)}",
            )


@router.post(
    "/api/v1/interop/epro/sync",
    status_code=status.HTTP_200_OK,
)
async def sync_epro_records(
    request: Request,
    payload: Dict[str, Any],
):
    """
    Gateway endpoint for ePRO bulk sync.
    """
    from apps.gateway.main import SERVICES

    token_payload = await validate_gateway_session(request)
    info = await enforce_permissions_and_scope(request, token_payload, "form:write")

    interop_url = SERVICES.get("interop", "http://localhost:8004")
    target_url = f"{interop_url}/api/v1/interop/epro/sync"

    async with httpx.AsyncClient() as client:
        try:
            resp = await client.post(
                target_url,
                json=payload,
                headers=info["headers"],
                timeout=10.0,
            )
            return Response(
                content=resp.content,
                status_code=resp.status_code,
                headers=dict(resp.headers),
            )
        except httpx.RequestError as e:
            raise HTTPException(
                status_code=status.HTTP_502_BAD_GATEWAY,
                detail=f"Bad Gateway: {str(e)}",
            )


@router.post(
    "/api/v1/offline/sync-batch",
    status_code=status.HTTP_200_OK,
)
async def sync_offline_batch_gateway(
    request: Request,
    payload: Dict[str, Any],
):
    """
    Gateway endpoint for offline batch sync.
    """
    from apps.gateway.main import SERVICES

    token_payload = await validate_gateway_session(request)
    info = await enforce_permissions_and_scope(request, token_payload, "form:write")

    execution_url = SERVICES.get("execution", "http://localhost:8002")
    target_url = f"{execution_url}/api/v1/offline/sync-batch"

    async with httpx.AsyncClient() as client:
        try:
            resp = await client.post(
                target_url,
                json=payload,
                headers=info["headers"],
                timeout=10.0,
            )
            return Response(
                content=resp.content,
                status_code=resp.status_code,
                headers=dict(resp.headers),
            )
        except httpx.RequestError as e:
            raise HTTPException(
                status_code=status.HTTP_502_BAD_GATEWAY,
                detail=f"Bad Gateway: {str(e)}",
            )
