import hashlib
import hmac
import io
import json
import os
import uuid
import zipfile
from datetime import UTC, datetime

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.etmf.adapters.models import TMFAuditLog, TMFDocument, is_site_level_artifact
from apps.etmf.adapters.watermark import apply_watermark
from apps.etmf.domain.ems.models import (
    TmfEmsAuditRecord,
    TmfEmsDocument,
    TmfEmsPackage,
    TmfEmsSignatureRecord,
    TmfEmsVersion,
)
from apps.etmf.domain.tmf_reference_model import get_active_catalog
from packages.security.rbac import Principal


def deterministic_mask(val: str, secret: str = "internal-gateway-secret-12345") -> str:
    """Computes a deterministic, HMAC-SHA256-based keyed mask for the given string value.

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
    principal: Principal | None = None,
) -> bytes:
    """Generates an inspection-ready ZIP binder for an eTMF study.

    It contains watermarked documents organized by DIA TMF zones and sections,
    a manifest.json of exported files, and a masked_audit_summary.json of study events.
    """
    from apps.etmf.adapters.lifecycle import authorize_document_read

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

    # Omit quarantined documents or documents with unresolved site metadata
    filtered_docs = []
    for doc in authorized_docs:
        if doc.site_id == "QUARANTINED":
            continue
        is_site = is_site_level_artifact(doc.artifact_type or "", doc.artifact_code)
        if is_site and (not doc.site_id or not doc.site_id.strip()):
            continue
        filtered_docs.append(doc)
    authorized_docs = filtered_docs

    # 2. Filter latest versions or full history
    if not include_history:
        latest_by_key: dict[tuple[str | None, str | None], TMFDocument] = {}
        for doc in authorized_docs:
            if is_site_level_artifact(doc.artifact_type or "", doc.artifact_code):
                key = (doc.artifact_code, doc.site_id)
            else:
                key = (doc.artifact_code, None)
            if (
                key not in latest_by_key
                or doc.version_index > latest_by_key[key].version_index
            ):
                latest_by_key[key] = doc
        documents_to_export = list(latest_by_key.values())
    else:
        documents_to_export = authorized_docs

    # Deterministically sort documents by zone -> section -> artifact_code -> site_id -> version_index
    documents_to_export = sorted(
        documents_to_export,
        key=lambda d: (
            d.zone or 0,
            d.section or "",
            d.artifact_code or "",
            d.site_id or "",
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

            # Watermark the copy via dual-read resolver
            from apps.etmf.storage import get_document_bytes

            raw_content_bytes = await get_document_bytes(doc)
            watermarked_bytes = apply_watermark(
                content=raw_content_bytes,
                mime_type=doc.mime_type,
                user_id=requester_id,
                user_role=requester_role,
            )
            if isinstance(watermarked_bytes, str):
                watermarked_bytes = watermarked_bytes.encode("utf-8")
            z.writestr(archive_path, watermarked_bytes)

        # Build manifest
        manifest_data = {
            "study_id": study_id,
            "export_timestamp": datetime.now(UTC).isoformat(),
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
                    "site_id": doc.site_id
                    if is_site_level_artifact(
                        doc.artifact_type or "", doc.artifact_code
                    )
                    else None,
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


async def generate_tmf_ems_package(
    session: AsyncSession,
    study_id: str,
    study_title: str | None,
    requester_id: str,
    requester_role: str,
    principal: Principal | None = None,
) -> bytes:
    """Generates a standardized DIA TMF Exchange Mechanism Standard (EMS) export package.

    Produces a compliant ZIP package containing `tmf-ems.xml`, `tmf-ems.json`,
    `checksums.sha256`, and all document binary assets organized in standard DIA hierarchy.
    """
    from apps.etmf.adapters.lifecycle import authorize_document_read

    catalog = get_active_catalog()

    # 1. Fetch all documents for study
    stmt_docs = select(TMFDocument).where(TMFDocument.study_id == study_id)
    res_docs = await session.execute(stmt_docs)
    all_docs = res_docs.scalars().all()

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

    # Filter quarantined / invalid site records
    valid_docs = [
        d
        for d in authorized_docs
        if d.site_id != "QUARANTINED"
        and not (
            is_site_level_artifact(d.artifact_type or "", d.artifact_code)
            and not (d.site_id and d.site_id.strip())
        )
    ]

    # Group by artifact code and site
    docs_by_lineage: dict[tuple[str, str | None], list[TMFDocument]] = {}
    for d in valid_docs:
        key = (d.artifact_code, d.site_id)
        if key not in docs_by_lineage:
            docs_by_lineage[key] = []
        docs_by_lineage[key].append(d)

    # 2. Build EMS Document structures
    ems_documents: list[TmfEmsDocument] = []
    total_versions_count = 0
    file_payloads: dict[str, bytes] = {}
    checksum_lines: list[str] = []

    package_id = f"EMS-{study_id}-{uuid.uuid4().hex[:8].upper()}"
    export_now_iso = datetime.now(UTC).isoformat()

    for (art_code, site_id), versions in docs_by_lineage.items():
        # Sort versions ascending
        sorted_versions = sorted(versions, key=lambda v: v.version_index)
        latest_ver = sorted_versions[-1]

        zone_obj = catalog.get_zone(latest_ver.zone)
        sec_obj = catalog.get_section(latest_ver.section)
        art_obj = catalog.get_artifact(latest_ver.artifact_code)

        zone_name = zone_obj.name if zone_obj else f"Zone {latest_ver.zone}"
        sec_name = sec_obj.name if sec_obj else f"Section {latest_ver.section}"
        art_name = art_obj.name if art_obj else latest_ver.artifact_type

        ems_versions: list[TmfEmsVersion] = []
        for ver in sorted_versions:
            total_versions_count += 1
            zone_dir = f"Zone {ver.zone:02d}"
            sec_dir = f"{ver.section}"
            base, ext = os.path.splitext(ver.filename)
            rel_path = f"{zone_dir}/{sec_dir}/{base}_v{ver.version_index}{ext}"

            # Extract raw binary via dual-read resolver
            from apps.etmf.storage import get_document_bytes

            raw_bytes = await get_document_bytes(ver)
            file_payloads[rel_path] = raw_bytes

            # SHA-256 digest
            sha256 = hashlib.sha256(raw_bytes).hexdigest()
            checksum_lines.append(f"{sha256}  {rel_path}")

            # Signatures
            ems_sigs: list[TmfEmsSignatureRecord] = []
            if ver.signature_manifestation and isinstance(
                ver.signature_manifestation, dict
            ):
                sig_data = ver.signature_manifestation
                ems_sigs.append(
                    TmfEmsSignatureRecord(
                        signer_id=sig_data.get("signer_id", ver.signer or "unknown"),
                        signer_name=sig_data.get("signer_id", ver.signer),
                        signing_reason=sig_data.get("signing_reason", "APPROVAL"),
                        timestamp=sig_data.get("timestamp", ver.created_at.isoformat()),
                        signature_digest=sig_data.get("signature"),
                        certificate_fingerprint=sig_data.get("key_identifier"),
                    )
                )

            ems_versions.append(
                TmfEmsVersion(
                    version_index=ver.version_index,
                    status=ver.status,
                    created_at=ver.created_at.isoformat(),
                    created_by=ver.created_by,
                    filename=ver.filename,
                    relative_path=rel_path,
                    mime_type=ver.mime_type,
                    sha256_checksum=sha256,
                    reason_for_change=ver.reason_for_change,
                    signatures=ems_sigs,
                    is_redacted=ver.is_redacted,
                    redaction_source_id=ver.redaction_source_id,
                )
            )

        ems_documents.append(
            TmfEmsDocument(
                document_id=latest_ver.id,
                study_id=study_id,
                site_id=site_id,
                zone_code=latest_ver.zone,
                zone_name=zone_name,
                section_code=latest_ver.section,
                section_name=sec_name,
                artifact_code=latest_ver.artifact_code,
                artifact_name=art_name,
                taxonomy_version=latest_ver.taxonomy_version,
                latest_status=latest_ver.status,
                issue_date=latest_ver.issue_date.isoformat()
                if latest_ver.issue_date
                else None,
                expiration_date=latest_ver.expiration_date.isoformat()
                if latest_ver.expiration_date
                else None,
                document_owner_id=latest_ver.document_owner_id,
                versions=ems_versions,
                metadata=latest_ver.metadata_json or {},
            )
        )

    # 3. Fetch Audit Trail
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

    audit_records: list[TmfEmsAuditRecord] = [
        TmfEmsAuditRecord(
            id=log.id,
            timestamp=log.timestamp.isoformat(),
            user_id=log.user_id,
            user_role=log.user_role,
            action=log.action,
            document_id=log.document_id,
            details=log.details,
            cryptographic_seal=log.cryptographic_seal,
        )
        for log in logs
    ]

    # 4. Construct complete TmfEmsPackage
    package = TmfEmsPackage(
        ems_version="1.0",
        package_id=package_id,
        study_id=study_id,
        study_title=study_title,
        source_system="Cadence Clinical eTMF",
        export_timestamp=export_now_iso,
        exported_by=requester_id,
        exported_by_role=requester_role,
        document_count=len(ems_documents),
        version_count=total_versions_count,
        documents=ems_documents,
        audit_trail=audit_records,
    )

    # 5. Pack in ZIP
    zip_buffer = io.BytesIO()
    with zipfile.ZipFile(zip_buffer, "w", zipfile.ZIP_DEFLATED) as z:
        # Write files
        for rel_path, file_data in file_payloads.items():
            z.writestr(rel_path, file_data)

        # Write EMS metadata manifests
        z.writestr("tmf-ems.json", package.model_dump_json(indent=2))
        z.writestr("tmf-ems.xml", package.to_xml_string())

        # Write SHA-256 Checksums
        checksum_content = "\n".join(checksum_lines) + "\n"
        z.writestr("checksums.sha256", checksum_content)

    return zip_buffer.getvalue()
