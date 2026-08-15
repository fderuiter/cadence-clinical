"""FastAPI Router for eISF regulatory binder browsing, document ingestion, completeness, and sync endpoints.

Requirements: PRD-SYS-001
"""

import hashlib
import os
import time
from datetime import UTC, date, datetime

from fastapi import APIRouter, Depends, HTTPException, Query, Request, Response, status

from apps.eisf.domain.eisf_transport_models import (
    EISFDocumentDetail,
    EISFDocumentUploadRequest,
    EISFFolderNode,
)
from apps.eisf.domain.ports import EISFRepositoryPort
from apps.eisf.infrastructure.database import transactional
from apps.eisf.infrastructure.models import ISFAuditLog, ISFDocument
from apps.eisf.presentation.dtos import (
    BinderCompletenessResponse,
    BinderSectionStatus,
    DocumentCreate,
    DocumentResponse,
    DocumentUpdate,
    EISFIngestionRequest,
    EISFSyncRequest,
    EISFSyncResponse,
)
from packages.security.audit_logger import AuditLogPayload
from packages.security.audit_logger import audit_logger_engine as central_audit_logger
from packages.security.rbac import (
    SITE_SCOPED_ROLES,
    Principal,
    can_access_site,
    get_principal,
    has_permission,
    require_permission,
)

router = APIRouter()

_repo_instance = None


def get_eisf_repository() -> EISFRepositoryPort:
    import apps.eisf.main as main_module

    if hasattr(main_module, "_repo_instance"):
        return main_module._repo_instance
    from apps.eisf.infrastructure.repositories import SQLEISFRepository

    return SQLEISFRepository()


async def enforce_site_isolation(
    principal: Principal,
    site_id: str,
    repo: EISFRepositoryPort,
) -> None:
    if not can_access_site(principal, site_id):
        actor_id = principal.user_id or "system"
        actor_roles = (
            ",".join(principal.raw_roles)
            if principal.raw_roles
            else (",".join(principal.roles) if principal.roles else "anonymous")
        )
        caller_scope = (
            ",".join(principal.assigned_sites) if principal.assigned_sites else "global"
        )

        details = (
            f"SECURITY ALERT: Access Violation. User '{actor_id}' with roles '{actor_roles}' (scope: '{caller_scope}') "
            f"attempted to access/mutate resource at site '{site_id}' but is not permitted."
        )
        reason_for_change = "Security Violation: Cross-site access denied"

        alert = ISFAuditLog(
            actor_id=actor_id,
            actor_role=actor_roles,
            action="SECURITY_ALERT",
            details=details,
            reason_for_change=reason_for_change,
        )
        await repo.save_audit_log(alert)

        await repo.save_security_alert_out_of_band(
            ISFAuditLog(
                actor_id=actor_id,
                actor_role=actor_roles,
                action="SECURITY_ALERT",
                details=details,
                reason_for_change=reason_for_change,
            )
        )

        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Forbidden: Access is restricted to your assigned site.",
        )


enforce_document_site_visibility = enforce_site_isolation


async def write_local_audit_log(
    repo: EISFRepositoryPort,
    actor_id: str,
    actor_role: str,
    action: str,
    document_id: str | None,
    details: str,
    reason_for_change: str,
) -> None:
    log_entry = ISFAuditLog(
        actor_id=actor_id,
        actor_role=actor_role,
        action=action,
        document_id=document_id,
        details=details,
        reason_for_change=reason_for_change,
    )
    await repo.save_audit_log(log_entry)


write_audit_log = write_local_audit_log


@router.get(
    "/api/v1/eisf/sites/{site_id}/binder",
    response_model=list[EISFFolderNode],
)
@transactional
async def get_site_eisf_binder(
    site_id: str,
    repo: EISFRepositoryPort = Depends(get_eisf_repository),
    principal: Principal = Depends(get_principal),
) -> list[EISFFolderNode]:
    await enforce_site_isolation(principal, site_id, repo)

    docs = await repo.get_documents_by_site(site_id)

    sec01_count = sum(
        1
        for d in docs
        if d.binder_classification
        in (
            "SEC_01",
            "1_INVESTIGATOR_CV",
            "Investigator CV",
            "Investigator & Staff",
            "Investigator CVs",
            "Investigator Qualifications",
        )
    )
    sec02_count = sum(
        1
        for d in docs
        if d.binder_classification
        in (
            "SEC_02",
            "4_IRB_IEC_APPROVAL",
            "IRB Approval",
            "Regulatory Approvals",
            "IRB Approvals",
        )
    )
    sec03_count = sum(
        1
        for d in docs
        if d.binder_classification
        in (
            "SEC_03",
            "3_PROTOCOL_APPROVAL",
            "Protocol Sign-off",
            "Protocols & Amendments",
            "Protocol Sign-Offs",
        )
    )
    sec04_count = sum(
        1
        for d in docs
        if d.binder_classification
        in (
            "SEC_04",
            "7_DELEGATION_OF_AUTHORITY",
            "Delegation of Authority Log",
            "Delegation Log",
        )
    )

    return [
        EISFFolderNode(
            section_code="SEC_01",
            title="Section 1: Investigator CVs",
            document_count=sec01_count,
            subfolders=[],
        ),
        EISFFolderNode(
            section_code="SEC_02",
            title="Section 2: IRB Approvals",
            document_count=sec02_count,
            subfolders=[],
        ),
        EISFFolderNode(
            section_code="SEC_03",
            title="Section 3: Protocol Sign-Offs",
            document_count=sec03_count,
            subfolders=[],
        ),
        EISFFolderNode(
            section_code="SEC_04",
            title="Section 4: Delegation Log",
            document_count=sec04_count,
            subfolders=[],
        ),
    ]


@router.get(
    "/api/v1/eisf/sites/{site_id}/documents/{doc_id}",
    response_model=EISFDocumentDetail,
)
@transactional
async def get_site_document_detail(
    site_id: str,
    doc_id: str,
    repo: EISFRepositoryPort = Depends(get_eisf_repository),
    principal: Principal = Depends(get_principal),
) -> EISFDocumentDetail:
    await enforce_site_isolation(principal, site_id, repo)

    doc = await repo.get_document_by_id(doc_id)
    if not doc or doc.site_id != site_id:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"eISF Document with ID '{doc_id}' not found.",
        )

    actor_roles = (
        ",".join(principal.raw_roles)
        if principal.raw_roles
        else (",".join(principal.roles) if principal.roles else "anonymous")
    )
    await write_local_audit_log(
        repo=repo,
        actor_id=principal.user_id or "system",
        actor_role=actor_roles,
        action="VIEW",
        document_id=doc.id,
        details=f"Viewed document '{doc.filename}' (ID: {doc.id}).",
        reason_for_change="Standard document access",
    )

    audit_payload = AuditLogPayload(
        service_name="eisf",
        action_type="EISF_DOCUMENT_ACCESSED",
        entity_name="ISFDocument",
        entity_id=doc.id,
        user_id=principal.user_id or "system",
        tenant_id=getattr(principal, "tenant_id", "tenant_default") or "tenant_default",
        reason_for_change="Standard document detail view",
        details={
            "event": "EISF_DOCUMENT_ACCESSED",
            "filename": doc.filename,
            "site_id": site_id,
            "section_code": doc.binder_classification,
            "version": str(doc.version_index),
        },
    )
    central_audit_logger.log_event(audit_payload)

    return EISFDocumentDetail(
        id=doc.id,
        site_id=site_id,
        section_code=doc.binder_classification,
        filename=doc.filename,
        version=str(doc.version_index),
        expiration_date=doc.expiration_date.isoformat()
        if doc.expiration_date
        else None,
        created_at=doc.created_at,
        created_by=doc.created_by,
        download_url=f"/api/v1/eisf/sites/{site_id}/documents/{doc.id}/download",
    )


@router.get(
    "/api/v1/eisf/sites/{site_id}/documents/{doc_id}/download",
)
@transactional
async def download_site_document(
    site_id: str,
    doc_id: str,
    repo: EISFRepositoryPort = Depends(get_eisf_repository),
    principal: Principal = Depends(get_principal),
):
    await enforce_site_isolation(principal, site_id, repo)

    doc = await repo.get_document_by_id(doc_id)
    if not doc or doc.site_id != site_id:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"eISF Document with ID '{doc_id}' not found.",
        )

    actor_roles = (
        ",".join(principal.raw_roles)
        if principal.raw_roles
        else (",".join(principal.roles) if principal.roles else "anonymous")
    )
    await write_local_audit_log(
        repo=repo,
        actor_id=principal.user_id or "system",
        actor_role=actor_roles,
        action="DOWNLOAD",
        document_id=doc.id,
        details=f"Downloaded document '{doc.filename}' (ID: {doc.id}).",
        reason_for_change="Standard document download",
    )

    audit_payload = AuditLogPayload(
        service_name="eisf",
        action_type="EISF_DOCUMENT_ACCESSED",
        entity_name="ISFDocument",
        entity_id=doc.id,
        user_id=principal.user_id or "system",
        tenant_id=getattr(principal, "tenant_id", "tenant_default") or "tenant_default",
        reason_for_change="Standard document download",
        details={
            "event": "EISF_DOCUMENT_ACCESSED",
            "filename": doc.filename,
            "site_id": site_id,
            "section_code": doc.binder_classification,
            "version": str(doc.version_index),
        },
    )
    central_audit_logger.log_event(audit_payload)

    return Response(
        content=doc.content,
        media_type=doc.mime_type,
        headers={"Content-Disposition": f"attachment; filename={doc.filename}"},
    )


@router.post(
    "/api/v1/eisf/sites/{site_id}/documents/upload",
    response_model=EISFDocumentDetail,
    status_code=status.HTTP_201_CREATED,
)
@transactional
async def upload_site_document(
    site_id: str,
    payload: EISFDocumentUploadRequest,
    _not_auditor=Depends(require_permission("eisf_document:create")),
    repo: EISFRepositoryPort = Depends(get_eisf_repository),
    principal: Principal = Depends(get_principal),
) -> EISFDocumentDetail:
    await enforce_site_isolation(principal, site_id, repo)

    if not payload.reason_for_change or len(payload.reason_for_change.strip()) < 10:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Part 11 change justification reason is required and must be at least 10 characters long.",
        )

    exp_date = None
    if payload.expiration_date:
        try:
            exp_date = date.fromisoformat(payload.expiration_date)
        except ValueError:
            raise HTTPException(
                status_code=status.HTTP_400_BAD_REQUEST,
                detail="expiration_date must be in YYYY-MM-DD format",
            )

    checksum = hashlib.sha256(payload.content.encode("utf-8")).hexdigest()

    latest_doc = await repo.get_latest_document(
        payload.study_id, site_id, payload.section_code
    )
    new_version_index = (latest_doc.version_index + 1) if latest_doc else 1

    doc = ISFDocument(
        study_id=payload.study_id,
        site_id=site_id,
        binder_classification=payload.section_code,
        filename=payload.filename,
        content=payload.content,
        mime_type=payload.mime_type,
        version_index=new_version_index,
        created_by=principal.user_id or "system",
        expiration_date=exp_date,
        content_checksum=checksum,
        sync_status="PENDING",
        source_system="eISF",
    )
    await repo.save_document(doc)

    actor_roles = (
        ",".join(principal.raw_roles)
        if principal.raw_roles
        else (",".join(principal.roles) if principal.roles else "anonymous")
    )
    await write_local_audit_log(
        repo=repo,
        actor_id=principal.user_id or "system",
        actor_role=actor_roles,
        action="CREATE_DOCUMENT",
        document_id=doc.id,
        details=f"Uploaded document '{payload.filename}' for study '{payload.study_id}' and site '{site_id}' (Version {new_version_index}).",
        reason_for_change=payload.reason_for_change,
    )

    audit_payload = AuditLogPayload(
        service_name="eisf",
        action_type="EISF_DOCUMENT_ACCESSED",
        entity_name="ISFDocument",
        entity_id=doc.id,
        user_id=principal.user_id or "system",
        tenant_id=getattr(principal, "tenant_id", "tenant_default") or "tenant_default",
        reason_for_change=payload.reason_for_change,
        details={
            "event": "EISF_DOCUMENT_ACCESSED",
            "filename": payload.filename,
            "site_id": site_id,
            "section_code": payload.section_code,
            "version": str(new_version_index),
        },
    )
    central_audit_logger.log_event(audit_payload)

    return EISFDocumentDetail(
        id=doc.id,
        site_id=site_id,
        section_code=doc.binder_classification,
        filename=doc.filename,
        version=str(doc.version_index),
        expiration_date=doc.expiration_date.isoformat()
        if doc.expiration_date
        else None,
        created_at=doc.created_at,
        created_by=doc.created_by,
        download_url=f"/api/v1/eisf/sites/{site_id}/documents/{doc.id}/download",
    )


@router.get("/api/v1/eisf/binders/{site_id}", response_model=list[DocumentResponse])
@transactional
async def get_site_binder_endpoint(
    site_id: str,
    repo: EISFRepositoryPort = Depends(get_eisf_repository),
    principal: Principal = Depends(get_principal),
):
    await enforce_site_isolation(principal, site_id, repo)
    return await repo.get_documents_by_site(site_id)


@router.get("/api/v1/eisf/documents", response_model=list[DocumentResponse])
@transactional
async def list_documents(
    request: Request,
    study_id: str | None = Query(None),
    site_id: str | None = Query(None),
    binder_section: str | None = Query(
        None, description="Filter by binder section / classification"
    ),
    binder_classification: str | None = Query(
        None, description="Filter by binder section / classification"
    ),
    repo: EISFRepositoryPort = Depends(get_eisf_repository),
    principal: Principal = Depends(get_principal),
):
    is_site_scoped = any(r in SITE_SCOPED_ROLES for r in principal.roles) or bool(
        principal.assigned_sites
    )

    if is_site_scoped:
        if principal.assigned_sites:
            if site_id:
                if site_id in principal.assigned_sites:
                    site_id_filter = site_id
                else:
                    site_id_filter = principal.assigned_sites
            else:
                site_id_filter = principal.assigned_sites
        else:
            site_id_filter = []
    else:
        site_id_filter = site_id

    if site_id_filter is None:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="site_id is required and must be provided either in the query or via authenticated claim.",
        )

    docs = await repo.list_documents_filtered(
        site_id_filter, study_id, binder_section, binder_classification
    )

    actor_id = principal.user_id or "system"
    actor_roles = (
        ",".join(principal.raw_roles)
        if principal.raw_roles
        else (",".join(principal.roles) if principal.roles else "anonymous")
    )

    await write_local_audit_log(
        repo=repo,
        actor_id=actor_id,
        actor_role=actor_roles,
        action="LIST",
        document_id=None,
        details=f"Listed documents (study_id={study_id}, site_id={site_id_filter}).",
        reason_for_change="Standard document access",
    )

    return docs


@router.post(
    "/api/v1/eisf/documents",
    response_model=DocumentResponse,
    status_code=status.HTTP_201_CREATED,
)
@transactional
async def create_document(
    request: Request,
    payload: DocumentCreate,
    _not_auditor=Depends(require_permission("eisf_document:create")),
    repo: EISFRepositoryPort = Depends(get_eisf_repository),
    principal: Principal = Depends(get_principal),
):
    user_id = principal.user_id or "system"
    actor_roles = (
        ",".join(principal.raw_roles)
        if principal.raw_roles
        else (",".join(principal.roles) if principal.roles else "anonymous")
    )

    await enforce_site_isolation(principal, payload.site_id, repo)

    if (
        payload.issue_date is not None
        or payload.expiration_date is not None
        or payload.document_owner_id is not None
    ):
        if not has_permission(principal, "etmf_document:manage_expiration"):
            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN,
                detail="Forbidden: Lacks manage_expiration permission to set or change expiration metadata.",
            )

    checksum = (
        payload.content_checksum
        or hashlib.sha256(payload.content.encode("utf-8")).hexdigest()
    )

    correlation_key = payload.correlation_key
    if not correlation_key:
        from apps.eisf.infrastructure.adapter import derive_correlation_key

        artifact_type = (payload.metadata_json or {}).get(
            "artifact_type"
        ) or payload.binder_classification
        correlation_key = derive_correlation_key(
            payload.study_id,
            payload.site_id,
            payload.binder_classification,
            artifact_type,
        )

    latest_doc = await repo.get_latest_document(
        payload.study_id, payload.site_id, payload.binder_classification
    )
    new_version_index = (latest_doc.version_index + 1) if latest_doc else 1

    doc = ISFDocument(
        study_id=payload.study_id,
        site_id=payload.site_id,
        binder_classification=payload.binder_classification,
        filename=payload.filename,
        content=payload.content,
        mime_type=payload.mime_type,
        version_index=new_version_index,
        created_by=user_id,
        metadata_json=payload.metadata_json,
        correlation_key=correlation_key,
        content_checksum=checksum,
        source_system=payload.source_system,
        sync_status="PENDING",
        issue_date=payload.issue_date,
        expiration_date=payload.expiration_date,
        document_owner_id=payload.document_owner_id,
    )
    await repo.save_document(doc)

    await write_local_audit_log(
        repo=repo,
        actor_id=user_id,
        actor_role=actor_roles,
        action="CREATE_DOCUMENT",
        document_id=doc.id,
        details=f"Created document '{payload.filename}' for study '{payload.study_id}' and site '{payload.site_id}' (Version {new_version_index}).",
        reason_for_change=payload.reason_for_change,
    )

    return doc


@router.post(
    "/events/publish",
    response_model=DocumentResponse,
    status_code=status.HTTP_201_CREATED,
)
@router.post(
    "/api/v1/eisf/ingest",
    response_model=DocumentResponse,
    status_code=status.HTTP_201_CREATED,
)
@transactional
async def ingest_document(
    request: Request,
    payload: EISFIngestionRequest,
    _not_auditor=Depends(require_permission("eisf_document:create")),
    repo: EISFRepositoryPort = Depends(get_eisf_repository),
    principal: Principal = Depends(get_principal),
):
    user_id = principal.user_id or "system"
    actor_roles = (
        ",".join(principal.raw_roles)
        if principal.raw_roles
        else (",".join(principal.roles) if principal.roles else "anonymous")
    )

    await enforce_site_isolation(principal, payload.site_id, repo)

    if (
        payload.issue_date is not None
        or payload.expiration_date is not None
        or payload.document_owner_id is not None
    ):
        if not has_permission(principal, "etmf_document:manage_expiration"):
            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN,
                detail="Forbidden: Lacks manage_expiration permission to set or change expiration metadata.",
            )

    change_reason = (
        payload.reason_for_change
        or getattr(request.state, "change_reason", None)
        or request.headers.get("x-change-reason")
        or request.headers.get("X-Change-Reason")
    )
    if not change_reason or len(change_reason.strip()) < 10:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Part 11 change justification reason is required and must be at least 10 characters long.",
        )

    checksum = (
        payload.content_checksum
        or hashlib.sha256(payload.content.encode("utf-8")).hexdigest()
    )

    binder_class = payload.binder_classification or payload.artifact_type
    if not binder_class:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="binder_classification or artifact_type is required",
        )

    correlation_key = payload.correlation_key
    if not correlation_key:
        from apps.eisf.infrastructure.adapter import derive_correlation_key

        artifact_type = (
            (payload.metadata_json or {}).get("artifact_type")
            or payload.binder_classification
            or payload.artifact_type
            or ""
        )
        correlation_key = derive_correlation_key(
            payload.study_id, payload.site_id, binder_class, artifact_type
        )

    latest_doc = await repo.get_latest_document(
        payload.study_id, payload.site_id, binder_class
    )
    new_version_index = (latest_doc.version_index + 1) if latest_doc else 1

    doc = ISFDocument(
        study_id=payload.study_id,
        site_id=payload.site_id,
        binder_classification=binder_class,
        filename=payload.filename,
        content=payload.content,
        mime_type=payload.mime_type,
        version_index=new_version_index,
        created_by=user_id,
        metadata_json=payload.metadata_json,
        correlation_key=correlation_key,
        content_checksum=checksum,
        source_system=payload.source_system,
        sync_status="PENDING",
        issue_date=payload.issue_date,
        expiration_date=payload.expiration_date,
        document_owner_id=payload.document_owner_id,
    )
    await repo.save_document(doc)

    await write_local_audit_log(
        repo=repo,
        actor_id=user_id,
        actor_role=actor_roles,
        action="INGEST",
        document_id=doc.id,
        details=f"Ingested document '{payload.filename}' for study '{payload.study_id}' and site '{payload.site_id}' (Version {new_version_index}).",
        reason_for_change=change_reason,
    )

    return doc


@router.get("/api/v1/eisf/documents/{document_id}", response_model=DocumentResponse)
@transactional
async def get_document(
    request: Request,
    document_id: str,
    repo: EISFRepositoryPort = Depends(get_eisf_repository),
    principal: Principal = Depends(get_principal),
):
    doc = await repo.get_document_by_id(document_id)

    if not doc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"eISF Document with ID '{document_id}' not found.",
        )

    await enforce_site_isolation(principal, doc.site_id, repo)

    actor_id = principal.user_id or "system"
    actor_roles = (
        ",".join(principal.raw_roles)
        if principal.raw_roles
        else (",".join(principal.roles) if principal.roles else "anonymous")
    )

    await write_local_audit_log(
        repo=repo,
        actor_id=actor_id,
        actor_role=actor_roles,
        action="VIEW",
        document_id=doc.id,
        details=f"Viewed document '{doc.filename}' (ID: {doc.id}).",
        reason_for_change="Standard document access",
    )

    return doc


@router.get("/api/v1/eisf/documents/{document_id}/download")
@transactional
async def download_document(
    request: Request,
    document_id: str,
    repo: EISFRepositoryPort = Depends(get_eisf_repository),
    principal: Principal = Depends(get_principal),
):
    doc = await repo.get_document_by_id(document_id)

    if not doc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"eISF Document with ID '{document_id}' not found.",
        )

    await enforce_site_isolation(principal, doc.site_id, repo)

    actor_id = principal.user_id or "system"
    actor_roles = (
        ",".join(principal.raw_roles)
        if principal.raw_roles
        else (",".join(principal.roles) if principal.roles else "anonymous")
    )

    await write_local_audit_log(
        repo=repo,
        actor_id=actor_id,
        actor_role=actor_roles,
        action="DOWNLOAD",
        document_id=doc.id,
        details=f"Downloaded document '{doc.filename}' (ID: {doc.id}).",
        reason_for_change="Standard document download",
    )

    return Response(
        content=doc.content,
        media_type=doc.mime_type,
        headers={"Content-Disposition": f"attachment; filename={doc.filename}"},
    )


@router.put("/api/v1/eisf/documents/{document_id}", response_model=DocumentResponse)
@transactional
async def update_document(
    request: Request,
    document_id: str,
    payload: DocumentUpdate,
    _not_auditor=Depends(require_permission("eisf_document:update")),
    repo: EISFRepositoryPort = Depends(get_eisf_repository),
    principal: Principal = Depends(get_principal),
):
    user_id = principal.user_id or "system"
    actor_roles = (
        ",".join(principal.raw_roles)
        if principal.raw_roles
        else (",".join(principal.roles) if principal.roles else "anonymous")
    )

    doc = await repo.get_document_by_id(document_id)

    if not doc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"eISF Document with ID '{document_id}' not found.",
        )

    await enforce_site_isolation(principal, doc.site_id, repo)
    await enforce_site_isolation(principal, payload.site_id, repo)

    is_expiration_metadata_changing = (
        payload.issue_date != doc.issue_date
        or payload.expiration_date != doc.expiration_date
        or payload.document_owner_id != doc.document_owner_id
    )
    if is_expiration_metadata_changing:
        if not has_permission(principal, "etmf_document:manage_expiration"):
            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN,
                detail="Forbidden: Lacks manage_expiration permission to set or change expiration metadata.",
            )

    doc.study_id = payload.study_id
    doc.site_id = payload.site_id
    doc.binder_classification = payload.binder_classification
    doc.filename = payload.filename
    doc.content = payload.content
    doc.mime_type = payload.mime_type
    doc.metadata_json = payload.metadata_json
    doc.correlation_key = payload.correlation_key
    doc.content_checksum = (
        payload.content_checksum
        or hashlib.sha256(payload.content.encode("utf-8")).hexdigest()
    )
    doc.source_system = payload.source_system
    doc.version_index += 1
    doc.issue_date = payload.issue_date
    doc.expiration_date = payload.expiration_date
    doc.document_owner_id = payload.document_owner_id

    await repo.save_document(doc)

    await write_local_audit_log(
        repo=repo,
        actor_id=user_id,
        actor_role=actor_roles,
        action="UPDATE_DOCUMENT",
        document_id=doc.id,
        details=f"Updated document '{doc.filename}' (ID: {doc.id}) to version {doc.version_index}.",
        reason_for_change=payload.reason_for_change,
    )

    return doc


@router.delete(
    "/api/v1/eisf/documents/{document_id}", status_code=status.HTTP_204_NO_CONTENT
)
@transactional
async def delete_document(
    request: Request,
    document_id: str,
    reason_for_change: str = Query(
        ..., min_length=10, max_length=1000, description="Part 11 reason for change"
    ),
    _not_auditor=Depends(require_permission("eisf_document:delete")),
    repo: EISFRepositoryPort = Depends(get_eisf_repository),
    principal: Principal = Depends(get_principal),
):
    user_id = principal.user_id or "system"
    actor_roles = (
        ",".join(principal.raw_roles)
        if principal.raw_roles
        else (",".join(principal.roles) if principal.roles else "anonymous")
    )

    doc = await repo.get_document_by_id(document_id)

    if not doc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"eISF Document with ID '{document_id}' not found.",
        )

    await enforce_site_isolation(principal, doc.site_id, repo)

    await write_local_audit_log(
        repo=repo,
        actor_id=user_id,
        actor_role=actor_roles,
        action="DELETE_DOCUMENT",
        document_id=doc.id,
        details=f"Deleted document '{doc.filename}' (ID: {doc.id}).",
        reason_for_change=reason_for_change,
    )

    await repo.delete_document(doc)


REQUIRED_BINDER_SECTIONS = {
    "Investigator & Staff": [
        "Investigator CV",
        "Delegation of Authority Log",
        "Financial Disclosure",
        "Medical License",
    ],
    "Protocols & Amendments": [
        "Approved Protocol",
        "Protocol Sign-off",
    ],
    "Regulatory Approvals": [
        "IRB Approval",
        "FDA Form 1572",
    ],
}


@router.get("/api/v1/eisf/completeness", response_model=BinderCompletenessResponse)
@transactional
async def get_binder_completeness(
    request: Request,
    study_id: str = Query(..., description="The clinical study ID"),
    site_id: str | None = Query(None, description="The site ID"),
    repo: EISFRepositoryPort = Depends(get_eisf_repository),
    principal: Principal = Depends(get_principal),
):
    is_site_scoped = any(r in SITE_SCOPED_ROLES for r in principal.roles) or bool(
        principal.assigned_sites
    )

    if is_site_scoped:
        if principal.assigned_sites:
            if site_id:
                if site_id in principal.assigned_sites:
                    site_id_filter = site_id
                else:
                    site_id_filter = principal.assigned_sites
            else:
                site_id_filter = principal.assigned_sites
        else:
            site_id_filter = []
    else:
        site_id_filter = site_id

    if site_id_filter is None:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="site_id is required and must be provided either in the query or via authenticated claim.",
        )

    docs = await repo.list_documents_filtered(
        site_ids=site_id_filter,
        study_id=study_id,
        binder_section=None,
        binder_classification=None,
    )

    actual_classifications = {doc.binder_classification.strip().lower() for doc in docs}

    sections_status = []
    global_is_complete = True

    for section_name, required_artifacts in REQUIRED_BINDER_SECTIONS.items():
        present = []
        missing = []
        for req_art in required_artifacts:
            if req_art.strip().lower() in actual_classifications:
                present.append(req_art)
            else:
                missing.append(req_art)
                global_is_complete = False

        sections_status.append(
            BinderSectionStatus(
                section_name=section_name,
                required_artifacts=required_artifacts,
                present=present,
                missing=missing,
            )
        )

    actor_id = principal.user_id or "system"
    actor_roles = (
        ",".join(principal.raw_roles)
        if principal.raw_roles
        else (",".join(principal.roles) if principal.roles else "anonymous")
    )

    await write_local_audit_log(
        repo=repo,
        actor_id=actor_id,
        actor_role=actor_roles,
        action="COMPLETENESS",
        document_id=None,
        details=f"Checked completeness for study '{study_id}', site '{site_id_filter}'. Complete: {global_is_complete}.",
        reason_for_change="Standard completeness verification",
    )

    response_site_id = (
        site_id
        if (
            site_id
            and (not principal.assigned_sites or site_id in principal.assigned_sites)
        )
        else (principal.assigned_sites[0] if principal.assigned_sites else "unknown")
    )

    return BinderCompletenessResponse(
        site_id=response_site_id,
        is_complete=global_is_complete,
        sections=sections_status,
    )


async def propagate_to_etmf(
    study_id: str,
    site_id: str,
    binder_classification: str,
    filename: str,
    content: str,
    mime_type: str,
    metadata_json: dict | None = None,
    correlation_key: str | None = None,
    content_checksum: str | None = None,
    source_system: str | None = "eISF",
    reason_for_change: str | None = None,
) -> None:
    import logging

    logging.getLogger("eisf_sync")
    from apps.eisf.infrastructure.adapter import (
        derive_correlation_key,
        map_eisf_to_etmf,
    )
    from packages.security.signing import generate_gateway_signature

    artifact_type = (metadata_json or {}).get("artifact_type") or binder_classification
    try:
        mapped = map_eisf_to_etmf(binder_classification, artifact_type)
        zone = mapped["zone"]
        section = mapped["section"]
        etmf_art_type = mapped["artifact_type"]
        etmf_art_code = mapped["artifact_code"]
    except ValueError:
        zone = None
        section = None
        etmf_art_type = binder_classification
        etmf_art_code = None

    resolved_correlation_key = correlation_key
    if not resolved_correlation_key:
        resolved_correlation_key = derive_correlation_key(
            study_id, site_id, binder_classification, artifact_type
        )

    resolved_checksum = content_checksum
    if not resolved_checksum and content is not None:
        resolved_checksum = hashlib.sha256(content.encode("utf-8")).hexdigest()

    payload = {
        "study_id": study_id,
        "site_id": site_id,
        "artifact_type": etmf_art_type,
        "filename": filename,
        "content": content,
        "mime_type": mime_type,
        "zone": zone,
        "section": section,
        "artifact_code": etmf_art_code,
        "metadata_json": metadata_json or {},
        "correlation_key": resolved_correlation_key,
        "content_checksum": resolved_checksum,
        "source_system": source_system,
    }

    user_id = "eisf_sync_service"
    roles = "system"
    timestamp = str(time.time())
    secret = os.getenv(
        "GATEWAY_SECRET", default="internal-gateway-secret-12345"
    ).encode()

    resolved_change_reason = reason_for_change
    if not resolved_change_reason:
        resolved_change_reason = "eISF to eTMF bidirectional sync propagation"

    sig = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=secret,
        change_reason=resolved_change_reason,
    )

    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": resolved_change_reason,
    }

    etmf_base_url = os.getenv("ETMF_URL", "http://localhost:8003")
    url = f"{etmf_base_url}/api/v1/etmf/ingest"

    try:
        import httpx

        async with httpx.AsyncClient() as client:
            resp = await client.post(url, json=payload, headers=headers, timeout=5.0)
            if resp.status_code not in (200, 201):
                pass
    except Exception:
        pass


@router.post(
    "/api/v1/eisf/sync",
    response_model=EISFSyncResponse,
)
@transactional
async def sync_documents(
    request: Request,
    payload: EISFSyncRequest,
    _not_auditor=Depends(require_permission("eisf_document:sync")),
    repo: EISFRepositoryPort = Depends(get_eisf_repository),
    principal: Principal = Depends(get_principal),
):
    import logging

    logging.getLogger("eisf_sync")
    user_id = principal.user_id or "system"
    role_str = (
        ",".join(principal.raw_roles)
        if principal.raw_roles
        else (",".join(principal.roles) if principal.roles else "anonymous")
    )

    processed_count = 0
    created_count = 0
    updated_count = 0
    ignored_count = 0

    from apps.eisf.infrastructure.adapter import derive_correlation_key

    for item in payload.submissions:
        await enforce_site_isolation(principal, item.site_id, repo)

        processed_count += 1

        correlation_key = item.correlation_key
        if not correlation_key:
            art_type = (item.metadata_json or {}).get(
                "artifact_type"
            ) or item.binder_classification
            correlation_key = derive_correlation_key(
                item.study_id, item.site_id, item.binder_classification, art_type
            )

        checksum = (
            item.content_checksum
            or hashlib.sha256(item.content.encode("utf-8")).hexdigest()
        )

        existing_docs = await repo.get_documents_by_correlation_or_logical_fields(
            correlation_key, item.study_id, item.site_id, item.binder_classification
        )

        is_duplicate = False
        for ex_doc in existing_docs:
            if ex_doc.content_checksum == checksum:
                if (ex_doc.metadata_json or {}) == (item.metadata_json or {}):
                    is_duplicate = True
                    break

        if is_duplicate:
            ignored_count += 1
            await write_local_audit_log(
                repo=repo,
                actor_id=user_id,
                actor_role=role_str,
                action="SYNC",
                document_id=existing_docs[0].id if existing_docs else None,
                details=f"SYNC: Ignored duplicate document with correlation_key '{correlation_key}' (checksum matching).",
                reason_for_change="Bidirectional sync: Exact duplicate ignored",
            )
            continue

        if not existing_docs:
            new_version_index = item.version_index or 1
            doc = ISFDocument(
                study_id=item.study_id,
                site_id=item.site_id,
                binder_classification=item.binder_classification,
                filename=item.filename,
                content=item.content,
                mime_type=item.mime_type,
                version_index=new_version_index,
                created_by=user_id,
                metadata_json=item.metadata_json,
                correlation_key=correlation_key,
                content_checksum=checksum,
                source_system=item.source_system,
                sync_status="SYNCED",
            )
            await repo.save_document(doc)

            if item.source_system != "eTMF":
                await propagate_to_etmf(
                    study_id=item.study_id,
                    site_id=item.site_id,
                    binder_classification=item.binder_classification,
                    filename=item.filename,
                    content=item.content,
                    mime_type=item.mime_type,
                    metadata_json=item.metadata_json,
                    correlation_key=correlation_key,
                    content_checksum=checksum,
                    source_system=item.source_system,
                    reason_for_change=request.headers.get("X-Change-Reason")
                    or item.metadata_json.get("change_reason")
                    if item.metadata_json
                    else None,
                )

            created_count += 1
            await write_local_audit_log(
                repo=repo,
                actor_id=user_id,
                actor_role=role_str,
                action="SYNC",
                document_id=doc.id,
                details=f"SYNC: Created new document '{item.filename}' (correlation_key: '{correlation_key}', version: {new_version_index}) from '{item.source_system}'.",
                reason_for_change=f"Bidirectional sync: Created from {item.source_system}",
            )
            continue

        latest_existing = existing_docs[0]
        policy = item.conflict_policy.upper() if item.conflict_policy else "CLIENT_WINS"

        if policy == "CLIENT_WINS":
            new_version_index = latest_existing.version_index + 1
            doc = ISFDocument(
                study_id=item.study_id,
                site_id=item.site_id,
                binder_classification=item.binder_classification,
                filename=item.filename,
                content=item.content,
                mime_type=item.mime_type,
                version_index=new_version_index,
                created_by=user_id,
                metadata_json=item.metadata_json,
                correlation_key=correlation_key,
                content_checksum=checksum,
                source_system=item.source_system,
                sync_status="SYNCED",
            )
            await repo.save_document(doc)

            if item.source_system != "eTMF":
                await propagate_to_etmf(
                    study_id=item.study_id,
                    site_id=item.site_id,
                    binder_classification=item.binder_classification,
                    filename=item.filename,
                    content=item.content,
                    mime_type=item.mime_type,
                    metadata_json=item.metadata_json,
                    correlation_key=correlation_key,
                    content_checksum=checksum,
                    source_system=item.source_system,
                    reason_for_change=request.headers.get("X-Change-Reason")
                    or item.metadata_json.get("change_reason")
                    if item.metadata_json
                    else None,
                )

            updated_count += 1
            await write_local_audit_log(
                repo=repo,
                actor_id=user_id,
                actor_role=role_str,
                action="SYNC",
                document_id=doc.id,
                details=f"SYNC: Updated document '{item.filename}' to version {new_version_index} via CLIENT_WINS policy.",
                reason_for_change=f"Bidirectional sync: CLIENT_WINS update from {item.source_system}",
            )

        elif policy == "SERVER_WINS":
            ignored_count += 1
            await write_local_audit_log(
                repo=repo,
                actor_id=user_id,
                actor_role=role_str,
                action="SYNC",
                document_id=latest_existing.id,
                details=f"SYNC: Ignored incoming document '{item.filename}' (correlation_key: '{correlation_key}') via SERVER_WINS policy.",
                reason_for_change="Bidirectional sync: SERVER_WINS - incoming ignored",
            )

        elif policy == "MERGE":
            t_inc = None
            if item.metadata_json:
                ts_val = item.metadata_json.get("timestamp") or item.metadata_json.get(
                    "timestamps", {}
                ).get("content")
                if ts_val:
                    try:
                        t_inc = datetime.fromisoformat(str(ts_val))
                        if t_inc.tzinfo is not None:
                            t_inc = t_inc.astimezone(UTC).replace(tzinfo=None)
                    except Exception:
                        pass
            if not t_inc:
                t_inc = datetime.utcnow()

            t_exist = None
            if latest_existing.metadata_json:
                ts_val = latest_existing.metadata_json.get(
                    "timestamp"
                ) or latest_existing.metadata_json.get("timestamps", {}).get("content")
                if ts_val:
                    try:
                        t_exist = datetime.fromisoformat(str(ts_val))
                        if t_exist.tzinfo is not None:
                            t_exist = t_exist.astimezone(UTC).replace(tzinfo=None)
                    except Exception:
                        pass
            if not t_exist:
                t_exist = (
                    latest_existing.created_at.replace(tzinfo=None)
                    if latest_existing.created_at
                    else datetime.utcnow()
                )

            inc_mod_by = (
                (item.metadata_json or {}).get("modified_by")
                or item.source_system
                or "eISF"
            )
            exist_mod_by = (
                (latest_existing.metadata_json or {}).get("modified_by")
                or latest_existing.source_system
                or "server"
            )

            incoming_wins = False
            if t_inc > t_exist:
                incoming_wins = True
            elif t_inc < t_exist:
                incoming_wins = False
            else:
                if inc_mod_by > exist_mod_by:
                    incoming_wins = True

            if incoming_wins:
                merged_content = item.content
                merged_filename = item.filename
                merged_mime_type = item.mime_type
                merged_checksum = checksum
            else:
                merged_content = latest_existing.content
                merged_filename = latest_existing.filename
                merged_mime_type = latest_existing.mime_type
                merged_checksum = latest_existing.content_checksum

            merged_metadata = dict(latest_existing.metadata_json or {})
            incoming_meta = dict(item.metadata_json or {})

            for k, v in incoming_meta.items():
                if k not in merged_metadata:
                    merged_metadata[k] = v
                else:
                    t_k_inc = None
                    t_k_exist = None
                    if item.metadata_json and "timestamps" in item.metadata_json:
                        tk_val = item.metadata_json["timestamps"].get(k)
                        if tk_val:
                            try:
                                t_k_inc = datetime.fromisoformat(str(tk_val))
                                if t_k_inc.tzinfo is not None:
                                    t_k_inc = t_k_inc.astimezone(UTC).replace(
                                        tzinfo=None
                                    )
                            except Exception:
                                pass
                    if (
                        latest_existing.metadata_json
                        and "timestamps" in latest_existing.metadata_json
                    ):
                        tk_val = latest_existing.metadata_json["timestamps"].get(k)
                        if tk_val:
                            try:
                                t_k_exist = datetime.fromisoformat(str(tk_val))
                                if t_k_exist.tzinfo is not None:
                                    t_k_exist = t_k_exist.astimezone(UTC).replace(
                                        tzinfo=None
                                    )
                            except Exception:
                                pass

                    if not t_k_inc:
                        t_k_inc = t_inc
                    if not t_k_exist:
                        t_k_exist = t_exist

                    if t_k_inc > t_k_exist:
                        merged_metadata[k] = v
                    elif t_k_inc < t_k_exist:
                        merged_metadata[k] = latest_existing.metadata_json[k]
                    else:
                        if inc_mod_by > exist_mod_by:
                            merged_metadata[k] = v
                        else:
                            merged_metadata[k] = latest_existing.metadata_json[k]

            has_changes = (
                merged_checksum != latest_existing.content_checksum
                or merged_metadata != latest_existing.metadata_json
                or merged_filename != latest_existing.filename
                or merged_mime_type != latest_existing.mime_type
            )

            if has_changes:
                new_version_index = latest_existing.version_index + 1
                doc = ISFDocument(
                    study_id=item.study_id,
                    site_id=item.site_id,
                    binder_classification=item.binder_classification,
                    filename=merged_filename,
                    content=merged_content,
                    mime_type=merged_mime_type,
                    version_index=new_version_index,
                    created_by=user_id,
                    metadata_json=merged_metadata,
                    correlation_key=correlation_key,
                    content_checksum=merged_checksum,
                    source_system=item.source_system,
                    sync_status="SYNCED",
                )
                await repo.save_document(doc)

                if item.source_system != "eTMF":
                    await propagate_to_etmf(
                        study_id=item.study_id,
                        site_id=item.site_id,
                        binder_classification=item.binder_classification,
                        filename=merged_filename,
                        content=merged_content,
                        mime_type=merged_mime_type,
                        metadata_json=merged_metadata,
                        correlation_key=correlation_key,
                        content_checksum=merged_checksum,
                        source_system=item.source_system,
                        reason_for_change=request.headers.get("X-Change-Reason")
                        or item.metadata_json.get("change_reason")
                        if item.metadata_json
                        else None,
                    )

                updated_count += 1
                await write_local_audit_log(
                    repo=repo,
                    actor_id=user_id,
                    actor_role=role_str,
                    action="SYNC",
                    document_id=doc.id,
                    details=f"SYNC: Merged and updated document '{merged_filename}' to version {new_version_index}.",
                    reason_for_change="Bidirectional sync: MERGE update",
                )
            else:
                ignored_count += 1
                await write_local_audit_log(
                    repo=repo,
                    actor_id=user_id,
                    actor_role=role_str,
                    action="SYNC",
                    document_id=latest_existing.id,
                    details=f"SYNC: Merged document result was identical to existing version {latest_existing.version_index}. Ignored.",
                    reason_for_change="Bidirectional sync: MERGE - no changes detected",
                )

    return EISFSyncResponse(
        status="success",
        processed_count=processed_count,
        created_count=created_count,
        updated_count=updated_count,
        ignored_count=ignored_count,
    )
