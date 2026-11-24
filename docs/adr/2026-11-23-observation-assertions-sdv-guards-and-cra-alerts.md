# ADR-2626: Observation Assertion Audit Coverage, SDV Precondition Guards, and CRA Modification Alerts

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-QRY-005 (source data verification preconditions and open-query blocking), PRD-QRY-006 (re-verification state, pre-save warning, and CRA modification alerts), PRD-SYS-001 (21 CFR Part 11 complete observation and assertion audit trail), kernel spec §8.2, K-XS-1, K-XS-2, K-MOD-1, K-AUTH-4

---

## 1. Context & Problem Statement

[ADR-2621](2026-10-19-observation-corrections-are-revisions-locks-are-scoped-assertions.md) and [ADR-2625](2026-11-22-bitemporal-form-query-aggregate-and-provenance.md) established revision-bound `Verified` and `Queried` kernel `Assertion`s alongside `FormQuery` in `Cadence.Modules.Execution`, but four compliance and workflow gaps remained across `PRD-QRY-005`, `PRD-QRY-006`, and Clinical Kernel §8.2:

1. **Incomplete observation audit reconstruction:** `KernelObservationRecords` queried `kernel.audit_entries` only where `AggregateType == "Observation"`, omitting covering `Assertion` audit entries (such as `Verified`, `Queried`, `QueryAnswered`, `Frozen`, `DatabaseLocked`, `Signed`, and `Retracted`) from `GET /api/v1/kernel/observations/{ref}`.
2. **Unserialized SDV preconditions:** `VerifyObservationHandler` did not acquire a `FOR UPDATE` row lock on the target `Observation` alongside `RaiseQueryHandler`, and allowed `VerifyObservation` to succeed even when an open `Queried` assertion or an un-retracted `Verified` assertion already existed on the target revision.
3. **Silent verified-value supersession for monitors:** Correcting a `Verified` observation left prior revision assertions intact so SDV reverted to `Needs re-verification` by revision comparison, yet neither `ObservationView.vue` warned site staff before saving nor `Cadence.Modules.Execution` recorded an operational CRA alert when `ObservationCorrected` superseded a verified revision.
4. **Standalone observation view lacked assertion and query visibility:** `ObservationView.vue` rendered revision history and provenance without SDV status badges (`Verified (rev N)`, `Needs re-verification`, `Unverified`) or open query threads from `observation.assertions`.

## 2. Decision Drivers & Constraints

- **Kernel Isolation & Identifier Discipline (`K-XS-1`, `K-XS-2`, `K-MOD-1`):** `Cadence.Modules.Execution` must consume `ObservationCorrected` through `KernelEventConsumer<T>` reading `IObservationRecords` and `ICollectionInstanceRecords` without referencing `Cadence.Kernel.Domain` or `Cadence.Kernel.Persistence`, persist kernel identifiers (`StudyId`, `StudySiteId`, `EnrollmentId`, `CollectionInstanceId`, `ObservationId`) via `KernelIdConversions`, and expose plain `Guid` DTOs at `/api/v1/execution/forms/sdv-alerts`.
- **Serialized Epistemic Preconditions (`PRD-QRY-005`):** Concurrent `RaiseQuery` and `VerifyObservation` commands against the same observation must serialize on the observation row in PostgreSQL so an open query cannot race past verification, returning structured 409 `KernelError` codes (`K.Assertion.QueryOpen` and `K.Assertion.AlreadyVerified`).
- **Append-Only CRA Notification Ledger (`PRD-QRY-006`, `K-AUTH-4`):** Operational CRA alerts belong in `execution.form_sdv_alerts` under forced tenant RLS (`KA001` insert-only trigger) rather than mutating immutable kernel `Assertion`s, and read endpoints must enforce `IKernelReadAccess.FilterVisibleAsync` for site scope and blinding parity while omitting observations once monitors verify the latest revision.

## 3. Options Considered

### Option 1: Kernel SDV Precondition Locking + Covering Assertion Audit Join + Outbox-Projected `FormSdvAlert` Read Model (Selected)

- **Overview:** Extend `ScopedAssertions` with `CoveringAuditIds` so `KernelObservationRecords` returns `Observation` and covering `Assertion` audit entries ordered by `Sequence`; lock the target observation row (`FOR UPDATE`) in `ObservationAssertions.OpenAsync` across `VerifyObservationHandler` and `RaiseQueryHandler` while rejecting `VerifyObservation` with `AssertionErrors.QueryOpen` (`K.Assertion.QueryOpen`, 409) or `AssertionErrors.AlreadyVerified` (`K.Assertion.AlreadyVerified`, 409); add `AlertMonitorOnVerifiedObservationCorrected : KernelEventConsumer<ObservationCorrected>` (`svc:execution-sdv-alerts`) dispatching an idempotent `RecordFormSdvAlert` command into `execution.form_sdv_alerts`; expose `GET /api/v1/execution/forms/sdv-alerts` filtered by `study_id` and `enrollment_id` with live revision-verification filtering and `IKernelReadAccess` scope/blinding checks; and surface SDV badges, open query threads, pre-save re-verification warnings, and CRA alerts across `ObservationView.vue` and `NeedsAttention.vue`.
- **Pros:**
  - Enforces `PRD-QRY-005` and `PRD-QRY-006` end to end without violating `K-XS-1`, `K-XS-2`, or `K-MOD-1`.
  - Serializes concurrent `RaiseQuery` and `VerifyObservation` transactions at the database row level and keeps `execution.form_sdv_alerts` idempotent under outbox replay via deterministic UUIDv5 alert IDs.
  - Automatically clears CRA alerts from `GET /api/v1/execution/forms/sdv-alerts` as soon as a monitor verifies the latest observation revision, without destructive updates or deletes on `execution.form_sdv_alerts`.
- **Cons:**
  - Adds one insert-only table (`execution.form_sdv_alerts`) and an outbox consumer (`svc:execution-sdv-alerts`) to `Cadence.Modules.Execution`.

### Option 2: Query Historical `ObservationCorrected` Events on Every CRA Dashboard Request Without a Module Projection

- **Overview:** Scan kernel domain events or every study observation's revision history on demand when rendering `NeedsAttention.vue` instead of projecting `FormSdvAlert` rows on `ObservationCorrected`.
- **Pros:**
  - Avoids adding `execution.form_sdv_alerts` and its migration.
- **Cons:**
  - Requires O(observations) revision and assertion scans on every Study Home load and cannot record immutable point-in-time supersession metadata (whether the prior revision was actively verified at the exact moment `ObservationCorrected` was recorded vs. retracted later).

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Locking the observation row in `ObservationAssertions.OpenAsync` closes the concurrent query/SDV race in PostgreSQL, `ScopedAssertions.CoveringAuditIds` completes the 21 CFR Part 11 audit view per Clinical Kernel §8.2, and projecting `ObservationCorrected` into an insert-only `execution.form_sdv_alerts` table via `IObservationRecords` gives CRAs fast, scope-filtered alerts while keeping operational notification state inside `Cadence.Modules.Execution`.

## 5. Consequences & Trade-offs

- **Positive Impact:** `GET /api/v1/kernel/observations/{ref}` returns a single `Sequence`-ordered audit stream spanning both value revisions and covering assertions; `VerifyObservation` deterministically rejects open-query and duplicate-verification attempts with typed 409 problem details; site staff see an explicit re-verification warning before editing verified values in `ObservationView.vue`; and CRAs see actionable `SDV re-verification required` items in `NeedsAttention.vue` that resolve automatically once the latest revision is verified.
- **Negative Impact / Technical Debt:** Because `AlertMonitorOnVerifiedObservationCorrected` runs asynchronously via the kernel outbox, `GET /api/v1/execution/forms/sdv-alerts` reflects newly created alerts once the outbox relays `ObservationCorrected`.
- **Mitigation Strategy:** Deterministic UUIDv5 keys `(TenantId, ObservationId, NewRevision)` guarantee idempotent replay, while `ObservationView.vue` and `EcrfFormPanel.vue` compute `Needs re-verification` synchronously from `observation.assertions` immediately upon save.

## 6. Implementation & Verification

- **Affected Packages / Services:** `packages/kernel`, `apps/execution/module`, `apps/kernel-api`, `apps/web`, `docs/schema`, `docs/openapi`, `docs/api`, `docs/adr`.
- **Verification Plan:** Verified by `KernelBoundaryTests`, `RouteAuthorizationTests`, `KernelAssertionPostgresTests`, `KernelObservationHostPostgresTests`, `ExecutionSdvAlertPostgresTests`, `cadence migrations verify executionmodule`, `cadence check`, and Vitest suites (`ObservationView.spec.ts`, `StudyCanvasQueries.spec.js`, `StudyCanvasProposals.spec.js`, `kernelApi.test.js`).
