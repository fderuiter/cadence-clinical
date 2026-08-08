import base64
import hashlib
from datetime import UTC, date, datetime
from typing import Any

from cryptography import x509
from cryptography.hazmat.primitives import hashes
from cryptography.x509.oid import NameOID
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.etmf.cryptography import (
    extract_signature_from_content,
    validate_document_signature,
)
from apps.etmf.models import TMFAuditLog, TMFDocument, is_site_level_artifact
from apps.etmf.src.domain.acl import ProtocolVersionRefDTO
from apps.etmf.src.domain.tmf_reference_model import (
    get_active_catalog,
    validate_hierarchy,
)
from packages.security.signature import SignatureManifestation, SigningReason

ProtocolVersionRef = ProtocolVersionRefDTO


async def ingest_tmf_document(
    session: AsyncSession,
    *,
    study_id: str,
    artifact_type: str,
    filename: str,
    content: str | bytes,
    mime_type: str,
    created_by: str,
    created_role: str,
    site_id: str | None = None,
    idempotency_key: str | None = None,
    assigned_sites: list[str] | None = None,
    zone: int | None = None,
    section: str | None = None,
    artifact_code: str | None = None,
    taxonomy_version: str | None = None,
    metadata_json: dict[str, Any] | None = None,
    audit_action: str = "INGEST",
    audit_details: str | None = None,
    reason_for_change: str | None = None,
    protocol_version: ProtocolVersionRef | None = None,
    issue_date: date | None = None,
    expiration_date: date | None = None,
    document_owner_id: str | None = None,
    correlation_key: str | None = None,
    content_checksum: str | None = None,
    source_system: str | None = None,
) -> TMFDocument:
    """Service layer workflow for eTMF document ingestion.

    Performs classification mapping, taxonomy resolution/validation,
    version-index allocation, immutable-status checks, document signature
    validation, and TMFDocument persistence, all wrapped in transactional
    boundaries with corresponding audit log registration.
    """
    # 0. Normalize binary content to prevent database string conversion loss
    mime_lower = mime_type.lower().strip()
    is_binary = (
        "pdf" in mime_lower
        or "wordprocessingml" in mime_lower
        or "docx" in mime_lower
        or mime_lower == "application/octet-stream"
    )

    if is_binary:
        if isinstance(content, bytes):
            raw_bytes = content
            base64_str = base64.b64encode(content).decode("utf-8")
            sig_validation_content = content.decode("utf-8", errors="ignore")
        else:
            is_already_b64 = False
            try:
                decoded = base64.b64decode(content)
                if decoded.startswith(b"%PDF") or decoded.startswith(b"PK\x03\x04"):
                    is_already_b64 = True
                    raw_bytes = decoded
                    base64_str = content
                    sig_validation_content = decoded.decode("utf-8", errors="ignore")
            except Exception:
                pass

            if not is_already_b64:
                raw_bytes = content.encode("utf-8", errors="surrogateescape")
                base64_str = base64.b64encode(raw_bytes).decode("utf-8")
                sig_validation_content = content
    else:
        if isinstance(content, bytes):
            raw_bytes = content
            content = content.decode("utf-8", errors="ignore")
            sig_validation_content = content
        else:
            raw_bytes = content.encode("utf-8", errors="surrogateescape")
            sig_validation_content = content

    # 1. Determine TMF taxonomy version
    tax_version = taxonomy_version or get_active_catalog().version

    # 2 & 3. Classify and resolve artifact, section, and zone via the shared classification service
    from apps.etmf.classification_service import (
        classify_tmf_document,
        resolve_document_type,
    )

    hint = artifact_code or artifact_type
    classification = classify_tmf_document(
        filename=filename, artifact_type=hint, version=tax_version
    )
    if classification is None:
        raise ValueError(
            f"Validation Error: Could not resolve artifact for input '{hint}' or filename '{filename}'."
        )

    res_zone = classification.resolved_zone
    res_section = classification.resolved_section
    resolved_artifact_code = classification.artifact_code
    canonical_artifact_type = classification.artifact_type

    doc_type = resolve_document_type(resolved_artifact_code)

    # Validate and normalize site_id
    resolved_site_id = site_id
    if resolved_site_id is not None:
        stripped = resolved_site_id.strip()
        if not stripped:
            raise ValueError(
                "Validation Error: site_id cannot be empty or whitespace-only"
            )
        resolved_site_id = stripped
    else:
        if is_site_level_artifact(canonical_artifact_type, resolved_artifact_code):
            resolved_site_id = "QUARANTINED"

    # Enforce site scope if the caller is site-scoped and the document is site-scoped
    if resolved_site_id and assigned_sites and len(assigned_sites) > 0:
        if resolved_site_id not in assigned_sites:
            raise PermissionError(
                "Forbidden: You can only ingest documents for your assigned site(s)."
            )

    # 4. Validate hierarchy if user supplied specific zone/section hierarchy
    supplied_zone = zone
    supplied_section = section
    if metadata_json:
        if supplied_zone is None:
            supplied_zone = metadata_json.get("zone")
        if supplied_section is None:
            supplied_section = metadata_json.get("section")

    if supplied_zone is not None or supplied_section is not None:
        try:
            validate_hierarchy(
                version=tax_version,
                zone_code=supplied_zone if supplied_zone is not None else res_zone,
                section_code=(
                    supplied_section if supplied_section is not None else res_section
                ),
                artifact_code=resolved_artifact_code,
            )
        except ValueError as e:
            raise ValueError(f"Validation Error: {str(e)}")

    # 5. Validate embedded X.509 signature
    is_valid, status_msg = validate_document_signature(
        artifact_type=canonical_artifact_type,
        content=sig_validation_content,
        metadata_json=metadata_json,
    )
    if not is_valid:
        raise ValueError(f"Validation Error: {status_msg}")

    # Extract signature to set signature verification status in metadata
    cert_pem, sig_bytes, _ = extract_signature_from_content(sig_validation_content)
    if not cert_pem and metadata_json:
        for key in ["signature", "digital_signature", "x509_signature"]:
            sig_obj = metadata_json.get(key)
            if isinstance(sig_obj, dict):
                cert_pem = (
                    sig_obj.get("certificate")
                    or sig_obj.get("x509_certificate")
                    or sig_obj.get("cert")
                )
                break

    # Record verification status in metadata_json
    resolved_metadata_json = dict(metadata_json) if metadata_json else {}
    resolved_metadata_json["signature_verification_status"] = (
        "VERIFIED" if cert_pem else "NOT_REQUIRED"
    )

    # Reconstruct signature manifestation if validated and present
    sig_b64 = None
    if cert_pem and sig_bytes:
        sig_b64 = base64.b64encode(sig_bytes).decode("utf-8")
    elif metadata_json:
        for key in ["signature", "digital_signature", "x509_signature"]:
            sig_obj = metadata_json.get(key)
            if isinstance(sig_obj, dict):
                sig_val = sig_obj.get("signature_value") or sig_obj.get("signature")
                if sig_val:
                    sig_b64 = sig_val.strip()
                    break

    approval_status_val = "PENDING"
    signature_manifestation_data = None
    signer_val = None
    signing_timestamp_val = None

    if cert_pem and sig_b64:
        # We have a valid validated signature!
        # Compute hash of the payload content
        content_hash = hashlib.sha256(raw_bytes).hexdigest()

        # Extract signer identity (CN) from cert_pem
        signer_name = None
        key_id = None
        if "MOCK_SIGNATURE" in cert_pem:
            signer_name = "Mock Signer"
            key_id = "MOCK_KEY"
        else:
            try:
                cert_obj = x509.load_pem_x509_certificate(cert_pem.encode("utf-8"))
                cn_attr = cert_obj.subject.get_attributes_for_oid(NameOID.COMMON_NAME)
                if cn_attr:
                    signer_name = cn_attr[0].value
                key_id = cert_obj.fingerprint(hashes.SHA256()).hex()
            except Exception:
                pass

        if not signer_name:
            signer_name = created_by or "system"

        now_utc = datetime.now(UTC)
        sig_man = SignatureManifestation(
            signer_id=signer_name,
            timestamp=now_utc,
            signing_reason=SigningReason.APPROVAL,
            ip_address="127.0.0.1",
            user_agent="eTMF Ingest Service",
            sha256_hash=content_hash,
            signature=sig_b64,
            certificate_pem=cert_pem,
            key_identifier=key_id,
        )
        signature_manifestation_data = sig_man.model_dump(mode="json")
        approval_status_val = "APPROVED"
        signer_val = signer_name
        signing_timestamp_val = now_utc

    # Compute deterministic SHA-256 of raw content UTF-8 if not provided
    resolved_checksum = content_checksum
    if not resolved_checksum and raw_bytes is not None:
        resolved_checksum = hashlib.sha256(raw_bytes).hexdigest()

    # 5b. Idempotency Key check
    if idempotency_key:
        stmt_idem = select(TMFDocument).where(
            TMFDocument.idempotency_key == idempotency_key
        )
        res_idem = await session.execute(stmt_idem)
        existing_idem = res_idem.scalars().first()
        if existing_idem:
            log_entry = TMFAuditLog(
                user_id=created_by,
                user_role=created_role,
                action="DEDUPLICATED",
                document_id=existing_idem.id,
                details=(
                    f"Deduplicated ingestion request with idempotency_key '{idempotency_key}'. "
                    f"Returned existing document ID '{existing_idem.id}' (Version {existing_idem.version_index}) as a no-op."
                ),
                reason_for_change=reason_for_change,
            )
            session.add(log_entry)
            await session.flush()
            existing_idem._ingest_result_status = "ignored"
            return existing_idem

    # 6. Check if a document version already exists
    if correlation_key:
        # Scope lookup and latest version by stable correlation identity
        stmt = select(TMFDocument).where(TMFDocument.correlation_key == correlation_key)
    else:
        # Fall back to study_id + artifact_code + site_id
        stmt = (
            select(TMFDocument)
            .where(TMFDocument.study_id == study_id)
            .where(TMFDocument.artifact_code == resolved_artifact_code)
        )
        if resolved_site_id:
            stmt = stmt.where(TMFDocument.site_id == resolved_site_id)
        else:
            stmt = stmt.where(TMFDocument.site_id.is_(None))

    stmt = stmt.order_by(TMFDocument.version_index.desc())
    result = await session.execute(stmt)
    existing_doc = result.scalars().first()

    # Redaction-derivative safety check:
    # "in ingest_tmf_document, when the latest document in a correlation chain is a redacted derivative
    # (is_redacted=True) or its raw original has a linked derivative via redaction_source_id,
    # treat an incoming raw-content sync for that correlation key as a no-op/ignored result rather than appending a new raw version."
    if correlation_key and existing_doc:
        # Check if the latest is a redacted derivative or if there's any linked redacted derivative
        # Let's search if any redacted document exists with redaction_source_id = existing_doc.id or if existing_doc is redacted
        is_redacted_case = False
        if existing_doc.is_redacted:
            is_redacted_case = True
        else:
            # Check if there is any sibling document in this correlation chain or study that has redaction_source_id matching this or any prior version
            # To be safe, we can query if any document has is_redacted=True and matches correlation_key
            stmt_red = select(TMFDocument).where(
                TMFDocument.correlation_key == correlation_key,
                TMFDocument.is_redacted.is_(True),
            )
            res_red = await session.execute(stmt_red)
            if res_red.scalars().first():
                is_redacted_case = True

        if is_redacted_case:
            # Treat as a durable no-op, write INGEST_NOOP audit entry, return existing_doc as a no-op
            log_entry = TMFAuditLog(
                user_id=created_by,
                user_role=created_role,
                action="INGEST_NOOP",
                document_id=existing_doc.id,
                details=(
                    f"Ignored incoming raw-content sync for correlation key '{correlation_key}' because "
                    f"a redacted derivative exists."
                ),
                reason_for_change=reason_for_change,
            )
            session.add(log_entry)
            await session.flush()
            existing_doc._ingest_result_status = "ignored"
            return existing_doc

    # Check for checksum-based exact duplicate no-op (durable no-op)
    if correlation_key and existing_doc:
        if existing_doc.content_checksum == resolved_checksum:
            log_entry = TMFAuditLog(
                user_id=created_by,
                user_role=created_role,
                action="INGEST_NOOP",
                document_id=existing_doc.id,
                details=(
                    f"Durable no-op: Replayed synchronized document with correlation_key '{correlation_key}' "
                    f"and matching content checksum. Version {existing_doc.version_index} returned as a no-op."
                ),
                reason_for_change=reason_for_change,
            )
            session.add(log_entry)
            await session.flush()
            existing_doc._ingest_result_status = "ignored"
            return existing_doc

    new_version_index = 1
    if existing_doc:
        if (
            existing_doc.status == "SIGNED"
            or existing_doc.status == "ARCHIVED"
            or existing_doc.approval_status == "APPROVED"
            or existing_doc.signature_manifestation is not None
        ):
            # Already signed or archived. Reject with IMMUTABILITY_VIOLATION and write rejected audit log!
            reject_log = TMFAuditLog(
                user_id=created_by,
                user_role=created_role,
                action="MUTATION_REJECTED",
                document_id=existing_doc.id,
                details=(
                    f"Rejected attempt to ingest new version for signed document '{existing_doc.filename}' "
                    f"(ID: {existing_doc.id}). Error: IMMUTABILITY_VIOLATION."
                ),
                reason_for_change=reason_for_change,
            )
            session.add(reject_log)
            await session.commit()
            raise PermissionError(
                "IMMUTABILITY_VIOLATION: Document is already signed and cannot be modified"
            )
        new_version_index = existing_doc.version_index + 1

    # Determine sync_status for eISF-originated ingestion
    resolved_sync_status = None
    if source_system == "eISF":
        resolved_sync_status = "SYNCED"

    # 7. Add document and log action within transactional boundaries
    try:
        async with session.begin_nested():
            resolved_expiration_date = expiration_date
            if resolved_expiration_date is not None and not isinstance(
                resolved_expiration_date, datetime
            ):
                resolved_expiration_date = datetime.combine(
                    resolved_expiration_date, datetime.min.time()
                ).replace(tzinfo=UTC)

            doc = TMFDocument(
                study_id=study_id,
                site_id=resolved_site_id,
                idempotency_key=idempotency_key,
                zone=res_zone,
                section=res_section,
                artifact_type=canonical_artifact_type,
                filename=filename,
                content=base64_str if is_binary else content,
                mime_type=mime_type,
                created_by=created_by,
                version_index=new_version_index,
                taxonomy_version=tax_version,
                artifact_code=resolved_artifact_code,
                metadata_json=resolved_metadata_json,
                document_type=doc_type,
                approval_status=approval_status_val,
                signature_manifestation=signature_manifestation_data,
                signer=signer_val,
                signing_timestamp=signing_timestamp_val,
                reason_for_change=reason_for_change,
                protocol_version_tag=protocol_version.version_tag
                if protocol_version
                else None,
                protocol_version_index=protocol_version.version_index
                if protocol_version
                else None,
                protocol_version_status=protocol_version.status.value
                if protocol_version
                else None,
                issue_date=issue_date,
                expiration_date=resolved_expiration_date,
                document_owner_id=document_owner_id,
                correlation_key=correlation_key,
                content_checksum=resolved_checksum,
                source_system=source_system,
                sync_status=resolved_sync_status,
            )

            session.add(doc)
            await session.flush()

            # Determine audit details
            if not audit_details:
                resolved_audit_details = (
                    f"Ingested artifact type '{canonical_artifact_type}' for study '{study_id}' "
                    f"as Version {new_version_index} (TMF Zone {res_zone}, Section {res_section})."
                )
            else:
                resolved_audit_details = audit_details

            # Log action to immutable audit trail
            log_entry = TMFAuditLog(
                user_id=created_by,
                user_role=created_role,
                action=audit_action,
                document_id=doc.id,
                details=resolved_audit_details,
                reason_for_change=reason_for_change,
            )
            session.add(log_entry)
            await session.flush()

            doc._ingest_result_status = "created"
            return doc
    except Exception as e:
        # Savepoint automatically rolled back on exception block exit
        raise e
