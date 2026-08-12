import email.utils
import os
import time
from datetime import UTC, datetime
from typing import Any

from fastapi import APIRouter, Depends, HTTPException, Query, Request
from fastapi.responses import Response

from apps.etmf.domain.ports import ETMFRepositoryPort
from apps.etmf.domain.tmf_reference_model import (
    get_active_catalog,
    get_mandatory_artifacts,
    resolve_artifact,
)
from apps.etmf.export import generate_binder_zip
from apps.etmf.infrastructure.database import transactional
from apps.etmf.infrastructure.models import (
    DocumentStatus,
    ExpectedDocument,
    TMFAuditLog,
    TMFDocument,
)
from apps.etmf.ingestion_service import ingest_tmf_document
from apps.etmf.lifecycle import (
    authorize_document_read,
    validate_and_transition_document_status,
)
from apps.etmf.presentation.dtos import (
    ArtifactDetail,
    AuditLogResponse,
    AutomatedRedactRequest,
    AutomatedRedactResponse,
    BinderArtifactNode,
    BinderSectionNode,
    BinderStructureResponse,
    BinderZoneNode,
    CompletenessResponse,
    DocumentExpirationUpdate,
    DocumentResponse,
    DocumentVersionEntry,
    DocumentVersionsResponse,
    ExpectedDocumentCreate,
    ExpectedDocumentResponse,
    IngestionRequest,
    ManualRedactRequest,
    ManualRedactResponse,
    PaginatedAuditLogResponse,
    RedactRequest,
    SignDocumentRequest,
    StudyArchiveItemResult,
    StudyArchiveRequest,
    StudyArchiveResponse,
    TransitionRequest,
    TransitionResponse,
    to_document_response,
)
from packages.deid.detector import DeidDetector
from packages.deid.manifest import build_redaction_manifest, sign_manifest_symmetric
from packages.deid.models import DetectionResult, DetectorCategory
from packages.deid.transforms import apply_deid_transforms
from packages.security.rbac import Principal, get_principal, has_permission

router = APIRouter(tags=["eTMF"])


def get_etmf_repository() -> ETMFRepositoryPort:
    import apps.etmf.main as main_module

    if hasattr(main_module, "_repo_instance"):
        return main_module._repo_instance
    from apps.etmf.infrastructure.repositories import SQLETMFRepository

    return SQLETMFRepository()


def normalize_milestone(milestone: str) -> str:
    norm = milestone.strip().upper()
    if norm in ("INITIATION", "STUDY START"):
        return "INITIATION"
    if norm in ("CONDUCT", "DATA COLLECTION"):
        return "CONDUCT"
    if norm in ("CLOSEOUT", "STUDY CLOSED", "LOCK"):
        return "CLOSEOUT"
    return norm


async def seed_default_edl(
    repo: ETMFRepositoryPort, study_id: str, milestone: str
) -> None:
    canonical = normalize_milestone(milestone)
    existing = await repo.get_expected_documents_by_study_and_site(study_id, None)
    existing = [e for e in existing if e.milestone == canonical]
    if existing:
        return

    version = get_active_catalog().version
    try:
        mandatory_artifacts = get_mandatory_artifacts(canonical, version)
    except ValueError:
        return

    for art in mandatory_artifacts:
        doc = ExpectedDocument(
            study_id=study_id,
            milestone=canonical,
            artifact_type=art.name,
            zone=art.zone_code,
            section=art.section_code,
            created_by="system",
            reason_for_change="System-initiated default seeding of expected documents list",
            version_index=1,
            metadata_json={"default_seeded": True},
        )
        await repo.save_expected_document(doc)
    await repo.session.flush()


def map_artifact_to_tmf(artifact_type: str) -> tuple[int, str]:
    from apps.etmf.classification_service import classify_tmf_document

    classification = classify_tmf_document(filename="", artifact_type=artifact_type)
    if classification is None:
        raise ValueError(f"Unresolvable artifact: {artifact_type}")
    return classification.resolved_zone, classification.resolved_section


async def write_audit_log(
    repo: ETMFRepositoryPort,
    user_id: str,
    user_role: str | list[str],
    action: str,
    document_id: str | None,
    details: str,
    reason_for_change: str | None = None,
) -> None:
    user_role_str = ",".join(user_role) if isinstance(user_role, list) else user_role

    log_entry = TMFAuditLog(
        user_id=user_id,
        user_role=user_role_str,
        action=action,
        document_id=document_id,
        details=details,
        reason_for_change=reason_for_change,
    )
    await repo.save_audit_log(log_entry)


def enforce_document_site_visibility(doc: TMFDocument, principal: Principal) -> None:
    from packages.security.rbac import can_access_site, can_access_study

    if not can_access_study(principal, doc.study_id):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Access is restricted to authorized studies.",
        )
    if not can_access_site(principal, doc.site_id):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Access is restricted to authorized sites.",
        )


@router.get("/health")
async def health_check() -> dict[str, str]:
    return {"status": "ok", "service": "etmf"}


@router.post("/events/publish", status_code=201)
@router.post("/api/v1/etmf/ingest", status_code=201)
@transactional
async def ingest_document(
    request: Request,
    payload: IngestionRequest,
    repo: ETMFRepositoryPort = Depends(get_etmf_repository),
    principal: Principal = Depends(get_principal),
) -> dict[str, Any]:
    session = repo.session
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "etmf_document:create"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Inspectors are restricted to read-only access.",
        )

    if (
        payload.issue_date is not None
        or payload.expiration_date is not None
        or payload.document_owner_id is not None
    ):
        if not has_permission(principal, "etmf_document:manage_expiration"):
            raise HTTPException(
                status_code=403,
                detail="Forbidden: Lacks manage_expiration permission to set or change expiration metadata.",
            )

    from apps.etmf.lock_client import verify_trial_lock_status

    if await verify_trial_lock_status():
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Trial is currently locked in a read-only state due to a security violation.",
        )

    reason_for_change = request.headers.get("X-Change-Reason", "").strip()
    if not reason_for_change:
        reason_for_change = getattr(request.state, "change_reason", "").strip()
    if not reason_for_change:
        reason_for_change = principal.change_reason or "system_operation"
    reason_for_change = reason_for_change.strip()

    try:
        doc = await ingest_tmf_document(
            session=session,
            study_id=payload.study_id,
            site_id=payload.site_id,
            idempotency_key=payload.idempotency_key,
            artifact_type=payload.artifact_type,
            filename=payload.filename,
            content=payload.content,
            mime_type=payload.mime_type,
            created_by=user_id,
            created_role=user_roles,
            assigned_sites=principal.assigned_sites,
            zone=payload.zone,
            section=payload.section,
            artifact_code=payload.artifact_code,
            taxonomy_version=payload.taxonomy_version,
            metadata_json=payload.metadata_json,
            reason_for_change=reason_for_change,
            protocol_version=payload.protocol_version,
            issue_date=payload.issue_date,
            expiration_date=payload.expiration_date,
            document_owner_id=payload.document_owner_id,
            correlation_key=payload.correlation_key,
            content_checksum=payload.content_checksum,
            source_system=payload.source_system,
        )
    except ValueError as e:
        raise HTTPException(
            status_code=422,
            detail=str(e),
        )
    except PermissionError as e:
        raise HTTPException(
            status_code=403,
            detail=str(e),
        )

    result_status = getattr(doc, "_ingest_result_status", "created")

    return {
        "status": "success",
        "id": doc.id,
        "document_id": doc.id,
        "zone": doc.zone,
        "section": doc.section,
        "version_index": doc.version_index,
        "taxonomy_version": doc.taxonomy_version,
        "artifact_code": doc.artifact_code,
        "document_status": doc.status,
        "site_id": doc.site_id,
        "correlation_key": doc.correlation_key,
        "content_checksum": doc.content_checksum,
        "source_system": doc.source_system,
        "sync_status": doc.sync_status,
        "result": result_status,
    }


@router.get("/api/v1/etmf/documents", response_model=list[DocumentResponse])
@transactional
async def list_documents(
    request: Request,
    study_id: str | None = Query(None, description="Filter by study ID"),
    zone: int | None = Query(None, description="Filter by TMF Zone"),
    search: str | None = Query(None, description="Search document content"),
    status: str | None = Query(None, description="Filter by status"),
    repo: ETMFRepositoryPort = Depends(get_etmf_repository),
    principal: Principal = Depends(get_principal),
) -> list[DocumentResponse]:
    session = repo.session
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "etmf_document:read"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Insufficient permissions to read eTMF documents.",
        )

    docs = await repo.get_documents_filtered(study_id, zone, search, status, principal)

    filtered_docs = []
    for doc in docs:
        try:
            await authorize_document_read(principal, doc, session)
            filtered_docs.append(doc)
        except Exception:
            continue

    search_criteria = f"study_id={study_id}, zone={zone}, search={search}"
    await write_audit_log(
        repo=repo,
        user_id=user_id,
        user_role=user_roles,
        action="LIST",
        document_id=None,
        details=f"Listed eTMF documents matching criteria: {search_criteria}.",
    )

    return [to_document_response(doc) for doc in filtered_docs]


@router.get("/api/v1/etmf/documents/{document_id}", response_model=DocumentResponse)
@transactional
async def view_document(
    request: Request,
    document_id: str,
    repo: ETMFRepositoryPort = Depends(get_etmf_repository),
    principal: Principal = Depends(get_principal),
) -> DocumentResponse:
    session = repo.session
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "etmf_document:read"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Insufficient permissions to read eTMF documents.",
        )

    doc = await repo.get_document_by_id(document_id)

    if not doc:
        raise HTTPException(status_code=404, detail="eTMF Document not found")

    await authorize_document_read(principal, doc, session)

    await write_audit_log(
        repo=repo,
        user_id=user_id,
        user_role=user_roles,
        action="VIEW",
        document_id=doc.id,
        details=f"Viewed metadata for eTMF document '{doc.filename}' (ID: {doc.id}).",
    )

    return to_document_response(doc)


@router.get(
    "/api/v1/etmf/documents/{document_id}/versions",
    response_model=DocumentVersionsResponse,
)
@transactional
async def get_document_versions(
    request: Request,
    document_id: str,
    repo: ETMFRepositoryPort = Depends(get_etmf_repository),
    principal: Principal = Depends(get_principal),
) -> DocumentVersionsResponse:
    session = repo.session
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    doc = await repo.get_document_by_id(document_id)

    if not doc:
        raise HTTPException(status_code=404, detail="eTMF Document not found")

    await authorize_document_read(principal, doc, session)

    versions_docs = await repo.get_document_lineage(doc.study_id, doc.artifact_code)

    versions_list = []
    for v in versions_docs:
        transitions = await repo.get_qc_transitions_by_document_id_asc(v.id)

        versions_list.append(
            DocumentVersionEntry(
                id=v.id,
                version_index=v.version_index,
                status=v.status,
                approval_status=v.approval_status,
                created_at=v.created_at.isoformat(),
                created_by=v.created_by,
                filename=v.filename,
                artifact_code=v.artifact_code,
                signer=v.signer,
                signing_timestamp=(
                    v.signing_timestamp.isoformat() if v.signing_timestamp else None
                ),
                transitions=[
                    TransitionResponse(
                        id=t.id,
                        document_id=t.document_id,
                        from_status=t.from_status,
                        to_status=t.to_status,
                        actor_id=t.actor_id,
                        actor_role=t.actor_role,
                        reason_for_change=t.reason_for_change,
                        timestamp=t.timestamp.isoformat(),
                    )
                    for t in transitions
                ],
            )
        )

    await write_audit_log(
        repo=repo,
        user_id=user_id,
        user_role=user_roles,
        action="VERSION_HISTORY_VIEW",
        document_id=doc.id,
        details=f"Viewed version history and QC transitions for document lineage (study: {doc.study_id}, artifact: {doc.artifact_code}).",
    )
    await session.commit()

    return DocumentVersionsResponse(
        study_id=doc.study_id,
        artifact_code=doc.artifact_code,
        versions=versions_list,
    )


@router.get("/api/v1/etmf/documents/{document_id}/download")
@transactional
async def download_document(
    request: Request,
    document_id: str,
    watermark: bool = Query(False, description="Request watermarked document"),
    repo: ETMFRepositoryPort = Depends(get_etmf_repository),
    principal: Principal = Depends(get_principal),
) -> Response:
    session = repo.session
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    is_auditor = "auditor" in principal.roles or any(
        r in {"auditor", "inspector", "regulatory_inspector"}
        for r in principal.raw_roles
    )

    if watermark and not is_auditor:
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Access is restricted to authorized auditor/inspection roles.",
        )

    should_watermark = watermark or is_auditor

    doc = await repo.get_document_by_id(document_id)

    if not doc:
        raise HTTPException(status_code=404, detail="eTMF Document not found")

    await authorize_document_read(principal, doc, session)

    if should_watermark:
        from apps.etmf.watermark import apply_watermark

        final_content = apply_watermark(doc.content, doc.mime_type, user_id, user_roles)
        action_name = "WATERMARKED_DOWNLOAD"
        details_msg = f"Downloaded watermarked content for eTMF document '{doc.filename}' (ID: {doc.id})."
    else:
        final_content = doc.content
        action_name = "DOWNLOAD"
        details_msg = (
            f"Downloaded content for eTMF document '{doc.filename}' (ID: {doc.id})."
        )

    mime_lower = doc.mime_type.lower().strip()
    is_binary = (
        "pdf" in mime_lower
        or "wordprocessingml" in mime_lower
        or "docx" in mime_lower
        or mime_lower == "application/octet-stream"
    )
    if is_binary:
        import base64

        try:
            if isinstance(final_content, str):
                try:
                    decoded = base64.b64decode(final_content)
                    if decoded.startswith(b"%PDF") or decoded.startswith(b"PK\x03\x04"):
                        final_content = decoded
                except Exception:
                    pass
        except Exception:
            pass

    await write_audit_log(
        repo=repo,
        user_id=user_id,
        user_role=user_roles,
        action=action_name,
        document_id=doc.id,
        details=details_msg,
    )

    return Response(
        content=final_content,
        media_type=doc.mime_type,
        headers={"Content-Disposition": f"attachment; filename={doc.filename}"},
    )


@router.get("/api/v1/etmf/documents/{document_id}/watermark")
@transactional
async def download_watermarked_document(
    request: Request,
    document_id: str,
    repo: ETMFRepositoryPort = Depends(get_etmf_repository),
    principal: Principal = Depends(get_principal),
) -> Response:
    session = repo.session
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    is_auditor = "auditor" in principal.roles or any(
        r in {"auditor", "inspector", "regulatory_inspector"}
        for r in principal.raw_roles
    )

    if not is_auditor:
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Access is restricted to authorized auditor/inspection roles.",
        )

    doc = await repo.get_document_by_id(document_id)

    if not doc:
        raise HTTPException(status_code=404, detail="eTMF Document not found")

    await authorize_document_read(principal, doc, session)

    from apps.etmf.watermark import apply_watermark

    watermarked_content = apply_watermark(
        doc.content, doc.mime_type, user_id, user_roles
    )

    mime_lower = doc.mime_type.lower().strip()
    is_binary = (
        "pdf" in mime_lower
        or "wordprocessingml" in mime_lower
        or "docx" in mime_lower
        or mime_lower == "application/octet-stream"
    )
    if is_binary:
        import base64

        try:
            if isinstance(watermarked_content, str):
                try:
                    decoded = base64.b64decode(watermarked_content)
                    if decoded.startswith(b"%PDF") or decoded.startswith(b"PK\x03\x04"):
                        watermarked_content = decoded
                except Exception:
                    pass
        except Exception:
            pass

    await write_audit_log(
        repo=repo,
        user_id=user_id,
        user_role=user_roles,
        action="WATERMARKED_DOWNLOAD",
        document_id=doc.id,
        details=f"Downloaded watermarked content for eTMF document '{doc.filename}' (ID: {doc.id}).",
    )

    return Response(
        content=watermarked_content,
        media_type=doc.mime_type,
        headers={"Content-Disposition": f"attachment; filename={doc.filename}"},
    )


@router.get("/api/v1/etmf/audit-logs", response_model=PaginatedAuditLogResponse)
@transactional
async def get_audit_trail(
    request: Request,
    user_id: str | None = Query(None, description="Filter logs by user ID"),
    action: str | None = Query(None, description="Filter logs by action"),
    document_id: str | None = Query(None, description="Filter logs by document ID"),
    start_time: datetime | None = Query(
        None, description="Filter logs starting from this timestamp (inclusive)"
    ),
    end_time: datetime | None = Query(
        None, description="Filter logs up to this timestamp (inclusive)"
    ),
    limit: int = Query(
        50, ge=1, le=250, description="Limit the number of audit log records returned"
    ),
    offset: int = Query(0, ge=0, description="Offset for pagination"),
    repo: ETMFRepositoryPort = Depends(get_etmf_repository),
    principal: Principal = Depends(get_principal),
) -> PaginatedAuditLogResponse:
    request_user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "etmf_audit_logs:read"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Access is restricted to authorized auditor/inspection roles.",
        )

    await write_audit_log(
        repo=repo,
        user_id=request_user_id,
        user_role=user_roles,
        action="AUDIT_VIEW",
        document_id=document_id,
        details="Accessed eTMF immutable audit trail logs.",
    )

    total_count, logs = await repo.get_audit_logs_paginated(
        user_id=user_id,
        action=action,
        document_id=document_id,
        start_time=start_time,
        end_time=end_time,
        offset=offset,
        limit=limit,
    )

    has_more = (offset + limit) < total_count
    next_page = None
    next_cursor = None
    if has_more:
        next_cursor = str(offset + limit)
        base_path = "/api/v1/etmf/audit-logs"
        params = []
        if user_id:
            params.append(f"user_id={user_id}")
        if action:
            params.append(f"action={action}")
        if document_id:
            params.append(f"document_id={document_id}")
        if start_time:
            params.append(f"start_time={start_time.isoformat()}")
        if end_time:
            params.append(f"end_time={end_time.isoformat()}")
        params.append(f"limit={limit}")
        params.append(f"offset={offset + limit}")
        next_page = f"{base_path}?" + "&".join(params)

    items = [
        AuditLogResponse(
            id=log.id,
            timestamp=log.timestamp.isoformat(),
            user_id=log.user_id,
            user_role=log.user_role,
            action=log.action,
            document_id=log.document_id,
            details=log.details,
        )
        for log in logs
    ]

    return PaginatedAuditLogResponse(
        items=items,
        total_count=total_count,
        limit=limit,
        offset=offset,
        next_page=next_page,
        next_cursor=next_cursor,
        has_more=has_more,
    )


@router.get("/api/v1/etmf/edl", response_model=list[ExpectedDocumentResponse])
@transactional
async def list_expectations(
    request: Request,
    study_id: str = Query(..., description="The clinical study ID"),
    site_id: str | None = Query(None, description="Optional clinical site ID"),
    milestone: str | None = Query(None, description="Optional milestone"),
    repo: ETMFRepositoryPort = Depends(get_etmf_repository),
    principal: Principal = Depends(get_principal),
) -> list[ExpectedDocumentResponse]:
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    expectations = await repo.get_expected_documents_filtered(
        study_id, site_id, milestone
    )

    await write_audit_log(
        repo=repo,
        user_id=user_id,
        user_role=user_roles,
        action="EDL_VIEW",
        document_id=None,
        details=f"Listed EDL expectations for study '{study_id}', site '{site_id}', milestone '{milestone}'.",
    )

    return [
        ExpectedDocumentResponse(
            id=exp.id,
            study_id=exp.study_id,
            site_id=exp.site_id,
            milestone=exp.milestone,
            artifact_type=exp.artifact_type,
            zone=exp.zone,
            section=exp.section,
            metadata_json=exp.metadata_json,
            created_at=exp.created_at.isoformat(),
            created_by=exp.created_by,
            reason_for_change=exp.reason_for_change,
            version_index=exp.version_index,
        )
        for exp in expectations
    ]


@router.post(
    "/api/v1/etmf/edl", response_model=ExpectedDocumentResponse, status_code=201
)
@transactional
async def create_expectation(
    request: Request,
    payload: ExpectedDocumentCreate,
    repo: ETMFRepositoryPort = Depends(get_etmf_repository),
    principal: Principal = Depends(get_principal),
) -> ExpectedDocumentResponse:
    session = repo.session
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "etmf_edl:create"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Inspectors are restricted to read-only access.",
        )

    from apps.etmf.lock_client import verify_trial_lock_status

    if await verify_trial_lock_status():
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Trial is currently locked in a read-only state due to a security violation.",
        )

    milestone_normalized = normalize_milestone(payload.milestone)

    exp = ExpectedDocument(
        study_id=payload.study_id,
        site_id=payload.site_id,
        milestone=milestone_normalized,
        artifact_type=payload.artifact_type,
        zone=payload.zone,
        section=payload.section,
        metadata_json=payload.metadata_json,
        created_by=user_id,
        reason_for_change=payload.reason_for_change,
        version_index=1,
    )

    session.add(exp)
    await session.flush()

    await write_audit_log(
        repo=repo,
        user_id=user_id,
        user_role=user_roles,
        action="EDL_UPDATE",
        document_id=exp.id,
        details=f"Created expected document '{payload.artifact_type}' for study '{payload.study_id}', site '{payload.site_id}', milestone '{milestone_normalized}'. Reason: {payload.reason_for_change}",
    )

    return ExpectedDocumentResponse(
        id=exp.id,
        study_id=exp.study_id,
        site_id=exp.site_id,
        milestone=exp.milestone,
        artifact_type=exp.artifact_type,
        zone=exp.zone,
        section=exp.section,
        metadata_json=exp.metadata_json,
        created_at=exp.created_at.isoformat(),
        created_by=exp.created_by,
        reason_for_change=exp.reason_for_change,
        version_index=exp.version_index,
    )


@router.put("/api/v1/etmf/edl/{edl_id}", response_model=ExpectedDocumentResponse)
@transactional
async def update_expectation(
    request: Request,
    edl_id: str,
    payload: ExpectedDocumentCreate,
    repo: ETMFRepositoryPort = Depends(get_etmf_repository),
    principal: Principal = Depends(get_principal),
) -> ExpectedDocumentResponse:
    session = repo.session
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "etmf_edl:create"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Inspectors are restricted to read-only access.",
        )

    from apps.etmf.lock_client import verify_trial_lock_status

    if await verify_trial_lock_status():
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Trial is currently locked in a read-only state due to a security violation.",
        )

    exp = await repo.get_expected_document_by_id(edl_id)

    if not exp:
        raise HTTPException(
            status_code=404, detail="ExpectedDocument expectation not found"
        )

    milestone_normalized = normalize_milestone(payload.milestone)

    exp.study_id = payload.study_id
    exp.site_id = payload.site_id
    exp.milestone = milestone_normalized
    exp.artifact_type = payload.artifact_type
    exp.zone = payload.zone
    exp.section = payload.section
    exp.metadata_json = payload.metadata_json
    exp.reason_for_change = payload.reason_for_change
    exp.version_index += 1

    await session.flush()

    await write_audit_log(
        repo=repo,
        user_id=user_id,
        user_role=user_roles,
        action="EDL_UPDATE",
        document_id=exp.id,
        details=f"Updated expected document '{payload.artifact_type}' (ID: {edl_id}) for study '{payload.study_id}', site '{payload.site_id}', milestone '{milestone_normalized}'. Reason: {payload.reason_for_change}",
    )

    return ExpectedDocumentResponse(
        id=exp.id,
        study_id=exp.study_id,
        site_id=exp.site_id,
        milestone=exp.milestone,
        artifact_type=exp.artifact_type,
        zone=exp.zone,
        section=exp.section,
        metadata_json=exp.metadata_json,
        created_at=exp.created_at.isoformat(),
        created_by=exp.created_by,
        reason_for_change=exp.reason_for_change,
        version_index=exp.version_index,
    )


@router.get("/api/v1/etmf/completeness", response_model=CompletenessResponse)
@transactional
async def check_completeness(
    request: Request,
    study_id: str = Query(..., description="The clinical study ID"),
    milestone: str = Query(..., description="The transition milestone to check"),
    site_id: str | None = Query(None, description="Optional clinical site ID"),
    repo: ETMFRepositoryPort = Depends(get_etmf_repository),
    principal: Principal = Depends(get_principal),
) -> CompletenessResponse:
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "etmf_document:read"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Insufficient permissions to read eTMF documents.",
        )

    from packages.security.rbac import can_access_site, can_access_study

    if not can_access_study(principal, study_id):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: You cannot check completeness for this study.",
        )
    if not can_access_site(principal, site_id):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: You can only check completeness for your assigned site(s).",
        )

    milestone_normalized = normalize_milestone(milestone)

    version = get_active_catalog().version
    try:
        get_mandatory_artifacts(milestone_normalized, version)
    except ValueError as e:
        raise HTTPException(
            status_code=400,
            detail=f"Unknown milestone. Supported: INITIATION, CONDUCT, CLOSEOUT. Error: {str(e)}",
        )

    await seed_default_edl(repo, study_id, milestone_normalized)

    expected_docs = await repo.get_expected_documents_by_study_and_site(
        study_id, site_id
    )
    expected_docs = [e for e in expected_docs if e.milestone == milestone_normalized]
    archived_docs = await repo.get_documents_by_study(study_id)

    present_artifacts = []
    missing_artifacts = []
    per_artifact_detail = []

    for exp in expected_docs:
        try:
            resolved_exp = resolve_artifact(version, name=exp.artifact_type)
            exp_code = resolved_exp["artifact"].code
            canonical_name = resolved_exp["artifact"].name
        except ValueError:
            exp_code = None
            canonical_name = exp.artifact_type

        matched_doc = None
        for arch in archived_docs:
            if arch.site_id is not None and str(arch.site_id).upper() == "QUARANTINED":
                continue

            arch_site = arch.site_id if arch.site_id else None
            exp_site = exp.site_id if exp.site_id else None
            if arch_site != exp_site:
                continue

            is_match = False
            if exp_code and arch.artifact_code:
                is_match = arch.artifact_code == exp_code
            else:
                is_match = canonical_name.lower() in arch.artifact_type.lower()

            if is_match:
                if not matched_doc or arch.version_index > matched_doc.version_index:
                    matched_doc = arch

        from apps.etmf.cryptography import requires_signature

        sig_required = requires_signature(canonical_name)

        scope = "site" if exp.site_id else "study"
        if matched_doc:
            is_signed = matched_doc.approval_status == "APPROVED"

            if sig_required:
                if is_signed:
                    status_val = "SIGNED"
                    if canonical_name not in present_artifacts:
                        present_artifacts.append(canonical_name)
                else:
                    status_val = "UNSIGNED"
                    if canonical_name not in missing_artifacts:
                        missing_artifacts.append(canonical_name)
            else:
                status_val = "PRESENT"
                if canonical_name not in present_artifacts:
                    present_artifacts.append(canonical_name)

            per_artifact_detail.append(
                ArtifactDetail(
                    artifact_type=canonical_name,
                    scope=scope,
                    status=status_val,
                    document_id=matched_doc.id,
                    version_index=matched_doc.version_index,
                )
            )
        else:
            status_val = "ABSENT"
            if canonical_name not in missing_artifacts:
                missing_artifacts.append(canonical_name)
            per_artifact_detail.append(
                ArtifactDetail(
                    artifact_type=canonical_name,
                    scope=scope,
                    status=status_val,
                    document_id=None,
                    version_index=None,
                )
            )

    is_complete = len(missing_artifacts) == 0
    scope_repr = "site" if site_id else "study"

    await write_audit_log(
        repo=repo,
        user_id=user_id,
        user_role=user_roles,
        action="COMPLETENESS",
        document_id=None,
        details=f"Performed completeness checking for study '{study_id}', site '{site_id}', milestone '{milestone_normalized}'. Complete: {is_complete}.",
    )

    return CompletenessResponse(
        study_id=study_id,
        site_id=site_id,
        milestone=milestone_normalized,
        is_complete=is_complete,
        scope=scope_repr,
        present_artifacts=present_artifacts,
        missing_artifacts=missing_artifacts,
        per_artifact_detail=per_artifact_detail,
    )


@router.post(
    "/api/v1/etmf/documents/{document_id}/redact",
    response_model=DocumentResponse,
    status_code=201,
)
@transactional
async def redact_document_endpoint(
    request: Request,
    document_id: str,
    payload: RedactRequest,
    repo: ETMFRepositoryPort = Depends(get_etmf_repository),
    principal: Principal = Depends(get_principal),
) -> DocumentResponse:
    session = repo.session
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "etmf_document:redact"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Inspectors are restricted to read-only access.",
        )

    from apps.etmf.lock_client import verify_trial_lock_status

    if await verify_trial_lock_status():
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Trial is currently locked in a read-only state due to a security violation.",
        )

    source_doc = await repo.get_document_by_id(document_id)
    if not source_doc:
        raise HTTPException(status_code=404, detail="eTMF Document not found")

    enforce_document_site_visibility(source_doc, principal)

    if (
        source_doc.status == "SIGNED"
        or source_doc.status == "ARCHIVED"
        or source_doc.approval_status == "APPROVED"
        or source_doc.signature_manifestation is not None
    ):
        await write_audit_log(
            repo=repo,
            user_id=user_id,
            user_role=user_roles,
            action="MUTATION_REJECTED",
            document_id=source_doc.id,
            details=f"Rejected attempt to redact signed document '{source_doc.filename}' (ID: {source_doc.id}). Error: IMMUTABILITY_VIOLATION.",
        )
        await session.commit()
        raise HTTPException(
            status_code=403,
            detail="IMMUTABILITY_VIOLATION: Document is already signed and cannot be modified",
        )

    change_reason = request.headers.get("X-Change-Reason", "").strip()
    if not change_reason:
        change_reason = principal.change_reason or "system_operation".strip()
    if not change_reason:
        raise HTTPException(
            status_code=400,
            detail="Missing change justification reason under X-Change-Reason",
        )

    lineage_docs = await repo.get_document_lineage(
        source_doc.study_id, source_doc.artifact_code
    )
    versions = [d.version_index for d in lineage_docs]
    new_version_index = max(versions) + 1 if versions else source_doc.version_index + 1

    metadata_json = dict(source_doc.metadata_json) if source_doc.metadata_json else {}
    metadata_json["change_reason"] = change_reason
    metadata_json["is_redacted"] = True

    redacted_doc = TMFDocument(
        study_id=source_doc.study_id,
        site_id=source_doc.site_id,
        zone=source_doc.zone,
        section=source_doc.section,
        artifact_type=source_doc.artifact_type,
        filename=payload.redacted_filename
        or f"{os.path.splitext(source_doc.filename)[0]}_redacted{os.path.splitext(source_doc.filename)[1]}",
        content=payload.redacted_content,
        mime_type=source_doc.mime_type,
        created_by=user_id,
        version_index=new_version_index,
        taxonomy_version=source_doc.taxonomy_version,
        artifact_code=source_doc.artifact_code,
        metadata_json=metadata_json,
        document_type=source_doc.document_type,
        approval_status=source_doc.approval_status,
        signature_manifestation=source_doc.signature_manifestation,
        signer=source_doc.signer,
        signing_timestamp=source_doc.signing_timestamp,
        is_redacted=True,
        redaction_source_id=source_doc.id,
        redaction_manifest_json=payload.manifest,
    )

    session.add(redacted_doc)
    await session.flush()

    manifest_signature = payload.manifest.get("signature", "unsigned")
    details_str = (
        f"REDACT action executed. Actor: {user_id}, Role: {user_roles}. "
        f"Source Document Reference ID: {source_doc.id} (Version {source_doc.version_index}). "
        f"Redacted Document Reference ID: {redacted_doc.id} (Version {redacted_doc.version_index}). "
        f"Manifest Signature: {manifest_signature}."
    )
    await write_audit_log(
        repo=repo,
        user_id=user_id,
        user_role=user_roles,
        action="REDACT",
        document_id=redacted_doc.id,
        details=details_str,
    )

    return to_document_response(redacted_doc)


@router.post(
    "/api/v1/etmf/documents/{document_id}/auto-redact",
    response_model=AutomatedRedactResponse,
    status_code=201,
)
@transactional
async def auto_redact_document_endpoint(
    request: Request,
    document_id: str,
    payload: AutomatedRedactRequest,
    repo: ETMFRepositoryPort = Depends(get_etmf_repository),
    principal: Principal = Depends(get_principal),
) -> AutomatedRedactResponse:
    session = repo.session
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "etmf_document:redact"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Inspectors are restricted to read-only access.",
        )

    from apps.etmf.lock_client import verify_trial_lock_status

    if await verify_trial_lock_status():
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Trial is currently locked in a read-only state due to a security violation.",
        )

    source_doc = await repo.get_document_by_id(document_id)
    if not source_doc:
        raise HTTPException(status_code=404, detail="eTMF Document not found")

    from packages.deid.models import ComplianceProfile

    if payload.profile not in ComplianceProfile:
        raise HTTPException(
            status_code=422,
            detail=f"Invalid compliance profile: '{payload.profile}'.",
        )

    enforce_document_site_visibility(source_doc, principal)

    if (
        source_doc.status == "SIGNED"
        or source_doc.status == "ARCHIVED"
        or source_doc.approval_status == "APPROVED"
        or source_doc.signature_manifestation is not None
    ):
        await write_audit_log(
            repo=repo,
            user_id=user_id,
            user_role=user_roles,
            action="MUTATION_REJECTED",
            document_id=source_doc.id,
            details=f"Rejected attempt to auto-redact signed document '{source_doc.filename}' (ID: {source_doc.id}). Error: IMMUTABILITY_VIOLATION.",
        )
        await session.commit()
        raise HTTPException(
            status_code=403,
            detail="IMMUTABILITY_VIOLATION: Document is already signed and cannot be modified",
        )

    change_reason = request.headers.get("X-Change-Reason", "").strip()
    if not change_reason:
        change_reason = principal.change_reason or "system_operation".strip()
    if not change_reason:
        raise HTTPException(
            status_code=400,
            detail="Missing change justification reason under X-Change-Reason",
        )

    detector = DeidDetector()
    try:
        results = detector.detect(
            source_doc.content,
            profile=payload.profile,
            custom_terms=payload.custom_terms,
        )
    except Exception as e:
        raise HTTPException(status_code=422, detail=f"Detection failed: {str(e)}")

    try:
        redacted_content, record = apply_deid_transforms(
            source_doc.content,
            results,
            strategies=payload.strategies,
            default_strategy="mask",
        )
    except Exception as e:
        raise HTTPException(
            status_code=422, detail=f"Redaction transforms failed: {str(e)}"
        )

    lineage_docs = await repo.get_document_lineage(
        source_doc.study_id, source_doc.artifact_code
    )
    versions = [d.version_index for d in lineage_docs]
    new_version_index = max(versions) + 1 if versions else source_doc.version_index + 1

    try:
        manifest = build_redaction_manifest(
            redaction_record=record,
            operator_identity=user_id,
            reason=change_reason,
            source_version="v" + str(source_doc.version_index),
            target_version="v" + str(new_version_index),
        )
    except Exception as e:
        raise HTTPException(status_code=400, detail=str(e))

    secret_key = os.getenv(
        "REDACTION_SIGNING_SECRET", "internal-gateway-secret-12345"
    ).encode("utf-8")
    signed_manifest = sign_manifest_symmetric(manifest, secret_key)
    manifest_data = signed_manifest.model_dump()

    metadata_json = dict(source_doc.metadata_json) if source_doc.metadata_json else {}
    metadata_json["change_reason"] = change_reason
    metadata_json["is_redacted"] = True

    redacted_filename = (
        payload.redacted_filename
        or f"{os.path.splitext(source_doc.filename)[0]}_redacted{os.path.splitext(source_doc.filename)[1]}"
    )

    redacted_doc = TMFDocument(
        study_id=source_doc.study_id,
        site_id=source_doc.site_id,
        zone=source_doc.zone,
        section=source_doc.section,
        artifact_type=source_doc.artifact_type,
        filename=redacted_filename,
        content=redacted_content,
        mime_type=source_doc.mime_type,
        created_by=user_id,
        version_index=new_version_index,
        taxonomy_version=source_doc.taxonomy_version,
        artifact_code=source_doc.artifact_code,
        metadata_json=metadata_json,
        document_type=source_doc.document_type,
        approval_status=source_doc.approval_status,
        signature_manifestation=source_doc.signature_manifestation,
        signer=source_doc.signer,
        signing_timestamp=source_doc.signing_timestamp,
        is_redacted=True,
        redaction_source_id=source_doc.id,
        redaction_manifest_json=manifest_data,
    )

    session.add(redacted_doc)
    await session.flush()

    manifest_signature = manifest_data.get("signature", "unsigned")
    details_str = (
        f"REDACT action executed. Actor: {user_id}, Role: {user_roles}. "
        f"Source Document Reference ID: {source_doc.id} (Version {source_doc.version_index}). "
        f"Redacted Document Reference ID: {redacted_doc.id} (Version {redacted_doc.version_index}). "
        f"Manifest Signature: {manifest_signature}."
    )
    await write_audit_log(
        repo=repo,
        user_id=user_id,
        user_role=user_roles,
        action="REDACT",
        document_id=redacted_doc.id,
        details=details_str,
    )

    return AutomatedRedactResponse(
        status="success",
        document_id=redacted_doc.id,
        version_index=redacted_doc.version_index,
        filename=redacted_doc.filename,
        categories_counts=manifest_data.get("categories_counts", {}),
        manifest=manifest_data,
    )


@router.post(
    "/api/v1/etmf/documents/{document_id}/manual-redact",
    response_model=ManualRedactResponse,
    status_code=201,
)
@transactional
async def manual_redact_document_endpoint(
    request: Request,
    document_id: str,
    payload: ManualRedactRequest,
    repo: ETMFRepositoryPort = Depends(get_etmf_repository),
    principal: Principal = Depends(get_principal),
) -> ManualRedactResponse:
    session = repo.session
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "etmf_document:redact"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Inspectors are restricted to read-only access.",
        )

    from apps.etmf.lock_client import verify_trial_lock_status

    if await verify_trial_lock_status():
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Trial is currently locked in a read-only state due to a security violation.",
        )

    source_doc = await repo.get_document_by_id(document_id)
    if not source_doc:
        raise HTTPException(status_code=404, detail="eTMF Document not found")

    enforce_document_site_visibility(source_doc, principal)

    if (
        source_doc.status == "SIGNED"
        or source_doc.status == "ARCHIVED"
        or source_doc.approval_status == "APPROVED"
        or source_doc.signature_manifestation is not None
    ):
        await write_audit_log(
            repo=repo,
            user_id=user_id,
            user_role=user_roles,
            action="MUTATION_REJECTED",
            document_id=source_doc.id,
            details=f"Rejected attempt to manually redact signed document '{source_doc.filename}' (ID: {source_doc.id}). Error: IMMUTABILITY_VIOLATION.",
        )
        await session.commit()
        raise HTTPException(
            status_code=403,
            detail="IMMUTABILITY_VIOLATION: Document is already signed and cannot be modified",
        )

    change_reason = request.headers.get("X-Change-Reason", "").strip()
    if not change_reason:
        change_reason = principal.change_reason or "system_operation".strip()
    if not change_reason:
        raise HTTPException(
            status_code=400,
            detail="Missing change justification reason under X-Change-Reason",
        )

    content_len = len(source_doc.content)
    results = []

    if payload.spans:
        sorted_spans = sorted(payload.spans, key=lambda s: s.start)
        seen = []
        for span in sorted_spans:
            if span.start < 0 or span.end > content_len or span.start >= span.end:
                raise HTTPException(
                    status_code=422,
                    detail=f"Invalid span offsets: [{span.start}, {span.end}] is invalid or out of range for document of length {content_len}.",
                )
            for prev in seen:
                if max(span.start, prev.start) < min(span.end, prev.end):
                    raise HTTPException(
                        status_code=422,
                        detail=f"Overlapping or conflicting span inputs detected: [{span.start}, {span.end}] conflicts with [{prev.start}, {prev.end}].",
                    )
            seen.append(span)

            span_value = source_doc.content[span.start : span.end]
            results.append(
                DetectionResult(
                    category=span.label or "manual",
                    start=span.start,
                    end=span.end,
                    value=span_value,
                )
            )

    if payload.terms:
        import re

        valid_terms = [t for t in payload.terms if t and t.strip()]
        if valid_terms:
            valid_terms.sort(key=len, reverse=True)
            escaped_terms = [re.escape(term) for term in valid_terms]
            patterns = []
            for term in escaped_terms:
                start_b = r"\b" if re.match(r"^\w", term) else ""
                end_b = r"\b" if re.search(r"\w$", term) else ""
                patterns.append(f"{start_b}{term}{end_b}")

            custom_regex = re.compile("|".join(patterns), re.IGNORECASE)
            for m in custom_regex.finditer(source_doc.content):
                results.append(
                    DetectionResult(
                        category=DetectorCategory.CUSTOM,
                        start=m.start(),
                        end=m.end(),
                        value=m.group(),
                    )
                )

    try:
        redacted_content, record = apply_deid_transforms(
            source_doc.content,
            results,
            default_strategy="mask",
        )
    except Exception as e:
        raise HTTPException(
            status_code=422, detail=f"Redaction transforms failed: {str(e)}"
        )

    lineage_docs = await repo.get_document_lineage(
        source_doc.study_id, source_doc.artifact_code
    )
    versions = [d.version_index for d in lineage_docs]
    new_version_index = max(versions) + 1 if versions else source_doc.version_index + 1

    try:
        manifest = build_redaction_manifest(
            redaction_record=record,
            operator_identity=user_id,
            reason=change_reason,
            source_version="v" + str(source_doc.version_index),
            target_version="v" + str(new_version_index),
        )
    except Exception as e:
        raise HTTPException(status_code=400, detail=str(e))

    secret_key = os.getenv(
        "REDACTION_SIGNING_SECRET", "internal-gateway-secret-12345"
    ).encode("utf-8")
    signed_manifest = sign_manifest_symmetric(manifest, secret_key)
    manifest_data = signed_manifest.model_dump()

    metadata_json = dict(source_doc.metadata_json) if source_doc.metadata_json else {}
    metadata_json["change_reason"] = change_reason
    metadata_json["is_redacted"] = True

    redacted_filename = (
        payload.redacted_filename
        or f"{os.path.splitext(source_doc.filename)[0]}_redacted{os.path.splitext(source_doc.filename)[1]}"
    )

    redacted_doc = TMFDocument(
        study_id=source_doc.study_id,
        site_id=source_doc.site_id,
        zone=source_doc.zone,
        section=source_doc.section,
        artifact_type=source_doc.artifact_type,
        filename=redacted_filename,
        content=redacted_content,
        mime_type=source_doc.mime_type,
        created_by=user_id,
        version_index=new_version_index,
        taxonomy_version=source_doc.taxonomy_version,
        artifact_code=source_doc.artifact_code,
        metadata_json=metadata_json,
        document_type=source_doc.document_type,
        approval_status=source_doc.approval_status,
        signature_manifestation=source_doc.signature_manifestation,
        signer=source_doc.signer,
        signing_timestamp=source_doc.signing_timestamp,
        is_redacted=True,
        redaction_source_id=source_doc.id,
        redaction_manifest_json=manifest_data,
    )

    session.add(redacted_doc)
    await session.flush()

    manifest_signature = manifest_data.get("signature", "unsigned")
    details_str = (
        f"REDACT action executed. Actor: {user_id}, Role: {user_roles}. "
        f"Source Document Reference ID: {source_doc.id} (Version {source_doc.version_index}). "
        f"Redacted Document Reference ID: {redacted_doc.id} (Version {redacted_doc.version_index}). "
        f"Manifest Signature: {manifest_signature}."
    )
    await write_audit_log(
        repo=repo,
        user_id=user_id,
        user_role=user_roles,
        action="REDACT",
        document_id=redacted_doc.id,
        details=details_str,
    )

    return ManualRedactResponse(
        status="success",
        document_id=redacted_doc.id,
        version_index=redacted_doc.version_index,
        filename=redacted_doc.filename,
        categories_counts=manifest_data.get("categories_counts", {}),
        manifest=manifest_data,
    )


@router.get("/api/v1/etmf/test-exception")
@transactional
async def test_exception_route(repo: ETMFRepositoryPort = Depends(get_etmf_repository)):
    raise RuntimeError("Intentional test database rollback error")


@router.post(
    "/api/v1/etmf/documents/{document_id}/transition", response_model=dict[str, Any]
)
@transactional
async def transition_document_status_endpoint(
    request: Request,
    document_id: str,
    payload: TransitionRequest,
    repo: ETMFRepositoryPort = Depends(get_etmf_repository),
    principal: Principal = Depends(get_principal),
) -> dict[str, Any]:
    session = repo.session
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    doc = await repo.get_document_by_id(document_id)
    if not doc:
        raise HTTPException(status_code=404, detail="eTMF Document not found")

    enforce_document_site_visibility(doc, principal)

    valid_qc_statuses = {
        DocumentStatus.DRAFT.value,
        DocumentStatus.TECHNICAL_QC.value,
        DocumentStatus.CLINICAL_QC.value,
        DocumentStatus.APPROVED.value,
        DocumentStatus.ARCHIVED.value,
        DocumentStatus.REJECTED.value,
    }
    if payload.to_status not in valid_qc_statuses:
        raise HTTPException(
            status_code=422,
            detail=f"Invalid status: '{payload.to_status}'. Must be one of {sorted(list(valid_qc_statuses))}.",
        )

    if (
        doc.status == "SIGNED"
        or doc.status == "ARCHIVED"
        or doc.approval_status == "APPROVED"
        or doc.signature_manifestation is not None
    ):
        await write_audit_log(
            repo=repo,
            user_id=user_id,
            user_role=user_roles,
            action="MUTATION_REJECTED",
            document_id=doc.id,
            details=f"Rejected attempt to transition status of signed document '{doc.filename}' (ID: {doc.id}). Error: IMMUTABILITY_VIOLATION.",
        )
        await session.commit()
        raise HTTPException(
            status_code=403,
            detail="IMMUTABILITY_VIOLATION: Document is already signed and cannot be modified",
        )

    try:
        await validate_and_transition_document_status(
            session=session,
            document=doc,
            to_status=payload.to_status,
            actor_id=user_id,
            actor_role=user_roles,
            reason_for_change=payload.reason_for_change,
        )
    except ValueError as e:
        raise HTTPException(status_code=422, detail=str(e))
    except PermissionError as e:
        raise HTTPException(status_code=403, detail=str(e))

    await write_audit_log(
        repo=repo,
        user_id=user_id,
        user_role=user_roles,
        action="QC_TRANSITION",
        document_id=doc.id,
        details=f"Document '{doc.filename}' (ID: {doc.id}) transitioned to status '{payload.to_status}'.",
    )

    return {
        "status": "success",
        "document_id": doc.id,
        "new_status": doc.status,
    }


@router.put(
    "/api/v1/etmf/documents/{document_id}/expiration",
    response_model=DocumentResponse,
)
@transactional
async def update_document_expiration_endpoint(
    request: Request,
    document_id: str,
    payload: DocumentExpirationUpdate,
    repo: ETMFRepositoryPort = Depends(get_etmf_repository),
    principal: Principal = Depends(get_principal),
) -> DocumentResponse:
    session = repo.session
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    doc = await repo.get_document_by_id(document_id)
    if not doc:
        raise HTTPException(status_code=404, detail="eTMF Document not found")

    enforce_document_site_visibility(doc, principal)

    if not has_permission(principal, "etmf_document:manage_expiration"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Lacks manage_expiration permission to set or change expiration metadata.",
        )

    from apps.etmf.lock_client import verify_trial_lock_status

    if await verify_trial_lock_status():
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Trial is currently locked in a read-only state due to a security violation.",
        )

    if (
        doc.status == "SIGNED"
        or doc.status == "ARCHIVED"
        or doc.approval_status == "APPROVED"
        or doc.signature_manifestation is not None
    ):
        await write_audit_log(
            repo=repo,
            user_id=user_id,
            user_role=user_roles,
            action="MUTATION_REJECTED",
            document_id=doc.id,
            details=f"Rejected attempt to update expiration of signed document '{doc.filename}' (ID: {doc.id}). Error: IMMUTABILITY_VIOLATION.",
        )
        await session.commit()
        raise HTTPException(
            status_code=403,
            detail="IMMUTABILITY_VIOLATION: Document is already signed and cannot be modified",
        )

    doc.issue_date = payload.issue_date
    resolved_expiration_date = payload.expiration_date
    if resolved_expiration_date is not None and not isinstance(
        resolved_expiration_date, datetime
    ):
        resolved_expiration_date = datetime.combine(
            resolved_expiration_date, datetime.min.time()
        ).replace(tzinfo=UTC)
    doc.expiration_date = resolved_expiration_date
    doc.document_owner_id = payload.document_owner_id
    doc.version_index += 1

    await session.flush()

    details = f"Updated expiration metadata for document '{doc.filename}' (ID: {doc.id}): issue_date={payload.issue_date}, expiration_date={payload.expiration_date}, owner={payload.document_owner_id}."
    reason_for_change = request.headers.get("X-Change-Reason", "").strip()
    if not reason_for_change:
        reason_for_change = getattr(request.state, "change_reason", "").strip()
    if not reason_for_change:
        reason_for_change = principal.change_reason or "System operation"
    reason_for_change = reason_for_change.strip()

    await write_audit_log(
        repo=repo,
        user_id=user_id,
        user_role=user_roles,
        action="UPDATE_EXPIRATION",
        document_id=doc.id,
        details=details + f" Reason: {reason_for_change}",
    )

    return to_document_response(doc)


@router.post(
    "/api/v1/etmf/documents/{document_id}/sign-off",
    response_model=DocumentResponse,
    status_code=200,
)
@router.post(
    "/api/v1/etmf/documents/{document_id}/approve",
    response_model=DocumentResponse,
    status_code=200,
)
@transactional
async def sign_document_endpoint(
    request: Request,
    document_id: str,
    payload: SignDocumentRequest,
    repo: ETMFRepositoryPort = Depends(get_etmf_repository),
    principal: Principal = Depends(get_principal),
) -> DocumentResponse:
    session = repo.session
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "etmf_document:sign"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Inspectors are restricted to read-only access.",
        )

    from apps.etmf.lock_client import verify_trial_lock_status

    if await verify_trial_lock_status():
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Trial is currently locked in a read-only state due to a security violation.",
        )

    doc = await repo.get_document_by_id(document_id)
    if not doc:
        raise HTTPException(status_code=404, detail="eTMF Document not found")

    enforce_document_site_visibility(doc, principal)

    if (
        doc.status == "SIGNED"
        or doc.status == "ARCHIVED"
        or doc.approval_status == "APPROVED"
        or doc.signature_manifestation is not None
    ):
        await write_audit_log(
            repo=repo,
            user_id=user_id,
            user_role=user_roles,
            action="MUTATION_REJECTED",
            document_id=doc.id,
            details=f"Rejected attempt to sign already signed document '{doc.filename}' (ID: {doc.id}). Error: IMMUTABILITY_VIOLATION.",
        )
        await session.commit()
        raise HTTPException(
            status_code=403,
            detail="IMMUTABILITY_VIOLATION: Document is already signed and cannot be modified",
        )

    import hashlib
    from datetime import datetime, timedelta

    from cryptography import x509
    from cryptography.hazmat.primitives import hashes, serialization
    from cryptography.hazmat.primitives.asymmetric import rsa
    from cryptography.x509.oid import NameOID

    from packages.security.signature import SignatureManifestation
    from packages.security.signing import (
        asymmetric_sign,
        capture_certificate_identifiers,
    )

    client_ip = getattr(request.state, "ip_address", None)
    if not client_ip:
        client_ip = request.headers.get("x-forwarded-for") or (
            request.client.host if request.client else "127.0.0.1"
        )
        if "," in client_ip:
            client_ip = client_ip.split(",")[0].strip()

    user_agent = request.headers.get("user-agent") or "eTMF Service"
    now_utc = datetime.now(UTC)
    doc_hash = hashlib.sha256(doc.content.encode("utf-8")).hexdigest()

    manifest = SignatureManifestation(
        signer_id=user_id,
        timestamp=now_utc,
        signing_reason=payload.signing_reason,
        ip_address=client_ip,
        user_agent=user_agent,
        sha256_hash=doc_hash,
    )

    private_key = rsa.generate_private_key(
        public_exponent=65537,
        key_size=2048,
    )

    subject = issuer = x509.Name(
        [
            x509.NameAttribute(NameOID.COUNTRY_NAME, "US"),
            x509.NameAttribute(NameOID.STATE_OR_PROVINCE_NAME, "California"),
            x509.NameAttribute(NameOID.ORGANIZATION_NAME, "Cadence Clinical"),
            x509.NameAttribute(NameOID.COMMON_NAME, f"user-{user_id}"),
        ]
    )

    cert = (
        x509.CertificateBuilder()
        .subject_name(subject)
        .issuer_name(issuer)
        .public_key(private_key.public_key())
        .serial_number(x509.random_serial_number())
        .not_valid_before(now_utc - timedelta(days=1))
        .not_valid_after(now_utc + timedelta(days=10))
        .add_extension(
            x509.SubjectKeyIdentifier.from_public_key(private_key.public_key()),
            critical=False,
        )
        .sign(private_key, hashes.SHA256())
    )

    private_key_pem = private_key.private_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PrivateFormat.TraditionalOpenSSL,
        encryption_algorithm=serialization.NoEncryption(),
    ).decode("utf-8")

    cert_pem = cert.public_bytes(serialization.Encoding.PEM).decode("utf-8")

    canonical_bytes = manifest.get_canonical_bytes()
    sig_b64 = asymmetric_sign(canonical_bytes, private_key_pem)
    ids = capture_certificate_identifiers(cert_pem)

    manifest.signature = sig_b64
    manifest.certificate_pem = cert_pem
    manifest.key_identifier = ids["subject_key_identifier"]

    if not manifest.verify():
        await write_audit_log(
            repo=repo,
            user_id=user_id,
            user_role=user_roles,
            action="SIGNATURE_FAILED",
            document_id=doc.id,
            details=f"Signature verification failed for document '{doc.filename}' (ID: {doc.id}).",
        )
        await session.commit()
        raise HTTPException(
            status_code=400,
            detail="Signature verification failed: Cryptographic signature is invalid.",
        )

    doc.status = "SIGNED"
    doc.approval_status = "APPROVED"
    doc.signature_manifestation = manifest.model_dump(mode="json")
    doc.signer = user_id
    doc.signing_timestamp = now_utc.replace(tzinfo=None)

    await write_audit_log(
        repo=repo,
        user_id=user_id,
        user_role=user_roles,
        action="SIGN",
        document_id=doc.id,
        details=f"Successfully signed document '{doc.filename}' (ID: {doc.id}) with reason '{payload.signing_reason.value}'.",
    )

    await write_audit_log(
        repo=repo,
        user_id=user_id,
        user_role=user_roles,
        action="APPROVE",
        document_id=doc.id,
        details=f"Successfully approved document '{doc.filename}' (ID: {doc.id}) with reason '{payload.signing_reason.value}'.",
    )

    # Write outbox archival record within the service transaction
    import uuid

    from apps.etmf.infrastructure.models import IntegrationOutbox

    outbox_entry = IntegrationOutbox(
        id=str(uuid.uuid4()),
        event_type="DOCUMENT_ARCHIVAL",
        payload={
            "document_id": doc.id,
            "filename": doc.filename,
            "content": doc.content,
            "study_id": doc.study_id,
            "site_id": doc.site_id,
        },
        status="PENDING",
        attempts=0,
        correlation_id=f"archive-{uuid.uuid4().hex[:12]}",
        created_by=user_id,
        reason_for_change=payload.signing_reason.value,
    )
    session.add(outbox_entry)

    await session.flush()

    return to_document_response(doc)


@router.get(
    "/api/v1/etmf/studies/{study_id}/artifacts/{artifact_type}/history",
    response_model=list[DocumentResponse],
)
@transactional
async def get_artifact_history(
    study_id: str,
    artifact_type: str,
    repo: ETMFRepositoryPort = Depends(get_etmf_repository),
    principal: Principal = Depends(get_principal),
) -> list[DocumentResponse]:
    session = repo.session
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "etmf_document:read"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Insufficient permissions to read eTMF documents.",
        )

    version = get_active_catalog().version
    canonical_name = artifact_type
    try:
        resolved = resolve_artifact(version=version, name=artifact_type)
        canonical_name = resolved["artifact"].name
    except ValueError:
        pass

    docs = await repo.get_document_history(
        study_id, artifact_type, canonical_name, principal
    )

    filtered_docs = []
    for doc in docs:
        try:
            await authorize_document_read(principal, doc, session)
            filtered_docs.append(doc)
        except Exception:
            continue

    await write_audit_log(
        repo=repo,
        user_id=user_id,
        user_role=user_roles,
        action="HISTORY_VIEW",
        document_id=None,
        details=f"Viewed artifact history for study '{study_id}', artifact_type '{artifact_type}'.",
    )

    return [to_document_response(doc) for doc in filtered_docs]


@router.get(
    "/api/v1/etmf/documents/{document_id}/transitions",
    response_model=list[TransitionResponse],
)
@transactional
async def get_document_transition_history(
    request: Request,
    document_id: str,
    repo: ETMFRepositoryPort = Depends(get_etmf_repository),
    principal: Principal = Depends(get_principal),
) -> list[TransitionResponse]:
    session = repo.session
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "etmf_document:read"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Insufficient permissions to read eTMF documents.",
        )

    doc_obj = await repo.get_document_by_id(document_id)
    if not doc_obj:
        raise HTTPException(status_code=404, detail="eTMF Document not found")

    await authorize_document_read(principal, doc_obj, session)

    transitions = await repo.get_qc_transitions_by_document_id_asc(document_id)

    await write_audit_log(
        repo=repo,
        user_id=user_id,
        user_role=user_roles,
        action="QC_HISTORY_VIEW",
        document_id=document_id,
        details=f"Viewed QC transition history for document ID: {document_id}.",
    )

    return [
        TransitionResponse(
            id=t.id,
            document_id=t.document_id,
            from_status=t.from_status,
            to_status=t.to_status,
            actor_id=t.actor_id,
            actor_role=t.actor_role,
            reason_for_change=t.reason_for_change,
            timestamp=t.timestamp.isoformat(),
        )
        for t in transitions
    ]


def parse_recipient_address(recipient: str) -> tuple[str, str | None]:
    _, address = email.utils.parseaddr(recipient)
    if not address or "@" not in address:
        raise ValueError("Invalid recipient address format")
    local_part = address.split("@")[0]
    if not local_part.startswith("study-"):
        raise ValueError("Recipient address local part must start with 'study-'")

    parts = local_part[len("study-") :].split("+", 1)
    study_id = parts[0].strip()
    if not study_id:
        raise ValueError("Study ID cannot be empty or whitespace")

    binder_hint = parts[1].strip() if len(parts) > 1 else None
    if binder_hint == "":
        binder_hint = None
    return study_id, binder_hint


def resolve_binder_hint(binder_hint: str | None) -> tuple[int, str, str, str]:
    if not binder_hint:
        return 5, "04", "05.04.01", "Site Communication Log"

    cleaned_hint = binder_hint.strip()
    if cleaned_hint.lower() in ("conduct", "initiation", "closeout", "milestone"):
        cleaned_hint = "Site Communication Log"

    from apps.etmf.classification_service import classify_tmf_document

    classification = classify_tmf_document(filename="", artifact_type=cleaned_hint)
    if classification is None:
        raise ValueError(f"Unresolvable binder hint: {binder_hint}")

    return (
        classification.resolved_zone,
        classification.resolved_section,
        classification.artifact_code,
        classification.artifact_type,
    )


@router.post("/api/v1/etmf/inbound-email", status_code=201)
@transactional
async def receive_inbound_email(
    request: Request,
    repo: ETMFRepositoryPort = Depends(get_etmf_repository),
) -> dict[str, Any]:
    session = repo.session
    content_length_str = request.headers.get("content-length")
    max_size = int(os.getenv("INBOUND_EMAIL_MAX_SIZE_BYTES", str(10 * 1024 * 1024)))
    if content_length_str:
        try:
            content_length = int(content_length_str)
            if content_length > max_size:
                raise HTTPException(status_code=413, detail="Payload too large")
        except ValueError:
            raise HTTPException(status_code=400, detail="Invalid Content-Length")

    try:
        form_data = await request.form()
    except Exception:
        raise HTTPException(status_code=400, detail="Invalid form data")

    sender = form_data.get("sender")
    recipient = form_data.get("recipient")
    subject = form_data.get("subject")
    body_plain = form_data.get("body-plain") or form_data.get("body_plain") or ""
    timestamp = form_data.get("timestamp")
    token = form_data.get("token")
    signature = form_data.get("signature")
    message_id = (
        form_data.get("Message-Id")
        or form_data.get("message-id")
        or form_data.get("Message-ID")
    )

    sender = str(sender) if sender is not None else ""
    recipient = str(recipient) if recipient is not None else ""
    subject = str(subject) if subject is not None else ""
    body_plain = str(body_plain)
    timestamp = str(timestamp) if timestamp is not None else ""
    token = str(token) if token is not None else ""
    signature = str(signature) if signature is not None else ""
    message_id = str(message_id) if message_id is not None else ""

    from packages.security.signing import verify_inbound_email_signature

    if not verify_inbound_email_signature(timestamp, token, signature, message_id):
        raise HTTPException(status_code=401, detail="Unauthorized")

    try:
        study_id, binder_hint = parse_recipient_address(recipient)
        zone, section, artifact_code, artifact_type = resolve_binder_hint(binder_hint)
    except Exception:
        raise HTTPException(status_code=422, detail="Invalid routing metadata")

    if message_id:
        existing_doc = await repo.get_document_by_message_id(message_id)
        if existing_doc:
            return {"status": "accepted"}

    try:
        attachments = []
        for key, value in form_data.multi_items():
            if hasattr(value, "filename") and value.filename:
                attachments.append(value)

        body_filename = f"email_body_{message_id or int(time.time())}.txt"

        metadata_json = {
            "message_id": message_id,
            "sender": sender,
            "recipient": recipient,
            "subject": subject,
            "ingested_via": "inbound-email",
        }

        audit_details = (
            f"Inbound email webhook ingestion. Sender: {sender}, Subject: '{subject}', "
            f"Message-Id: {message_id}."
        )

        async with session.begin_nested():
            await ingest_tmf_document(
                session=session,
                study_id=study_id,
                artifact_type=artifact_type,
                filename=body_filename,
                content=body_plain
                or f"Subject: {subject}\nFrom: {sender}\n(Empty Body)",
                mime_type="text/plain",
                created_by="system",
                created_role="system",
                zone=zone,
                section=section,
                artifact_code=artifact_code,
                metadata_json=metadata_json,
                audit_action="EMAIL_INGEST",
                audit_details=audit_details,
                reason_for_change="inbound_email_webhook",
            )

            for idx, att in enumerate(attachments):
                att_bytes = await att.read()
                if len(att_bytes) > max_size:
                    raise HTTPException(status_code=413, detail="Attachment too large")

                att_mime = att.content_type or "application/octet-stream"
                att_mime_lower = att_mime.lower().strip()
                is_att_binary = (
                    "pdf" in att_mime_lower
                    or "wordprocessingml" in att_mime_lower
                    or "docx" in att_mime_lower
                    or att_mime_lower == "application/octet-stream"
                )

                if is_att_binary:
                    att_content = att_bytes
                else:
                    att_content = att_bytes.decode("utf-8", errors="ignore")
                att_filename = att.filename or f"attachment_{idx}"
                att_metadata = dict(metadata_json)
                att_metadata["attachment_index"] = idx
                att_metadata["original_filename"] = att_filename

                await ingest_tmf_document(
                    session=session,
                    study_id=study_id,
                    artifact_type=artifact_type,
                    filename=att_filename,
                    content=att_content,
                    mime_type=att_mime,
                    created_by="system",
                    created_role="system",
                    zone=zone,
                    section=section,
                    artifact_code=artifact_code,
                    metadata_json=att_metadata,
                    audit_action="EMAIL_INGEST",
                    audit_details=f"Ingested attachment '{att_filename}' from email Message-Id {message_id}.",
                    reason_for_change="inbound_email_webhook",
                )

        await session.commit()
        return {"status": "accepted"}

    except Exception as e:
        if isinstance(e, PermissionError):
            if "IMMUTABILITY_VIOLATION" in str(e):
                raise HTTPException(
                    status_code=403,
                    detail="IMMUTABILITY_VIOLATION: Document is already signed and cannot be modified",
                )
            raise HTTPException(status_code=403, detail="Forbidden")
        if isinstance(e, HTTPException):
            raise e
        raise HTTPException(status_code=500, detail="Internal processing failure")


def build_binder_structure(
    catalog: Any,
    archived_docs: list[TMFDocument],
    expected_codes: set[str],
    site_id: str | None = None,
    is_site_scoped: bool = False,
    principal: Principal | None = None,
) -> tuple[list[BinderZoneNode], list[str], list[str]]:
    from packages.security.rbac import can_access_site, can_access_study

    highest_docs = {}
    for doc in archived_docs:
        if not doc.artifact_code:
            continue

        if principal:
            if not can_access_study(principal, doc.study_id):
                continue
            if not can_access_site(principal, doc.site_id):
                continue
            if site_id and doc.site_id != site_id:
                continue
            if not site_id and not is_site_scoped and doc.site_id is not None:
                continue
        else:
            if site_id:
                if doc.site_id != site_id:
                    continue
            else:
                if doc.site_id is not None:
                    continue

        existing = highest_docs.get(doc.artifact_code)
        if not existing or doc.version_index > existing.version_index:
            highest_docs[doc.artifact_code] = doc

    zones_list = []
    present_artifacts = []
    missing_artifacts = []

    for z in catalog.zones:
        zone = catalog.get_zone(z.code)
        if not zone:
            continue
        sections_list = []
        for s in zone.sections:
            section = catalog.get_section(s.code)
            if not section:
                continue
            artifacts_list = []
            for artifact in section.artifacts:
                doc = highest_docs.get(artifact.code)
                if doc:
                    status = "PRESENT"
                    doc_id = doc.id
                    v_idx = doc.version_index
                    if artifact.name not in present_artifacts:
                        present_artifacts.append(artifact.name)
                else:
                    doc_id = None
                    v_idx = None
                    if artifact.code in expected_codes:
                        status = "MISSING"
                        if artifact.name not in missing_artifacts:
                            missing_artifacts.append(artifact.name)
                    else:
                        status = "EXPECTED"

                artifacts_list.append(
                    BinderArtifactNode(
                        artifact_code=artifact.code,
                        artifact_name=artifact.name,
                        status=status,
                        document_id=doc_id,
                        version_index=v_idx,
                    )
                )
            sections_list.append(
                BinderSectionNode(
                    section_code=section.code,
                    section_name=section.name,
                    artifacts=artifacts_list,
                )
            )
        zones_list.append(
            BinderZoneNode(
                zone_code=zone.code,
                zone_name=zone.name,
                sections=sections_list,
            )
        )

    return zones_list, present_artifacts, missing_artifacts


@router.get(
    "/api/v1/etmf/studies/{study_id}/binder/structure",
    response_model=BinderStructureResponse,
)
@transactional
async def get_binder_structure(
    study_id: str,
    milestone: str | None = Query(
        None, description="Optional clinical study milestone"
    ),
    site_id: str | None = Query(None, description="Optional clinical site ID"),
    repo: ETMFRepositoryPort = Depends(get_etmf_repository),
    principal: Principal = Depends(get_principal),
) -> BinderStructureResponse:
    session = repo.session
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "etmf_document:read"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Insufficient permissions to read eTMF documents.",
        )

    from packages.security.rbac import can_access_site, can_access_study

    if not can_access_study(principal, study_id):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: You cannot view binder structure for this study.",
        )
    if not can_access_site(principal, site_id):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: You can only view binder structure for your assigned site(s).",
        )

    is_site_scoped = len(principal.assigned_sites) > 0

    version = get_active_catalog().version

    milestone_normalized = None
    if milestone:
        milestone_normalized = normalize_milestone(milestone)
        try:
            get_mandatory_artifacts(milestone_normalized, version)
        except ValueError as e:
            raise HTTPException(
                status_code=400,
                detail=f"Unknown milestone. Supported: INITIATION, CONDUCT, CLOSEOUT. Error: {str(e)}",
            )
        await seed_default_edl(repo, study_id, milestone_normalized)

    expected_docs = await repo.get_expected_documents_by_study_and_site(
        study_id, site_id
    )
    if milestone_normalized:
        expected_docs = [
            e for e in expected_docs if e.milestone == milestone_normalized
        ]
    archived_docs = await repo.get_documents_by_study(study_id)

    expected_codes = set()
    for exp in expected_docs:
        try:
            resolved_exp = resolve_artifact(version, name=exp.artifact_type)
            expected_codes.add(resolved_exp["artifact"].code)
        except ValueError:
            pass

    catalog = get_active_catalog()
    zones_list, present_artifacts, missing_artifacts = build_binder_structure(
        catalog=catalog,
        archived_docs=archived_docs,
        expected_codes=expected_codes,
        site_id=site_id,
        is_site_scoped=is_site_scoped,
        principal=principal,
    )

    await write_audit_log(
        repo=repo,
        user_id=user_id,
        user_role=user_roles,
        action="BINDER_STRUCTURE_VIEW",
        document_id=None,
        details=f"Viewed binder structure for study '{study_id}', site '{site_id}', milestone '{milestone_normalized}'.",
    )
    await session.commit()

    return BinderStructureResponse(
        study_id=study_id,
        milestone=milestone_normalized,
        site_id=site_id,
        zones=zones_list,
        present_artifacts=present_artifacts,
        missing_artifacts=missing_artifacts,
    )


@router.get("/api/v1/etmf/studies/{study_id}/binder")
@transactional
async def export_regulatory_binder(
    study_id: str,
    include_history: bool = Query(
        False, description="Include full version history of documents"
    ),
    repo: ETMFRepositoryPort = Depends(get_etmf_repository),
    principal: Principal = Depends(get_principal),
) -> Response:
    session = repo.session
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "etmf_audit_logs:read"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Access is restricted to authorized auditor/inspection roles.",
        )

    await write_audit_log(
        repo=repo,
        user_id=user_id,
        user_role=user_roles,
        action="BINDER_EXPORT",
        document_id=None,
        details=f"Exported regulatory binder for study '{study_id}' (include_history={include_history}).",
    )
    await session.flush()

    zip_bytes = await generate_binder_zip(
        session=session,
        study_id=study_id,
        include_history=include_history,
        requester_id=user_id,
        requester_role=user_roles,
        principal=principal,
    )

    filename = f"study_{study_id}_binder.zip"
    return Response(
        content=zip_bytes,
        media_type="application/zip",
        headers={"Content-Disposition": f"attachment; filename={filename}"},
    )


@router.post(
    "/api/v1/etmf/studies/{study_id}/archive",
    response_model=StudyArchiveResponse,
    status_code=200,
)
@transactional
async def bulk_archive_study_documents(
    request: Request,
    study_id: str,
    payload: StudyArchiveRequest,
    repo: ETMFRepositoryPort = Depends(get_etmf_repository),
    principal: Principal = Depends(get_principal),
) -> StudyArchiveResponse:
    session = repo.session
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "etmf_document:transition_archived"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Caller lacks the required etmf_document:transition_archived permission.",
        )

    documents = await repo.get_documents_by_study(study_id)

    if not documents:
        return StudyArchiveResponse(
            status="success",
            study_id=study_id,
            total_processed=0,
            successful_count=0,
            failed_count=0,
            skipped_count=0,
            results=[],
        )

    results: list[StudyArchiveItemResult] = []
    successful_count = 0
    failed_count = 0
    skipped_count = 0

    async with session.begin_nested() as nested_tx:
        failed = False
        first_error = None
        for doc in documents:
            from_status = doc.status or "DRAFT"

            if from_status == "ARCHIVED":
                skipped_count += 1
                results.append(
                    StudyArchiveItemResult(
                        document_id=doc.id,
                        filename=doc.filename,
                        from_status=from_status,
                        to_status="ARCHIVED",
                        status="skipped",
                    )
                )
                continue

            try:
                await validate_and_transition_document_status(
                    session=session,
                    document=doc,
                    to_status="ARCHIVED",
                    actor_id=user_id,
                    actor_role=user_roles,
                    reason_for_change=payload.reason_for_change,
                )
                successful_count += 1
                results.append(
                    StudyArchiveItemResult(
                        document_id=doc.id,
                        filename=doc.filename,
                        from_status=from_status,
                        to_status="ARCHIVED",
                        status="success",
                    )
                )
            except Exception as e:
                failed_count += 1
                results.append(
                    StudyArchiveItemResult(
                        document_id=doc.id,
                        filename=doc.filename,
                        from_status=from_status,
                        to_status="ARCHIVED",
                        status="failed",
                        error_message=str(e),
                    )
                )
                if payload.all_or_nothing:
                    failed = True
                    first_error = str(e)
                    break

        if failed and payload.all_or_nothing:
            await nested_tx.rollback()
            raise HTTPException(
                status_code=400,
                detail=f"All-or-nothing validation failure: Archival aborted because document transition failed. Error: {first_error}",
            )

    overall_status = "success"
    if failed_count > 0:
        overall_status = "partial_success" if successful_count > 0 else "failed"

    details_msg = (
        f"Bulk study archive completed for study '{study_id}'. "
        f"Status: {overall_status}. Successful: {successful_count}, Failed: {failed_count}, Skipped: {skipped_count}."
    )
    await write_audit_log(
        repo=repo,
        user_id=user_id,
        user_role=user_roles,
        action="STUDY_ARCHIVE",
        document_id=None,
        details=details_msg,
    )

    return StudyArchiveResponse(
        status=overall_status,
        study_id=study_id,
        total_processed=len(documents),
        successful_count=successful_count,
        failed_count=failed_count,
        skipped_count=skipped_count,
        results=results,
    )


@router.get(
    "/api/v1/etmf/documents/{document_id}/qc-history",
    response_model=list[TransitionResponse],
)
@transactional
async def get_document_qc_history(
    request: Request,
    document_id: str,
    repo: ETMFRepositoryPort = Depends(get_etmf_repository),
    principal: Principal = Depends(get_principal),
) -> list[TransitionResponse]:
    session = repo.session
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "etmf_document:read"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Insufficient permissions to read eTMF documents.",
        )

    doc_obj = await repo.get_document_by_id(document_id)
    if not doc_obj:
        raise HTTPException(status_code=404, detail="eTMF Document not found")

    await authorize_document_read(principal, doc_obj, session)

    transitions = await repo.get_qc_transitions_by_document_id_asc(document_id)

    await write_audit_log(
        repo=repo,
        user_id=user_id,
        user_role=user_roles,
        action="QC_HISTORY_VIEW",
        document_id=document_id,
        details=f"Viewed QC transition history for document ID: {document_id}.",
    )

    return [
        TransitionResponse(
            id=t.id,
            document_id=t.document_id,
            from_status=t.from_status,
            to_status=t.to_status,
            actor_id=t.actor_id,
            actor_role=t.actor_role,
            reason_for_change=t.reason_for_change,
            timestamp=t.timestamp.isoformat(),
        )
        for t in transitions
    ]


@router.post("/api/v1/etmf/locks/trial/lock")
async def etmf_lock_trial_endpoint(request: Request) -> dict[str, str]:
    from apps.etmf.infrastructure import lock_client

    lock_client.trial_lock_override = True
    return {"status": "success", "message": "Trial lock propagated to eTMF."}


@router.post("/api/v1/etmf/locks/trial/unlock")
async def etmf_unlock_trial_endpoint(request: Request) -> dict[str, str]:
    from apps.etmf.infrastructure import lock_client

    lock_client.trial_lock_override = False
    return {"status": "success", "message": "Trial unlock propagated to eTMF."}


@router.get("/api/v1/admin/outbox")
async def etmf_admin_outbox_endpoint(
    status: str | None = None,
    event_type: str | None = None,
) -> list[dict]:
    from sqlalchemy import select

    from apps.etmf.infrastructure.database import db_manager
    from apps.etmf.infrastructure.models import IntegrationOutbox

    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        stmt = select(IntegrationOutbox)
        if status:
            stmt = stmt.where(IntegrationOutbox.status == status)
        if event_type:
            stmt = stmt.where(IntegrationOutbox.event_type == event_type)
        stmt = stmt.order_by(IntegrationOutbox.created_at.desc())
        res = await session.execute(stmt)
        records = res.scalars().all()

        return [
            {
                "id": r.id,
                "event_type": r.event_type,
                "payload": r.payload,
                "status": r.status,
                "attempts": r.attempts,
                "last_error": r.last_error,
                "next_retry_at": r.next_retry_at.isoformat()
                if r.next_retry_at
                else None,
                "completed_at": r.completed_at.isoformat() if r.completed_at else None,
                "retry_eligible": r.retry_eligible,
                "correlation_id": r.correlation_id,
                "created_at": r.created_at.isoformat(),
                "created_by": r.created_by,
                "reason_for_change": r.reason_for_change,
            }
            for r in records
        ]
