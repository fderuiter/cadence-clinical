from typing import Any, Dict, List, Optional

from protocol_version_ref import ProtocolVersionRef
from sqlalchemy.ext.asyncio import AsyncSession

from apps.etmf.ingestion_service import ingest_tmf_document
from apps.etmf.models import TMFDocument


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
    idempotency_key: Optional[str] = None,
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
    """Compatibility wrapper that delegates to ingest_tmf_document."""
    return await ingest_tmf_document(
        session=session,
        study_id=study_id,
        artifact_type=artifact_type,
        filename=filename,
        content=content,
        mime_type=mime_type,
        created_by=user_id,
        created_role=user_roles,
        site_id=site_id,
        idempotency_key=idempotency_key,
        assigned_sites=assigned_sites,
        zone=zone,
        section=section,
        artifact_code=artifact_code,
        taxonomy_version=taxonomy_version,
        metadata_json=metadata_json,
        audit_action=audit_action,
        audit_details=audit_details,
        reason_for_change=reason_for_change,
        protocol_version=protocol_version,
    )
