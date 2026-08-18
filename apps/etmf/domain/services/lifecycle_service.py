"""Quality Control (QC) review lifecycle state machine for the eTMF domain.

Defines allowed transitions, role permissions, and validation logic in compliance with 21 CFR Part 11.
"""

from typing import Any

from apps.etmf.domain.models import DocumentStatus
from packages.security.rbac import Principal, has_permission, normalize_role

ALLOWED_TRANSITIONS: dict[str, set[str]] = {
    DocumentStatus.DRAFT.value: {DocumentStatus.TECHNICAL_QC.value},
    DocumentStatus.TECHNICAL_QC.value: {
        DocumentStatus.CLINICAL_QC.value,
        DocumentStatus.REJECTED.value,
    },
    DocumentStatus.CLINICAL_QC.value: {
        DocumentStatus.APPROVED.value,
        DocumentStatus.REJECTED.value,
        DocumentStatus.SIGNED.value,
    },
    DocumentStatus.APPROVED.value: {
        DocumentStatus.ARCHIVED.value,
        DocumentStatus.REJECTED.value,
        DocumentStatus.SIGNED.value,
    },
    DocumentStatus.REJECTED.value: {DocumentStatus.DRAFT.value},
    DocumentStatus.ARCHIVED.value: set(),
    DocumentStatus.SIGNED.value: set(),
}


def has_required_role(actor_role: str | list[str], target_status: str) -> bool:
    """Checks if the given actor roles possess the permission to transition to the target status."""
    if isinstance(actor_role, str):
        raw_roles = [r.strip() for r in actor_role.split(",") if r.strip()]
    else:
        raw_roles = [str(r).strip() for r in actor_role if str(r).strip()]
    actor_roles = [normalize_role(r) for r in raw_roles]

    principal = Principal(
        user_id="lifecycle_actor",
        roles=actor_roles,
        raw_roles=raw_roles,
    )
    perm_to_check = f"etmf_document:transition_{target_status.lower()}"
    return has_permission(principal, perm_to_check)


def is_site_scoped_user(principal: Principal) -> bool:
    """Determines if the principal is a site-scoped user."""
    site_scoped_roles = {
        "investigator",
        "crc",
        "monitor",
        "external_monitor",
    }
    has_site_role = any(r in site_scoped_roles for r in principal.roles)
    if has_site_role:
        return True
    if "cra" in principal.roles:
        return len(principal.assigned_sites) > 0
    return False


def validate_document_transition(
    document: Any,
    to_status: str,
    actor_role: str | list[str],
    reason_for_change: str,
) -> None:
    """Validates that a status transition conforms to the state-machine, RBAC rules, and Part 11 requirements."""
    valid_statuses = {
        DocumentStatus.DRAFT.value,
        DocumentStatus.TECHNICAL_QC.value,
        DocumentStatus.CLINICAL_QC.value,
        DocumentStatus.APPROVED.value,
        DocumentStatus.ARCHIVED.value,
        DocumentStatus.REJECTED.value,
        DocumentStatus.SIGNED.value,
    }

    if to_status not in valid_statuses:
        raise ValueError(
            f"Invalid status: '{to_status}'. Must be one of {sorted(list(valid_statuses))}."
        )

    current = getattr(document, "status", None) or DocumentStatus.DRAFT.value
    allowed = ALLOWED_TRANSITIONS.get(current, set())
    if to_status not in allowed:
        raise ValueError(
            f"Invalid transition: Cannot transition document from '{current}' to '{to_status}'."
        )

    if not has_required_role(actor_role, to_status):
        raise PermissionError(
            f"Permission Denied: User with role(s) '{actor_role}' is not authorized to transition document to status '{to_status}'."
        )

    if not reason_for_change or len(reason_for_change.strip()) < 10:
        raise ValueError(
            "Reason for change is mandatory and must be at least 10 characters long."
        )
