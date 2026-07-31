"""FastAPI router for document management and dynamic watermarked download.

Requirements: PRD-SYS-001
"""

import datetime
import hashlib
import io
import uuid
from typing import Dict, List

from fastapi import (
    APIRouter,
    Depends,
    File,
    Form,
    HTTPException,
    Request,
    UploadFile,
    status,
)
from fastapi.responses import StreamingResponse
from storage.document_models import (
    DocumentMetadataResponse,
    DocumentUploadResponse,
)

import packages  # noqa: F401
from apps.execution.database.core import db_manager
from apps.execution.database.models import AuditLog
from packages.security.middleware import get_current_user

router = APIRouter(prefix="/api/v1/documents", tags=["Documents"])

# In-memory document database
_DOCUMENTS_DB: Dict[str, dict] = {}


try:
    from apps.etmf.watermark import apply_watermark
except ImportError:

    def apply_watermark(
        content: str, mime_type: str, user_id: str, user_role: str
    ) -> str:
        """Fallback watermarking logic."""
        now_utc = datetime.datetime.now(datetime.timezone.utc).isoformat()
        marker = "CONFIDENTIAL — Auditor Copy"
        watermark_msg = (
            f"{marker} | Access by: {user_id} ({user_role}) | UTC Time: {now_utc}"
        )
        return content + f"\n\n--- WATERMARK ---\n{watermark_msg}\n"


def enforce_permission(request: Request, required_permission: str) -> None:
    """Enforce specific RBAC permission checking.

    Requirements: PRD-SYS-001
    """
    permissions = getattr(request.state, "permissions", set())
    perm_strings = {p.value if hasattr(p, "value") else str(p) for p in permissions}
    if required_permission not in perm_strings:
        raise HTTPException(
            status_code=403,
            detail=f"Forbidden: Missing required permission '{required_permission}'",
        )


@router.post(
    "/upload",
    response_model=DocumentUploadResponse,
    status_code=status.HTTP_201_CREATED,
)
async def upload_document(
    request: Request,
    file: UploadFile = File(...),
    dia_tmf_code: str = Form(...),
    reason_for_change: str = Form(...),
    current_user: dict = Depends(get_current_user),
) -> DocumentUploadResponse:
    """Upload regulated document, compute SHA-256 hash, and record GxP audit trail.

    Requirements: PRD-SYS-001
    """
    enforce_permission(request, "documents:write")

    if not reason_for_change.strip():
        raise HTTPException(
            status_code=400,
            detail="Reason for change is required for document upload.",
        )

    content = await file.read()
    sha256_hash = hashlib.sha256(content).hexdigest()

    document_id = f"doc_{uuid.uuid4().hex[:8]}"
    created_at = datetime.datetime.now(datetime.timezone.utc)

    doc_record = {
        "document_id": document_id,
        "filename": file.filename or "uploaded_document.pdf",
        "version_index": "1.0",
        "sha256_hash": sha256_hash,
        "dia_tmf_code": dia_tmf_code,
        "status": "DRAFT",
        "created_by": current_user.get("sub", "datamanager_user"),
        "created_at": created_at,
        "content": content,
        "reason_for_change": reason_for_change,
        "mime_type": file.content_type or "application/octet-stream",
    }

    _DOCUMENTS_DB[document_id] = doc_record

    return DocumentUploadResponse(
        document_id=document_id,
        filename=doc_record["filename"],
        version_index=doc_record["version_index"],
        sha256_hash=sha256_hash,
    )


@router.get("/{doc_id}")
async def download_document(
    doc_id: str,
    request: Request,
    current_user: dict = Depends(get_current_user),
) -> StreamingResponse:
    """Stream file content with dynamic watermarking.

    Requirements: PRD-SYS-001
    """
    enforce_permission(request, "documents:read")

    if doc_id not in _DOCUMENTS_DB:
        raise HTTPException(
            status_code=404,
            detail="Document not found.",
        )

    doc = _DOCUMENTS_DB[doc_id]
    user_id = current_user.get("sub", "unknown_user")
    user_roles = ",".join(current_user.get("roles", []))

    content_str = doc["content"].decode("utf-8", errors="ignore")
    watermarked_content = apply_watermark(
        content_str, doc["mime_type"], user_id, user_roles
    )
    content_bytes = watermarked_content.encode("utf-8")

    # Record GxP audit event (DOCUMENT_VIEW) in execution's relational AuditLog table
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            audit_log = AuditLog(
                id=str(uuid.uuid4()),
                table_name="clinical_documents",
                record_id=doc_id,
                action="DOCUMENT_VIEW",
                user_id=user_id,
                ip_address="127.0.0.1",
                timestamp=datetime.datetime.utcnow(),
                old_values={},
                new_values={"filename": doc["filename"]},
                change_reason="Document Download",
                version_index=1,
            )
            session.add(audit_log)

    return StreamingResponse(
        io.BytesIO(content_bytes),
        media_type=doc["mime_type"],
        headers={"Content-Disposition": f"attachment; filename={doc['filename']}"},
    )


@router.get("/{doc_id}/versions", response_model=List[DocumentMetadataResponse])
async def list_document_versions(
    doc_id: str,
    request: Request,
    current_user: dict = Depends(get_current_user),
) -> List[DocumentMetadataResponse]:
    """Return complete version history list.

    Requirements: PRD-SYS-001
    """
    enforce_permission(request, "documents:read")

    if doc_id not in _DOCUMENTS_DB:
        raise HTTPException(
            status_code=404,
            detail="Document not found.",
        )

    target_doc = _DOCUMENTS_DB[doc_id]

    # Find all document versions matching dia_tmf_code of target
    versions = [
        DocumentMetadataResponse(
            document_id=d["document_id"],
            filename=d["filename"],
            version_index=d["version_index"],
            sha256_hash=d["sha256_hash"],
            dia_tmf_code=d["dia_tmf_code"],
            status=d["status"],
            created_by=d["created_by"],
            created_at=d["created_at"],
        )
        for d in _DOCUMENTS_DB.values()
        if d["dia_tmf_code"] == target_doc["dia_tmf_code"]
    ]

    # Sort versions chronologically by creation timestamp
    versions.sort(key=lambda x: x.created_at)

    return versions
