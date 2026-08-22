"""
Integration and Unit Tests for Grounded Protocol Knowledge Hub and Support Ticket RAG Triage.

Requirements:
- @req:PRD-TCK-005 (Grounded Protocol Knowledge Hub & Support Ticket RAG Triage)
- @req:PRD-SYS-051 (AI Gateway Microservice and Three-Tier Clinical Intelligence Architecture)
- @req:PRD-SYS-001 (21 CFR Part 11 Audit Trail Logging)
"""

from __future__ import annotations

import json
import uuid

import pytest
import pytest_asyncio
from httpx import ASGITransport, AsyncClient
from sqlalchemy import select

from apps.knowledge.adapters.database import (
    db_manager as knowledge_db_manager,
)
from apps.knowledge.infrastructure.models import (
    Base as KnowledgeBase,
)
from apps.knowledge.services.protocol_service import (
    ProtocolKnowledgeService,
)
from apps.tickets.adapters.database import (
    db_manager as tickets_db_manager,
)
from apps.tickets.adapters.knowledge_client import (
    register_in_process_knowledge_provider,
)
from apps.tickets.adapters.models import (
    Ticket,
    TicketAuditLog,
    TicketCategory,
    TicketPriority,
    TicketStatus,
)
from apps.tickets.infrastructure.models import (
    Base as TicketsBase,
)
from apps.tickets.main import app as tickets_app
from apps.tickets.services.rag_triage_service import (
    SupportTicketRAGTriageService,
)
from packages.compliance.services.protocol_rag import (
    CONFIDENCE_THRESHOLD,
    ProtocolRAGContextChunk,
    evaluate_rag_faithfulness,
    format_grounded_rag_prompt,
)
from packages.testing.security import create_test_auth_headers


def get_auth_headers(
    roles: str = "data_manager",
    user_id: str = "dm_user_001",
    change_reason: str = "Execute RAG triage verification",
    site_id: str | None = None,
) -> dict[str, str]:
    """Generates canonical gateway HMAC authentication headers for testing."""
    return create_test_auth_headers(
        user_id=user_id,
        roles=[r.strip() for r in roles.split(",")],
        change_reason=change_reason,
        site_id=site_id,
    )


SAMPLE_PROTOCOL_TEXT = """
--- Page 1 ---
PROTOCOL CDNC-2026-001
Phase 3 Randomized Study of Investigational Drug Alpha vs Standard of Care

Section 1.0 Study Objectives and Design
The primary objective of this study is to evaluate progression-free survival in subjects with metastatic carcinoma.
Subjects will be randomized 1:1 to receive Investigational Drug Alpha (200 mg PO daily) or Standard of Care.

--- Page 2 ---
Section 4.1 Inclusion Criteria
1. Age 18 to 75 years at the time of signing informed consent.
2. Histologically confirmed metastatic solid tumor refractory to standard frontline chemotherapy.
3. ECOG performance status 0 or 1.
4. Adequate renal function defined as serum creatinine <= 1.5 x ULN.

Section 4.2 Exclusion Criteria
1. Active central nervous system metastases requiring daily corticosteroid therapy.
2. History of severe cardiac arrhythmia or QTc prolongation > 470 ms.
3. Prior exposure to investigational kinase inhibitors within 28 days of Day 1.

--- Page 3 ---
Section 7.3 Adverse Event and SAE Reporting Timeframes
All Adverse Events (AEs) occurring after the first dose of study drug must be documented in the eCRF within 5 business days.
Any Serious Adverse Event (SAE), regardless of suspected causality, must be reported to the Sponsor Pharmacovigilance Team within 24 hours of site awareness using the electronic safety portal.
Fatal or life-threatening SAEs require immediate telephone notification followed by formal written documentation within 24 hours.

--- Page 4 ---
Section 8.4 Concomitant Medications and Washout Periods
Strong CYP3A4 inhibitors (e.g. ketoconazole, clarithromycin) and strong inducers (e.g. rifampin, St. John's Wort) are strictly prohibited during study participation.
A mandatory 14-day washout period is required prior to Cycle 1 Day 1 for any prohibited herbal supplements.
"""


@pytest_asyncio.fixture(autouse=True)
async def setup_dual_test_databases():
    """Initializes in-memory databases and provisions schemas for tickets and knowledge."""
    test_db_url = f"sqlite+aiosqlite:///file:test_{uuid.uuid4().hex}?mode=memory&cache=shared&uri=true"

    tickets_db_manager.init_db(test_db_url, echo=False)
    knowledge_db_manager.init_db(test_db_url, echo=False)

    async with tickets_db_manager.engine.begin() as conn:
        await conn.run_sync(TicketsBase.metadata.create_all)
        await conn.run_sync(KnowledgeBase.metadata.create_all)

    async def _test_knowledge_searcher(**kw):
        async with knowledge_db_manager.get_session_maker()() as s:
            return await ProtocolKnowledgeService.search_protocol_chunks(
                session=s, **kw
            )

    register_in_process_knowledge_provider(_test_knowledge_searcher)

    yield

    register_in_process_knowledge_provider(None)

    if tickets_db_manager.engine is not None:
        async with tickets_db_manager.engine.begin() as conn:
            await conn.run_sync(KnowledgeBase.metadata.drop_all)
            await conn.run_sync(TicketsBase.metadata.drop_all)
        await tickets_db_manager.close()
    if knowledge_db_manager.engine is not None:
        await knowledge_db_manager.close()


@pytest.mark.asyncio
async def test_protocol_document_ingestion_and_chunking():
    """Validates protocol text parsing, structural coordinate extraction, and embedding generation.

    @req:PRD-TCK-005
    """
    async with knowledge_db_manager.get_session_maker()() as session:
        chunks = await ProtocolKnowledgeService.ingest_protocol_document(
            session=session,
            study_id="CDNC-2026-001",
            protocol_version="v2.1",
            file_bytes=SAMPLE_PROTOCOL_TEXT.encode("utf-8"),
            filename="protocol_v2_1.txt",
            document_id="DOC-PROT-001",
            is_approved=True,
            created_by="sponsor_user",
            reason_for_change="Baseline protocol ingestion",
        )

        assert len(chunks) >= 4

        # Verify structural coordinates
        sae_chunk = next(
            (c for c in chunks if c.section_number == "7.3"),
            None,
        )
        assert sae_chunk is not None
        assert sae_chunk.page_number == 3
        assert (
            "24 hours" in sae_chunk.chunk_text
            or "Serious Adverse Event" in sae_chunk.chunk_text
        )
        assert sae_chunk.protocol_version == "v2.1"
        assert sae_chunk.is_approved is True
        assert sae_chunk.embedding_json is not None

        # Verify embedding format
        embedding = json.loads(sae_chunk.embedding_json)
        assert len(embedding) == 64
        assert any(val != 0.0 for val in embedding)


@pytest.mark.asyncio
async def test_protocol_vector_cosine_search():
    """Validates dense vector cosine similarity search and citation marker formatting.

    @req:PRD-TCK-005
    """
    async with knowledge_db_manager.get_session_maker()() as session:
        await ProtocolKnowledgeService.ingest_protocol_document(
            session=session,
            study_id="CDNC-2026-001",
            protocol_version="v2.1",
            file_bytes=SAMPLE_PROTOCOL_TEXT.encode("utf-8"),
            filename="protocol_v2_1.txt",
            is_approved=True,
        )

        # Search for SAE reporting window
        results = await ProtocolKnowledgeService.search_protocol_chunks(
            session=session,
            query="What is the timeframe for reporting serious adverse events?",
            study_id="CDNC-2026-001",
            top_k=3,
        )

        assert len(results) > 0
        top_match = results[0]
        assert top_match["section_number"] == "7.3"
        assert top_match["page_number"] == 3
        assert top_match["citation_marker"] == "[Protocol v2.1, Section 7.3, Page 3]"
        assert top_match["similarity_score"] > 0.3


def test_grounded_rag_tier_2_prompt_and_faithfulness_evaluation():
    """Validates Tier 2 prompt construction, verbatim citation extraction, and faithfulness scoring.

    @req:PRD-TCK-005
    @req:PRD-SYS-051
    """
    chunk1 = ProtocolRAGContextChunk(
        chunk_id="chunk-1",
        protocol_version="v2.1",
        section_number="7.3",
        section_title="Adverse Event Reporting",
        page_number=3,
        chunk_text="Any Serious Adverse Event (SAE) must be reported within 24 hours of site awareness.",
        is_approved=True,
    )

    # 1. Prompt formatting
    prompt = format_grounded_rag_prompt(
        query="When must an SAE be reported?",
        chunks=[chunk1],
    )
    assert "--- PROTOCOL EXCERPTS ---" in prompt
    assert "[EXCERPT 1: Protocol v2.1, Section 7.3" in prompt
    assert "Page 3" in prompt
    assert "CLINICAL INQUIRY:" in prompt

    # 2. High confidence pass with exact citation marker
    answer_grounded = "Any Serious Adverse Event must be reported to Pharmacovigilance within 24 hours of site awareness [Protocol v2.1, Section 7.3, Page 3]."
    score, citations, is_grounded = evaluate_rag_faithfulness(
        query="When must an SAE be reported?",
        answer=answer_grounded,
        chunks=[chunk1],
    )
    assert score >= CONFIDENCE_THRESHOLD
    assert is_grounded is True
    assert len(citations) == 1
    assert citations[0].page_number == 3
    assert citations[0].protocol_version == "v2.1"
    assert citations[0].is_verified is True

    # 3. Hallucinated citation coordinates fail closed (< 85%)
    answer_hallucinated = (
        "SAEs must be reported in 12 hours [Protocol v2.1, Section 99.9, Page 999]."
    )
    score_h, citations_h, is_grounded_h = evaluate_rag_faithfulness(
        query="When must an SAE be reported?",
        answer=answer_hallucinated,
        chunks=[chunk1],
    )
    assert score_h < CONFIDENCE_THRESHOLD
    assert is_grounded_h is False
    assert citations_h[0].is_verified is False

    # 4. Unapproved protocol chunks fail closed (< 85%)
    unapproved_chunk = ProtocolRAGContextChunk(
        chunk_id="chunk-unapproved",
        protocol_version="v3.0-DRAFT",
        section_number="7.3",
        section_title="Adverse Event Reporting",
        page_number=3,
        chunk_text="Any Serious Adverse Event (SAE) must be reported within 24 hours.",
        is_approved=False,
    )
    answer_unapproved = (
        "Report SAEs in 24 hours [Protocol v3.0-DRAFT, Section 7.3, Page 3]."
    )
    score_u, _, is_grounded_u = evaluate_rag_faithfulness(
        query="When must an SAE be reported?",
        answer=answer_unapproved,
        chunks=[unapproved_chunk],
    )
    assert score_u < CONFIDENCE_THRESHOLD
    assert is_grounded_u is False

    # 5. Fail-closed on insufficient information
    answer_insufficient = "The protocol excerpts do not contain sufficient information to answer this inquiry."
    score_i, _, is_grounded_i = evaluate_rag_faithfulness(
        query="What is the dosage for pediatric patients?",
        answer=answer_insufficient,
        chunks=[chunk1],
    )
    assert score_i == 0.0
    assert is_grounded_i is False


@pytest.mark.asyncio
async def test_support_ticket_rag_triage_high_confidence_pass():
    """Validates that high-confidence grounded protocol inquiries generate DRAFT_AI suggestions.

    @req:PRD-TCK-005
    @req:PRD-SYS-001
    """
    async with tickets_db_manager.get_session_maker()() as session:
        # 1. Ingest approved protocol
        await ProtocolKnowledgeService.ingest_protocol_document(
            session=session,
            study_id="STUDY-101",
            protocol_version="v2.1",
            file_bytes=SAMPLE_PROTOCOL_TEXT.encode("utf-8"),
            filename="protocol_v2_1.txt",
            is_approved=True,
        )

        # 2. Create support ticket
        ticket = Ticket(
            reference="TKT-2026-0001",
            title="SAE Reporting Timeframe Query",
            description="Site coordinator asking what is the exact deadline to report an SAE.",
            category=TicketCategory.CLINICAL,
            priority=TicketPriority.MEDIUM,
            status=TicketStatus.OPEN,
            reporter="site_crc_user",
            study_id="STUDY-101",
            site_id="SITE-001",
            created_by="site_crc_user",
            reason_for_change="Create support inquiry",
        )
        session.add(ticket)
        await session.commit()
        await session.refresh(ticket)

        # 3. Execute RAG Triage
        result = await SupportTicketRAGTriageService.triage_ticket(
            session=session,
            ticket=ticket,
            actor_user_id="lead_cra_user",
        )

        assert result.rag_status == "DRAFT_AVAILABLE"
        assert result.faithfulness_score >= 0.85
        assert result.is_grounded is True
        assert result.draft_answer is not None
        assert "[Protocol v2.1, Section 7.3, Page 3]" in result.draft_answer
        assert len(result.citations) > 0

        # Verify context payload updated on ticket
        assert ticket.context_payload is not None
        payload = json.loads(ticket.context_payload)
        assert payload["ai_triage"]["rag_status"] == "DRAFT_AVAILABLE"
        assert payload["ai_triage"]["faithfulness_score"] >= 0.85

        # Verify 21 CFR Part 11 Audit Log
        stmt = select(TicketAuditLog).where(
            TicketAuditLog.ticket_id == ticket.id,
            TicketAuditLog.action == "AI_RAG_TRIAGE_DRAFT_GENERATED",
        )
        audit_res = await session.execute(stmt)
        audit_log = audit_res.scalars().first()
        assert audit_log is not None
        assert "Grounded RAG draft generated" in audit_log.details


@pytest.mark.asyncio
async def test_support_ticket_rag_triage_low_confidence_fail_closed():
    """Validates that out-of-scope/unsupported queries fail closed and route to human Data Manager queue.

    @req:PRD-TCK-005
    @req:PRD-SYS-001
    """
    async with tickets_db_manager.get_session_maker()() as session:
        # Ingest protocol
        await ProtocolKnowledgeService.ingest_protocol_document(
            session=session,
            study_id="STUDY-101",
            protocol_version="v2.1",
            file_bytes=SAMPLE_PROTOCOL_TEXT.encode("utf-8"),
            filename="protocol_v2_1.txt",
            is_approved=True,
        )

        # Create ticket with query completely unmentioned in protocol
        ticket = Ticket(
            reference="TKT-2026-0002",
            title="Parking and Travel Reimbursement Query",
            description="How much is the travel reimbursement for patient taxi fares in Munich?",
            category=TicketCategory.SITE_OPERATIONS,
            priority=TicketPriority.LOW,
            status=TicketStatus.OPEN,
            reporter="site_crc_user",
            assignee_role="site_crc",
            study_id="STUDY-101",
            site_id="SITE-001",
            created_by="site_crc_user",
            reason_for_change="Create out-of-scope inquiry",
        )
        session.add(ticket)
        await session.commit()
        await session.refresh(ticket)

        # Execute RAG Triage
        result = await SupportTicketRAGTriageService.triage_ticket(
            session=session,
            ticket=ticket,
            actor_user_id="lead_cra_user",
        )

        # Assert fail-closed behavior
        assert result.rag_status == "FAILED_CLOSED_TO_HUMAN_REVIEW"
        assert result.draft_answer is None
        assert result.is_grounded is False
        assert result.routed_to_role == "data_manager"

        # Assert ticket reassigned to human Data Manager queue
        assert ticket.assignee_role == "data_manager"

        # Verify audit trail
        stmt = select(TicketAuditLog).where(
            TicketAuditLog.ticket_id == ticket.id,
            TicketAuditLog.action == "AI_RAG_TRIAGE_FAILED_CLOSED",
        )
        audit_res = await session.execute(stmt)
        audit_log = audit_res.scalars().first()
        assert audit_log is not None
        assert (
            "auto-draft suppressed and routed to human Data Manager queue"
            in audit_log.details
        )


@pytest.mark.asyncio
async def test_api_endpoints_rag_triage_and_preview():
    """Validates FastAPI REST endpoints for RAG support triage and preview.

    @req:PRD-TCK-005
    """
    async with tickets_db_manager.get_session_maker()() as session:
        # Ingest protocol
        await ProtocolKnowledgeService.ingest_protocol_document(
            session=session,
            study_id="STUDY-202",
            protocol_version="v2.1",
            file_bytes=SAMPLE_PROTOCOL_TEXT.encode("utf-8"),
            filename="protocol_v2_1.txt",
            is_approved=True,
        )

        # Create ticket
        ticket = Ticket(
            reference="TKT-2026-0003",
            title="Concomitant Medication Restrictions",
            description="Is ketoconazole allowed during study participation?",
            category=TicketCategory.CLINICAL,
            priority=TicketPriority.HIGH,
            status=TicketStatus.OPEN,
            reporter="site_crc_user",
            study_id="STUDY-202",
            site_id="SITE-101",
            created_by="site_crc_user",
            reason_for_change="Medication inquiry",
        )
        session.add(ticket)
        await session.commit()
        await session.refresh(ticket)
        ticket_id = ticket.id

    transport = ASGITransport(app=tickets_app)
    async with AsyncClient(transport=transport, base_url="http://testserver") as client:
        auth_headers = get_auth_headers(
            roles="data_manager",
            user_id="dm_user_001",
            change_reason="Execute RAG endpoint triage",
        )

        # 1. Execute RAG triage endpoint
        resp = await client.post(
            f"/api/v1/tickets/{ticket_id}/rag-triage",
            headers=auth_headers,
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["ticket_id"] == ticket_id
        assert data["rag_status"] == "DRAFT_AVAILABLE"
        assert data["faithfulness_score"] >= 0.85
        assert data["is_grounded"] is True
        assert "[Protocol v2.1" in data["draft_answer"]

        # 2. Execute Preview endpoint
        preview_payload = {
            "query": "What is the washout period for CYP3A4 inhibitors?",
            "study_id": "STUDY-202",
            "protocol_version": "v2.1",
            "top_k": 3,
        }
        prev_resp = await client.post(
            "/api/v1/tickets/rag-triage/preview",
            json=preview_payload,
            headers=auth_headers,
        )
        assert prev_resp.status_code == 200
        prev_data = prev_resp.json()
        assert prev_data["rag_status"] == "DRAFT_AVAILABLE"
        assert prev_data["faithfulness_score"] >= 0.85
        assert "Section 8.4" in prev_data["draft_answer"]

        # 3. Test 404 on non-existent ticket
        missing_resp = await client.post(
            f"/api/v1/tickets/{uuid.uuid4()}/rag-triage",
            headers=auth_headers,
        )
        assert missing_resp.status_code == 404
