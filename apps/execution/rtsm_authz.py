from typing import Any, Optional

from fastapi import HTTPException, status

from apps.execution.notifications_client import publish_notification, run_async
from packages.security.rbac import (
    SITE_SCOPED_ROLES,
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
    """Construct and non-blockingly dispatch security access violation alerts to critical roles.

    Args:
        principal: The authenticated Principal who triggered the security violation.
        site_id: Optional site scope identifier associated with the request context.
        study_id: Optional study scope identifier associated with the request context.
        subject_id: Optional subject identifier associated with the request context.

    Returns:
        None.
    """
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
        # Non-blocking async submission
        run_async(publish_notification(payload))


def verify_site_access(
    principal: Principal,
    site_id: Optional[str],
    study_id: Optional[str] = None,
    subject_id: Optional[str] = None,
) -> None:
    """Enforce site and study isolation for site-scoped or restricted resources.

    Validates that the requesting principal possesses authorized access to the requested
    study and site boundaries. If access is unauthorized, dispatches a security alert and
    raises an HTTP 403 Forbidden exception.

    Args:
        principal: The authenticated Principal making the request.
        site_id: Target site identifier for the requested resource.
        study_id: Target study identifier for the requested resource.
        subject_id: Optional subject identifier for context tracking.

    Returns:
        None.

    Raises:
        HTTPException: HTTP 403 Forbidden if site or study access isolation checks fail.
    """
    if study_id is not None:
        if not can_access_study(principal, study_id):
            dispatch_access_violation_alert(
                principal, site_id, study_id=study_id, subject_id=subject_id
            )
            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN,
                detail="Forbidden: access restricted to your assigned study.",
            )

    # If the resource has no site restriction (site_id is None), site isolation does not apply
    if site_id is None or str(site_id).strip() == "":
        return

    is_site_scoped_user = any(role in SITE_SCOPED_ROLES for role in principal.roles)

    # Deny site-scoped users who lack site assignments when requesting a site-restricted resource
    if is_site_scoped_user and not principal.assigned_sites:
        dispatch_access_violation_alert(
            principal, site_id, study_id=study_id, subject_id=subject_id
        )
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Forbidden: access restricted to your assigned site(s).",
        )

    # Globally authorized non-site-scoped users (e.g. sysadmin, sponsor_dm, auditor) bypass site checks
    if not principal.assigned_sites and not is_site_scoped_user:
        return

    if not can_access_site(principal, site_id):
        dispatch_access_violation_alert(
            principal, site_id, study_id=study_id, subject_id=subject_id
        )
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Forbidden: access restricted to your assigned site(s).",
        )


def redact_response(payload: Any, principal: Principal) -> Any:
    """Apply role-aware masking/redaction to outgoing payloads based on principal roles and access.

    Args:
        payload: The outgoing data structure or model to redact.
        principal: The requesting Principal whose roles determine unmasking policies.

    Returns:
        The redacted data structure.
    """
    return mask_payload(payload, principal)
