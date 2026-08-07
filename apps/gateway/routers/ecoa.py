"""API Gateway router for eCOA/ePRO self-service and management endpoints.

Fronts the interop ePRO endpoints, enforces permissions, subject ownership,
scoping, and forwards signed requests downstream to the interop service.

Requirements: PRD-SYS-001
"""

import os
from typing import Any

from fastapi import APIRouter, Depends, HTTPException, Request, status

from apps.execution.src.domain.epro_transport_models import (
    InstrumentCreate,
    InstrumentResponse,
    SubjectAssignmentCreate,
    SubjectAssignmentResponse,
    SubjectComplianceResponse,
)
from apps.execution.src.domain.offline_models import (
    AcknowledgeNotificationRequest,
    EPROBulkSyncRequest,
    EPROBulkSyncResponse,
    EPROOfflineEntry,
    EPROSubmitResponse,
    SubjectNotificationResponse,
)
from packages.security.gateway_client import GatewayBaseClient
from packages.security.middleware import get_current_user
from packages.security.rbac import (
    Principal,
    require_permission,
    require_study_scope,
)

router = APIRouter()


def _get_interop_client() -> GatewayBaseClient:
    """Instantiate a GatewayBaseClient for the interop service."""
    interop_url = os.getenv("INTEROP_URL", "http://localhost:8004")
    return GatewayBaseClient(base_url=interop_url)


def _enforce_subject_boundary(user: dict, subject_id: str) -> None:
    """Enforce the Subject-role ownership boundary.

    If the authenticated user possesses the 'Subject' role, checks if
    the subject_id equals the authenticated sub (user_id). If there's a
    mismatch, aborts with a 403 detail "Access denied".
    """
    roles = [r.lower() for r in user.get("roles", [])]
    if "subject" in roles:
        if user.get("sub") != subject_id:
            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN,
                detail="Access denied",
            )


def _enforce_bulk_subject_boundary(user: dict, subject_ids: list[str]) -> None:
    """Enforce the Subject-role ownership boundary for bulk submissions."""
    roles = [r.lower() for r in user.get("roles", [])]
    if "subject" in roles:
        for sub_id in subject_ids:
            if user.get("sub") != sub_id:
                raise HTTPException(
                    status_code=status.HTTP_403_FORBIDDEN,
                    detail="Access denied",
                )


async def _forward_request(
    method: str,
    path: str,
    principal: Principal,
    user: dict,
    request: Request,
    headers: dict | None = None,
    json: Any = None,
    params: Any = None,
) -> Any:
    """Forward a gateway request downstream with signed headers."""
    client = _get_interop_client()
    change_reason = (
        principal.change_reason
        or request.headers.get("X-Change-Reason")
        or request.headers.get("x-change-reason")
        or "eCOA Operation"
    )
    site_id = ",".join(principal.assigned_sites) if principal.assigned_sites else None
    sponsor_id = principal.sponsor_id
    unblinded_access = principal.unblinded_access
    tenant_id = user.get("tenant_id", "tenant_default")

    extra_headers = headers or {}
    sig_token = request.headers.get("x-sig-token") or request.headers.get("X-Sig-Token")
    if sig_token:
        extra_headers["X-Sig-Token"] = sig_token
    extra_headers["X-Change-Reason"] = change_reason

    try:
        response = await client.request(
            method=method,
            path=path,
            user_id=principal.user_id,
            roles=",".join(principal.roles),
            change_reason=change_reason,
            site_id=site_id,
            sponsor_id=sponsor_id,
            unblinded_access=unblinded_access,
            tenant_id=tenant_id,
            headers=extra_headers,
            json=json,
            params=params,
        )
    except Exception as exc:
        raise HTTPException(
            status_code=status.HTTP_502_BAD_GATEWAY,
            detail=f"Bad Gateway: {str(exc)}",
        ) from exc

    if response.status_code < 200 or response.status_code >= 300:
        try:
            detail = response.json().get("detail", response.text)
        except Exception:
            detail = response.text
        raise HTTPException(status_code=response.status_code, detail=detail)

    return response.json()


@router.post(
    "/epro/submit",
    response_model=EPROSubmitResponse,
    status_code=status.HTTP_201_CREATED,
)
async def submit_epro_entry(
    request: Request,
    body: EPROOfflineEntry,
    user: dict = Depends(get_current_user),
    principal: Principal = Depends(require_permission("ecoa_submission:create")),
) -> EPROSubmitResponse:
    """Submit a single participant ePRO/eCOA diary submission.

    Requirements: PRD-SYS-001
    """
    _enforce_subject_boundary(user, body.subject_id)
    result = await _forward_request(
        method="POST",
        path="/api/v1/interop/epro/submit",
        principal=principal,
        user=user,
        request=request,
        json=body.model_dump(mode="json"),
    )
    return EPROSubmitResponse.model_validate(result)


@router.post(
    "/epro/sync",
    response_model=EPROBulkSyncResponse,
    status_code=status.HTTP_200_OK,
)
async def sync_epro_entries(
    request: Request,
    body: EPROBulkSyncRequest,
    user: dict = Depends(get_current_user),
    principal: Principal = Depends(require_permission("ecoa_submission:create")),
) -> EPROBulkSyncResponse:
    """Bulk sync offline participant ePRO submissions.

    Requirements: PRD-SYS-001
    """
    _enforce_bulk_subject_boundary(user, [sub.subject_id for sub in body.submissions])
    result = await _forward_request(
        method="POST",
        path="/api/v1/interop/epro/sync",
        principal=principal,
        user=user,
        request=request,
        json=body.model_dump(mode="json"),
    )
    return EPROBulkSyncResponse.model_validate(result)


@router.get(
    "/instruments/{id}",
    response_model=InstrumentResponse,
    status_code=status.HTTP_200_OK,
)
async def get_instrument(
    request: Request,
    id: str,
    user: dict = Depends(get_current_user),
    principal: Principal = Depends(require_permission("ecoa_diary:read")),
) -> InstrumentResponse:
    """Retrieve an eCOA instrument definition by ID.

    Requirements: PRD-SYS-001
    """
    result = await _forward_request(
        method="GET",
        path=f"/api/v1/interop/instruments/{id}",
        principal=principal,
        user=user,
        request=request,
    )
    return InstrumentResponse.model_validate(result)


@router.get(
    "/assignments/subject/{subject_id}",
    response_model=list[SubjectAssignmentResponse],
    status_code=status.HTTP_200_OK,
)
async def get_subject_assignments(
    request: Request,
    subject_id: str,
    user: dict = Depends(get_current_user),
    principal: Principal = Depends(require_permission("ecoa_schedule:read")),
    _scope: Principal = Depends(require_study_scope()),
) -> list[SubjectAssignmentResponse]:
    """Retrieve all assignments for a given subject.

    Requirements: PRD-SYS-001
    """
    _enforce_subject_boundary(user, subject_id)
    result = await _forward_request(
        method="GET",
        path=f"/api/v1/interop/assignments/subject/{subject_id}",
        principal=principal,
        user=user,
        request=request,
        params=request.query_params,
    )
    return [SubjectAssignmentResponse.model_validate(item) for item in result]


@router.get(
    "/subjects/{subject_id}/compliance",
    response_model=SubjectComplianceResponse,
    status_code=status.HTTP_200_OK,
)
async def get_subject_compliance(
    request: Request,
    subject_id: str,
    user: dict = Depends(get_current_user),
    principal: Principal = Depends(require_permission("ecoa_schedule:read")),
    _scope: Principal = Depends(require_study_scope()),
) -> SubjectComplianceResponse:
    """Retrieve/compute eCOA compliance status metrics for a subject.

    Requirements: PRD-SYS-001
    """
    _enforce_subject_boundary(user, subject_id)
    result = await _forward_request(
        method="GET",
        path=f"/api/v1/interop/subjects/{subject_id}/compliance",
        principal=principal,
        user=user,
        request=request,
        params=request.query_params,
    )
    return SubjectComplianceResponse.model_validate(result)


@router.get(
    "/subjects/{subject_id}/instruments",
    response_model=list[InstrumentResponse],
    status_code=status.HTTP_200_OK,
)
async def get_subject_instruments(
    request: Request,
    subject_id: str,
    user: dict = Depends(get_current_user),
    principal: Principal = Depends(require_permission("ecoa_diary:read")),
    _scope: Principal = Depends(require_study_scope()),
) -> list[InstrumentResponse]:
    """Retrieve all unique assigned instruments for a given subject.

    Requirements: PRD-SYS-001
    """
    _enforce_subject_boundary(user, subject_id)
    result = await _forward_request(
        method="GET",
        path=f"/api/v1/interop/subjects/{subject_id}/instruments",
        principal=principal,
        user=user,
        request=request,
        params=request.query_params,
    )
    return [InstrumentResponse.model_validate(item) for item in result]


@router.get(
    "/subjects/{subject_id}/notifications",
    response_model=list[SubjectNotificationResponse],
    status_code=status.HTTP_200_OK,
)
async def get_subject_notifications(
    request: Request,
    subject_id: str,
    user: dict = Depends(get_current_user),
    principal: Principal = Depends(require_permission("ecoa_schedule:read")),
    _scope: Principal = Depends(require_study_scope()),
) -> list[SubjectNotificationResponse]:
    """Retrieve all notifications for a given subject.

    Requirements: PRD-SYS-001
    """
    _enforce_subject_boundary(user, subject_id)
    result = await _forward_request(
        method="GET",
        path=f"/api/v1/interop/subjects/{subject_id}/notifications",
        principal=principal,
        user=user,
        request=request,
        params=request.query_params,
    )
    return [SubjectNotificationResponse.model_validate(item) for item in result]


@router.post(
    "/notifications/{notification_id}/acknowledge",
    response_model=SubjectNotificationResponse,
    status_code=status.HTTP_200_OK,
)
async def acknowledge_notification(
    request: Request,
    notification_id: str,
    body: AcknowledgeNotificationRequest,
    user: dict = Depends(get_current_user),
    principal: Principal = Depends(require_permission("ecoa_schedule:read")),
    _scope: Principal = Depends(require_study_scope()),
) -> SubjectNotificationResponse:
    """Acknowledge/read a notification.

    Requirements: PRD-SYS-001
    """
    result = await _forward_request(
        method="POST",
        path=f"/api/v1/interop/notifications/{notification_id}/acknowledge",
        principal=principal,
        user=user,
        request=request,
        json=body.model_dump(mode="json"),
    )
    # Perform extra Subject role check on the returned notification's subject_id
    _enforce_subject_boundary(user, result.get("subject_id"))
    return SubjectNotificationResponse.model_validate(result)


@router.post(
    "/instruments",
    response_model=InstrumentResponse,
    status_code=status.HTTP_201_CREATED,
)
async def create_instrument(
    request: Request,
    body: InstrumentCreate,
    user: dict = Depends(get_current_user),
    principal: Principal = Depends(require_permission("ecoa_diary:create")),
    _scope: Principal = Depends(require_study_scope()),
) -> InstrumentResponse:
    """Author a new eCOA questionnaire/diary definition.

    Requirements: PRD-SYS-001
    """
    result = await _forward_request(
        method="POST",
        path="/api/v1/interop/instruments",
        principal=principal,
        user=user,
        request=request,
        json=body.model_dump(mode="json"),
    )
    return InstrumentResponse.model_validate(result)


@router.post(
    "/assignments",
    response_model=SubjectAssignmentResponse,
    status_code=status.HTTP_201_CREATED,
)
async def create_subject_assignment(
    request: Request,
    body: SubjectAssignmentCreate,
    user: dict = Depends(get_current_user),
    principal: Principal = Depends(require_permission("ecoa_schedule:create")),
    _scope: Principal = Depends(require_study_scope()),
) -> SubjectAssignmentResponse:
    """Assign an eCOA instrument to a subject with due/recurrence window data.

    Requirements: PRD-SYS-001
    """
    result = await _forward_request(
        method="POST",
        path="/api/v1/interop/assignments",
        principal=principal,
        user=user,
        request=request,
        json=body.model_dump(mode="json"),
    )
    return SubjectAssignmentResponse.model_validate(result)
