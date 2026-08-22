"""
Support Ticket RAG Triage Service with Strict 85% Confidence Gating.

Synthesizes protocol citations and Tier 2 Grounded RAG triage.
Suppresses auto-drafts and fails closed to human Data Manager queues if faithfulness < 85%.

Requirements: PRD-TCK-005, PRD-SYS-051, ADR-2192
"""

from __future__ import annotations

import json
import logging
from collections.abc import Callable
from datetime import UTC, datetime
from typing import Any

from sqlalchemy.ext.asyncio import AsyncSession

from apps.tickets.adapters.knowledge_client import KnowledgeServiceClient
from apps.tickets.adapters.models import Ticket, TicketAuditLog
from packages.compliance.services.protocol_rag import (
    CONFIDENCE_THRESHOLD,
    ParsedCitation,
    ProtocolRAGContextChunk,
    evaluate_rag_faithfulness,
    format_citation_marker,
)

logger = logging.getLogger("tickets-rag-triage")


class RAGTriageResult:
    """Structured result of support ticket RAG triage execution."""

    def __init__(
        self,
        ticket_id: str,
        rag_status: str,
        faithfulness_score: float,
        is_grounded: bool,
        draft_answer: str | None,
        citations: list[dict[str, Any]],
        routed_to_role: str | None,
        routing_reason: str,
        latency_ms: float = 0.0,
    ) -> None:
        self.ticket_id = ticket_id
        self.rag_status = rag_status
        self.faithfulness_score = faithfulness_score
        self.is_grounded = is_grounded
        self.draft_answer = draft_answer
        self.citations = citations
        self.routed_to_role = routed_to_role
        self.routing_reason = routing_reason
        self.latency_ms = latency_ms

    def to_dict(self) -> dict[str, Any]:
        return {
            "ticket_id": self.ticket_id,
            "rag_status": self.rag_status,
            "faithfulness_score": self.faithfulness_score,
            "is_grounded": self.is_grounded,
            "draft_answer": self.draft_answer,
            "citations": self.citations,
            "routed_to_role": self.routed_to_role,
            "routing_reason": self.routing_reason,
            "latency_ms": self.latency_ms,
        }


class SupportTicketRAGTriageService:
    """Coordinates Grounded Protocol RAG retrieval, synthesis, and confidence gating."""

    @staticmethod
    async def triage_ticket(
        session: AsyncSession,
        ticket: Ticket,
        actor_user_id: str = "system",
        top_k: int = 5,
        custom_protocol_version: str | None = None,
        knowledge_provider: Callable[..., Any] | None = None,
    ) -> RAGTriageResult:
        """Runs grounded protocol RAG triage on a support ticket.

        If faithfulness >= 85%: attaches DRAFT_AI suggestion with exact page/section citations.
        If faithfulness < 85%: fails closed, suppresses draft, and routes to human Data Manager queue.

        Args:
            session: Async database session.
            ticket: Target Ticket ORM model.
            actor_user_id: User initiating triage.
            top_k: Number of protocol chunks to search.
            custom_protocol_version: Optional version override.
            knowledge_provider: Optional search provider override (for testing/mocking).

        Returns:
            RAGTriageResult with outcome telemetry.
        """
        query_text = f"{ticket.title}. {ticket.description}"
        study_id = ticket.study_id or "STUDY-DEFAULT"

        searcher = (
            knowledge_provider
            if knowledge_provider is not None
            else KnowledgeServiceClient.search_protocol_chunks
        )

        # 1. Retrieve matching approved protocol chunks from Knowledge Hub
        raw_matching_chunks = await searcher(
            query=query_text,
            study_id=study_id,
            protocol_version=custom_protocol_version,
            only_approved=True,
            top_k=top_k,
        )

        matching_chunks = [
            c for c in raw_matching_chunks if c.get("similarity_score", 0.0) >= 0.15
        ]

        context_chunks: list[ProtocolRAGContextChunk] = [
            ProtocolRAGContextChunk(
                chunk_id=c["chunk_id"],
                protocol_version=c["protocol_version"],
                section_number=c.get("section_number"),
                section_title=c.get("section_title"),
                page_number=c["page_number"],
                chunk_text=c["chunk_text"],
                is_approved=c.get("is_approved", True),
            )
            for c in matching_chunks
        ]

        # 2. Synthesize Grounded Draft Response
        if not context_chunks:
            draft_answer = None
            faithfulness_score = 0.0
            citations: list[ParsedCitation] = []
            is_grounded = False
        else:
            # Generate grounded synthesis with verbatim citation markers
            top_chunk = context_chunks[0]
            marker = format_citation_marker(
                top_chunk.protocol_version,
                top_chunk.section_number,
                top_chunk.page_number,
            )

            # Synthesize grounded clinical draft
            sec_ref = (
                f"Section {top_chunk.section_number} ({top_chunk.section_title})"
                if top_chunk.section_number
                else "the protocol specifications"
            )
            draft_answer = (
                f"According to approved Protocol {top_chunk.protocol_version}, {sec_ref}: "
                f"{top_chunk.chunk_text.strip()} {marker}."
            )

            faithfulness_score, citations, is_grounded = evaluate_rag_faithfulness(
                query=query_text,
                answer=draft_answer,
                chunks=context_chunks,
            )

        # 3. Confidence Gating (PRD-TCK-005: 85% threshold)
        now_iso = datetime.now(UTC).isoformat()
        current_payload: dict[str, Any] = {}
        if ticket.context_payload:
            try:
                current_payload = json.loads(ticket.context_payload)
            except Exception:
                current_payload = {"raw": ticket.context_payload}

        if faithfulness_score >= CONFIDENCE_THRESHOLD and is_grounded and draft_answer:
            # HIGH CONFIDENCE PASS: Attach DRAFT_AI suggestion
            rag_status = "DRAFT_AVAILABLE"
            routing_reason = (
                f"Grounded RAG draft generated with {int(faithfulness_score * 100)}% "
                f"confidence backed by {len(citations)} approved citations."
            )
            routed_to_role = ticket.assignee_role

            current_payload["ai_triage"] = {
                "rag_status": rag_status,
                "faithfulness_score": faithfulness_score,
                "is_grounded": True,
                "draft_answer": draft_answer,
                "citations": [c.model_dump() for c in citations],
                "triaged_at": now_iso,
                "routing_reason": routing_reason,
            }
            ticket.context_payload = json.dumps(current_payload)

            # 21 CFR Part 11 Audit Trail
            audit_log = TicketAuditLog(
                ticket_id=ticket.id,
                created_by=actor_user_id,
                action="AI_RAG_TRIAGE_DRAFT_GENERATED",
                details=(
                    f"Grounded RAG draft generated for ticket {ticket.reference} "
                    f"with {int(faithfulness_score * 100)}% faithfulness score."
                ),
                reason_for_change="Automated Grounded Protocol RAG Support Triage",
            )
            session.add(audit_log)
        else:
            # FAIL-CLOSED: Suppress auto-draft and route to Data Manager queue
            rag_status = "FAILED_CLOSED_TO_HUMAN_REVIEW"
            routed_to_role = "data_manager"
            routing_reason = (
                f"Faithfulness confidence ({int(faithfulness_score * 100)}%) is below the 85% threshold "
                "or citations unverified. Auto-draft suppressed and inquiry routed to human Data Manager review."
            )
            draft_answer = None

            current_payload["ai_triage"] = {
                "rag_status": rag_status,
                "faithfulness_score": faithfulness_score,
                "is_grounded": False,
                "draft_answer": None,
                "citations": [c.model_dump() for c in citations],
                "triaged_at": now_iso,
                "routing_reason": routing_reason,
                "routed_to_role": routed_to_role,
            }
            ticket.context_payload = json.dumps(current_payload)
            ticket.assignee_role = routed_to_role

            # 21 CFR Part 11 Audit Trail
            audit_log = TicketAuditLog(
                ticket_id=ticket.id,
                created_by=actor_user_id,
                action="AI_RAG_TRIAGE_FAILED_CLOSED",
                details=(
                    f"Low confidence ({int(faithfulness_score * 100)}% < 85%) for ticket {ticket.reference}; "
                    "auto-draft suppressed and routed to human Data Manager queue."
                ),
                reason_for_change="Fail-closed clinical safety gate enforcement (PRD-TCK-005)",
            )
            session.add(audit_log)

        await session.commit()
        await session.refresh(ticket)

        return RAGTriageResult(
            ticket_id=ticket.id,
            rag_status=rag_status,
            faithfulness_score=faithfulness_score,
            is_grounded=is_grounded,
            draft_answer=draft_answer,
            citations=[c.model_dump() for c in citations],
            routed_to_role=routed_to_role,
            routing_reason=routing_reason,
        )

    @staticmethod
    async def preview_rag_triage(
        session: AsyncSession,
        query: str,
        study_id: str,
        protocol_version: str | None = None,
        top_k: int = 5,
        knowledge_provider: Callable[..., Any] | None = None,
    ) -> dict[str, Any]:
        """Runs a read-only preview of grounded RAG triage before ticket submission."""
        searcher = (
            knowledge_provider
            if knowledge_provider is not None
            else KnowledgeServiceClient.search_protocol_chunks
        )

        raw_matching_chunks = await searcher(
            query=query,
            study_id=study_id,
            protocol_version=protocol_version,
            only_approved=True,
            top_k=top_k,
        )

        matching_chunks = [
            c for c in raw_matching_chunks if c.get("similarity_score", 0.0) >= 0.15
        ]

        context_chunks: list[ProtocolRAGContextChunk] = [
            ProtocolRAGContextChunk(
                chunk_id=c["chunk_id"],
                protocol_version=c["protocol_version"],
                section_number=c.get("section_number"),
                section_title=c.get("section_title"),
                page_number=c["page_number"],
                chunk_text=c["chunk_text"],
                is_approved=c.get("is_approved", True),
            )
            for c in matching_chunks
        ]

        if not context_chunks:
            return {
                "rag_status": "FAILED_CLOSED_TO_HUMAN_REVIEW",
                "faithfulness_score": 0.0,
                "is_grounded": False,
                "draft_answer": None,
                "citations": [],
                "routing_reason": "No approved protocol chunks found for study.",
            }

        top_chunk = context_chunks[0]
        marker = format_citation_marker(
            top_chunk.protocol_version,
            top_chunk.section_number,
            top_chunk.page_number,
        )
        sec_ref = (
            f"Section {top_chunk.section_number} ({top_chunk.section_title})"
            if top_chunk.section_number
            else "protocol guidelines"
        )
        draft_answer = (
            f"According to approved Protocol {top_chunk.protocol_version}, {sec_ref}: "
            f"{top_chunk.chunk_text.strip()} {marker}."
        )

        faithfulness, citations, is_grounded = evaluate_rag_faithfulness(
            query=query,
            answer=draft_answer,
            chunks=context_chunks,
        )

        if faithfulness >= CONFIDENCE_THRESHOLD and is_grounded:
            return {
                "rag_status": "DRAFT_AVAILABLE",
                "faithfulness_score": faithfulness,
                "is_grounded": True,
                "draft_answer": draft_answer,
                "citations": [c.model_dump() for c in citations],
                "routing_reason": f"Grounded draft ready with {int(faithfulness * 100)}% confidence.",
            }
        return {
            "rag_status": "FAILED_CLOSED_TO_HUMAN_REVIEW",
            "faithfulness_score": faithfulness,
            "is_grounded": False,
            "draft_answer": None,
            "citations": [],
            "routing_reason": f"Confidence ({faithfulness:.2f}) is below the 85% threshold.",
        }


__all__ = [
    "CONFIDENCE_THRESHOLD",
    "RAGTriageResult",
    "SupportTicketRAGTriageService",
]
