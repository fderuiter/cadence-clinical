import hashlib
import hmac
import io
import json
import os
import zipfile
from datetime import datetime, timezone
from typing import Optional

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.etmf.models import TMFAuditLog, TMFDocument
from apps.etmf.watermark import apply_watermark
from packages.security.rbac import Principal


def deterministic_mask(val: str, secret: str = "internal-gateway-secret-12345") -> str:
    """
    Computes a deterministic, HMAC-SHA256-based keyed mask for the given string value.
    This protects sensitive identity fields (like user_id) in exported audit summaries.
    """
    if not val:
        return ""
    h = hmac.new(secret.encode("utf-8"), val.encode("utf-8"), hashlib.sha256)
    return f"MASKED_{h.hexdigest()[:12].upper()}"


async def generate_binder_zip(
    session: AsyncSession,
    study_id: str,
    include_history: bool,
    requester_id: str,
    requester_role: str,
    principal: Optional[Principal] = None,
) -> bytes:
    """
    Generates an inspection-ready ZIP binder for an eTMF study.
    It contains watermarked documents organized by DIA TMF zones and sections,
    a manifest.json of exported files, and a masked_audit_summary.json of study events.
    """
    from apps.etmf.lifecycle import authorize_document_read

    # 1. Query all documents for this study
    stmt_docs = select(TMFDocument).where(TMFDocument.study_id == study_id)
    res_docs = await session.execute(stmt_docs)
    all_docs = res_docs.scalars().all()

    # Apply shared read-authorization logic (scoping, redaction-representation policy, etc.)
    authorized_docs = []
    if principal:
        for doc in all_docs:
            try:
                await authorize_document_read(principal, doc, session)
                authorized_docs.append(doc)
            except Exception:
                continue
    else:
        authorized_docs = list(all_docs)

    # 2. Filter latest versions or full history
    if not include_history:
        latest_by_code = {}
        for doc in authorized_docs:
            code = doc.artifact_code
            if (
                code not in latest_by_code
                or doc.version_index > latest_by_code[code].version_index
            ):
                latest_by_code[code] = doc
        documents_to_export = list(latest_by_code.values())
    else:
        documents_to_export = authorized_docs

    # Deterministically sort documents by zone -> section -> artifact_code -> version_index
    documents_to_export = sorted(
        documents_to_export,
        key=lambda d: (
            d.zone or 0,
            d.section or "",
            d.artifact_code or "",
            d.version_index or 0,
        ),
    )

    # Create in-memory zip file
    zip_buffer = io.BytesIO()
    existing_paths = set()

    def get_unique_path(path: str) -> str:
        if path not in existing_paths:
            existing_paths.add(path)
            return path
        base, ext = os.path.splitext(path)
        counter = 1
        while f"{base}_{counter}{ext}" in existing_paths:
            counter += 1
        new_path = f"{base}_{counter}{ext}"
        existing_paths.add(new_path)
        return new_path

    doc_paths = {}

    with zipfile.ZipFile(zip_buffer, "w", zipfile.ZIP_DEFLATED) as z:
        # Write individual documents
        for doc in documents_to_export:
            # Organise by zone & section
            zone_dir = f"Zone {doc.zone:02d}"
            section_dir = f"{doc.section}"

            if include_history:
                base, ext = os.path.splitext(doc.filename)
                archive_path = (
                    f"{zone_dir}/{section_dir}/{base}_v{doc.version_index}{ext}"
                )
            else:
                archive_path = f"{zone_dir}/{section_dir}/{doc.filename}"

            archive_path = get_unique_path(archive_path)
            doc_paths[doc.id] = archive_path

            # Watermark the copy
            watermarked_content = apply_watermark(
                content=doc.content,
                mime_type=doc.mime_type,
                user_id=requester_id,
                user_role=requester_role,
            )

            # Write to ZIP
            z.writestr(archive_path, watermarked_content.encode("utf-8"))

        # Build manifest
        manifest_data = {
            "study_id": study_id,
            "export_timestamp": datetime.now(timezone.utc).isoformat(),
            "exported_by": requester_id,
            "exported_by_role": requester_role,
            "include_history": include_history,
            "document_count": len(documents_to_export),
            "documents": [
                {
                    "id": doc.id,
                    "artifact_type": doc.artifact_type,
                    "artifact_code": doc.artifact_code,
                    "filename": doc.filename,
                    "version_index": doc.version_index,
                    "status": doc.status,
                    "zone": doc.zone,
                    "section": doc.section,
                    "site_id": doc.site_id,
                    "is_redacted": doc.is_redacted,
                    "redaction_source_id": doc.redaction_source_id,
                    "archive_path": doc_paths[doc.id],
                }
                for doc in documents_to_export
            ],
        }

        z.writestr("manifest.json", json.dumps(manifest_data, indent=2))

        # Build masked audit summary
        doc_ids = [d.id for d in all_docs]
        stmt_logs = select(TMFAuditLog)
        if doc_ids:
            stmt_logs = stmt_logs.where(
                (TMFAuditLog.document_id.in_(doc_ids))
                | (TMFAuditLog.details.contains(study_id))
            )
        else:
            stmt_logs = stmt_logs.where(TMFAuditLog.details.contains(study_id))

        stmt_logs = stmt_logs.order_by(TMFAuditLog.timestamp.desc())
        res_logs = await session.execute(stmt_logs)
        logs = res_logs.scalars().all()

        masked_logs_summary = []
        for log in logs:
            masked_user = deterministic_mask(log.user_id) if log.user_id else ""
            details_str = log.details
            if log.user_id and log.user_id in details_str:
                details_str = details_str.replace(log.user_id, masked_user)

            masked_logs_summary.append(
                {
                    "id": log.id,
                    "timestamp": log.timestamp.isoformat(),
                    "user_id": masked_user,
                    "user_role": log.user_role,
                    "action": log.action,
                    "document_id": log.document_id,
                    "details": details_str,
                    "cryptographic_seal": log.cryptographic_seal,
                }
            )

        z.writestr("audit_summary.json", json.dumps(masked_logs_summary, indent=2))

    return zip_buffer.getvalue()
