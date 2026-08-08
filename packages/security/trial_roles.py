from enum import StrEnum

from fastapi import HTTPException, Request

from packages.security.audit_logger import CentralAuditLogger
from packages.security.rbac import (
    Principal,
    can_access_site,
    normalize_role,
)


class TrialRole(StrEnum):
    SITE_PI = "principal_investigator"
    CRA_MONITOR = "cra"
    DATA_MANAGER = "sponsor_dm"
    UNBLINDED_STATISTICIAN = "unblinded_statistician"
    IDMC = "idmc"
    PHARMACIST = "pharmacist"


class ClinicalStaffRole(StrEnum):
    """
    Standard clinical staff role vocabulary from docs/SDLC/05_Security_Compliance_Audit_Spec.md,
    reused across organization directory and delegation of authority records. Includes the
    External Monitor persona aligned to CRO affiliation.
    """

    PRINCIPAL_INVESTIGATOR = "Principal Investigator"
    SUB_INVESTIGATOR = "Sub-Investigator"
    CRC = "CRC"
    CRA_MONITOR = "CRA/Monitor"
    EXTERNAL_MONITOR = "External Monitor"



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
    return [normalize_role(r) for r in raw_roles]


def check_trial_role(request: Request, role: TrialRole) -> bool:
    """
    Checks if any role in the request matches the specified TrialRole
    following the eTMF string-matching/normalization convention.
    """
    user_roles = get_normalized_request_roles(request)

    role_map = {
        TrialRole.SITE_PI: {
            "principal_investigator",
            "investigator",
            "lead_investigator",
            "authorized_er_physician",
        },
        TrialRole.CRA_MONITOR: {"cra", "monitor", "cra_monitor"},
        TrialRole.DATA_MANAGER: {
            "sponsor_dm",
            "data_manager",
            "dm",
            "sponsor_admin",
            "admin",
        },
        TrialRole.UNBLINDED_STATISTICIAN: {"unblinded_statistician"},
        TrialRole.IDMC: {"idmc"},
        TrialRole.PHARMACIST: {"pharmacist"},
    }

    allowed = role_map.get(role, set())
    return any(r in allowed for r in user_roles)


def enforce_site_isolation(
    request: Request, site_id: str, principal: Principal
) -> None:
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
