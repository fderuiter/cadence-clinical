# ADR-2553: Sync Subject Lifecycle Transitions to Clinical Kernel and Allow Blinded Subject Enrollment Lookup

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SUB-001, PRD-SYS-004, PRD-SYS-192

---

## 1. Context & Problem Statement

Participant screening in `apps/execution` (`POST /api/v1/subjects`) previously synchronized `RegisterParticipant` and `ScreenIntoStudy` to the Clinical Kernel, but subsequent subject lifecycle transitions—consent enrollment (`POST /api/v1/subjects/{subjectId}/consent` and `EConsentCompletedEvent`), randomization enrollment (`POST /api/v1/rtsm/randomize`), and subject status transitions (`PATCH /api/v1/subjects/{subjectId}/status` to `Enrolled`, `Active`, `ScreenFailed`, `Withdrawn`, `Completed`, or `LostToFollowUp`)—remained isolated in `edc.clinical_subjects`.

Consequently:

1. Kernel `StudyEnrollment` records stayed stuck in `Screening`, leaving `KernelLockService` unable to enforce pre-lock enrollment checks on active subjects and preventing `OpenForm` (`K-INV-4`) from enforcing post-withdrawal capture cutoff (`WithdrawFromStudy.EffectiveAt`) unless callers bypassed `Execution` and called Kernel routes directly.
2. Blinded eCRF workspaces (`ParticipantFormsPanel.vue`) needed a non-unblinding read endpoint to resolve the authoritative Kernel `StudyEnrollment` by `(studyRef, siteRef, subjectCode)` without passing through `GET /api/v1/kernel/enrollments/{enrollmentId}` (which `ProductFamilyGuardMiddleware` reserves for treatment-unblinded roles) or supplying an internal `participantId` UUID.

## 2. Decision Drivers & Constraints

- **Single Source of Truth for Enrollment Lifecycle (`K-INV-4`, `K-XS-1`, `ADR-2449`, `PRD-SUB-001`):** `StudyEnrollment` in the Clinical Kernel must reflect every regulatory status transition (`Screening` -> `Enrolled`, `ScreenFailed`, `Withdrawn`, `Completed`, `LostToFollowUp`) and withdrawal effective timestamp so downstream eCRF form capture (`OpenForm`) and study/site lock gates evaluate authoritative state.
- **Treatment Blinding Isolation (`PRD-SYS-004`, `PRD-SYS-192`):** Blinded site staff (`site_crc`, `site_pi`) and blinded sponsor roles must never gain access to treatment arm assignments, while still resolving blinded enrollment metadata (`enrollment_id`, `status`, `screened_at`, `enrolled_at`, `withdrawn_at`) by subject code.
- **Fail-Closed Transactional Integrity & Part 11 Provenance:** Synchronizing transitions from `Execution` to `Kernel` must propagate signed gateway identity headers, deterministic `Idempotency-Key` values, and explicit `X-Change-Reason` metadata, rolling back the `Execution` database transaction if the Kernel rejects or fails the transition.

## 3. Options Considered

### Option 1: Eventual Outbox Synchronization Only and Reuse `GET /api/v1/kernel/enrollments/{enrollmentId}`

- **Overview:** Publish asynchronous domain events for subject status changes and relax `ProductFamilyGuardMiddleware` on `/api/v1/kernel/enrollments/{enrollmentId}`.
- **Pros:**
  - Avoids synchronous HTTP coordination during `Execution` mutations.
- **Cons:**
  - Introduces race windows where withdrawn subjects can still open new Kernel eCRF forms (`K-INV-4` violation) before outbox delivery completes, and weakens treatment-blinding route guards on direct enrollment-ID lookups.

### Option 2: Synchronous Fail-Closed Kernel Transition Sync and Blinded `by-subject` Lookup Route (Selected)

- **Overview:** Expose `GET /api/v1/kernel/enrollments/by-subject`, `POST /api/v1/kernel/enrollments/{enrollmentId}/screen-fail`, and `POST /api/v1/kernel/enrollments/{enrollmentId}/complete` on `Cadence.Kernel.Api`; allow `GET /api/v1/kernel/enrollments/by-subject` alongside `/api/v1/kernel/enrollments/lookup` in `ProductFamilyGuardMiddleware.GetTreatmentBlindingRequirement` while keeping `/api/v1/kernel/enrollments/{enrollmentId}` unblinded-only; and introduce `IKernelEnrollmentSynchronizer` (`HttpKernelEnrollmentSynchronizer`) in `apps/execution` inside transactional subject/consent/randomization mutations plus outbox-backed `EConsentCompletedConsumer` with synthesized service-principal gateway headers when running outside an HTTP request.
- **Pros:**
  - Guarantees immediate `K-INV-4` withdrawal enforcement and accurate Kernel lock/export readiness with fail-closed rollback on Kernel errors.
  - Preserves treatment blinding on `ProductFamilyGuardMiddleware` by exposing only blinded `KernelEnrollmentSummaryResponse` fields on `GET /api/v1/kernel/enrollments/by-subject`.
  - Eliminates fragile client-side participant UUID discovery in `ParticipantFormsPanel.vue`.
- **Cons:**
  - Couples `Execution` subject lifecycle mutations to `Kernel` availability when synchronization is enabled (`Services:KernelUrl` configured).

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Synchronous fail-closed transition forwarding inside `Execution` transactions combined with a dedicated blinded `GET /api/v1/kernel/enrollments/by-subject` lookup upholds `K-INV-4`, `PRD-SUB-001`, `PRD-SYS-004`, and `PRD-SYS-192` without leaking treatment arm assignments to blinded roles.

## 5. Consequences & Trade-offs

- **Positive Impact:** Screening failure, consent/randomization enrollment, study completion, lost-to-follow-up, and withdrawal transitions in `Execution` stay strictly aligned with `StudyEnrollment` in the Clinical Kernel, and blinded eCRF views resolve Kernel enrollments directly by subject code.
- **Negative Impact / Technical Debt:** `Execution` continues maintaining its local `ClinicalSubject` read/workflow projection alongside `StudyEnrollment` until standalone `Execution` subject routes fully consolidate into `Cadence.Modules.Execution`.
- **Mitigation Strategy:** Deterministic UUIDv8 idempotency keys (`cadence:execution:kernel-sync:...`) make retries across HTTP callers and MassTransit consumers idempotent, and Kernel `409 Conflict` / validation problem details propagate cleanly to callers.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/kernel` (`Abstractions`, `Domain`, `Application`, `Persistence`, `Api`), `packages/security`, `apps/kernel-api`, `apps/execution`, `apps/web`, `docs/openapi`, `docs/api`.
- **Verification Plan:** Verified via `TreatmentBlindingHostRegressionTests`, `KernelEnrollmentEndpointsTests`, `HttpKernelEnrollmentSynchronizerTests`, `EConsentCompletedConsumerTests`, `Cadence.ArchitectureTests`, PostgreSQL 18 integration tests (`SubjectKernelTransitionSyncPostgresTests`, `KernelCaptureAfterWithdrawalPostgresTests`), and Vitest (`ParticipantFormsPanel.spec.ts`, `kernelStudy.spec.ts`).
