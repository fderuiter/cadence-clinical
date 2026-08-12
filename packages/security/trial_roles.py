from fastapi import HTTPException, Request

from packages.security.audit_logger import CentralAuditLogger
from packages.security.permissions import DynamicStrEnum


class TrialRole(DynamicStrEnum):
    """Dynamically registered trial roles."""

    _members = {}


class ClinicalStaffRole(DynamicStrEnum):
    """Dynamically registered clinical staff roles."""

    _members = {}


def register_trial_role(name: str, value: str):
    """Dynamically register a TrialRole."""
    TrialRole._add_member(name, value)


def register_clinical_staff_role(name: str, value: str):
    """Dynamically register a ClinicalStaffRole."""
    ClinicalStaffRole._add_member(name, value)


_TRIAL_ROLE_CHECK_MAP: dict[str, set[str]] = {}


def register_trial_role_check_mapping(role: str, allowed_normalized_roles: set[str]):
    """Register mapping of allowed normalized roles for checking check_trial_role."""
    _TRIAL_ROLE_CHECK_MAP[role] = allowed_normalized_roles


def get_normalized_request_roles(request: Request) -> list[str]:
    """
    Retrieves and normalizes request.state.roles or raw X-User-Roles headers.
    Following the eTMF convention, converts string values to lowercase, stripped lists.
    """
    roles_val = getattr(request.state, "roles", None)
    if roles_val is None:
        roles_val = (
            request.headers.get("X-User-Roles")
            or request.headers.get("x-user-roles")
            or ""
        )

    if isinstance(roles_val, str):
        raw_roles = [r.strip() for r in roles_val.split(",") if r.strip()]
    elif isinstance(roles_val, (list, tuple, set)):
        raw_roles = [str(r).strip() for r in roles_val if str(r).strip()]
    else:
        raw_roles = []

    # Import normalize_role dynamically to avoid circular import issues
    from packages.security.rbac import normalize_role

    return [normalize_role(r) for r in raw_roles]


def check_trial_role(request: Request, role: TrialRole) -> bool:
    """
    Checks if any role in the request matches the specified TrialRole
    following the eTMF string-matching/normalization convention.
    """
    user_roles = get_normalized_request_roles(request)
    allowed = _TRIAL_ROLE_CHECK_MAP.get(role, set())
    return any(r in allowed for r in user_roles)


def enforce_site_isolation(request: Request, site_id: str, principal: any) -> None:
    """
    Site-isolation guard (PRD-SYS-004):
    Raises a 403 Forbidden exception and writes a security audit alert to the audit log
    when a restricted request crosses site boundaries.
    """
    if site_id is None or str(site_id).strip() == "":
        return

    from packages.security.rbac import SITE_SCOPED_ROLES

    user_roles = get_normalized_request_roles(request)
    is_site_scoped = any(r in SITE_SCOPED_ROLES for r in user_roles)

    if is_site_scoped or principal.assigned_sites:
        # Import can_access_site dynamically to avoid circular import issues
        from packages.security.rbac import can_access_site

        if not can_access_site(principal, site_id):
            ip_address = (
                getattr(request.state, "ip_address", None)
                or (request.client.host if request.client else "unknown")
                or request.headers.get("X-Forwarded-For")
                or "unknown"
            )

            # Log security alert to CentralAuditLogger
            CentralAuditLogger.log_event(
                service_name="execution",
                action_type="SECURITY_ALERT",
                entity_name="site",
                entity_id=site_id,
                user_id=principal.user_id,
                reason_for_change="CROSS_SITE_ACCESS_DENIED",
                details={
                    "alert_type": "CROSS_SITE_ACCESS_DENIED",
                    "user_id": principal.user_id,
                    "ip_address": ip_address,
                    "requested_site_id": site_id,
                    "assigned_sites": principal.assigned_sites,
                    "roles": user_roles,
                },
            )

            raise HTTPException(
                status_code=403,
                detail=f"Forbidden: Access to site {site_id} is unauthorized.",
            )
