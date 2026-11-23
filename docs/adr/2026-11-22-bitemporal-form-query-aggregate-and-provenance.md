# ADR-2625: Bitemporal FormQuery Aggregate, Separation of Duties, and SLA Escalation

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-QRY-001 (query raising, automated answering on value correction, separation of duties on closure), PRD-QRY-002 (query ownership, SLA aging escalation, and de-identified study-lead digests), PRD-SYS-001 (signed reason for change and per-tenant audit trail), kernel spec K-XS-2, K-TIME-2, K-TIME-3

---

## 1. Context & Problem Statement

[ADR-2622](2026-10-19-ecrf-forms-are-collection-instances-saved-in-one-command.md) and [ADR-2624](2026-11-21-outbox-collection-edit-checks-and-pre-commit-preview.md) introduced `FormQuery` in `Cadence.Modules.Execution` for manual and rule-driven eCRF data discrepancies, mirroring open and answered states to kernel `Assertion`s through `MirrorFormQueryToKernel`. However, six regulatory and operational gaps remained against `PRD-QRY-001`, `PRD-QRY-002`, and Clinical Kernel temporal/lineage invariants:

1. **Mutable JSON history and untyped aggregate identity:** `FormQuery` stored its conversation as a mutable JSON column on a plain `Guid`-keyed entity without bitemporal state intervals (`valid_period`, `recorded_period`) or `as_of` point-in-time reconstruction (`K-TIME-2`).
2. **Missing ownership, origin, SLA due dates, and escalation:** Queries carried neither `origin` (`manual` vs. `system`), `owner_role` (`site`, `monitor`, `data_manager`), `priority` (`low`, `normal`, `high`), nor a 14-day response `due_at` with automated escalation and PII-free study-lead digests (`PRD-QRY-002`).
3. **No separation of duties on manual query closure:** An `open` manual query could be closed directly without an answer, and a dual-hatted user could answer and close their own manual query (`PRD-QRY-001`).
4. **Manual queries stayed open when the queried observation changed:** Saving a corrected value via `RecordCollectedValues` or `CorrectObservation` updated `value_changed` on read, but did not automatically transition open manual queries on prior revisions to `answered` (`PRD-QRY-001`).
5. **Mirrored kernel assertions lost revision, human actor, and verbatim change reason:** `MirrorFormQueryToKernel` stamped `svc:execution-queries` as `AssertedBy`, substituted synthetic text for `ReasonForChange`, and resolved `TargetRevision` from the observation's latest revision rather than the revision targeted by the query step.
6. **`FormQuery` was invisible to kernel lineage, scope authorization, and activity streams:** Without `FormQueryId` and `NodeKinds.FormQuery`, tracing `form_query:{id}` or scoping study/site/enrollment activity feeds missed query lifecycle transitions.

## 2. Decision Drivers & Constraints

- **Module Isolation (`K-XS-2`):** `Cadence.Modules.Execution` references only `Cadence.Kernel.Abstractions` and `Cadence.Kernel.Modules`, never `Cadence.Kernel.Domain` or `Cadence.Kernel.Persistence`.
- **Database-Enforced Temporal & Append-Only Integrity (`K-TIME-2`, `K-TIME-3`):** Bitemporal intervals must forbid non-overlapping regressions and destructive edits via PostgreSQL `tstzrange` GiST exclusion constraints (`KT001`) and insert-only triggers (`KA001`), using injected `IClock` exclusively.
- **21 CFR Part 11 Separation of Duties & Attributable Provenance (`PRD-QRY-001`, `PRD-SYS-001`):** Manual queries must be answered before closure, cannot be closed by `LastAnsweredBy`, and must preserve both conversational notes and verbatim `X-Change-Reason` across module history, kernel assertions, and casebook exports.
- **Zero-PII Escalation Digests (`PRD-QRY-002`):** Daily study-lead aging digests may carry only de-identified study/site/subject codes, query identifiers, item references, and SLA ages—never participant identifiers or sealed vault details.

## 3. Options Considered

### Option 1: Bitemporal Module Aggregate + Outbox Auto-Answer & Escalation + Kernel Lineage/Assertion Extensions (Selected)

- **Overview:** Promote `FormQueryId` and `NodeKinds.FormQuery` (`form_query`) plus bitemporal contracts (`TimeRange`, `TemporalPeriod`, `IBitemporalEntity`) into `Cadence.Kernel.Abstractions` and reusable PostgreSQL trigger/exclusion helpers (`ModuleBitemporalSql`, `ModuleInsertOnlySql`) into `Cadence.Kernel.Modules`; refactor `FormQuery : AggregateRoot<FormQueryId>` with `execution.form_query_periods` (`KT001` + `btree_gist`), `execution.form_query_messages` (`KA001`), and `execution.form_query_aging_digests` (`KA001`, forced RLS); enforce manual separation of duties (`M.Execution.Query.SeparationOfDuties`); auto-answer open manual queries in `EvaluateEditChecksOnCollectionChanged` when `ObservationCorrected` advances the observation's revision; forward `(Revision, ActorId, ReasonForChange)` through `MirrorFormQueryToKernel` to `RaiseQuery`, `AnswerQuery`, and `RetractAssertion` (honoring explicit `AssertedBy` strictly for `ActorKind.ServiceAccount`); wire `form_query` into `KernelScopeGraph`, `KernelAuthorityStore`, `KernelEventFamilies`, and casebook exports; and update `apps/web` eCRF query forms and `QueriesSection.vue` filters.
- **Pros:**
  - ✅ Satisfies `PRD-QRY-001` and `PRD-QRY-002` end to end while preserving `K-XS-2` module isolation.
  - ✅ Provides database-enforced bitemporal history (`as_of`, `valid_at`, `known_at`), append-only messages, and PII-free aging digests under forced tenant RLS.
  - ✅ Preserves human actor attribution, target revision, and verbatim `X-Change-Reason` across module messages, kernel assertions, and regulatory casebook HTML/JSON exports.
- **Cons:**
  - ❌ Adds companion period, message, and digest tables to the `execution` schema and requires migrating any existing JSON `messages` rows forward.

### Option 2: Collapse `FormQuery` Entirely Into Kernel `Assertion` Rows

- **Overview:** Store ownership, SLA due dates, priority, escalations, and multi-turn query threads directly on `kernel.assertions` and remove `execution.form_queries`.
- **Pros:**
  - ✅ Eliminates outbox mirroring between `execution.form_queries` and `kernel.assertions`.
- **Cons:**
  - ❌ Violates Clinical Kernel Specification §8.2 and §11, which keep `Assertion` as an atomic, immutable epistemic statement about an `Observation` while placing operational EDC query routing, ownership queues, and SLA escalation in the Execution module.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Keeping operational query workflow state in `Cadence.Modules.Execution` as a bitemporal `FormQueryId` aggregate while mirroring revision-bound epistemic assertions with preserved human provenance into the kernel honors both Clinical Kernel §8.2/§11 and `K-XS-2` without leaking EDC operational queues into `kernel.assertions`.

## 5. Consequences & Trade-offs

- **Positive Impact:** Monitors, data managers, and site coordinators get full query ownership (`site`, `monitor`, `data_manager`), origin (`manual`, `system`), priority, 14-day SLA due dates, automated overdue escalation with PII-free study-lead digests, automatic answering when queried values are corrected, separation-of-duties protection against self-closure, bitemporal `as_of` inspection, backward lineage tracing from `form_query:{id}`, and separate conversational notes vs. `X-Change-Reason` in the web client.
- **Negative Impact / Technical Debt:** `MirrorFormQueryToKernel` remains an asynchronous outbox projection, so `kernel.assertions` reflects query state after outbox relay rather than in the same database transaction.
- **Mitigation Strategy:** Deterministic `AssertionId`s chosen by `FormQuery` make outbox replay idempotent, and `FormQueryEndpoints` serves real-time query counts and states directly from `execution.form_queries`.

## 6. Implementation & Verification

- **Affected Packages / Services:** `packages/kernel`, `apps/execution/module`, `apps/web`, `docs/schema`, `docs/openapi`, `docs/api`.
- **Verification Plan:** Verified by `ExecutionFormQueryTests`, `ExecutionFormQueryPostgresTests`, `ExecutionFormQueryHostPostgresTests`, `ExecutionOutboxEditChecksPostgresTests`, `RouteAuthorizationTests`, `Cadence.ArchitectureTests`, and `apps/web` Vitest suites (`ecrfQueriesApi.test.js`, `useFormQueries.spec.ts`, `EcrfFormPanel.spec.ts`, `StudyCanvasQueries.spec.js`, `StudyCanvasRecord.spec.js`).
