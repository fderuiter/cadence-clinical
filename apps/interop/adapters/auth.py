"""
Authorization and identity helper utilities for FHIR and eCOA/ePRO.
"""

from fastapi import HTTPException, Request, status


def has_subject_role(request: Request) -> bool:
    roles_str = getattr(request.state, "roles", "")
    roles = [r.strip().lower() for r in roles_str.split(",") if r.strip()]
    return "subject" in roles


def verify_subject_identity(request: Request, subject_id: str) -> None:
    if has_subject_role(request):
        user_id = getattr(request.state, "user_id", "")
        if user_id != subject_id:
            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN,
                detail="Access denied: Subject cannot access or mutate records of another subject.",
            )


def verify_subject_bulk_identity(request: Request, subject_ids: list[str]) -> None:
    if has_subject_role(request):
        user_id = getattr(request.state, "user_id", "")
        for sub_id in subject_ids:
            if user_id != sub_id:
                raise HTTPException(
                    status_code=status.HTTP_403_FORBIDDEN,
                    detail="Access denied: Subject cannot access or mutate records of another subject.",
                )


def require_staff_role(request: Request) -> None:
    roles_str = getattr(request.state, "roles", "")
    roles = [r.strip().lower() for r in roles_str.split(",") if r.strip()]
    if "subject" in roles or not roles:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Access denied: Subject/unauthorized user cannot access staff endpoints.",
        )
