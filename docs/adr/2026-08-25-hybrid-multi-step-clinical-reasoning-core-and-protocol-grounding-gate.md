# ADR-2217: Hybrid Multi-Step Clinical Reasoning Core and Protocol Grounding Gate

- **Status:** Accepted
- **Date:** 2026-08-25
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

As Cadence Clinical Research Software expands its AI capabilities in `apps/ai_gateway`, clinical trial protocol synthesis, eligibility criteria extraction, and patient record reasoning require multi-step reasoning workflows. Simple single-pass prompt completions are prone to hallucinations, lack verified protocol citations, and risk exposing Protected Health Information (PHI) to third-party models during intermediate planning steps. Additionally, generated clinical records must be staged in a draft state requiring human review and cryptographic electronic signature before release, while microservices must remain stateless without internal session persistence. A robust architectural solution is required to fulfill **PRD-SYS-051**.

## 2. Decision Drivers & Constraints

* **Strict In-Flight PHI Masking**: Sensitive patient attributes must be automatically masked using ephemeral surrogate tokens (`[SURROGATE_NAME_1]`, etc.) during all intermediate reasoning steps, re-hydrating clear text only in final client-facing draft outputs.
* **85% Protocol Citation Faithfulness Gate**: Synthesized outputs and eligibility rules must cite verbatim protocol markers (`[Protocol {v}, Section {sec}, Page {page}]`). Outputs with faithfulness scores below 85% (`< 0.85`) must immediately fail closed (HTTP 422 Unprocessable Content) and suppress draft record creation.
* **Draft Record Staging & 21 CFR Part 11 Governance**: All synthesized clinical records must enter `DRAFT_STAGED` state with `requires_esignature: True`, requiring human investigator sign-off prior to release.
* **Stateless Microservice Boundaries**: The AI Gateway must execute reasoning loops using caller-managed state context (`ExecutionStateContext`) across request boundaries without internal session database state.
* **Tenant Isolation & Security Headers**: Mandatory tenant context (`X-Tenant-Id` / `tenant_id`) and HMAC signature verification must be enforced on every request.

## 3. Options Considered

### Option 1: **Option A (Selected): Stateless Multi-Step Planning Loop Engine with In-Flight Air-Gap Vault & Citation Verification Gate in `apps/ai_gateway`**: Implements a dedicated application service (`ClinicalReasoningService`) that orchestrates 5 discrete reasoning steps (Planning, Extraction, Synthesis, Verification, Draft Staging). Uses `DeidAirGapVault` for in-flight surrogate masking, `evaluate_rag_faithfulness` for citation scoring, and caller-managed `ExecutionStateContext`.

- **Overview:** **Option A (Selected): Stateless Multi-Step Planning Loop Engine with In-Flight Air-Gap Vault & Citation Verification Gate in `apps/ai_gateway`**: Implements a dedicated application service (`ClinicalReasoningService`) that orchestrates 5 discrete reasoning steps (Planning, Extraction, Synthesis, Verification, Draft Staging). Uses `DeidAirGapVault` for in-flight surrogate masking, `evaluate_rag_faithfulness` for citation scoring, and caller-managed `ExecutionStateContext`.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead

### Option 2: **Option B: Stateful Database-Backed Session Persistence**: Storing multi-step reasoning states in PostgreSQL or Redis sessions across HTTP requests.

- **Overview:** **Option B: Stateful Database-Backed Session Persistence**: Storing multi-step reasoning states in PostgreSQL or Redis sessions across HTTP requests.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead

### Option 3: **Option C: Direct Unvalidated LLM Pipeline**: Returning LLM completions directly to calling services without citation confidence gating or surrogate PHI masking.

- **Overview:** **Option C: Direct Unvalidated LLM Pipeline**: Returning LLM completions directly to calling services without citation confidence gating or surrogate PHI masking.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead


## 4. Decision Outcome

- **Chosen Option:** **Option A**.
- **Justification:** Selected as primary architectural approach.
Option A preserves strict microservice statelessness, enforces GxP and HIPAA compliance at the AI Gateway boundary, guarantees protocol citation grounding with an explicit 85% confidence gate, and ensures human-in-the-loop governance through staged draft records.

## 5. Consequences & Trade-offs

* **Positive**: Absolute zero PHI persistence on disk during intermediate LLM planning steps.
* **Positive**: Fail-closed protection against ungrounded protocol hallucinations (< 85% confidence threshold rejects execution).
* **Positive**: Clean microservice scalability due to stateless caller-managed execution context.
* **Positive**: Compliance with 21 CFR Part 11 electronic signature requirements.
* **Negative**: In-flight PHI surrogate substitution and citation verification add marginal latency (well within the platform SLA).

## 6. Implementation & Verification

* Implemented `ClinicalReasoningService` in `apps/ai_gateway/application/reasoning_service.py`.
* Defined Pydantic v2 DTOs and domain entities in `apps/ai_gateway/domain/reasoning.py` and `apps/ai_gateway/presentation/dtos.py`.
* Exposed HTTP endpoints `/api/v1/ai/reasoning/multi-step` and `/api/v1/ai/reasoning/plan` in `apps/ai_gateway/presentation/routers/reasoning.py`.
* Added comprehensive unit and integration test suite in `apps/ai_gateway/tests/test_clinical_reasoning.py` covering grounded synthesis, low-confidence fail-closed rejection, surrogate PHI masking, tenant validation, and stateless state propagation.

