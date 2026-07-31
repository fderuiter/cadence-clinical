"""FastAPI Router for eISF regulatory binder browsing and document upload endpoints.

Requirements: PRD-SYS-001
"""

import hashlib
from datetime import date
from typing import List, Optional

from etmf.eisf_transport_models import (
    EISFDocumentDetail,
    EISFDocumentUploadRequest,
    EISFFolderNode,
)
from fastapi import APIRouter, Depends, HTTPException, Response, status
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.eisf.database import db_manager
from apps.eisf.models import ISFAuditLog, ISFDocument
from packages.database import DatabaseSessionDependency
from packages.security.audit_logger import AuditLogPayload
from packages.security.audit_logger import audit_logger_engine as CentralAuditLogger
from packages.security.rbac import (
    Principal,
    can_access_site,
    get_principal,
    require_permission,
)

router = APIRouter(prefix="/api/v1/eisf")

get_db_session = DatabaseSessionDependency(db_manager)


async def enforce_site_isolation(
    principal: Principal,
    site_id: str,
    session: AsyncSession,
) -> None:
    """Enforces clinical site-scoped isolation based on requesting user's Principal.

    If access is not authorized, commits a GxP SECURITY_ALERT to the persistent
    audit trail and raises a 403 Forbidden exception.

    Requirements: PRD-SYS-001
    """
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
        session.add(alert)
        await session.flush()

        # Write to a separate committed session to ensure the alert survives the HTTP route rollback
        async with db_manager.get_session_maker()() as audit_session:
            audit_alert = ISFAuditLog(
                actor_id=actor_id,
                actor_role=actor_roles,
                action="SECURITY_ALERT",
                details=details,
                reason_for_change=reason_for_change,
            )
            audit_session.add(audit_alert)
            await audit_session.commit()

        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Forbidden: Access is restricted to your assigned site.",
        )


async def write_local_audit_log(
    session: AsyncSession,
    actor_id: str,
    actor_role: str,
    action: str,
    document_id: Optional[str],
    details: str,
    reason_for_change: str,
) -> None:
    """Appends an entry to the 21 CFR Part 11 compliant persistent ISFAuditLog.

    Requirements: PRD-SYS-001
    """
    log_entry = ISFAuditLog(
        actor_id=actor_id,
        actor_role=actor_role,
        action=action,
        document_id=document_id,
        details=details,
        reason_for_change=reason_for_change,
    )
    session.add(log_entry)
    await session.flush()


@router.get(
    "/sites/{site_id}/binder",
    response_model=List[EISFFolderNode],
)
async def get_site_eisf_binder(
    site_id: str,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> List[EISFFolderNode]:
    """Retrieve eISF regulatory binder folder taxonomy with document counts.

    Requirements: PRD-SYS-001
    """
    await enforce_site_isolation(principal, site_id, session)

    # Query all filed documents for the site
    stmt = select(ISFDocument).where(ISFDocument.site_id == site_id)
    result = await session.execute(stmt)
    docs = result.scalars().all()

    # Define standard binder classifications mapping to the target sections
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
    "/sites/{site_id}/documents/{doc_id}",
    response_model=EISFDocumentDetail,
)
async def get_site_document_detail(
    site_id: str,
    doc_id: str,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> EISFDocumentDetail:
    """Fetch metadata details for a specific clinical site eISF document.

    Requirements: PRD-SYS-001
    """
    await enforce_site_isolation(principal, site_id, session)

    stmt = select(ISFDocument).where(
        ISFDocument.id == doc_id,
        ISFDocument.site_id == site_id,
    )
    result = await session.execute(stmt)
    doc = result.scalars().first()

    if not doc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"eISF Document with ID '{doc_id}' not found.",
        )

    # Record GxP audit view event
    actor_roles = (
        ",".join(principal.raw_roles)
        if principal.raw_roles
        else (",".join(principal.roles) if principal.roles else "anonymous")
    )
    await write_local_audit_log(
        session=session,
        actor_id=principal.user_id or "system",
        actor_role=actor_roles,
        action="VIEW",
        document_id=doc.id,
        details=f"Viewed document '{doc.filename}' (ID: {doc.id}).",
        reason_for_change="Standard document access",
    )

    # Record EISF_DOCUMENT_ACCESSED event in CentralAuditLogger for document views
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
    CentralAuditLogger.log_event(audit_payload)

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
    "/sites/{site_id}/documents/{doc_id}/download",
)
async def download_site_document(
    site_id: str,
    doc_id: str,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
):
    """Download/stream file content for a specific eISF document.

    Requirements: PRD-SYS-001
    """
    await enforce_site_isolation(principal, site_id, session)

    stmt = select(ISFDocument).where(
        ISFDocument.id == doc_id,
        ISFDocument.site_id == site_id,
    )
    result = await session.execute(stmt)
    doc = result.scalars().first()

    if not doc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"eISF Document with ID '{doc_id}' not found.",
        )

    # Record GxP audit download event
    actor_roles = (
        ",".join(principal.raw_roles)
        if principal.raw_roles
        else (",".join(principal.roles) if principal.roles else "anonymous")
    )
    await write_local_audit_log(
        session=session,
        actor_id=principal.user_id or "system",
        actor_role=actor_roles,
        action="DOWNLOAD",
        document_id=doc.id,
        details=f"Downloaded document '{doc.filename}' (ID: {doc.id}).",
        reason_for_change="Standard document download",
    )

    # Record EISF_DOCUMENT_ACCESSED event in CentralAuditLogger for document downloads
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
    CentralAuditLogger.log_event(audit_payload)

    return Response(
        content=doc.content,
        media_type=doc.mime_type,
        headers={"Content-Disposition": f"attachment; filename={doc.filename}"},
    )


@router.post(
    "/sites/{site_id}/documents/upload",
    response_model=EISFDocumentDetail,
    status_code=status.HTTP_201_CREATED,
)
async def upload_site_document(
    site_id: str,
    payload: EISFDocumentUploadRequest,
    _not_auditor=Depends(require_permission("eisf_document:create")),
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> EISFDocumentDetail:
    """Upload a new clinical site document to the eISF binder structure.

    Requirements: PRD-SYS-001
    """
    await enforce_site_isolation(principal, site_id, session)

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

    # Calculate version index
    stmt = (
        select(ISFDocument)
        .where(
            ISFDocument.study_id == payload.study_id,
            ISFDocument.site_id == site_id,
            ISFDocument.binder_classification == payload.section_code,
        )
        .order_by(ISFDocument.version_index.desc())
    )
    res = await session.execute(stmt)
    latest_doc = res.scalars().first()
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
    session.add(doc)
    await session.flush()

    actor_roles = (
        ",".join(principal.raw_roles)
        if principal.raw_roles
        else (",".join(principal.roles) if principal.roles else "anonymous")
    )
    await write_local_audit_log(
        session=session,
        actor_id=principal.user_id or "system",
        actor_role=actor_roles,
        action="CREATE_DOCUMENT",
        document_id=doc.id,
        details=f"Uploaded document '{payload.filename}' for study '{payload.study_id}' and site '{site_id}' (Version {new_version_index}).",
        reason_for_change=payload.reason_for_change,
    )

    # Record EISF_DOCUMENT_ACCESSED event in CentralAuditLogger for document upload
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
    CentralAuditLogger.log_event(audit_payload)

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
