# ADR-2196: Generative Pharmacovigilance Safety Narratives with Part 11 E-Signature

* **Status:** Accepted
* **Date:** 2026-08-21
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Pharmacovigilance (PV) operations in clinical trials require authoring comprehensive, medically sound, and chronologically grounded Serious Adverse Event (SAE) safety narratives for regulatory submissions (FDA MedWatch 3500A, CIOMS-I, and ICH E2B(R3) ICSRs). Manually assembling disparate clinical data points—demographics, baseline medical history, study drug administration logs, concomitant medications, adverse event progression, diagnostic laboratory panels, hospitalization records, and dechallenge/rechallenge outcomes—from Electronic Data Capture (EDC) systems is labor-intensive, error-prone, and prone to transcription inconsistencies.

Furthermore, per FDA 21 CFR Part 11 and GxP regulatory guidelines, artificial intelligence models cannot autonomously submit clinical safety assessments or alter regulatory filings without human medical governance. A unified architecture is required to satisfy **PRD-SYS-052**.

## 2. Decision Drivers & Constraints

* **ICH E2B(R3) & FDA MedWatch 3500A Structural Alignment**: AI narratives must strictly map to standard regulatory narrative sections (Demographics, Medical History, Index Event, Diagnostic Workup, Hospital Course, Outcome & Causality).
* **Grounded Claim Traceability**: Every sentence and factual claim in the drafted narrative must maintain deterministic cross-reference links back to source eCRF observation records in `apps/execution`.
* **21 CFR Part 11 Dual-Attribution & E-Signature Gate**: AI outputs must be persisted in a `DRAFT_AI` review status with full model and prompt attribution (`AIAssistedRecordMixin`). Regulatory export (e.g. ICH E2B XML) must be blocked until a qualified Safety Physician or Medical Monitor signs off using a cryptographic electronic signature.
* **Microservice Decoupling & In-Flight Privacy**: Cross-service data fetching must adhere to REST API contracts with HMAC authentication, routing AI inference through `apps/ai_gateway` with `packages/deid` air-gap scrubbing.

## 3. Options Considered

1. **Option A (Selected): Dedicated Generative Pipeline in `apps/safety` with Chronological Timeline Aggregation, Tier 3 Frontier AI Drafting, and Part 11 E-Signature Gate**:
   - `apps/safety` pulls de-identified SDTM/observation timelines from `apps/execution`.
   - Inferences are routed to `apps/ai_gateway` (`ModelTier.TIER_3_FRONTIER`) with structured JSON schema outputs.
   - Grounded claims are parsed and stored alongside the narrative.
   - Human-in-the-loop review UI in `apps/web` provides interactive cross-references and cryptographic e-signature sign-off.
   - E2B(R3) export verifies `assert_ai_record_approved` before XML generation.
2. **Option B: Real-Time Dynamic LLM Generation at Export Time**: Generates text on-the-fly when E2B(R3) XML is exported, bypassing dedicated persistence. Rejected because it lacks Part 11 auditability and pre-export physician review.
3. **Option C: Client-Side Browser Prompting**: Rejected due to exposed credentials, lack of PHI de-identification air-gap, and inability to maintain immutable backend audit ledgers.

## 4. Decision Outcome

Chosen option: **Option A**.
This architecture provides absolute regulatory compliance, ensures deterministic grounding of clinical claims against source EDC data, enforces strict 21 CFR Part 11 dual-attribution e-signature gating, and leverages Tier 3 frontier reasoning models via the centralized AI Gateway.

## 5. Consequences & Trade-offs

* **Positive**: Complete automated drafting of complex SAE clinical courses adhering to ICH E2B(R3) structure.
* **Positive**: 100% claim-level traceability from narrative sentences to source eCRF clinical events.
* **Positive**: Strict regulatory compliance preventing unapproved AI draft dissemination.
* **Negative**: Requires maintaining chronological event normalization logic across multiple SDTM domains.

## 6. Implementation & Verification

* Implement domain models in `apps/safety/domain/narrative_models.py`.
* Implement persistence ORM in `apps/safety/infrastructure/models.py`.
* Implement chronological timeline aggregator in `apps/safety/services/timeline_aggregator.py`.
* Implement AI Gateway client in `apps/safety/adapters/ai_narrative_client.py`.
* Implement application orchestration and Part 11 signature gate in `apps/safety/services/narrative_service.py`.
* Implement interactive reviewer component in `apps/web/src/components/clinical/SafetyNarrativeReviewer.vue`.
* Automated test coverage in `apps/safety/tests/test_generative_safety_narratives.py`.
