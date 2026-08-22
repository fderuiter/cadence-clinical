# ADR-2192: Grounded Protocol Knowledge Hub and Support Ticket RAG Triage

* **Status:** Accepted
* **Date:** 2026-08-21
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Clinical Research Associates (CRAs) and site personnel frequently encounter ambiguities regarding study protocol requirements, inclusion/exclusion criteria, schedule of assessments, and standard operating procedures (SOPs). Direct manual support ticket triage by Data Managers creates operational bottlenecks, while ungrounded LLM usage introduces unacceptable risks of clinical hallucination and regulatory non-conformance. To address **PRD-TCK-005** and **PRD-SYS-051**, Cadence Clinical requires a fully grounded protocol knowledge hub and support ticket RAG triage workflow enforcing verbatim citation markers (`[Protocol v2.1, Section 7.3, Page 42]`), mathematical faithfulness evaluation, strict confidence gating ($< 85\%$ fails closed to human Data Managers), and interactive citation navigation.

## 2. Decision Drivers & Constraints

* **Strict Clinical Grounding & Hallucination Prevention**: No AI draft may be presented to clinical users unless backed by exact structural coordinates (protocol version, section title, and page number) from approved study protocols.
* **Fail-Closed Confidence Gating (85% Threshold)**: Queries yielding confidence/faithfulness scores $< 0.85$ or unsupported assertions must suppress auto-drafting and route immediately to the human Data Manager queue.
* **21 CFR Part 11 Auditability & Dual Attribution**: Inbound AI triage recommendations must enter a `DRAFT_AI` state and emit immutable audit records documenting the model, faithfulness score, and cited chunks.
* **Decoupled Hexagonal Architecture**: Protocol ingestion and vector indexing reside in `apps/knowledge`, Tier 2 RAG synthesis in `apps/ai_gateway`, ticket orchestration in `apps/tickets`, and interactive citation preview in `apps/web`.

## 3. Options Considered

1. **Option A (Selected): Dedicated Vector Index in `apps/knowledge` + Tier 2 Grounded RAG Router in `apps/ai_gateway` + Fail-Closed Confidence Gating in `apps/tickets`**: Protocol PDFs are chunked preserving page numbers and section headers. RAG queries retrieve approved chunks, execute Tier 2 prompt templates requiring verbatim citation markers, compute mathematical faithfulness scores, and route tickets based on an 85% confidence gate.
2. **Option B: Monolithic RAG Engine inside Tickets Microservice**: Embed vector databases and LLM orchestration directly within `apps/tickets`.
3. **Option C: Direct Unstructured RAG without Structural Coordinate Extraction**: Ingest protocol documents as flat text without preserving page numbers or section titles.

## 4. Decision Outcome

Chosen option: **Option A**.
This architecture preserves microservice isolation, ensures full GxP traceability with exact page and section citations, guarantees fail-closed safety for unsupported inquiries, and provides seamless interactive document previews in the clinical UI.

## 5. Consequences & Trade-offs

* **Positive**: 100% verifiable citations linking directly to exact protocol version and page numbers.
* **Positive**: Absolute clinical safety with automatic fail-closed escalation for ambiguous or out-of-scope inquiries.
* **Positive**: Full 21 CFR Part 11 audit compliance and dual attribution across all AI-assisted actions.
* **Negative**: Requires multi-service orchestration across `apps/knowledge`, `apps/ai_gateway`, and `apps/tickets`.

## 6. Implementation & Verification

* Ingest protocol documents into `ProtocolKnowledgeChunk` in `apps/knowledge/services/protocol_service.py`.
* Implement Tier 2 grounded RAG synthesis and faithfulness scoring in `apps/ai_gateway/domain/rag.py`.
* Implement `SupportTicketRAGTriageService` in `apps/tickets/services/rag_triage_service.py` with 85% confidence gating.
* Render interactive citation links in `apps/web/src/components/tickets/TicketDetailDrawer.vue`.
* Verify with unit and integration tests in `apps/tickets/tests/test_rag_support_triage.py`.

