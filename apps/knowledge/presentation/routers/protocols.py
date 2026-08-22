"""
FastAPI router for Study Protocol and SOP Document Ingestion and Vector Search endpoints.

Requirements: PRD-TCK-005, PRD-SYS-051, ADR-2192
"""

from __future__ import annotations

import logging

from fastapi import (
    APIRouter,
    Depends,
    File,
    Form,
    HTTPException,
    UploadFile,
    status,
)
from pydantic import BaseModel, Field
from sqlalchemy.ext.asyncio import AsyncSession

from apps.knowledge.adapters.database import get_db_session
from apps.knowledge.services.protocol_service import ProtocolKnowledgeService
from packages.security.context import current_user_id
from packages.security.rbac import get_normalized_roles, require_roles

logger = logging.getLogger("protocols-router")

router = APIRouter(prefix="/api/v1/knowledge/protocols", tags=["protocols"])

ALL_CLINICAL_ROLES = [
    "super_admin",
    "sponsor_designer",
    "site_crc",
    "cra_monitor",
    "data_manager",
    "auditor",
]
AUTHOR_ROLES = ["super_admin", "sponsor_designer", "data_manager"]


# ---------------------------------------------------------------------------
# DTOs
# ---------------------------------------------------------------------------


class ProtocolIngestTextRequest(BaseModel):
    """Payload for text-based protocol ingestion."""

    study_id: str = Field(..., description="Clinical study scope identifier.")
    protocol_version: str = Field(..., description="Version label, e.g. 'v2.1'.")
    raw_text: str = Field(..., description="Protocol or SOP text content.")
    filename: str = Field(default="protocol.txt", description="Document file name.")
    document_id: str | None = Field(
        default=None, description="Optional custom document ID."
    )
    document_type: str = Field(default="PROTOCOL", description="'PROTOCOL' or 'SOP'.")
    is_approved: bool = Field(
        default=True, description="Whether this is an approved version."
    )
    reason_for_change: str = Field(
        default="Ingest protocol document for grounded RAG support triage",
        description="21 CFR Part 11 justification.",
    )


class ProtocolChunkSummary(BaseModel):
    """DTO summarizing an ingested protocol chunk."""

    chunk_id: str
    document_id: str
    study_id: str
    protocol_version: str
    section_number: str | None
    section_title: str
    page_number: int
    token_count: int
    is_approved: bool
    citation_marker: str


class ProtocolSearchRequest(BaseModel):
    """Payload for searching protocol chunks via dense vector cosine similarity."""

    query: str = Field(..., min_length=2, description="Inquiry search query.")
    study_id: str = Field(..., description="Study scope filter.")
    protocol_version: str | None = Field(
        default=None, description="Optional protocol version filter."
    )
    only_approved: bool = Field(
        default=True, description="Only search approved protocol versions."
    )
    top_k: int = Field(
        default=5, ge=1, le=20, description="Maximum matching chunks to return."
    )


class ProtocolSearchResult(BaseModel):
    """Matching protocol chunk with similarity score and citation marker."""

    chunk_id: str
    document_id: str
    study_id: str
    protocol_version: str
    section_number: str | None
    section_title: str
    page_number: int
    chunk_text: str
    is_approved: bool
    similarity_score: float
    citation_marker: str


# ---------------------------------------------------------------------------
# Endpoints
# ---------------------------------------------------------------------------


@router.post(
    "/ingest",
    response_model=list[ProtocolChunkSummary],
    status_code=status.HTTP_201_CREATED,
    summary="Ingest a protocol or SOP document and index dense vector chunks",
)
async def ingest_protocol_document(
    payload: ProtocolIngestTextRequest,
    session: AsyncSession = Depends(get_db_session),
    roles: list[str] = Depends(get_normalized_roles),
) -> list[ProtocolChunkSummary]:
    """Ingests protocol text, extracts section/page coordinates, and indexes vector chunks.

    Requires sponsor_designer, data_manager, or super_admin role.
    """
    require_roles(roles, AUTHOR_ROLES)
    user_id = current_user_id.get() or "system"

    file_bytes = payload.raw_text.encode("utf-8")

    chunks = await ProtocolKnowledgeService.ingest_protocol_document(
        session=session,
        study_id=payload.study_id,
        protocol_version=payload.protocol_version,
        file_bytes=file_bytes,
        filename=payload.filename,
        document_id=payload.document_id,
        document_type=payload.document_type,
        is_approved=payload.is_approved,
        created_by=user_id,
        reason_for_change=payload.reason_for_change,
    )

    from apps.knowledge.application.protocol_service import (
        format_citation_marker,
    )

    return [
        ProtocolChunkSummary(
            chunk_id=c.id,
            document_id=c.document_id,
            study_id=c.study_id,
            protocol_version=c.protocol_version,
            section_number=c.section_number,
            section_title=c.section_title,
            page_number=c.page_number,
            token_count=c.token_count,
            is_approved=c.is_approved,
            citation_marker=format_citation_marker(
                c.protocol_version, c.section_number, c.page_number
            ),
        )
        for c in chunks
    ]


@router.post(
    "/upload",
    response_model=list[ProtocolChunkSummary],
    status_code=status.HTTP_201_CREATED,
    summary="Upload and ingest a protocol PDF/document binary file",
)
async def upload_protocol_file(
    study_id: str = Form(...),
    protocol_version: str = Form(...),
    document_type: str = Form("PROTOCOL"),
    is_approved: bool = Form(True),
    reason_for_change: str = Form("Upload protocol PDF for vector RAG triage"),
    file: UploadFile = File(...),
    session: AsyncSession = Depends(get_db_session),
    roles: list[str] = Depends(get_normalized_roles),
) -> list[ProtocolChunkSummary]:
    """Uploads a PDF or document binary, parses pages, and indexes dense vector chunks."""
    require_roles(roles, AUTHOR_ROLES)
    user_id = current_user_id.get() or "system"

    file_bytes = await file.read()
    filename = file.filename or "protocol.pdf"

    chunks = await ProtocolKnowledgeService.ingest_protocol_document(
        session=session,
        study_id=study_id,
        protocol_version=protocol_version,
        file_bytes=file_bytes,
        filename=filename,
        document_type=document_type,
        is_approved=is_approved,
        created_by=user_id,
        reason_for_change=reason_for_change,
    )

    from apps.knowledge.application.protocol_service import (
        format_citation_marker,
    )

    return [
        ProtocolChunkSummary(
            chunk_id=c.id,
            document_id=c.document_id,
            study_id=c.study_id,
            protocol_version=c.protocol_version,
            section_number=c.section_number,
            section_title=c.section_title,
            page_number=c.page_number,
            token_count=c.token_count,
            is_approved=c.is_approved,
            citation_marker=format_citation_marker(
                c.protocol_version, c.section_number, c.page_number
            ),
        )
        for c in chunks
    ]


@router.post(
    "/search",
    response_model=list[ProtocolSearchResult],
    status_code=status.HTTP_200_OK,
    summary="Perform dense vector semantic search over protocol chunks",
)
async def search_protocol_chunks(
    payload: ProtocolSearchRequest,
    session: AsyncSession = Depends(get_db_session),
    roles: list[str] = Depends(get_normalized_roles),
) -> list[ProtocolSearchResult]:
    """Searches protocol chunks via dense vector cosine similarity."""
    require_roles(roles, ALL_CLINICAL_ROLES)

    results = await ProtocolKnowledgeService.search_protocol_chunks(
        session=session,
        query=payload.query,
        study_id=payload.study_id,
        protocol_version=payload.protocol_version,
        only_approved=payload.only_approved,
        top_k=payload.top_k,
    )

    return [
        ProtocolSearchResult(
            chunk_id=r["chunk_id"],
            document_id=r["document_id"],
            study_id=r["study_id"],
            protocol_version=r["protocol_version"],
            section_number=r["section_number"],
            section_title=r["section_title"],
            page_number=r["page_number"],
            chunk_text=r["chunk_text"],
            is_approved=r["is_approved"],
            similarity_score=r["similarity_score"],
            citation_marker=r["citation_marker"],
        )
        for r in results
    ]


@router.get(
    "/chunks/{chunk_id}",
    response_model=ProtocolSearchResult,
    status_code=status.HTTP_200_OK,
    summary="Retrieve full details for a single protocol chunk by ID",
)
async def get_protocol_chunk(
    chunk_id: str,
    session: AsyncSession = Depends(get_db_session),
    roles: list[str] = Depends(get_normalized_roles),
) -> ProtocolSearchResult:
    """Retrieves full chunk text and metadata for interactive UI document preview."""
    require_roles(roles, ALL_CLINICAL_ROLES)

    chunk = await ProtocolKnowledgeService.get_chunk_by_id(session, chunk_id)
    if not chunk:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Protocol chunk with ID '{chunk_id}' not found.",
        )

    from apps.knowledge.application.protocol_service import (
        format_citation_marker,
    )

    return ProtocolSearchResult(
        chunk_id=chunk.id,
        document_id=chunk.document_id,
        study_id=chunk.study_id,
        protocol_version=chunk.protocol_version,
        section_number=chunk.section_number,
        section_title=chunk.section_title,
        page_number=chunk.page_number,
        chunk_text=chunk.chunk_text,
        is_approved=chunk.is_approved,
        similarity_score=1.0,
        citation_marker=format_citation_marker(
            chunk.protocol_version, chunk.section_number, chunk.page_number
        ),
    )


__all__ = [
    "ProtocolChunkSummary",
    "ProtocolIngestTextRequest",
    "ProtocolSearchRequest",
    "ProtocolSearchResult",
    "router",
]
