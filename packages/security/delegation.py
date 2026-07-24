"""
Reusable delegation and role authorization helper utilities.

Provides dependency-injectable authorization primitives for site-scoped
delegation workflows in compliance with ICH E6(R2) and 21 CFR Part 11.
"""

from typing import List, Optional

from fastapi import HTTPException, Request, status
from organization_domain import ClinicalStaffRole

from packages.security.rbac import get_normalized_roles

# StaffRole alias matching shared ClinicalStaffRole vocabulary
StaffRole = ClinicalStaffRole


def normalize_and_validate_staff_role(role_str: str) -> StaffRole:
    """
    Normalizes a role string and validates it against the shared ClinicalStaffRole (StaffRole) vocabulary.

    Returns:
        StaffRole: The mapped ClinicalStaffRole enum value.

    Raises:
        HTTPException: Raises 400 Bad Request if the role is malformed or invalid.
    """
    norm = role_str.strip().lower().replace("_", " ").replace("-", " ")
    if norm in {
        "principal investigator",
        "pi",
        "principal_investigator",
        "principalinvestigator",
    }:
        return ClinicalStaffRole.PRINCIPAL_INVESTIGATOR
    if norm in {
        "sub-investigator",
        "sub investigator",
        "sub_investigator",
        "subinvestigator",
        "sub-invest",
    }:
        return ClinicalStaffRole.SUB_INVESTIGATOR
    if norm in {"crc", "clinical research coordinator"}:
        return ClinicalStaffRole.CRC
    if norm in {
        "cra/monitor",
        "cra monitor",
        "cra_monitor",
        "cra-monitor",
        "cra",
        "monitor",
    }:
        return ClinicalStaffRole.CRA_MONITOR

    raise HTTPException(
        status_code=status.HTTP_400_BAD_REQUEST,
        detail=f"Invalid clinical staff role: '{role_str}'",
    )


def validate_request_staff_roles(request: Request) -> List[StaffRole]:
    """
    Obtains and validates all roles from request.state.roles or headers.

    Ensures that every role present is validated against the StaffRole vocabulary.

    Raises:
        HTTPException:
            - 400 Bad Request for malformed/invalid roles.
            - 403 Forbidden if roles list is empty.
    """
    roles_list = get_normalized_roles(request)
    if not roles_list:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Forbidden: Missing required clinical staff roles.",
        )

    validated_roles = []
    for r in roles_list:
        validated = normalize_and_validate_staff_role(r)
        validated_roles.append(validated)
    return validated_roles


def verify_delegation_scope(
    request: Request,
    target_site_id: str,
    target_sponsor_id: Optional[str] = None,
    enforce_pi: bool = True,
) -> List[StaffRole]:
    """
    Validates the delegator's roles and context against target scopes (site_id and/or sponsor_id).

    Args:
        request: FastAPI Request containing roles, user_id, state/headers context.
        target_site_id: Target clinical site ID to verify against the delegator's scope.
        target_sponsor_id: Optional target sponsor ID to verify against the delegator's scope.
        enforce_pi: If True, requires the delegator to possess the Principal Investigator role.

    Returns:
        List[StaffRole]: The list of validated ClinicalStaffRoles for the user.

    Raises:
        HTTPException:
            - 400 Bad Request if roles are malformed, or context is missing.
            - 403 Forbidden if not authorized (e.g. non-PI when enforce_pi is True, or scope mismatch).
    """
    # 1. Validate staff roles
    validated_roles = validate_request_staff_roles(request)

    # 2. Enforce only PI can delegate (if enforce_pi is True)
    if enforce_pi:
        if ClinicalStaffRole.PRINCIPAL_INVESTIGATOR not in validated_roles:
            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN,
                detail="Forbidden: Only a Principal Investigator may delegate.",
            )

    # 3. Extract delegator site_id and sponsor_id from request.state / headers
    delegator_site_id = (
        getattr(request.state, "delegator_site_id", None)
        or getattr(request.state, "site_id", None)
        or request.headers.get("X-Delegator-Site-Id")
        or request.headers.get("X-Site-Id")
    )

    delegator_sponsor_id = (
        getattr(request.state, "delegator_sponsor_id", None)
        or getattr(request.state, "sponsor_id", None)
        or request.headers.get("X-Delegator-Sponsor-Id")
        or request.headers.get("X-Sponsor-Id")
    )

    # 4. Perform scope verification
    if not target_site_id:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Missing target site context for delegation scope check.",
        )

    if not delegator_site_id:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Missing delegator site context for delegation scope check.",
        )

    if target_site_id != delegator_site_id:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Forbidden: Delegation scope mismatch for site_id.",
        )

    if target_sponsor_id:
        if not delegator_sponsor_id:
            raise HTTPException(
                status_code=status.HTTP_400_BAD_REQUEST,
                detail="Missing delegator sponsor context for delegation scope check.",
            )
        if target_sponsor_id != delegator_sponsor_id:
            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN,
                detail="Forbidden: Delegation scope mismatch for sponsor_id.",
            )

    return validated_roles


class DelegationChecker:
    """
    FastAPI dependency-injectable class to verify delegation scopes.

    Extracts target parameters dynamically from query params, path params, headers, or body.
    """

    def __init__(self, enforce_pi: bool = True):
        self.enforce_pi = enforce_pi

    async def __call__(self, request: Request) -> List[StaffRole]:
        # Extract target_site_id and target_sponsor_id
        target_site_id = (
            request.query_params.get("target_site_id")
            or request.query_params.get("site_id")
            or request.path_params.get("target_site_id")
            or request.path_params.get("site_id")
            or request.headers.get("X-Target-Site-Id")
            or request.headers.get("X-Site-Id")
        )
        target_sponsor_id = (
            request.query_params.get("target_sponsor_id")
            or request.query_params.get("sponsor_id")
            or request.path_params.get("target_sponsor_id")
            or request.path_params.get("sponsor_id")
            or request.headers.get("X-Target-Sponsor-Id")
            or request.headers.get("X-Sponsor-Id")
        )

        # Check request body JSON if target_site_id is not yet resolved
        if not target_site_id:
            try:
                body = await request.json()
                if isinstance(body, dict):
                    target_site_id = body.get("target_site_id") or body.get("site_id")
                    target_sponsor_id = body.get("target_sponsor_id") or body.get(
                        "sponsor_id"
                    )
            except Exception:
                pass

        return verify_delegation_scope(
            request=request,
            target_site_id=target_site_id,
            target_sponsor_id=target_sponsor_id,
            enforce_pi=self.enforce_pi,
        )


def require_delegation(enforce_pi: bool = True) -> DelegationChecker:
    """
    FastAPI dependency helper factory that returns a DelegationChecker.

    Example: Depends(require_delegation(enforce_pi=True))
    """
    return DelegationChecker(enforce_pi=enforce_pi)
