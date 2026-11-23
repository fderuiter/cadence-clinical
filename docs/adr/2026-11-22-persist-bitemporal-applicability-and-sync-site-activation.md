# ADR-2607: Persist Bitemporal Applicability, Re-Consent Capture Locks, and Greenlight-Synchronized Site Activation

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SUB-007, PRD-SSU-003

---

## 1. Context & Problem Statement

Following `ADR-2456` (`K024`), `Cadence.Kernel` modeled study- and site-scoped protocol `Effectivity` declarations and `StudySite` activation commands in `packages/kernel`, while `apps/execution` maintained `SiteComplianceCache` (`ADR-2642`) and `apps/ctms` issued HMAC-SHA256 startup `GreenlightToken` certifications (`PRD-SSU-003`). However, four regulated clinical governance gaps remained across the kernel and execution boundaries:

1. **Unexposed Effectivity HTTP Routes & Unapproved Declarations:** `DeclareEffectivity`, `ChangeEffectivityStatus`, and `ActivateStudySite` were not exposed over signed HTTP on `Cadence.Kernel.Api`, and `DeclareEffectivityHandler` accepted `Draft`, `InReview`, and `Withdrawn` protocol versions rather than restricting effectivity declarations to attested `Approved` (or historically `Superseded`) versions.
2. **Missing Bitemporal Participant `Applicability` & `ConsentStatus` (`K-TIME-1`, `K-TIME-6`, `PRD-SUB-007`):** Enrolled participants had no append-only bitemporal `Applicability` or `ConsentStatus` intervals in `KernelDbContext`. Activating a protocol amendment could not differentiate `NewParticipantsOnly`, `Automatic`, `RequiresReconsent`, and `ByCohort` transition policies, nor could capture commands pause new eCRF data entry while preserving corrections during a pending re-consent window.
3. **Latest-Active Schedule & Provenance Drift (`K-TIME-8`):** Observation capture and eCRF Casebook rendering resolved a study's latest active protocol version rather than the participant's governing `Applicability` at the observation's clinical `valid_at` timestamp, risking misattributed `GovernedBy` lineage on late-recorded visits and displaying newer `NewParticipantsOnly` schedules to legacy cohorts.
4. **Split Site Activation & Unverified Greenlight Tokens (`PRD-SSU-003`):** `SiteComplianceApplicationService.ActivateSiteAsync` verified `SiteComplianceCache.IsComplete` without verifying the CTMS HMAC-SHA256 `GreenlightToken` and without synchronizing `ActivateStudySite` into `Cadence.Kernel`, leaving `StudySite.Status` as `"Selected"` across kernel screening and capture paths.

## 2. Decision Drivers & Constraints

- **Append-Only Bitemporal Integrity (`K-TIME-1`, `K-TIME-3`, `K-TIME-6`, `K-TIME-8`):** Every `Applicability` and `ConsentStatus` interval must be persisted in `kernel` with `FORCE ROW LEVEL SECURITY`, `KT001` append-only triggers (`recorded_period` upper-bound closure only), PostgreSQL `btree_gist` exclusion constraints preventing overlapping active intervals for the same enrollment/version, and deterministic clock reads exclusively through `IClock`. Zero or inconsistent `Applicability` intervals at capture time must fail closed (`K.Collection.NoGoverningVersion`, `K.Observation.NoGoverningVersion`, and `K.Observation.ApplicabilityInconsistent`).
- **Amendment Transition Semantics & Correction Continuity (`PRD-SUB-007`):** Under `RequiresReconsent`, existing participants remain governed by their prior version until signed re-consent is recorded at `SignedAt`, while new data capture (`OpenCollectionInstance`, new items in `RecordCollectedValues`, `RecordObservation`) is blocked (`K.Observation.ReconsentRequired`) and corrections (`CorrectObservation`, updating previously recorded form items) remain permitted.
- **Cryptographic Startup Readiness & Cross-Boundary Synchronization (`PRD-SSU-003`, `K-XS-1`, `K-MOD-1`):** Site activation must verify both `SiteComplianceCache.IsComplete` and a non-expired CTMS HMAC-SHA256 `GreenlightToken` matching `(StudyId, SiteId)` before flipping `SiteComplianceCache.IsActivated` and synchronizing `ActivateStudySite` to `Cadence.Kernel` over gateway-signed HTTP, committing an immutable `BLOCKED_ACTIVATION` audit entry whenever verification fails. `Cadence.Modules.Execution` must continue referencing only `Cadence.Kernel.Abstractions` and `Cadence.Kernel.Modules`.

## 3. Options Considered

### Option 1: Mutable `CurrentProtocolVersionId` Column on `Enrollment` and Direct Cross-Schema Writes

- **Overview:** Store a mutable governing version pointer and boolean re-consent flag directly on `kernel.enrollments`, and update `kernel.study_sites` directly from `apps/execution` SQL.
- **Pros:**
  - Fewer tables and HTTP calls.
- **Cons:**
  - Violates `K-TIME-1` and `K-TIME-8` (loses valid-time history needed to stamp `GovernedBy` on late-entered observations collected prior to an amendment), violates `K-XS-2` cross-schema isolation, and bypasses kernel command auditing and outbox guarantees.

### Option 2: Bitemporal `Applicability` & `ConsentStatus` Tables, `TransitionPolicy` Evaluation, Signed Gateway Sync Clients, and HMAC `GreenlightToken` Verification (Selected)

- **Overview:** Persist bitemporal `Applicability` and `ConsentStatus` rows in `packages/kernel` with `FORCE ROW LEVEL SECURITY`, `KT001` append-only triggers, and `btree_gist` exclusion constraints; evaluate `ProtocolAmendment.TransitionPolicy` (`NewParticipantsOnly`, `Automatic`, `RequiresReconsent`, `ByCohort` structured predicates) inside `ChangeEffectivityStatusHandler` and seed initial `Applicability` on `ScreenParticipant`; synchronize signed consent events from `SubjectApplicationService.RecordConsentAsync` and `EConsentCompletedConsumer` (`svc:econsent-consumer`) to `POST /api/v1/kernel/consents/sync` to atomically close prior `Applicability` intervals and open the consented version at `SignedAt`; resolve `GovernedBy` lineage at `valid_at` and serve participant-specific schedules via `GET /api/v1/kernel/enrollments/{enrollmentRef}/schedule-of-activities`; and verify CTMS HMAC-SHA256 `GreenlightToken` tokens in `SiteComplianceApplicationService.ActivateSiteAsync` before idempotently activating `Kernel` `StudySite` via `POST /api/v1/kernel/study-sites/activate` and enforcing `"Active"` site gates across screening, enrollment, randomization, and eCRF capture.
- **Pros:**
  - Preserves complete valid-time and recorded-time provenance (`K-TIME-1`, `K-TIME-6`, `K-TIME-8`) across multi-amendment studies and late data entry.
  - Enforces `PRD-SUB-007` re-consent capture locks consistently in both `CaptureConditions` / `ExecutionCommandHandlers` and the Clinical Canvas Casebook (`ParticipantFormsPanel.vue`, `CanvasFormWorkspace.vue`) while keeping existing observations and form items correctable.
  - Enforces `PRD-SSU-003` cryptographic startup greenlight verification and synchronizes site activation between `Execution` and `Kernel` over gateway-signed HTTP without breaking `K-XS-1` or `K-XS-2`.
- **Cons:**
  - Introduces synchronous gateway-signed HTTP calls from `Execution` to `Kernel` during site activation and consent recording.
- **Mitigation Strategy:** Both `/api/v1/kernel/study-sites/activate` and `/api/v1/kernel/consents/sync` accept deterministic `Idempotency-Key` headers and treat repeat activations/consents idempotently so retries converge cleanly; when `Kernel:BaseUrl` is unconfigured in isolated unit harnesses, no-op synchronizers preserve unit isolation while full-stack hosts configure `HttpKernelSiteActivationSynchronizer` and `HttpKernelConsentSynchronizer`.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Only bitemporal `Applicability` and `ConsentStatus` intervals satisfy `K-TIME-1`, `K-TIME-6`, and `K-TIME-8` while honoring `PRD-SUB-007` cohort/re-consent transition rules, and gateway-signed HTTP synchronization with HMAC `GreenlightToken` verification closes the `PRD-SSU-003` site-activation gap without violating kernel module or schema boundaries.

## 5. Consequences & Trade-offs

- **Positive Impact:** Unapproved protocol drafts cannot be declared effective; participant schedules and `GovernedBy` observation lineage track exact valid-time `Applicability`; pending `RequiresReconsent` transitions lock new eCRF capture with clear Clinical Canvas alerts while permitting corrections; unactivated sites cannot screen, enroll, randomize, or capture clinical data; and `K-TIME-6` and `K-TIME-8` are enforced by PostgreSQL integration tests and removed from `kernel-rule-coverage.baseline.txt`.
- **Negative Impact / Technical Debt:** `ByCohort` transition evaluation currently supports structured cohort predicates (`enrolled_before`, `enrolled_after`, `site`, `country`, `subject`); arbitrary clinical expression DSL editors remain deferred.
- **Mitigation Strategy:** Cohort predicate strings are evaluated deterministically in `ApplicabilityGovernance.MatchesCohortPredicate` with explicit `DefaultCohortPolicy` fallback so future expression evaluators can extend predicate evaluation without schema migration.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/kernel` (`Abstractions`, `Domain`, `Application`, `Persistence`, `Api`), `packages/security`, `apps/ctms`, `apps/execution/module`, `apps/execution`, `apps/kernel-api`, `apps/web`, `docs/openapi/`, `docs/schema/`, `packages/demo-fixtures`.
- **Verification Plan:** Verified via `Cadence.UnitTests` (`GreenlightTokenValidatorTests`, `HttpKernelSiteActivationAndConsentSynchronizerTests`, `SiteComplianceEndpointsRoutingTests`, `SubjectApplicationServiceStateTests`, `EtmfCompletenessWebhookTests`), `Cadence.ArchitectureTests` (including shrunk `kernel-rule-coverage.baseline.txt`), PostgreSQL 18 forced-RLS integration tests (`KernelApplicabilityAndConsentPostgresTests`, `KernelEffectivityPostgresTests`, `KernelStudyRegistrationPostgresTests`), `Cadence.E2ETests`, and Vitest/axe-core specs (`StudyCanvasCasebookMatrix.spec.ts`, `ctms_site_activation.spec.js`, `kernelStudy.spec.ts`, `executionApi.test.js`).
