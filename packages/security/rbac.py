from collections.abc import Callable
from typing import Any

import pydantic
from fastapi import Depends, HTTPException, Request, status
from pydantic import BaseModel, Field

# Dynamic containers for roles, permissions, aliases, masking rules and unblinded fields
ROLE_PERMISSIONS: dict[str, dict[str, set[str]]] = {}
ROLE_ALIASES: dict[str, str] = {}
MASKING_RULES: dict[str, Callable[[Any], Any]] = {}
SITE_SCOPED_ROLES: set[str] = set()
ROLE_UNMASKED_FIELDS: dict[str, set[str]] = {}
AUDITOR_ROLES: set[str] = set()

_DYNAMIC_CONSTANTS: dict[str, Any] = {}


def register_rbac_role_permissions(role: str, resource_permissions: dict[str, set[str]]):
    """Dynamically register RBAC permissions for a role."""
    if role not in ROLE_PERMISSIONS:
        ROLE_PERMISSIONS[role] = {}
    for resource, actions in resource_permissions.items():
        if resource not in ROLE_PERMISSIONS[role]:
            ROLE_PERMISSIONS[role][resource] = set()
        ROLE_PERMISSIONS[role][resource].update(actions)


def register_rbac_role_alias(alias: str, canonical_role: str):
    """Dynamically register a role alias."""
    ROLE_ALIASES[alias.strip().lower()] = canonical_role


def register_rbac_masking_rule(field_name: str, mask_fn: Callable[[Any], Any]):
    """Dynamically register a masking rule."""
    MASKING_RULES[field_name] = mask_fn


def register_rbac_site_scoped_role(role: str):
    """Dynamically register a role as site-scoped."""
    SITE_SCOPED_ROLES.add(role)


def register_rbac_role_unmasked_fields(role: str, fields: set[str]):
    """Dynamically register unmasked fields for a role."""
    if role not in ROLE_UNMASKED_FIELDS:
        ROLE_UNMASKED_FIELDS[role] = set()
    ROLE_UNMASKED_FIELDS[role].update(fields)


def register_rbac_constant(name: str, value: Any):
    """Dynamically register any module-level constant."""
    _DYNAMIC_CONSTANTS[name] = value
    if name == "AUDITOR_ROLES":
        AUDITOR_ROLES.clear()
        AUDITOR_ROLES.update(value)
    # Also expose in global namespace of this module
    globals()[name] = value


def __getattr__(name: str) -> Any:
    if name in _DYNAMIC_CONSTANTS:
        return _DYNAMIC_CONSTANTS[name]
    if name.startswith("ROLE_"):
        # Derive canonical name on the fly
        parts = name[5:].lower().split("_")
        if len(parts) > 1 and parts[-1] == "canonical":
            parts = parts[:-1]
        val = "_".join(parts)
        if name == "ROLE_CRA":
            val = "CRA"
        elif name == "ROLE_DATA_MANAGER":
            val = "Data Manager"
        elif name == "ROLE_SITE_INVESTIGATOR":
            val = "Site Investigator"
        elif name == "ROLE_AUDITOR":
            val = "Auditor"
        elif name == "ROLE_SPONSOR_ADMIN":
            val = "Sponsor Admin"
        return val
    if name == "AUDITOR_ROLES":
        return AUDITOR_ROLES
    raise AttributeError(f"module '{__name__}' has no attribute '{name}'")

# Traceability Note: Principal now captures sponsor scope (sponsor_id) as a contract change per ADR-86.
class Principal(BaseModel):
    user_id: str
    roles: list[str]  # Normalized canonical roles
    assigned_sites: list[str] = Field(default_factory=list)
    assigned_studies: list[str] = Field(default_factory=list)
    unblinded_access: bool = False
    sponsor_id: str | None = None
    change_reason: str | None = None
    raw_roles: list[str] = Field(default_factory=list)


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

    # Determine expanded list of roles to check permissions for
    roles_to_check = list(principal.roles)
    for r in principal.raw_roles:
        norm_r = r.strip().lower()
        if norm_r not in roles_to_check:
            roles_to_check.append(norm_r)
        if norm_r in ("admin", "sponsor admin", "sponsor_admin"):
            if "admin" not in roles_to_check:
                roles_to_check.append("admin")

    for role in roles_to_check:
        perms = ROLE_PERMISSIONS.get(role, {})
        if resource in perms and action in perms[resource]:
            return True
    return False


def can_access_site(principal: Principal, site_id: str) -> bool:
    """
    Determine whether a principal is permitted to access a given site.

    Site-scoped roles (e.g., investigators, CRCs, CRAs, ER physicians, lead
    investigators) are restricted to the sites listed in *principal.assigned_sites*.
    Sponsor/SysAdmin principals with an empty *assigned_sites* list are granted
    global access. The function is fail-closed: a site-scoped user with no
    assigned sites is denied access everywhere.

    Args:
        principal: The authenticated principal making the request.
        site_id: The site identifier to check access for.

    Returns:
        True if the principal may access the site; False otherwise.
    """
    user_site_roles = [r for r in principal.roles if r in SITE_SCOPED_ROLES]

    # Fail-closed handling for missing/empty site_id on legacy/study-level rows
    if site_id is None or str(site_id).strip() == "":
        return not (user_site_roles or principal.assigned_sites)

    if user_site_roles:
        return site_id in principal.assigned_sites

    if principal.assigned_sites:
        return site_id in principal.assigned_sites

    return True


def can_access_study(principal: Principal, study_id: str) -> bool:
    """
    Checks if the principal has access to a specific study.
    Study-scoped users are restricted to their assigned_studies.
    """
    if study_id is None or str(study_id).strip() == "":
        return not ("external_monitor" in principal.roles or principal.assigned_studies)

    if "external_monitor" in principal.roles:
        return study_id in principal.assigned_studies
    if principal.assigned_studies:
        return study_id in principal.assigned_studies
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
        raw_roles_list = [r.strip() for r in roles_val.split(",") if r.strip()]
    elif isinstance(roles_val, list):
        raw_roles = [str(r).strip().lower() for r in roles_val if str(r).strip()]
        raw_roles_list = [str(r).strip() for r in roles_val if str(r).strip()]
    else:
        raw_roles = []
        raw_roles_list = []

    normalized_roles = [normalize_role(r) for r in raw_roles]

    if globals().get("ROLE_EXTERNAL_MONITOR", "external_monitor") in normalized_roles:
        raise HTTPException(
            status_code=500,
            detail="External Monitor principal must be resolved via the async get_principal path to allow directory enrichment.",
        )

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

    # 3.5. Sponsor ID
    sponsor_id_val = None
    if hasattr(request, "state"):
        sponsor_id_val = getattr(request.state, "sponsor_id", None)
    if sponsor_id_val is None and hasattr(request, "headers"):
        sponsor_id_val = (
            request.headers.get("X-Sponsor-Id")
            or request.headers.get("x-sponsor-id")
            or ""
        )

    sponsor_id = None
    if sponsor_id_val:
        if isinstance(sponsor_id_val, list):
            sponsor_id = ",".join(
                str(s).strip() for s in sponsor_id_val if str(s).strip()
            )
        else:
            sponsor_id = ",".join(
                s.strip() for s in str(sponsor_id_val).split(",") if s.strip()
            )
        if not sponsor_id:
            sponsor_id = None

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
        sponsor_id=sponsor_id,
        change_reason=change_reason,
        raw_roles=raw_roles_list,
    )


async def get_principal(request: Request) -> Principal:
    """
    FastAPI dependency to extract identity and authorization attributes
    from request context and headers, returning a normalized Principal.
    """
    import json

    # Bypass sync exception if we are inside get_principal by extracting manually
    user_id = ""
    if hasattr(request, "state"):
        user_id = getattr(request.state, "user_id", None) or ""
    if not user_id and hasattr(request, "headers"):
        user_id = (
            request.headers.get("X-User-Id") or request.headers.get("x-user-id") or ""
        )

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
        raw_roles_list = [r.strip() for r in roles_val.split(",") if r.strip()]
    elif isinstance(roles_val, list):
        raw_roles = [str(r).strip().lower() for r in roles_val if str(r).strip()]
        raw_roles_list = [str(r).strip() for r in roles_val if str(r).strip()]
    else:
        raw_roles = []
        raw_roles_list = []

    normalized_roles = [normalize_role(r) for r in raw_roles]

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

    # 3.5. Sponsor ID
    sponsor_id_val = None
    if hasattr(request, "state"):
        sponsor_id_val = getattr(request.state, "sponsor_id", None)
    if sponsor_id_val is None and hasattr(request, "headers"):
        sponsor_id_val = (
            request.headers.get("X-Sponsor-Id")
            or request.headers.get("x-sponsor-id")
            or ""
        )

    sponsor_id = None
    if sponsor_id_val:
        if isinstance(sponsor_id_val, list):
            sponsor_id = ",".join(
                str(s).strip() for s in sponsor_id_val if str(s).strip()
            )
        else:
            sponsor_id = ",".join(
                s.strip() for s in str(sponsor_id_val).split(",") if s.strip()
            )
        if not sponsor_id:
            sponsor_id = None

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

    change_reason = None
    if hasattr(request, "state"):
        change_reason = getattr(request.state, "change_reason", None) or getattr(
            request.state, "reason_for_change", None
        )
        if change_reason:
            change_reason = str(change_reason).strip()

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

    principal = Principal(
        user_id=user_id,
        roles=normalized_roles,
        assigned_sites=assigned_sites,
        unblinded_access=unblinded_access,
        sponsor_id=sponsor_id,
        change_reason=change_reason,
        raw_roles=raw_roles_list,
    )

    if globals().get("ROLE_EXTERNAL_MONITOR", "external_monitor") in principal.roles:
        from packages.security.org_client import resolve_personnel_assignments

        res = await resolve_personnel_assignments(principal.user_id)
        if res:
            principal.assigned_sites = res.get("assigned_sites", [])
            principal.assigned_studies = res.get("assigned_studies", [])

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

                    def find_reason_in_dict(d: dict) -> str | None:
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

    # Reject write operations with a descriptive error if the resolved change justification is missing
    if (
        hasattr(request, "method")
        and request.method
        in (
            "POST",
            "PUT",
            "PATCH",
            "DELETE",
        )
        and (not principal.change_reason or not principal.change_reason.strip())
    ):
        raise HTTPException(
            status_code=403,
            detail="Missing change justification reason",
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


class StudyScopeChecker:
    """
    A class dependency that ensures the incoming request principal has access
    to the study_id referenced in the request.
    It resolves study_id from the path parameters, query parameters,
    'X-Study-Id' or 'x-study-id' headers, or finally the JSON body (injecting it back).
    """

    async def __call__(
        self, request: Request, principal: Principal = Depends(get_principal)
    ) -> Principal:
        study_id = (
            request.path_params.get("study_id")
            or request.query_params.get("study_id")
            or request.headers.get("X-Study-Id")
            or request.headers.get("x-study-id")
        )
        if not study_id:
            try:
                content_type = request.headers.get("content-type", "")
                if "application/json" in content_type:
                    body_bytes = await request.body()
                    if body_bytes:
                        import json

                        body = json.loads(body_bytes)
                        if isinstance(body, dict):
                            study_id = body.get("study_id") or body.get("id")

                        async def receive():
                            return {
                                "type": "http.request",
                                "body": body_bytes,
                                "more_body": False,
                            }

                        request._receive = receive
            except Exception:
                pass

        if study_id:
            study_id = str(study_id).strip()

        if study_id and not can_access_study(principal, study_id):
            raise HTTPException(
                status_code=403,
                detail="Forbidden: Insufficient scope access for this study.",
            )

        return principal


def require_study_scope() -> StudyScopeChecker:
    """
    Dependency factory providing the StudyScopeChecker instance to enforce study access control.
    """
    return StudyScopeChecker()


def mask_payload(payload: Any, principal: Principal) -> Any:
    """Recursively mask sensitive fields in dictionaries, lists, or Pydantic models based on principal authorization.

    If principal.unblinded_access is True, no masking is performed and the original payload is returned unchanged.
    Otherwise, if the principal possesses unblinded RTSM roles configured in ROLE_UNMASKED_FIELDS, field-level
    masking rules are bypassed for fields included in the principal's unmasked set.

    Args:
        payload: The target data structure (dict, list, or Pydantic BaseModel instance) to mask.
        principal: The authenticated Principal whose roles and unblinded_access status govern field masking.

    Returns:
        The recursively masked data structure or dictionary.

    Raises:
        ValueError: If payload structure cannot be processed.
    """
    if principal.unblinded_access:
        return payload

    # Find if any RTSM role-specific policies apply
    rtsm_roles = [r for r in principal.roles if r in ROLE_UNMASKED_FIELDS]
    if rtsm_roles:
        # Union of all unmasked fields for their active RTSM roles
        unmasked_fields: set[str] = set()
        for r in rtsm_roles:
            unmasked_fields.update(ROLE_UNMASKED_FIELDS[r])
        return _recursive_mask(payload, unmasked_fields=unmasked_fields)

    return _recursive_mask(payload)


def _recursive_mask(data: Any, unmasked_fields: set[str] | None = None) -> Any:
    if data is None:
        return None

    if isinstance(data, pydantic.BaseModel):
        dumped = data.model_dump()
        masked = _recursive_mask(dumped, unmasked_fields)
        try:
            return data.__class__.model_validate(masked)
        except Exception:
            return masked

    if isinstance(data, dict):
        new_dict = {}
        for k, v in data.items():
            k_lower = k.lower()
            if k_lower in MASKING_RULES and (
                unmasked_fields is None or k_lower not in unmasked_fields
            ):
                new_dict[k] = MASKING_RULES[k_lower](v)
            else:
                new_dict[k] = _recursive_mask(v, unmasked_fields)
        return new_dict

    if isinstance(data, list):
        return [_recursive_mask(item, unmasked_fields) for item in data]

    if isinstance(data, tuple):
        return tuple(_recursive_mask(item, unmasked_fields) for item in data)

    if isinstance(data, set):
        return {_recursive_mask(item, unmasked_fields) for item in data}

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


ROLE_EXPANSIONS: dict[str, set[str]] = {}


def register_rbac_role_expansion(role: str, expansions: set[str]):
    """Dynamically register expansions for a role."""
    if role not in ROLE_EXPANSIONS:
        ROLE_EXPANSIONS[role] = set()
    ROLE_EXPANSIONS[role].update(expansions)



def require_roles(*allowed_roles: str, detail: str | None = None):
    """
    FastAPI dependency factory to enforce that the caller has at least one of the allowed roles.
    Allows case-insensitive, whitespace-insensitive matches and role synonym expansion.
    """

    def dependency(request: Request) -> list[str]:
        raw_roles = get_normalized_roles(request)
        roles = []
        for r in raw_roles:
            norm_r = r.strip().lower()
            if norm_r in ("sponsor admin", "sponsor_admin"):
                roles.append("sponsor_admin")
            else:
                roles.append(normalize_role(r))
        expanded_allowed = set()
        for role in allowed_roles:
            norm_role = role.strip().lower()
            # Normalize allowed roles as well so we can compare canonical forms
            norm_role_canonical = (
                "sponsor_admin"
                if norm_role in ("sponsor admin", "sponsor_admin")
                else normalize_role(norm_role)
            )
            expanded_allowed.add(norm_role_canonical)
            # Match role expansions case-insensitively for complete safety across different canonical forms
            for k, v in ROLE_EXPANSIONS.items():
                if k.lower() in (norm_role_canonical.lower(), norm_role.lower()):
                    expanded_allowed.update(v)

        if not any(role in expanded_allowed for role in roles):
            raise HTTPException(
                status_code=403,
                detail=detail or "User role is not authorized for this action.",
            )
        return roles

    return dependency
