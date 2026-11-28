# ADR-KERNEL-20261128-consolidate-delegation-of-authority-onto-kernel-grants-and-unify-protocol-training-in-knowledge: Consolidate Delegation of Authority onto Kernel Grants and Unify Protocol Training in Knowledge

- **Status:** Accepted
- **Date:** 2026-11-28
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001, PRD-SYS-192, PRD-TRN-001, PRD-TRN-002, PRD-TRN-003, PRD-TRN-004

---

## 1. Context & Problem Statement

Site-staff Delegation of Authority (DoA) and protocol training qualification were previously split across three parallel persistence models:

1. **Duplicate Delegation Ledgers:** `apps/ctms` (`ctms.ctms_delegations`, `ctms.ctms_training_qualifications`) and `apps/knowledge` (`knowledge.knowledge_delegations`) maintained standalone delegation and training tables alongside the Clinical Kernel authority ledger (`kernel.grants`, `kernel.qualification_evidence`, `kernel.qualification_requirements`).
2. **Disconnected Execution Enforcement:** Downstream clinical actions (such as eCRF sign-off in `apps/execution_forms`) could not rely on a single bitemporal authority evaluator (`IAuthorityEvaluator`) when site delegations and training completions lived outside Kernel grants and qualification evidence (`PRD-SYS-001`, `PRD-SYS-192`).
3. **Fragmented Training Lifecycle:** Protocol training curriculum authoring, reader acknowledgment, 21 CFR Part 11 electronic signature attestation (`PRD-TRN-001`, `PRD-TRN-002`, `PRD-TRN-003`, `PRD-TRN-004`), Kernel qualification evidence recording, and eTMF Zone 05.02.04 certificate filing were split across separate service endpoints and UI views.

## 2. Decision Drivers & Constraints

- **Single Bitemporal Source of Authority (`K004`, `PRD-SYS-001`):** Kernel grants (`with_grant_option`, `delegated_by_grant_id`, bitemporal effect windows, and qualification gates) must be the sole system of record for site-staff authority, delegation chains, and capability enforcement.
- **21 CFR Part 11 & ICH E6(R3) Traceability (`PRD-SYS-192`, `PRD-TRN-001`–`PRD-TRN-004`):** Delegating authority, revoking grants, and attesting protocol training completion require step-up electronic signature tokens, reason-for-change capture, immutable audit trails, and masked explanation nodes (`INodeVisibility`) on 403 authorization denials.
- **Clean Bounded-Context Ownership:** `apps/knowledge` owns protocol training curricula, reader modules, assignments, and eTMF certificate generation while recording resulting `QualificationEvidence` into the Clinical Kernel; `apps/ctms` projects live DoA logs from Kernel trust APIs without maintaining shadow tables.

## 3. Options Considered

### Option 1: Kernel Trust Endpoints as Single Authority Ledger + Unified Knowledge Training Pipeline (Selected)

- **Overview:** Expose `/api/v1/kernel/roles`, `/grants`, `/delegations`, `/qualification-requirements`, `/qualifications`, and `/authority/explain` via `KernelTrustEndpoints` and `KernelTrustReader` (`IKernelTrustReader`), guarded by tenant/study/site scope checks and `ProductFamilyGuardMiddleware`. Retire `ctms.ctms_delegations`, `ctms.ctms_training_qualifications`, and `knowledge.knowledge_delegations` via EF Core migrations (`20261128061949_RetireStandaloneDoaTables` and `20261128062100_ConsolidateTrainingAndRetireDelegations`). Route CTMS DoA projections and mutations through `CtmsKernelTrustClient`, enforce eCRF sign-off capability and qualification gates via Kernel authority evaluation in `ExecutionFormModule`, and unify training curriculum authoring, module reading, Part 11 signing, Kernel qualification evidence recording, and eTMF 05.02.04 certificate ingestion in `apps/knowledge` and `apps/web`.
- **Pros:**
  - Eliminates split-brain DoA and qualification state across Kernel, CTMS, Knowledge, and Execution Forms.
  - Enforces training prerequisites directly on Kernel capability checks (`IAuthorityEvaluator`) with visibility-masked `ProblemExplanation` diagnostics on denial.
  - Consolidates staff and Study Hub training workflows onto a single accessible console (`StudyTrainingView`, `CtmsDelegationSection`, and `TrainingSelectorHeader`).
- **Cons:**
  - Requires coordinated inter-service HTTP trust clients (`CtmsKernelTrustClient`, `TrainingTrustClients`) and OpenAPI wire-type schema transformation (`KernelWireTypesOpenApiTransformer`) for strongly typed Kernel value structs.

### Option 2: Bidirectional Event Synchronization Between CTMS, Knowledge, and Kernel Tables

- **Overview:** Retain standalone DoA and training tables in CTMS and Knowledge and synchronize mutations asynchronously with `kernel.grants` and `kernel.qualification_evidence`.
- **Pros:**
  - Preserves legacy per-service EF Core table schemas in CTMS and Knowledge.
- **Cons:**
  - Introduces eventual-consistency windows where revoked delegations or expired training remain active in downstream services, violating fail-closed GxP authority invariants.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Retiring shadow DoA tables and routing all role grants, sub-delegations, qualification requirements, and qualification evidence through the Clinical Kernel guarantees immediate, bitemporally auditable fail-closed enforcement across CTMS, Knowledge, Execution Forms, and Clinical Canvas surfaces.

## 5. Consequences & Trade-offs

- **Positive Impact:** Every site delegation, revocation, training requirement, and signed training completion is evaluated from a single Kernel authority graph (`PRD-SYS-001`, `PRD-TRN-001`–`PRD-TRN-004`), shrinking architectural baseline exemptions in `Cadence.Apps.Ctms` and `Cadence.Apps.Knowledge` while keeping OpenAPI specs (`kernel`, `ctms`, `knowledge`, `execution_forms`) and schema docs synchronized.
- **Negative Impact / Technical Debt:** CTMS DoA read/write endpoints depend on Kernel trust HTTP availability in distributed deployments.
- **Mitigation Strategy:** Typed HTTP clients with deterministic in-memory test doubles (`InMemoryCtmsKernelTrustClient`, `InMemoryKernelTrainingTrustClient`, `InMemoryEtmfTrainingIngestClient`), PostgreSQL migration and tenant-isolation integration tests, and architecture sentinel checks verify contract parity.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/kernel`, `packages/security`, `packages/telemetry`, `apps/ctms`, `apps/knowledge`, `apps/execution_forms`, `apps/web`, `docs/openapi/`, `docs/api/`, `docs/schema/`, and `docs/adr/kernel/`.
- **Verification Plan:** Verified via `KernelTrustAndTrainingConsolidationTests`, `DoaEndpointsTests`, `DelegationSignOffAuthorityTests`, `TrainingEndpointsTests`, `ExecutionFormModuleTests`, `CtmsStudySiteScopePostgresTests`, `StandaloneTenantIsolationPostgresTests`, `apps/web` Vitest suites, Playwright accessibility E2E suites, and `pnpm cadence check`.
