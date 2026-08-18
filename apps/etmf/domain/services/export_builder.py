"""Packaging and export service for DIA TMF EMS standard packages and regulatory binders."""

import hashlib
import io
import zipfile
from collections.abc import Sequence
from datetime import UTC, datetime
from typing import Any

from apps.etmf.domain.ems.models import (
    TmfEmsAuditRecord,
    TmfEmsDocument,
    TmfEmsPackage,
    TmfEmsSignatureRecord,
    TmfEmsVersion,
)
from apps.etmf.domain.tmf_reference_model import (
    get_active_catalog,
    resolve_artifact,
)


def generate_tmf_ems_package(
    study_id: str,
    documents: Sequence[Any],
    audit_logs: Sequence[Any],
    study_title: str | None = None,
    sponsor_name: str | None = None,
    exported_by: str = "system",
    exported_by_role: str = "regulatory_inspector",
) -> bytes:
    """Generates a fully compliant DIA TMF Exchange Mechanism Standard (EMS) ZIP package."""
    zip_buffer = io.BytesIO()
    checksums: list[str] = []
    catalog = get_active_catalog()

    ems_documents: list[TmfEmsDocument] = []
    ems_audit_trail: list[TmfEmsAuditRecord] = []

    # Map audit logs
    for log in audit_logs:
        ts = (
            log.timestamp
            if hasattr(log, "timestamp") and isinstance(log.timestamp, datetime)
            else datetime.now(UTC)
        )
        ems_audit_trail.append(
            TmfEmsAuditRecord(
                id=str(getattr(log, "id", None) or "0"),
                timestamp=ts.isoformat(),
                user_id=str(getattr(log, "user_id", "") or "unknown"),
                user_role=str(getattr(log, "user_role", "") or "unknown"),
                action=str(getattr(log, "action", "") or "ACTION"),
                document_id=(
                    str(getattr(log, "document_id", ""))
                    if getattr(log, "document_id", None)
                    else None
                ),
                details=str(getattr(log, "details", "") or ""),
                cryptographic_seal=getattr(log, "signature", None),
            )
        )

    with zipfile.ZipFile(zip_buffer, "w", zipfile.ZIP_DEFLATED) as zip_file:
        for doc in documents:
            try:
                resolved = resolve_artifact(catalog.version, name=doc.artifact_type)
                zone_num = resolved["zone"].code
                zone_name = resolved["zone"].name
                sec_code = resolved["section"].code
                sec_name = resolved["section"].name
                art_code = resolved["artifact"].code
                art_name = resolved["artifact"].name
            except ValueError:
                zone_num = getattr(doc, "zone", 1) or 1
                zone_name = f"Zone {zone_num:02d}"
                sec_code = (
                    getattr(doc, "section", f"{zone_num:02d}.01")
                    or f"{zone_num:02d}.01"
                )
                sec_name = "General"
                art_code = (
                    getattr(doc, "artifact_code", f"{sec_code}.01") or f"{sec_code}.01"
                )
                art_name = getattr(doc, "artifact_type", "Document")

            raw_content = getattr(doc, "content", "")
            content_bytes = (
                raw_content.encode("utf-8")
                if isinstance(raw_content, str)
                else raw_content
            )
            file_sha256 = hashlib.sha256(content_bytes).hexdigest()

            clean_filename = getattr(doc, "filename", "document.pdf")
            archive_path = (
                f"Zone {zone_num:02d} - {zone_name}/"
                f"Section {sec_code} - {sec_name}/"
                f"Artifact {art_code} - {art_name}/"
                f"v{getattr(doc, 'version_index', 1)}/{clean_filename}"
            )

            zip_file.writestr(archive_path, content_bytes)
            checksums.append(f"{file_sha256}  {archive_path}")

            signatures: list[TmfEmsSignatureRecord] = []
            if getattr(doc, "signer", None) and getattr(doc, "signing_timestamp", None):
                ts_sig = (
                    doc.signing_timestamp
                    if isinstance(doc.signing_timestamp, datetime)
                    else datetime.now(UTC)
                )
                sig_reason = "APPROVAL"
                if getattr(doc, "signature_manifestation", None):
                    if isinstance(doc.signature_manifestation, dict):
                        sig_reason = doc.signature_manifestation.get(
                            "signing_reason", "APPROVAL"
                        )
                signatures.append(
                    TmfEmsSignatureRecord(
                        signer_id=doc.signer,
                        timestamp=ts_sig.isoformat(),
                        signing_reason=sig_reason,
                        certificate_fingerprint=hashlib.sha256(
                            doc.signer.encode("utf-8")
                        ).hexdigest()[:32],
                    )
                )

            created_ts = (
                doc.created_at
                if hasattr(doc, "created_at") and isinstance(doc.created_at, datetime)
                else datetime.now(UTC)
            )

            version_entry = TmfEmsVersion(
                version_index=getattr(doc, "version_index", 1),
                filename=clean_filename,
                relative_path=archive_path,
                mime_type=(
                    getattr(doc, "mime_type", "application/octet-stream")
                    or "application/octet-stream"
                ),
                sha256_checksum=file_sha256,
                created_at=created_ts.isoformat(),
                created_by=getattr(doc, "created_by", "system") or "system",
                status=getattr(doc, "status", "APPROVED") or "APPROVED",
                reason_for_change=getattr(doc, "reason_for_change", None),
                signatures=signatures,
                is_redacted=bool(getattr(doc, "is_redacted", False)),
                redaction_source_id=getattr(doc, "redaction_source_id", None),
            )

            ems_doc = TmfEmsDocument(
                document_id=str(getattr(doc, "id", None) or "doc-0"),
                study_id=study_id,
                site_id=getattr(doc, "site_id", None),
                zone_code=zone_num,
                zone_name=zone_name,
                section_code=sec_code,
                section_name=sec_name,
                artifact_code=art_code,
                artifact_name=art_name,
                taxonomy_version=getattr(doc, "taxonomy_version", catalog.version)
                or catalog.version,
                latest_status=getattr(doc, "status", "APPROVED") or "APPROVED",
                document_owner_id=getattr(doc, "document_owner_id", None),
                versions=[version_entry],
                metadata=getattr(doc, "metadata_json", {}) or {},
            )
            ems_documents.append(ems_doc)

        ems_package = TmfEmsPackage(
            ems_version="1.0",
            package_id=f"EMS-{study_id}-{int(datetime.now(UTC).timestamp())}",
            study_id=study_id,
            study_title=study_title or f"Study {study_id}",
            source_system="Cadence Clinical eTMF",
            export_timestamp=datetime.now(UTC).isoformat(),
            exported_by=exported_by,
            exported_by_role=exported_by_role,
            document_count=len(ems_documents),
            version_count=sum(len(d.versions) for d in ems_documents),
            documents=ems_documents,
            audit_trail=ems_audit_trail,
        )

        zip_file.writestr(
            "tmf-ems.json",
            ems_package.model_dump_json(indent=2),
        )
        zip_file.writestr(
            "tmf-ems.xml",
            ems_package.to_xml_string(),
        )
        zip_file.writestr(
            "checksums.sha256",
            "\n".join(checksums) + "\n",
        )

    return zip_buffer.getvalue()


def generate_binder_zip(
    study_id: str,
    documents: Sequence[Any],
    include_history: bool = True,
    principal: Any = None,
) -> bytes:
    """Exports all study documents organized in standard DIA TMF taxonomy folder structure."""
    zip_buffer = io.BytesIO()
    catalog = get_active_catalog()

    with zipfile.ZipFile(zip_buffer, "w", zipfile.ZIP_DEFLATED) as zip_file:
        for doc in documents:
            if (
                getattr(doc, "site_id", None)
                and str(doc.site_id).upper() == "QUARANTINED"
            ):
                continue

            try:
                resolved = resolve_artifact(catalog.version, name=doc.artifact_type)
                zone_num = resolved["zone"].code
                zone_name = resolved["zone"].name
                sec_code = resolved["section"].code
                sec_name = resolved["section"].name
                art_code = resolved["artifact"].code
                art_name = resolved["artifact"].name
            except ValueError:
                zone_num = getattr(doc, "zone", 1) or 1
                zone_name = f"Zone {zone_num:02d}"
                sec_code = (
                    getattr(doc, "section", f"{zone_num:02d}.01")
                    or f"{zone_num:02d}.01"
                )
                sec_name = "General"
                art_code = (
                    getattr(doc, "artifact_code", f"{sec_code}.01") or f"{sec_code}.01"
                )
                art_name = getattr(doc, "artifact_type", "Document")

            clean_filename = getattr(doc, "filename", "document.pdf")
            archive_path = (
                f"Zone {zone_num:02d} - {zone_name}/"
                f"Section {sec_code} - {sec_name}/"
                f"Artifact {art_code} - {art_name}/"
                f"v{getattr(doc, 'version_index', 1)}/{clean_filename}"
            )

            raw_content = getattr(doc, "content", "")
            content_bytes = (
                raw_content.encode("utf-8")
                if isinstance(raw_content, str)
                else raw_content
            )
            zip_file.writestr(archive_path, content_bytes)

    return zip_buffer.getvalue()
