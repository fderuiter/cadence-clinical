from typing import Any

from sqlalchemy.ext.asyncio import AsyncSession

from apps.etmf.ingestion_service import ingest_tmf_document
from apps.etmf.models import TMFDocument
from apps.etmf.src.domain.acl import ProtocolVersionRefDTO

ProtocolVersionRef = ProtocolVersionRefDTO


async def ingest_document_service(
    session: AsyncSession,
    study_id: str,
    artifact_type: str,
    filename: str,
    content: str | bytes,
    mime_type: str,
    user_id: str,
    user_roles: str,
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
    correlation_key: str | None = None,
    content_checksum: str | None = None,
    source_system: str | None = None,
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
        correlation_key=correlation_key,
        content_checksum=content_checksum,
        source_system=source_system,
    )
