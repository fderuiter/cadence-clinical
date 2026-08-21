# ADR-2191: AI Gateway Microservice and Three-Tier Clinical Intelligence Architecture

* **Status:** Accepted
* **Date:** 2026-08-21
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

As Cadence Clinical Research Software incorporates artificial intelligence across upstream clinical metadata management (MDR) and downstream electronic data capture (EDC), integrating third-party AI models introduces significant architectural risks: model obsolescence, unpredictable token costs, privacy non-compliance (HIPAA/GDPR PHI exposure), and GxP non-conformance (FDA 21 CFR Part 11). Direct, unmediated calls from clinical microservices (`apps/execution`, `apps/designer`, `apps/safety`) to cloud model providers violate microservice isolation boundaries, prevent centralized auditing, and risk accidental transmission of Protected Health Information (PHI). A unified, compliant, and cost-controlled AI architecture is required to satisfy **PRD-SYS-051**.

## 2. Decision Drivers & Constraints

* **Strict Privacy Air-Gap (HIPAA / GDPR / GxP)**: No unmasked PHI/PII may ever leave the VPC to external third-party cloud LLMs. Outbound prompts must be de-identified in-flight, and inbound completions must be unmasked seamlessly in memory.
* **Cost Predictability & Model Agnosticism**: Microservices must not bind to specific proprietary APIs (OpenAI, Anthropic). A unified 3-tier routing strategy (Tier 1: Local SLMs/Embeddings, Tier 2: Fast/Cost-Effective LLMs, Tier 3: Frontier Reasoning Models) must be managed centrally.
* **GxP 21 CFR Part 11 Auditability & HITL Governance**: AI cannot execute regulated clinical actions autonomously. All AI outputs must enter a `DRAFT_AI` state with model/prompt attribution and require human review and cryptographic electronic signature.
* **Microservice Decoupling & 100ms SLA**: Inter-service communication must use HMAC-authenticated REST endpoints adhering to the platform's hexagonal architecture.

## 3. Options Considered

1. **Option A (Selected): Dedicated `apps/ai-gateway` Microservice with Embedded Hexagonal LiteLLM Adapter and In-Flight `packages/deid` Air-Gap**: A standalone FastAPI microservice that encapsulates model routing, rate limiting, and prompt execution behind HMAC-authenticated endpoints. Inbound prompts are scrubbed in memory via `packages/deid`, holding surrogate token maps ephemerally during the request lifecycle. Standardized `AIAssistedRecordMixin` guarantees dual-attribution 21 CFR Part 11 audit records.
2. **Option B: Embedded AI Router in API Gateway (`apps/gateway`)**: Integrating LLM calls and vector embeddings directly inside the OIDC reverse proxy.
3. **Option C: Shared Library Package (`packages/ai`) Imported by Each Service**: Each microservice manages its own model credentials and LLM SDK calls.

## 4. Decision Outcome

Chosen option: **Option A**.
A dedicated `apps/ai-gateway` microservice strictly adheres to Cadence architectural standards, isolates resource-heavy AI dependencies (tokenizers, ONNX runtimes, vector math), provides independent horizontal scaling for local GPU/CPU compute, and ensures a single point of enforcement for privacy de-identification and GxP audit logging.

## 5. Consequences & Trade-offs

* **Positive**: Full model provider independence; swapping models requires updating one gateway configuration without touching core clinical services.
* **Positive**: Zero PHI persistence on disk during external LLM inference due to ephemeral in-memory surrogate unmasking.
* **Positive**: Absolute FDA 21 CFR Part 11 audit compliance with dual attribution (`DRAFT_AI` -> `APPROVED_USER` via cryptographic e-signature).
* **Positive**: Cost optimization through strict tier routing (Tier 1 local embeddings for medical coding, Tier 2 fast models for RAG/OCR, Tier 3 frontier models for protocol digitization).
* **Negative**: Requires maintaining an additional microservice (`apps/ai-gateway`) and internal REST client contracts across calling apps.

## 6. Implementation & Verification

* Scaffold `apps/ai-gateway` with `AIEnginePort` and `LiteLLMAdapter`.
* Extend `packages/database/audit.py` and `packages/compliance` with `AIAssistedRecordMixin` and `AIGenerationManifest`.
* Wire `packages/deid` into `apps/ai-gateway` request lifespan middleware.
* Implement UI primitives (`<AiActionButton>`, `<AiSuggestionDrawer>`) in `packages/ui`.
* Verification through automated unit, integration, and GxP traceability tests under `apps/ai-gateway/tests/` and `packages/compliance/tests/`.
