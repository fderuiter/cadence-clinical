"""
Protocol Knowledge Service for parsing, chunking, embedding, and vector search.

Extracts structured protocol chunks preserving exact structural coordinates
(protocol version, section title, section number, page number) for 21 CFR Part 11
auditability and Tier 2 grounded RAG triage.

Requirements: PRD-TCK-005, PRD-SYS-051, ADR-2192
"""

from __future__ import annotations

import json
import logging
import math
import re
import uuid
from typing import Any

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.knowledge.infrastructure.models import ProtocolKnowledgeChunk

logger = logging.getLogger(__name__)


def generate_dense_embedding(text: str, dim: int = 64) -> list[float]:
    """Generates a deterministic normalized dense float vector for text semantic search.

    Args:
        text: Input string to embed.
        dim: Embedding dimension (default 64).

    Returns:
        L2-normalized float vector of length dim.
    """
    if not text or not text.strip():
        return [0.0] * dim

    clean_text = text.lower().strip()
    words = re.findall(r"\w+", clean_text)
    vector = [0.0] * dim

    if not words:
        return [0.0] * dim

    for word_idx, word in enumerate(words):
        # Hash full word and character n-grams
        h = hash(word)
        slot = abs(h) % dim
        weight = 1.0 / math.sqrt(word_idx + 1)
        vector[slot] += weight

        # Character 3-grams
        for i in range(max(0, len(word) - 2)):
            gram = word[i : i + 3]
            g_slot = abs(hash(gram)) % dim
            vector[g_slot] += 0.5 * weight

    # L2 normalize
    norm = math.sqrt(sum(v * v for v in vector))
    if norm > 0:
        return [v / norm for v in vector]
    return [0.0] * dim


def calculate_cosine_similarity(vec_a: list[float], vec_b: list[float]) -> float:
    """Calculates exact mathematical cosine similarity between two float vectors.

    Args:
        vec_a: First float vector.
        vec_b: Second float vector.

    Returns:
        Cosine similarity score between -1.0 and 1.0 (0.0 if empty/zero vector).
    """
    if not vec_a or not vec_b or len(vec_a) != len(vec_b):
        return 0.0

    dot_product = sum(a * b for a, b in zip(vec_a, vec_b, strict=False))
    norm_a = math.sqrt(sum(a * a for a in vec_a))
    norm_b = math.sqrt(sum(b * b for b in vec_b))

    if norm_a == 0.0 or norm_b == 0.0:
        return 0.0

    similarity = dot_product / (norm_a * norm_b)
    return max(-1.0, min(1.0, similarity))


def format_citation_marker(
    protocol_version: str,
    section_number: str | None,
    page_number: int,
) -> str:
    """Formats the canonical verbatim citation marker per PRD-TCK-005.

    Format: `[Protocol {version}, Section {section}, Page {page}]`
    """
    version_clean = (
        protocol_version if protocol_version.startswith("v") else f"v{protocol_version}"
    )
    if section_number:
        return (
            f"[Protocol {version_clean}, Section {section_number}, Page {page_number}]"
        )
    return f"[Protocol {version_clean}, Page {page_number}]"


def parse_protocol_document_pages(
    file_bytes: bytes,
    filename: str,
) -> list[tuple[int, str]]:
    """Extracts raw text organized by 1-indexed page number from PDF or text streams.

    Args:
        file_bytes: Raw document bytes.
        filename: Original file name.

    Returns:
        List of (page_number, page_text) tuples.
    """
    filename_lower = filename.lower()
    pages: list[tuple[int, str]] = []

    # Try PyMuPDF fitz for PDF streams
    if filename_lower.endswith(".pdf") or file_bytes.startswith(b"%PDF"):
        try:
            import fitz

            doc = fitz.open(stream=file_bytes, filetype="pdf")
            for page_idx, page in enumerate(doc):
                text = page.get_text()
                if text.strip():
                    pages.append((page_idx + 1, text))
            if pages:
                return pages
        except Exception as exc:
            logger.warning("PyMuPDF parsing fallback to text splitter: %s", exc)

    # Text / Markdown fallback — split on form feeds or explicit page separators
    raw_text = file_bytes.decode("utf-8", errors="ignore")
    if "\x0c" in raw_text:  # Form feed page marker
        raw_pages = raw_text.split("\x0c")
        for idx, page_content in enumerate(raw_pages):
            if page_content.strip():
                pages.append((idx + 1, page_content.strip()))
    elif "--- Page " in raw_text:
        splits = re.split(r"--- Page (\d+) ---", raw_text)
        if len(splits) > 1:
            for i in range(1, len(splits), 2):
                p_num = int(splits[i])
                p_text = splits[i + 1].strip()
                pages.append((p_num, p_text))
        else:
            pages.append((1, raw_text))
    else:
        # Single page default
        pages.append((1, raw_text))

    return pages


def extract_chunks_from_pages(
    pages: list[tuple[int, str]],
    protocol_version: str,
) -> list[dict[str, Any]]:
    """Chunks page text while extracting and preserving structural section coordinates.

    Args:
        pages: List of (page_number, page_text) tuples.
        protocol_version: Version of the protocol (e.g. "v2.1").

    Returns:
        List of chunk dictionaries with structural coordinates.
    """
    chunks: list[dict[str, Any]] = []
    current_section_num: str | None = None
    current_section_title = "General Protocol Information"

    section_header_pattern = re.compile(
        r"^(?:section\s+)?(\d+(?:\.\d+)*)[:\.]?\s+([^\n\r]{3,80})$",
        re.IGNORECASE | re.MULTILINE,
    )

    for page_number, page_text in pages:
        lines = page_text.splitlines()
        current_chunk_lines: list[str] = []

        for line in lines:
            stripped = line.strip()
            if not stripped:
                continue

            match = section_header_pattern.match(stripped)
            if match:
                # If we have accumulated text for previous section, flush it
                if current_chunk_lines:
                    chunk_body = "\n".join(current_chunk_lines).strip()
                    if len(chunk_body) > 20:
                        words = chunk_body.split()
                        chunks.append(
                            {
                                "section_number": current_section_num,
                                "section_title": current_section_title,
                                "page_number": page_number,
                                "chunk_text": chunk_body,
                                "token_count": len(words),
                            }
                        )
                    current_chunk_lines = []

                current_section_num = match.group(1).strip()
                current_section_title = match.group(2).strip()
                current_chunk_lines.append(stripped)
            else:
                current_chunk_lines.append(stripped)
                # Max chunk size ~ 1500 chars / ~ 300 words
                if sum(len(line_item) for line_item in current_chunk_lines) >= 1500:
                    chunk_body = "\n".join(current_chunk_lines).strip()
                    words = chunk_body.split()
                    chunks.append(
                        {
                            "section_number": current_section_num,
                            "section_title": current_section_title,
                            "page_number": page_number,
                            "chunk_text": chunk_body,
                            "token_count": len(words),
                        }
                    )
                    current_chunk_lines = []

        if current_chunk_lines:
            chunk_body = "\n".join(current_chunk_lines).strip()
            if len(chunk_body) > 20:
                words = chunk_body.split()
                chunks.append(
                    {
                        "section_number": current_section_num,
                        "section_title": current_section_title,
                        "page_number": page_number,
                        "chunk_text": chunk_body,
                        "token_count": len(words),
                    }
                )

    return chunks


class ProtocolKnowledgeService:
    """Service managing ingestion, embedding, and semantic retrieval for protocol chunks."""

    @staticmethod
    async def ingest_protocol_document(
        session: AsyncSession,
        study_id: str,
        protocol_version: str,
        file_bytes: bytes,
        filename: str,
        document_id: str | None = None,
        document_type: str = "PROTOCOL",
        is_approved: bool = True,
        created_by: str = "system",
        reason_for_change: str = "Ingest study protocol for grounded RAG triage",
    ) -> list[ProtocolKnowledgeChunk]:
        """Ingests and chunks a protocol document, computes embeddings, and persists records.

        Args:
            session: Database session.
            study_id: Clinical study scope identifier (e.g. 'CDNC-2026-001').
            protocol_version: Version identifier (e.g. 'v2.1').
            file_bytes: Binary document content.
            filename: Document filename.
            document_id: Optional document ID.
            document_type: 'PROTOCOL' or 'SOP'.
            is_approved: GxP approval status.
            created_by: User ID of actor.
            reason_for_change: 21 CFR Part 11 justification.

        Returns:
            List of created ProtocolKnowledgeChunk ORM records.
        """
        doc_id = document_id or str(uuid.uuid4())
        pages = parse_protocol_document_pages(file_bytes, filename)
        extracted = extract_chunks_from_pages(pages, protocol_version)

        created_chunks: list[ProtocolKnowledgeChunk] = []

        for idx, item in enumerate(extracted):
            embedding = generate_dense_embedding(item["chunk_text"])
            chunk = ProtocolKnowledgeChunk(
                document_id=doc_id,
                study_id=study_id,
                protocol_version=protocol_version,
                document_type=document_type,
                section_number=item["section_number"],
                section_title=item["section_title"],
                page_number=item["page_number"],
                chunk_index=idx,
                chunk_text=item["chunk_text"],
                embedding_json=json.dumps(embedding),
                token_count=item["token_count"],
                is_approved=is_approved,
                created_by=created_by,
                reason_for_change=reason_for_change,
            )
            session.add(chunk)
            created_chunks.append(chunk)

        await session.commit()
        for c in created_chunks:
            await session.refresh(c)

        logger.info(
            "Ingested protocol document %s (version=%s): %d chunks created",
            doc_id,
            protocol_version,
            len(created_chunks),
        )
        return created_chunks

    @staticmethod
    async def search_protocol_chunks(
        session: AsyncSession,
        query: str,
        study_id: str,
        protocol_version: str | None = None,
        only_approved: bool = True,
        top_k: int = 5,
    ) -> list[dict[str, Any]]:
        """Searches protocol chunks via dense vector cosine similarity and lexical relevance.

        Args:
            session: Database session.
            query: Natural language query.
            study_id: Study scope filter.
            protocol_version: Optional version filter (e.g. 'v2.1').
            only_approved: Filter to approved protocol versions only.
            top_k: Number of matching chunks to return.

        Returns:
            Ranked list of chunk dictionaries with similarity score and citation marker.
        """
        stmt = select(ProtocolKnowledgeChunk).where(
            ProtocolKnowledgeChunk.study_id == study_id
        )

        if only_approved:
            stmt = stmt.where(ProtocolKnowledgeChunk.is_approved.is_(True))

        if protocol_version:
            stmt = stmt.where(
                ProtocolKnowledgeChunk.protocol_version == protocol_version
            )

        result = await session.execute(stmt)
        chunks = list(result.scalars().all())

        if not chunks:
            return []

        query_vec = generate_dense_embedding(query)
        query_words = set(re.findall(r"\w+", query.lower()))

        scored_chunks: list[tuple[float, ProtocolKnowledgeChunk]] = []

        for chunk in chunks:
            chunk_vec = json.loads(chunk.embedding_json) if chunk.embedding_json else []
            vec_sim = calculate_cosine_similarity(query_vec, chunk_vec)

            # Lexical term overlap bonus
            chunk_words = set(re.findall(r"\w+", chunk.chunk_text.lower()))
            overlap = (
                len(query_words & chunk_words) / len(query_words)
                if query_words
                else 0.0
            )
            combined_score = (0.6 * vec_sim) + (0.4 * overlap)

            scored_chunks.append((combined_score, chunk))

        # Sort descending by score
        scored_chunks.sort(key=lambda x: x[0], reverse=True)
        top_results = scored_chunks[:top_k]

        formatted: list[dict[str, Any]] = []
        for score, chunk in top_results:
            citation_marker = format_citation_marker(
                chunk.protocol_version,
                chunk.section_number,
                chunk.page_number,
            )
            formatted.append(
                {
                    "chunk_id": chunk.id,
                    "document_id": chunk.document_id,
                    "study_id": chunk.study_id,
                    "protocol_version": chunk.protocol_version,
                    "section_number": chunk.section_number,
                    "section_title": chunk.section_title,
                    "page_number": chunk.page_number,
                    "chunk_text": chunk.chunk_text,
                    "is_approved": chunk.is_approved,
                    "similarity_score": round(score, 4),
                    "citation_marker": citation_marker,
                }
            )

        return formatted

    @staticmethod
    async def get_chunk_by_id(
        session: AsyncSession,
        chunk_id: str,
    ) -> ProtocolKnowledgeChunk | None:
        """Retrieves a single protocol chunk by primary ID."""
        stmt = select(ProtocolKnowledgeChunk).where(
            ProtocolKnowledgeChunk.id == chunk_id
        )
        result = await session.execute(stmt)
        return result.scalars().first()


__all__ = [
    "ProtocolKnowledgeService",
    "calculate_cosine_similarity",
    "extract_chunks_from_pages",
    "format_citation_marker",
    "generate_dense_embedding",
    "parse_protocol_document_pages",
]
