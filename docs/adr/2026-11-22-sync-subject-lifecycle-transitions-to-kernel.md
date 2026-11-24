# ADR-2553: Sync Subject Lifecycle Transitions to Clinical Kernel and Allow Blinded Subject Enrollment Lookup

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SUB-001, PRD-SYS-004, PRD-SYS-192

---

## 1. Context & Problem Statement

Participant screening in `apps/execution` (`POST /api/v1/subjects`) previously synchronized `RegisterParticipant` and `ScreenParticipant` to the Clinical Kernel, but subsequent subject lifecycle transitions—consent enrollment (`POST /api/v1/subjects/{subjectId}/consent` and `EConsentCompletedEvent`), randomization (`POST /api/v1/rtsm/randomize`), and subject status transitions (`PATCH /api/v1/subjects/{subjectId}/status` to `Enrolled`, `Randomized`, `Active`, `Completed`, `ScreenFailed`, or signed `Withdrawn`)—remained isolated in `edc.subjects`.

Consequently:

1. Kernel `Enrollment` records stayed stuck in `Screening`, leaving lock readiness checks unable to evaluate active enrollments and preventing `CaptureConditions.RefusalAsync` (`K-CONS-2`) from enforcing post-withdrawal and screen-failure capture cutoffs unless callers bypassed `Execution` and called Kernel routes directly.
2. Clinical Canvas workspaces (`CanvasInspector.vue`, `ParticipantFormsPanel.vue`) needed a blinded-safe read endpoint to resolve the authoritative Kernel `Enrollment` (including `enrollment_number` and lifecycle timestamps) by `(studyId, subjectCode)` without passing through `GET /api/v1/kernel/enrollments/{enrollmentId}` with an internal enrollment UUID or exposing unblinded treatment arm assignments.

## 2. Decision Drivers & Constraints

- **Single Source of Truth for Enrollment Lifecycle (`K-TRUTH-1`, `K-CONS-2`, `K-XS-1`, `K-XS-2`, `PRD-SUB-001`):** `Enrollment` in the Clinical Kernel must reflect every canonical regulatory status transition (`Screening` -> `Enrolled`, `Randomized`, `Active`, `Completed`, `ScreenFailed`, `Withdrawn`) and withdrawal timestamp so downstream capture guards (`RecordObservation`, `OpenCollectionInstance`, `RecordCollectedValues`) and lock/export projections evaluate authoritative state, while keeping operational overlays (`ReconsentRequired`, `Unblinded`) scoped to `Execution`.
- **Treatment Blinding Isolation (`K-AUTH-3`, `PRD-SYS-004`, `PRD-SYS-192`):** Blinded site staff and blinded sponsor roles must never gain access to `assigned_arm` or `stratum_key`, while still resolving blinded enrollment metadata (`enrollment_id`, `enrollment_number`, `status`, `screened_at`, `enrolled_at`, `randomized_at`, `withdrawn_at`, `withdrawal_kind`) by subject code.
- **Fail-Closed Transactional Integrity & Part 11 Provenance:** Synchronizing transitions from `Execution` to `Kernel` must propagate gateway-signed principal headers, deterministic `Idempotency-Key` values, and 21 CFR Part 11 `X-Sig-Token` step-up signatures on withdrawal (`AttestationMeaning.Withdrawn` over `Enrollment.ContentHash`), aborting the `Execution` mutation before commit if the Kernel rejects or fails the transition.

## 3. Options Considered

### Option 1: Eventual Outbox Synchronization Only and Client-Side Enrollment UUID Discovery

- **Overview:** Publish asynchronous domain events for subject status changes and require frontend views to discover internal `enrollmentId` UUIDs through existing unblinded routes.
- **Pros:**
  - Avoids synchronous HTTP coordination during `Execution` mutations.
- **Cons:**
  - Introduces race windows where withdrawn or screen-failed subjects can still record new Kernel observations or open collection instances (`K-CONS-2` violation) before outbox delivery completes, and complicates blinded enrollment lookup by subject code.

### Option 2: Synchronous Fail-Closed Kernel Transition Sync and Masked `by-subject` Lookup Route (Selected)

- **Overview:** Mount `GET /api/v1/kernel/enrollments/by-subject`, `POST /api/v1/kernel/enrollments/status` (`ChangeEnrollmentStatus`), and step-up signed `POST /api/v1/kernel/enrollments/withdraw` (`Attest` + `WithdrawParticipant`) on `Cadence.Kernel.Api`; mask `assigned_arm` and `stratum_key` for blinded callers on `GET /api/v1/kernel/enrollments/by-subject` while enforcing `ProductFamilyGuardMiddleware` route authorization; extend `CaptureConditions.RefusalAsync` (`K-CONS-2`, `UX-9`) to refuse new capture on both `Withdrawn` and `ScreenFailed` enrollments while preserving corrections; and extend `IKernelEnrollmentSynchronizer` (`HttpKernelEnrollmentSynchronizer`) in `apps/execution` across `SubjectApplicationService`, `RandomizationApplicationService`, and `EConsentCompletedConsumer`.
- **Pros:**
  - Guarantees immediate `K-CONS-2` terminal capture refusal and bitemporally accurate `EnrollmentNumber` casebook exports with fail-closed rollback on Kernel errors.
  - Preserves treatment blinding by masking `assigned_arm` and `stratum_key` on `GET /api/v1/kernel/enrollments/by-subject` unless the caller holds `study.unblind` (`K-AUTH-3`).
  - Supports single-arm/open-label `Enrolled -> Active` transitions alongside RTSM `Enrolled -> Randomized -> Active` workflows and Part 11 signed withdrawals.
- **Cons:**
  - Couples `Execution` subject lifecycle mutations to `Kernel` availability when synchronization is enabled (`KernelSyncOptions.Enabled`).

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Synchronous fail-closed transition forwarding before committing `ExecutionDbContext`, Part 11 step-up attestation on `POST /api/v1/kernel/enrollments/withdraw`, and blinded field masking on `GET /api/v1/kernel/enrollments/by-subject` uphold `K-TRUTH-1`, `K-CONS-2`, `K-XS-1`, `K-XS-2`, `PRD-SUB-001`, `PRD-SYS-004`, and `PRD-SYS-192` without leaking treatment arm assignments to blinded roles.

## 5. Consequences & Trade-offs

- **Positive Impact:** Consent enrollment, RTSM randomization, single-arm activation, study completion, screen failure, and signed withdrawal transitions in `Execution` stay strictly aligned with `Enrollment` in the Clinical Kernel; terminal capture refusal (`Withdrawn` and `ScreenFailed`) is enforced uniformly with `UX-9` explanations; and Clinical Canvas displays canonical `EnrollmentNumber` values.
- **Negative Impact / Technical Debt:** `Execution` continues maintaining its operational `Subject` projection (`edc.subjects`) alongside Kernel `Enrollment` for Execution-specific overlays (`ReconsentRequired`, `Unblinded`).
- **Mitigation Strategy:** Deterministic `Idempotency-Key` values make retries across HTTP callers and `EConsentCompletedConsumer` idempotent, `HttpWithdrawalSignatureVerifier` verifies `X-Sig-Token` when `Execution` runs standalone without Kernel sync, and Kernel `409 Conflict` / validation responses propagate cleanly to callers.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/kernel` (`Abstractions`, `Domain`, `Application`, `Infrastructure`, `Api`), `packages/security`, `apps/kernel-api`, `apps/execution`, `apps/web`, `packages/demo-fixtures`, `docs/openapi`, `docs/api`.
- **Verification Plan:** Verified via `KernelApiHostTests`, `TreatmentBlindingHostRegressionTests`, `SubjectApplicationServiceTests`, `HttpKernelEnrollmentSynchronizerTests`, `EConsentCompletedConsumerTests`, `Cadence.ArchitectureTests`, PostgreSQL 18 integration tests (`SubjectKernelTransitionSyncPostgresTests`, `SubjectRouteAuthorityTests`, `KernelCaptureAfterWithdrawalPostgresTests`, `KernelLockAndExportHostPostgresTests`), and Vitest (`kernelApi.test.js`, `StudyCanvasParticipantActions.spec.js`, `StudyCanvasRecord.spec.js`, `StudyCanvasView.spec.js`).
