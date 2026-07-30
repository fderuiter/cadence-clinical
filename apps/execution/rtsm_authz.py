from typing import Any, Optional

from fastapi import HTTPException, status

from apps.execution.notifications_client import publish_notification, run_async
from packages.security.rbac import (
    Principal,
    can_access_site,
    can_access_study,
    mask_payload,
)


def dispatch_access_violation_alert(
    principal: Principal,
    site_id: Optional[str],
    study_id: Optional[str] = None,
    subject_id: Optional[str] = None,
) -> None:
    """Construct and dispatch access violation alerts to critical roles."""
    site_str = site_id or "None"
    study_str = study_id or "None"
    subject_str = subject_id or "None"

    message_content = (
        f"RTSM Access Violation: User {principal.user_id} with roles {principal.roles} "
        f"attempted unauthorized cross-site access. Coordinate Scope - "
        f"Site: {site_str}, Study: {study_str}, Subject: {subject_str}."
    )

    roles_to_alert = ["Lead CRA", "Sponsor Safety Lead", "IDMC"]
    for role in roles_to_alert:
        payload = {
            "recipient_role": role,
            "category": "ALERTS",
            "priority": "CRITICAL",
            "channels": "IN_APP",
            "message_content": message_content,
            "related_entity_type": "rtsm-access-violation",
            "related_entity_id": f"{site_str}:{principal.user_id}",
        }
        # Safely run asynchronously
        run_async(publish_notification(payload))


def verify_site_access(
    principal: Principal,
    site_id: Optional[str],
    study_id: Optional[str] = None,
    subject_id: Optional[str] = None,
) -> None:
    """Enforce site and study isolation for site-scoped or restricted resources.

    Raises HTTP 403 and triggers an audit alert on unauthorized mismatch/cross-site access.
    """
    if study_id is not None:
        if not can_access_study(principal, study_id):
            dispatch_access_violation_alert(
                principal, site_id, study_id=study_id, subject_id=subject_id
            )
            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN,
                detail="Forbidden: access restricted to your assigned site(s).",
            )

    if not can_access_site(principal, site_id):
        dispatch_access_violation_alert(
            principal, site_id, study_id=study_id, subject_id=subject_id
        )
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Forbidden: access restricted to your assigned site(s).",
        )


def redact_response(payload: Any, principal: Principal) -> Any:
    """Apply role-aware masking/redaction to outgoing payloads based on principal roles and access."""
    return mask_payload(payload, principal)
