# ADR-2217: Dynamic Consent Engine Re-Consent Gating and Lifecycle

- **Status:** Accepted
- **Date:** 2026-08-24
- **Authors:** @fderuiter
- **Deciders:** Architecture Governance Board, Clinical Operations, Regulatory Compliance

---

## 1. Context & Problem Statement

Clinical trial operations under FDA 21 CFR Part 11, ICH GCP E6(R2)/(R3), and EU CTR 536/2014 require continuous, tamper-evident verification of participant informed consent. As protocols evolve through substantive amendments and minor participants reach legal adulthood, clinical execution systems must dynamically gate visits, forms, and procedures without disrupting ongoing patient care or blocking safety-critical reporting (e.g. Adverse Events).

Prior iterations of the eConsent module provided individual services for diffing (`diff_engine.py`), minor majority calculation (`majority_engine.py`), and quiz evaluation (`evaluator.py`), but lacked a unified, **local-substitutable, pure domain engine** capable of:

1. Evaluating multi-dimensional consent state (protocol version, substantive amendment deltas, pediatric transition milestones, and granular optional research choices) in a single deterministic pass.
2. Generating granular execution gating decisions across study encounters, clinical procedures, biological specimen collection, and investigational product dispensing.
3. Providing zero-latency (<1ms) in-memory evaluation for downstream consumers (`apps/execution`, RTSM, Gateway) while maintaining strict architectural decoupling.

Requirements: `PRD-SYS-177`, `PRD-SYS-178`, `PRD-SYS-179`, `PRD-SUB-007`.

---

## 2. Decision Drivers & Constraints

- **Pure Domain Substitutability:** Domain evaluation logic must execute in ephemeral memory with zero I/O or database dependencies, allowing seamless invocation in offline environments, unit test fixtures, and in-memory caches.
- **Patient Safety Carve-Outs (ICH GCP E6):** Pending re-consent or pediatric majority transition must _never_ block patient safety actions (Adverse Event reporting, concomitant medications, emergency unblinding, safety labs).
- **Granular Capability Tokenization:** Sub-study and optional research options (e.g., pharmacogenomics DNA biobanking, longitudinal re-contact) must resolve to typed capability tokens (`GranularConsentCapability`) rather than unstructured JSON blobs.
- **REST & In-Memory Snapshot Caching (<100ms SLA):** Adhere to the platform rule banning sibling database imports between microservices while providing high-performance gating decisions.
- **21 CFR Part 11 Audit Integrity:** All state transitions and gating evaluations must emit structured, immutable audit log events.

---

## 3. Options Considered

### Option 1: **Option A (Selected): Local-Substitutable Pure Domain Engine (`DynamicConsentEngine`) with Layered Service Adapters & Cached Snapshot Evaluation.**

- **Overview:** - Domain layer encapsulates pure dataclasses (`SubjectConsentSnapshot`, `EncounterContext`, `ConsentEvaluationVerdict`, `GranularConsentCapability`) and deterministic evaluation algorithms.
  - Application layer exposes `DynamicConsentService` for repository persistence, outbox event publishing, and 21 CFR Part 11 audit trails.
  - Presentation layer exposes `/api/v1/econsent/gating/evaluate` and `/api/v1/econsent/gating/capabilities`.
  - Execution microservice consumes cached consent snapshots invalidated via the clinical event spine.
- _**Pros:**_
  - ✅ Satisfies architectural requirements
- _**Cons:**_
  - ❌ Requires implementation and maintenance overhead

### Option 2: **Option B: Database-Bound Monolithic Consent Service.**

- **Overview:** - The engine performs database queries inside its evaluation methods.
  - _Rejected:_ Violates pure domain boundaries, prevents in-memory local substitution, and degrades performance during high-throughput form data entry.
- _**Pros:**_
  - ✅ Satisfies architectural requirements
- _**Cons:**_
  - ❌ Requires implementation and maintenance overhead

### Option 3: **Option C: Global Subject Lockout on Re-Consent Trigger.**

- **Overview:** - Hard-locks the entire participant record upon amendment or majority milestone.
  - _Rejected:_ Violates GxP patient safety standards by preventing immediate adverse event entry and medical history documentation.

---

- _**Pros:**_
  - ✅ Satisfies architectural requirements
- _**Cons:**_
  - ❌ Requires implementation and maintenance overhead

## 4. Decision Outcome

- **Chosen Option:** **Option A**. We implement `DynamicConsentEngine` as a pure, local-substitutable domain component under `apps/econsent/domain/evaluator.py` paired with policy matrices and application service adapters.
- **Justification:** Selected as primary architectural approach.

### Key Architectural Specifications:

1. **Engine Purity:** The domain engine takes immutable snapshot objects and returns a deterministic `ConsentEvaluationVerdict` with flags: `is_permitted`, `gating_reason`, `active_version_index`, `required_version_index`, `pending_milestones`, and `granted_capabilities`.
2. **Re-Consent Gating Matrix:** Evaluates activity codes against gating rules:
   - `GATED_ON_RECONSENT`: Standard visits, efficacy procedures, dose modifications, investigational product dispensing.
   - `SAFETY_EXEMPT`: Adverse events, serious adverse events, emergency unblinding, safety laboratory panels, vital signs.
   - `CAPABILITY_GATED`: Pharmacogenomic biobanking, genetic sequencing, exploratory biomarker assays, future research specimen storage.
3. **Minor-to-Adult Transition Lifecycle:**
   - $T - 30$ days: `APPROACHING_MAJORITY` pre-notification.
   - $T = 0$: Transitions to `PENDING_ADULT_TRANSITION`.
   - In-progress visits allowed to conclude; subsequent visits gated until direct adult self-consent is signed.
4. **Execution Gateway Caching:** `apps/execution` caches subject consent snapshots locally in memory/Redis, invalidating on `CONSENT_SIGNED`, `RECONSENT_TRIGGERED`, or `WITHDRAWAL_RECORDED` events.

---

## 5. Consequences & Trade-offs

- **Positive:**
  - Sub-millisecond deterministic evaluation suitable for offline simulation, unit testing, and real-time form validation.
  - Zero risk of blocking emergency safety data entry while maintaining 100% regulatory compliance.
  - Clean separation of concerns adhering to Hexagonal Architecture and Domain-Driven Design.
- **Negative:**
  - Requires maintaining snapshot serialization models and event-driven cache invalidation hooks across `apps/econsent` and `apps/execution`.

---

## 6. Implementation & Verification

- **Domain Engine:** `apps/econsent/domain/evaluator.py`, `apps/econsent/domain/diff_engine.py`, `apps/econsent/domain/entities.py`
- **Application Layer:** `apps/econsent/application/use_cases.py` (`DynamicConsentService`)
- **Presentation Layer:** `apps/econsent/presentation/routers/reconsent.py`, `apps/econsent/presentation/dtos.py`
- **Execution Integration:** `apps/execution/database/audit.py`
- **Verification Suites:** Unit tests in `apps/econsent/tests/test_econsent.py`, `apps/econsent/tests/test_econsent_amendment_diff.py`, and integration tests in `apps/econsent/tests/test_econsent_api_endpoints.py`.
