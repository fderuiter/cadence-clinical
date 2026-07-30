import base64
import hashlib
from datetime import datetime, timezone
from typing import Any, Dict, List, Optional

from cryptography import x509
from cryptography.hazmat.primitives import hashes
from cryptography.x509.oid import NameOID
from protocol_version_ref import ProtocolVersionRef
from signature import SignatureManifestation, SigningReason
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession
from tmf_reference_model import (
    get_active_catalog,
    resolve_artifact,
    validate_hierarchy,
)

from apps.etmf.cryptography import (
    extract_signature_from_content,
    validate_document_signature,
)
from apps.etmf.models import TMFAuditLog, TMFDocument, is_site_level_artifact


async def ingest_document_service(
    session: AsyncSession,
    study_id: str,
    artifact_type: str,
    filename: str,
    content: str,
    mime_type: str,
    user_id: str,
    user_roles: str,
    site_id: Optional[str] = None,
    assigned_sites: Optional[List[str]] = None,
    zone: Optional[int] = None,
    section: Optional[str] = None,
    artifact_code: Optional[str] = None,
    taxonomy_version: Optional[str] = None,
    metadata_json: Optional[Dict[str, Any]] = None,
    audit_action: str = "INGEST",
    audit_details: Optional[str] = None,
    reason_for_change: Optional[str] = None,
    protocol_version: Optional[ProtocolVersionRef] = None,
) -> TMFDocument:
    """Service layer workflow for eTMF document ingestion.

    Performs classification mapping, taxonomy resolution/validation,
    version-index allocation, immutable-status checks, document signature
    validation, and TMFDocument persistence, all wrapped in transactional
    boundaries with corresponding audit log registration.
    """
    # 1. Determine TMF taxonomy version
    tax_version = taxonomy_version or get_active_catalog().version

    code_input = artifact_code
    name_input = artifact_type

    # 2. Map/Normalize the document classification for FORM_1572, FINANCIAL_DISCLOSURE, and PROTOCOL_SIGNOFF
    doc_type = None
    if (
        name_input == "FORM_1572"
        or code_input == "05.02.01"
        or name_input == "FDA Form 1572"
    ):
        doc_type = "FORM_1572"
        name_input = "FDA Form 1572"
        code_input = "05.02.01"
    elif (
        name_input == "FINANCIAL_DISCLOSURE"
        or code_input == "05.02.02"
        or name_input == "Financial Disclosure"
    ):
        doc_type = "FINANCIAL_DISCLOSURE"
        name_input = "Financial Disclosure"
        code_input = "05.02.02"
    elif (
        name_input == "PROTOCOL_SIGNOFF"
        or code_input == "01.01.03"
        or name_input == "Protocol Sign-off"
    ):
        doc_type = "PROTOCOL_SIGNOFF"
        name_input = "Protocol Sign-off"
        code_input = "01.01.03"

    # 3. Resolve artifact, section, and zone via the shared catalog API
    # If artifact_code is not explicitly supplied, check if artifact_type is a code
    if not code_input and name_input and name_input.strip().replace(".", "").isdigit():
        code_input = name_input.strip()
        name_input = None

    try:
        resolved = resolve_artifact(
            version=tax_version, code=code_input, name=name_input
        )
    except ValueError as e:
        raise ValueError(f"Validation Error: {str(e)}")

    res_zone = resolved["zone"].code
    res_section = resolved["section"].code
    artifact_obj = resolved["artifact"]
    resolved_artifact_code = artifact_obj.code
    canonical_artifact_type = artifact_obj.name

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

    # Enforce site scope if the caller is site-scoped
    if assigned_sites and len(assigned_sites) > 0:
        if not resolved_site_id or resolved_site_id not in assigned_sites:
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
        content=content,
        metadata_json=metadata_json,
    )
    if not is_valid:
        raise ValueError(f"Validation Error: {status_msg}")

    # Extract signature to set signature verification status in metadata
    cert_pem, sig_bytes, _ = extract_signature_from_content(content)
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
        content_hash = hashlib.sha256(content.encode("utf-8")).hexdigest()

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
            signer_name = user_id or "system"

        now_utc = datetime.now(timezone.utc)
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

    # 6. Check if a document version already exists (for study_id + artifact_code + site_id)
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

    new_version_index = 1
    if existing_doc:
        if (
            existing_doc.status == "SIGNED"
            or existing_doc.approval_status == "APPROVED"
            or existing_doc.signature_manifestation is not None
        ):
            # Already signed. Reject with IMMUTABILITY_VIOLATION and write rejected audit log!
            reject_log = TMFAuditLog(
                user_id=user_id,
                user_role=user_roles,
                action="MUTATION_REJECTED",
                document_id=existing_doc.id,
                details=(
                    f"Rejected attempt to ingest new version for signed document '{existing_doc.filename}' "
                    f"(ID: {existing_doc.id}). Error: IMMUTABILITY_VIOLATION."
                ),
            )
            session.add(reject_log)
            await session.commit()
            raise PermissionError(
                "IMMUTABILITY_VIOLATION: Document is already signed and cannot be modified"
            )
        new_version_index = existing_doc.version_index + 1

    # 7. Add document and log action within transactional boundaries
    try:
        async with session.begin_nested():
            doc = TMFDocument(
                study_id=study_id,
                site_id=resolved_site_id,
                zone=res_zone,
                section=res_section,
                artifact_type=canonical_artifact_type,
                filename=filename,
                content=content,
                mime_type=mime_type,
                created_by=user_id,
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
                user_id=user_id,
                user_role=user_roles,
                action=audit_action,
                document_id=doc.id,
                details=resolved_audit_details,
            )
            session.add(log_entry)
            await session.flush()

            return doc
    except Exception as e:
        # Savepoint automatically rolled back on exception block exit
        raise e
