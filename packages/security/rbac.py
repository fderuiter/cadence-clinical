from typing import Any, Callable, Dict, List, Optional, Set

import pydantic
from fastapi import Depends, HTTPException, Request, status
from pydantic import BaseModel, Field

# Legacy, allow-list-based role constants
ROLE_CRA = "CRA"
ROLE_DATA_MANAGER = "Data Manager"
ROLE_SITE_INVESTIGATOR = "Site Investigator"
ROLE_AUDITOR = "Auditor"
ROLE_SPONSOR_ADMIN = "Sponsor Admin"

AUDITOR_ROLES = {"auditor", "inspector", "regulatory_inspector"}


# Canonical lower-case roles from docs/SDLC/05_Security_Compliance_Audit_Spec.md §2.1
ROLE_SYSADMIN = "sysadmin"
ROLE_SPONSOR_DESIGNER = "sponsor_designer"
ROLE_SPONSOR_DM = "sponsor_dm"
ROLE_SPONSOR_MM = "sponsor_mm"
ROLE_SPONSOR_STATISTICIAN = "sponsor_statistician"
ROLE_INVESTIGATOR = "investigator"
ROLE_CRC = "crc"
ROLE_CRA_CANONICAL = "cra"
ROLE_SUBJECT = "subject"
ROLE_AUDITOR_CANONICAL = "auditor"


ROLE_ALIASES = {
    "sysadmin": ROLE_SYSADMIN,
    "system administrator": ROLE_SYSADMIN,
    "system_admin": ROLE_SYSADMIN,
    "system-admin": ROLE_SYSADMIN,
    "sponsor study designer": ROLE_SPONSOR_DESIGNER,
    "sponsor_designer": ROLE_SPONSOR_DESIGNER,
    "sponsor-designer": ROLE_SPONSOR_DESIGNER,
    "designer": ROLE_SPONSOR_DESIGNER,
    "sponsor data manager": ROLE_SPONSOR_DM,
    "sponsor_dm": ROLE_SPONSOR_DM,
    "sponsor-dm": ROLE_SPONSOR_DM,
    "sponsor dm": ROLE_SPONSOR_DM,
    "data manager": ROLE_SPONSOR_DM,
    "data_manager": ROLE_SPONSOR_DM,
    "data-manager": ROLE_SPONSOR_DM,
    "dm": ROLE_SPONSOR_DM,
    "admin": ROLE_SPONSOR_DM,
    "sponsor admin": ROLE_SPONSOR_DM,
    "sponsor_admin": ROLE_SPONSOR_DM,
    "sponsor medical monitor": ROLE_SPONSOR_MM,
    "sponsor_mm": ROLE_SPONSOR_MM,
    "sponsor-mm": ROLE_SPONSOR_MM,
    "sponsor mm": ROLE_SPONSOR_MM,
    "medical monitor": ROLE_SPONSOR_MM,
    "medical_monitor": ROLE_SPONSOR_MM,
    "mm": ROLE_SPONSOR_MM,
    "sponsor statistician": ROLE_SPONSOR_STATISTICIAN,
    "sponsor_statistician": ROLE_SPONSOR_STATISTICIAN,
    "sponsor-statistician": ROLE_SPONSOR_STATISTICIAN,
    "statistician": ROLE_SPONSOR_STATISTICIAN,
    "investigator": ROLE_INVESTIGATOR,
    "site investigator": ROLE_INVESTIGATOR,
    "site_investigator": ROLE_INVESTIGATOR,
    "site-investigator": ROLE_INVESTIGATOR,
    "principal investigator": ROLE_INVESTIGATOR,
    "pi": ROLE_INVESTIGATOR,
    "principal_investigator": ROLE_INVESTIGATOR,
    "principalinvestigator": ROLE_INVESTIGATOR,
    "investigator_user": ROLE_INVESTIGATOR,
    "crc": ROLE_CRC,
    "clinical research coordinator": ROLE_CRC,
    "coordinator": ROLE_CRC,
    "cra": ROLE_CRA_CANONICAL,
    "clinical research associate": ROLE_CRA_CANONICAL,
    "monitor": ROLE_CRA_CANONICAL,
    "cra/monitor": ROLE_CRA_CANONICAL,
    "cra_monitor": ROLE_CRA_CANONICAL,
    "cra-monitor": ROLE_CRA_CANONICAL,
    "subject": ROLE_SUBJECT,
    "patient": ROLE_SUBJECT,
    "epro": ROLE_SUBJECT,
    "auditor": ROLE_AUDITOR_CANONICAL,
    "inspector": ROLE_AUDITOR_CANONICAL,
    "regulatory_inspector": ROLE_AUDITOR_CANONICAL,
}


# Declarative action vocabulary and role-to-permission matrix matching §2.2
# Key format: ROLE -> RESOURCE -> SET OF ACTIONS
# Actions: "create", "read", "update", "delete"
ROLE_PERMISSIONS: Dict[str, Dict[str, Set[str]]] = {
    ROLE_SYSADMIN: {
        "study_design": {"read"},
        "system_audit_logs": {"read"},
        "export_masked": {"read"},
    },
    ROLE_SPONSOR_DESIGNER: {
        "study_design": {"create", "read", "update", "delete"},
        "system_audit_logs": {"read"},
    },
    ROLE_SPONSOR_DM: {
        "study_design": {"read"},
        "subject_enrollment": {"read"},
        "ecrf_data_entry": {"read"},
        "query_lifecycle": {"create", "read", "update", "delete"},
        "system_audit_logs": {"read"},
        "export_masked": {"create", "read", "update"},
    },
    ROLE_SPONSOR_MM: {
        "study_design": {"read"},
        "subject_enrollment": {"read"},
        "ecrf_data_entry": {"read"},
        "query_lifecycle": {"create", "read", "update"},
        "system_audit_logs": {"read"},
        "export_masked": {"read"},
    },
    ROLE_SPONSOR_STATISTICIAN: {
        "study_design": {"read"},
        "system_audit_logs": {"read"},
        "export_masked": {"create", "read", "update"},
    },
    ROLE_INVESTIGATOR: {
        "study_design": {"read"},
        "subject_enrollment": {"create", "read", "update"},
        "ecrf_data_entry": {"create", "read", "update"},
        "query_lifecycle": {
            "read",
            "update",
        },  # 'Ans' (Answer query) maps to update/read
        "sdv": {"read"},
        "system_audit_logs": {"read"},
    },
    ROLE_CRC: {
        "study_design": {"read"},
        "subject_enrollment": {"create", "read", "update"},
        "ecrf_data_entry": {
            "create",
            "read",
            "update",
        },  # 'C/R/U (Draft)' maps to create/read/update
        "query_lifecycle": {"read", "update"},  # 'Ans' maps to update/read
        "system_audit_logs": {"read"},
    },
    ROLE_CRA_CANONICAL: {
        "study_design": {"read"},
        "subject_enrollment": {"read"},
        "ecrf_data_entry": {"read"},
        "query_lifecycle": {"create", "read", "update", "delete"},
        "sdv": {"create", "read", "update", "delete"},
        "system_audit_logs": {"read"},
        "export_masked": {"read"},
    },
    ROLE_SUBJECT: {
        "ecrf_data_entry": {"create", "update"},  # 'Diary' maps to create/update
    },
    ROLE_AUDITOR_CANONICAL: {
        "system_audit_logs": {"read"},
    },
}


# Field-level blinding/masking rules from §2.3
# These are applied to sensitive fields for blinded users.
MASKING_RULES: Dict[str, Callable[[Any], Any]] = {
    "initials": lambda val: "**" if val else val,
    "ssn": lambda val: "***-**-****" if val else val,
    "dob": lambda val: "MASKED" if val else val,
    "treatment_arm_id": lambda val: "BLINDED" if val else val,
    "treatment_arm": lambda val: "BLINDED" if val else val,
    "administered_drug_code": lambda val: "Obfuscated Kit" if val else val,
    "drug_code": lambda val: "Obfuscated Kit" if val else val,
    "changed_reason_for_blinded_field": lambda val: "Obfuscated" if val else val,
}


class Principal(BaseModel):
    user_id: str
    roles: List[str]  # Normalized canonical roles
    assigned_sites: List[str] = Field(default_factory=list)
    unblinded_access: bool = False
    change_reason: Optional[str] = None


def normalize_role(role: str) -> str:
    """Normalizes a role string to its canonical form using ROLE_ALIASES."""
    norm = role.strip().lower()
    return ROLE_ALIASES.get(norm, norm)


def has_permission(principal: Principal, permission: str) -> bool:
    """
    Checks if the principal has the specified permission.
    Permission string format: "resource:action" (e.g., "study_design:read")
    """
    if ":" not in permission:
        return False
    resource, action = permission.split(":", 1)
    resource = resource.strip().lower()
    action = action.strip().lower()

    for role in principal.roles:
        perms = ROLE_PERMISSIONS.get(role, {})
        if resource in perms and action in perms[resource]:
            return True
    return False


def can_access_site(principal: Principal, site_id: str) -> bool:
    """
    Checks if the principal has access to a specific site.
    Site-scoped users are restricted to their assigned_sites.
    Sponsor/SysAdmin users with empty assigned_sites are allowed global access.
    """
    site_scoped_roles = {ROLE_INVESTIGATOR, ROLE_CRC}
    user_site_roles = [r for r in principal.roles if r in site_scoped_roles]

    if user_site_roles:
        return site_id in principal.assigned_sites

    if principal.assigned_sites:
        return site_id in principal.assigned_sites

    return True


def get_principal_sync(request: Request) -> Principal:
    """
    Synchronous helper to extract identity and authorization attributes
    from request context, query parameters, and headers, returning a normalized Principal.
    """
    # 1. User ID
    user_id = ""
    if hasattr(request, "state"):
        user_id = getattr(request.state, "user_id", None) or ""
    if not user_id and hasattr(request, "headers"):
        user_id = (
            request.headers.get("X-User-Id") or request.headers.get("x-user-id") or ""
        )

    # 2. Roles (raw)
    roles_val = None
    if hasattr(request, "state"):
        roles_val = getattr(request.state, "roles", None)
    if roles_val is None and hasattr(request, "headers"):
        roles_val = (
            request.headers.get("X-User-Roles")
            or request.headers.get("x-user-roles")
            or ""
        )

    if isinstance(roles_val, str):
        raw_roles = [r.strip().lower() for r in roles_val.split(",") if r.strip()]
    elif isinstance(roles_val, list):
        raw_roles = [str(r).strip().lower() for r in roles_val if str(r).strip()]
    else:
        raw_roles = []

    normalized_roles = [normalize_role(r) for r in raw_roles]

    # 3. Assigned Sites
    site_id_val = None
    if hasattr(request, "state"):
        site_id_val = getattr(request.state, "site_id", None)
    if site_id_val is None and hasattr(request, "headers"):
        site_id_val = (
            request.headers.get("X-Site-Id")
            or request.headers.get("x-site-id")
            or request.headers.get("X-User-Site")
            or ""
        )

    assigned_sites = []
    if site_id_val:
        assigned_sites = [s.strip() for s in site_id_val.split(",") if s.strip()]

    # 4. Unblinded status
    unblinded_access = False
    if hasattr(request, "headers"):
        unblinded_header = (
            request.headers.get("X-Unblinded-Access")
            or request.headers.get("x-unblinded-access")
            or ""
        )
        if unblinded_header.lower() in ("true", "1", "yes"):
            unblinded_access = True
    if (
        not unblinded_access
        and hasattr(request, "state")
        and hasattr(request.state, "unblinded_access")
    ):
        unblinded_access = bool(request.state.unblinded_access)

    # 5. Change reason (State, query parameters, headers)
    change_reason = None

    # State
    if hasattr(request, "state"):
        change_reason = getattr(request.state, "change_reason", None) or getattr(
            request.state, "reason_for_change", None
        )
        if change_reason:
            change_reason = str(change_reason).strip()

    # Query Parameters
    if not change_reason:
        try:
            if hasattr(request, "query_params") and request.query_params:
                for key in ("change_reason", "reason_for_change", "reason"):
                    val = request.query_params.get(key)
                    if val and str(val).strip():
                        change_reason = str(val).strip()
                        break
        except Exception:
            pass

    # Headers
    if not change_reason and hasattr(request, "headers") and request.headers:
        for key in (
            "X-Change-Reason",
            "x-change-reason",
            "X-Reason-For-Change",
            "x-reason-for-change",
            "Reason-For-Change",
            "reason-for-change",
        ):
            val = request.headers.get(key)
            if val and str(val).strip():
                change_reason = str(val).strip()
                break

    return Principal(
        user_id=user_id,
        roles=normalized_roles,
        assigned_sites=assigned_sites,
        unblinded_access=unblinded_access,
        change_reason=change_reason,
    )


async def get_principal(request: Request) -> Principal:
    """
    FastAPI dependency to extract identity and authorization attributes
    from request context and headers, returning a normalized Principal.
    """
    import json

    principal = get_principal_sync(request)

    # If change_reason is not found yet, and it is a write operation, check body
    if (
        not principal.change_reason
        and hasattr(request, "method")
        and request.method in ("POST", "PUT", "PATCH")
    ):
        try:
            content_type = (
                request.headers.get("content-type", "")
                if hasattr(request, "headers")
                else ""
            )
            if "application/json" in content_type:
                body = await request.body()
                if body:
                    body_json = json.loads(body)

                    def find_reason_in_dict(d: dict) -> Optional[str]:
                        for key in ("reason_for_change", "change_reason", "reason"):
                            if key in d and isinstance(d[key], str) and d[key].strip():
                                return d[key].strip()
                        for v in d.values():
                            if isinstance(v, dict):
                                res = find_reason_in_dict(v)
                                if res:
                                    return res
                        return None

                    if isinstance(body_json, dict):
                        principal.change_reason = find_reason_in_dict(body_json)

                # Reset receive stream so downstream route can read it again
                async def receive():
                    return {"type": "http.request", "body": body, "more_body": False}

                request._receive = receive
            elif (
                "application/x-www-form-urlencoded" in content_type
                or "multipart/form-data" in content_type
            ):
                form = await request.form()
                for key in ("reason_for_change", "change_reason", "reason"):
                    val = form.get(key)
                    if val and str(val).strip():
                        principal.change_reason = str(val).strip()
                        break
        except Exception:
            pass

    # Ensure change_reason is clean
    if principal.change_reason:
        principal.change_reason = principal.change_reason.strip()

    # Reject write operations with a descriptive error if the resolved change justification is less than 10 characters long on ingestion/doc routes
    if hasattr(request, "method") and request.method in (
        "POST",
        "PUT",
        "PATCH",
        "DELETE",
    ):
        path_lower = request.url.path.lower() if hasattr(request, "url") else ""
        is_ingest_or_doc_route = any(
            p in path_lower
            for p in (
                "/eisf/",
                "/etmf/",
                "/econsent/",
                "document",
                "ingest",
                "upload",
                "expected-document",
                "edl",
            )
        )
        if is_ingest_or_doc_route:
            if not principal.change_reason or len(principal.change_reason) < 10:
                raise HTTPException(
                    status_code=400,
                    detail="Part 11 change justification reason is required and must be at least 10 characters long.",
                )

    return principal


def require_permission(permission: str) -> Callable[[Principal], Principal]:
    """
    FastAPI dependency factory to assert that the caller has a required permission.
    """

    def dependency(principal: Principal = Depends(get_principal)) -> Principal:
        if not has_permission(principal, permission):
            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN,
                detail=f"Forbidden: Insufficient permissions for {permission}.",
            )
        return principal

    return dependency


def mask_payload(payload: Any, principal: Principal) -> Any:
    """
    Recursively masks sensitive fields in dictionaries, lists, or Pydantic models
    based on the principal's unblinded status.
    If principal.unblinded_access is True, no masking is performed.
    """
    if principal.unblinded_access:
        return payload

    return _recursive_mask(payload)


def _recursive_mask(data: Any) -> Any:
    if data is None:
        return None

    if isinstance(data, pydantic.BaseModel):
        dumped = data.model_dump()
        masked = _recursive_mask(dumped)
        # Reconstruct the model safely
        return data.__class__.model_validate(masked)

    if isinstance(data, dict):
        new_dict = {}
        for k, v in data.items():
            k_lower = k.lower()
            if k_lower in MASKING_RULES:
                new_dict[k] = MASKING_RULES[k_lower](v)
            else:
                new_dict[k] = _recursive_mask(v)
        return new_dict

    if isinstance(data, list):
        return [_recursive_mask(item) for item in data]

    if isinstance(data, tuple):
        return tuple(_recursive_mask(item) for item in data)

    if isinstance(data, set):
        return {_recursive_mask(item) for item in data}

    return data


def get_normalized_roles(request: Request) -> list[str]:
    """
    Retrieves and normalizes request.state.roles or raw X-User-Roles headers.
    Updates request.state.roles to be a list of lowercase, stripped strings.
    """
    roles_val = getattr(request.state, "roles", None)
    if roles_val is None:
        roles_val = request.headers.get("X-User-Roles", "")

    if isinstance(roles_val, str):
        normalized = [r.strip().lower() for r in roles_val.split(",") if r.strip()]
    elif isinstance(roles_val, list):
        normalized = [str(r).strip().lower() for r in roles_val if str(r).strip()]
    else:
        normalized = []

    request.state.roles = normalized
    return normalized


def verify_not_auditor(request: Request) -> list[str]:
    """
    FastAPI dependency to verify that the request does not originate from an auditor persona.
    Raises HTTP 403 Forbidden if any auditor roles are detected.
    """
    roles = get_normalized_roles(request)
    if any(role in AUDITOR_ROLES for role in roles):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Auditor personas are restricted to read-only access.",
        )
    return roles


def verify_is_auditor(request: Request) -> list[str]:
    """
    FastAPI dependency to verify that the request is made by an authorized auditor persona.
    Raises HTTP 403 Forbidden if no authorized auditor/inspection roles are detected.
    """
    roles = get_normalized_roles(request)
    if not any(role in AUDITOR_ROLES for role in roles):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Access is restricted to authorized auditor/inspection roles.",
        )
    return roles


ROLE_EXPANSIONS = {
    "site investigator": {
        "site investigator",
        "investigator",
        "site-investigator",
        "site_investigator",
        "investigator_user",
    },
    "data manager": {
        "data manager",
        "data_manager",
        "data-manager",
        "sponsor_dm",
        "dm",
        "admin",
    },
    "cra": {"cra"},
    "auditor": {"auditor", "inspector", "regulatory_inspector"},
    "sponsor admin": {"sponsor admin", "sponsor_admin", "admin"},
}


def require_roles(*allowed_roles: str):
    """
    FastAPI dependency factory to enforce that the caller has at least one of the allowed roles.
    Allows case-insensitive, whitespace-insensitive matches and role synonym expansion.
    """

    def dependency(request: Request) -> list[str]:
        roles = get_normalized_roles(request)
        expanded_allowed = set()
        for role in allowed_roles:
            norm_role = role.strip().lower()
            expanded_allowed.add(norm_role)
            if norm_role in ROLE_EXPANSIONS:
                expanded_allowed.update(ROLE_EXPANSIONS[norm_role])

        if not any(role in expanded_allowed for role in roles):
            raise HTTPException(
                status_code=403,
                detail="User role is not authorized for this action.",
            )
        return roles

    return dependency
