"""
Authorization and identity helper utilities for FHIR and eCOA/ePRO.

Provides reusable role verification and subject-scoped identity checks
aligned with 21 CFR Part 11 and secure diary-only patient boundary specifications.
"""

from typing import List

from fastapi import HTTPException, Request, status


def _is_subject(roles: List[str]) -> bool:
    """
    Helper predicate checking if roles contains "subject".
    """
    return any(r.strip().lower() == "subject" for r in roles)


def _is_data_manager(roles: List[str]) -> bool:
    """
    Helper predicate checking if roles contains standard data manager roles.
    """
    dm_roles = {"data manager", "data_manager", "cra"}
    return any(r.strip().lower() in dm_roles for r in roles)


def _is_investigator(roles: List[str]) -> bool:
    """
    Helper predicate checking if roles contains standard investigator roles.
    """
    inv_roles = {"site investigator", "site_investigator", "investigator", "crc"}
    return any(r.strip().lower() in inv_roles for r in roles)


def has_subject_role(request: Request) -> bool:
    """
    Check if the authenticated requester possesses the Subject/Patient role.

    Extracts roles from the request state injected by GatewayAuthMiddleware,
    normalizes them to lowercase, and checks for the existence of the
    "subject" role.

    Args:
        request (Request): The incoming FastAPI request.

    Returns:
        bool: True if the user has the "Subject" role, False otherwise.
    """
    roles_str = getattr(request.state, "roles", "")
    roles = [r.strip().lower() for r in roles_str.split(",") if r.strip()]
    return _is_subject(roles)


def verify_subject_identity(request: Request, subject_id: str) -> None:
    """
    Verify that if the request has a Subject role, its authenticated user_id matches subject_id.

    This binds subject-facing requests to the gateway-propagated authenticated identity
    and prevents subjects from reading/mutating other subjects' records.

    Args:
        request (Request): The incoming FastAPI request.
        subject_id (str): The subject identifier from the request payload.

    Raises:
        HTTPException: Raises 403 Forbidden if a role mismatch or identity discrepancy is detected.
    """
    if has_subject_role(request):
        user_id = getattr(request.state, "user_id", "")
        if user_id != subject_id:
            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN,
                detail="Access denied: Subject cannot access or mutate records of another subject.",
            )


def subject_identity_guard(request: Request, subject_id: str) -> str:
    """
    A FastAPI guard dependency that binds the request's subject_id to the authenticated principal from request.state.
    Reject cross-subject access (any payload subject_id not matching the authenticated subject) with 403.
    """
    verify_subject_identity(request, subject_id)
    return subject_id


def verify_subject_bulk_identity(request: Request, subject_ids: List[str]) -> None:
    """
    Verify that if the request has a Subject role, its authenticated user_id matches all target subject_ids.

    Used for validating bulk ePRO offline queue sync reconciliation payloads.

    Args:
        request (Request): The incoming FastAPI request.
        subject_ids (List[str]): All subject identifiers present in the bulk payload.

    Raises:
        HTTPException: Raises 403 Forbidden if any mismatch is found.
    """
    if has_subject_role(request):
        user_id = getattr(request.state, "user_id", "")
        for sub_id in subject_ids:
            if user_id != sub_id:
                raise HTTPException(
                    status_code=status.HTTP_403_FORBIDDEN,
                    detail="Access denied: Subject cannot access or mutate records of another subject.",
                )


def require_staff_role(request: Request) -> None:
    """
    Ensure the requester is a staff member and does not have the Subject role.

    Blocks patients/subjects from accessing staff-only routes like FHIR / eSource ingestion.

    Args:
        request (Request): The incoming FastAPI request.

    Raises:
        HTTPException: Raises 403 Forbidden if the user is a Subject or lacks roles.
    """
    roles_str = getattr(request.state, "roles", "")
    roles = [r.strip().lower() for r in roles_str.split(",") if r.strip()]
    if "subject" in roles or not roles:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Access denied: Subject/unauthorized user cannot access staff endpoints.",
        )
