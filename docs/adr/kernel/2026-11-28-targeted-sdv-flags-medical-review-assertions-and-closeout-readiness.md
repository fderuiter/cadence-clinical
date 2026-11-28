# ADR-KERNEL-20261128-targeted-sdv-flags-medical-review-assertions-and-closeout-readiness: Targeted SDV Flags, Medical Review Assertions, and Closeout Readiness

- **Status:** Accepted
- **Date:** 2026-11-28
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-QRY-005 (source data verification preconditions and open-query blocking), PRD-QRY-006 (revision-bound verification and review assertions), PRD-SYS-001 (21 CFR Part 11 complete observation and assertion audit trail), K-FACT-1, K-TIME-1, K-ID-1, K-AUTH-4

---

## 1. Context & Problem Statement

Risk-Based Quality Management (ICH E6(R3)) distinguishes targeted source data verification (SDV) by clinical monitors from medical source data review (SDR) by medical monitors, while cross-functional database closeout requires site, monitoring, medical, and data management roles to inspect and clear readiness blockers prior to applying signed `Frozen` or `Locked` records. Previously, `LockReadinessEvaluator` and `KernelWorkQueues` treated every active observation as requiring 100% SDV, had no append-only medical review assertion (`AssertionKind.Reviewed`), did not exclude open-queried observations from SDV/SDR work queues or bulk SDV selection, and restricted closeout readiness inspection behind `sponsor_dm` / `lock.apply`.

## 2. Decision Drivers & Constraints

- **Driver 1:** Preserve Clinical Kernel immutability and bitemporal invariants (`K-FACT-1`, `K-TIME-1`, `K-ID-1`, `K-AUTH-4`) with zero new database tables.
- **Driver 2:** Support per-item `SdvRequired` (default `true`) and `MedicalReviewRequired` (default `false`) flags on `ItemSpec` while keeping unflagged protocols backward compatible.
- **Driver 3:** Prevent `K.Assertion.QueryOpen` failures during bulk SDV and surface actionable workspace routing across closeout readiness blockers while keeping signed lock mutations gated on `lock.apply` / `lock.release` and 60-second `X-Sig-Token` step-up tokens.

## 3. Options Considered

### Option 1: Extend `ItemSpec` and `AssertionKind` with Targeted SDV/SDR and Role-Scoped Readiness (Selected)

- **Overview:** Add `SdvRequired` and `MedicalReviewRequired` to `ItemSpec`, add `AssertionKind.Reviewed` with `POST /api/v1/kernel/observations/{observationRef}/reviews` (`observation:review` granted to `sysadmin`, `sponsor_mm`, `sponsor_dm`), emit `LockBlockerKind.UnreviewedValue` with rich blocker context (`form_ref`, `enrollment_ref`, `site_code`, `subject_code`, `collection_title`, `item_label`), filter open-queried items out of SDV/SDR queues and bulk SDV selection, and expose read-only closeout readiness to monitoring, medical, and site roles while retaining step-up signature enforcement on lock mutations.
- **Pros:**
  - Reuses the append-only `Assertions` ledger targeting `(ObservationId, Revision)` without schema migrations, invalidating stale verifications and reviews automatically when an observation supersedes to `Revision + 1`.
  - Aligns `LockReadinessEvaluator`, `KernelWorkQueues`, eCRF item projections, and Clinical Canvas closeout navigation on a single protocol-driven definition of required SDV and SDR.
  - Preserves `K-AUTH-4` blinding (`masked` placeholders) and `X-Sig-Token` step-up enforcement on `lock.apply` and `lock.release`.
- **Cons:**
  - Requires keeping OpenAPI contracts, generated TypeScript clients, permission matrix baselines, and stateful MSW demo parity synchronized.

### Option 2: Separate Monitoring Plan Tables and Dedicated Review Service

- **Overview:** Introduce standalone monitoring plan tables and a separate medical review persistence model outside `ProtocolVersion` and `Assertions`.
- **Pros:**
  - Decouples monitoring schedules from protocol version snapshots.
- **Cons:**
  - Violates the single-ledger `K-FACT-1` assertion model, duplicates revision-binding and retraction logic, and introduces unnecessary schema and operational complexity.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Extending `ItemSpec`, `AssertionKind.Reviewed`, `LockReadinessEvaluator`, and `PermissionMatrix` (`observation:review`) satisfies targeted SDV, medical review, open-query exclusion, and cross-functional closeout readiness within existing Clinical Kernel primitives and zero database schema additions.

## 5. Consequences & Trade-offs

- **Positive Impact:** Targeted SDV (`SdvRequired: false`) eliminates false `UnverifiedValue` closeout blockers, medical monitors gain revision-bound `Reviewed` assertions and `medical_reviews` work queues, bulk SDV skips open-queried items cleanly, and CRAs/investigators/coordinators can inspect and clear closeout blockers directly from the Locks workspace.
- **Negative Impact / Technical Debt:** Protocol-level SDV/SDR flags remain versioned with `ProtocolVersion` rather than dynamically overridden per site; standalone dynamic SDV sampling plans remain future work.
- **Mitigation Strategy:** Default `SdvRequired = true` and `MedicalReviewRequired = false` preserve existing protocol behavior, enforced by unit, architecture, and frontend component tests.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `Cadence.Kernel.Abstractions`, `Cadence.Kernel.Domain`, `Cadence.Kernel.Application`, `Cadence.Kernel.Persistence`, `Cadence.Kernel.Api`, `Cadence.Execution`, `Cadence.Security`, `apps/kernel-api`, `apps/web`, `packages/demo-fixtures`, `docs/openapi`, `docs/api`.
- **Verification Plan:** Validated via `LockReadinessEvaluatorTests`, `AssertionReviewAndWorkQueueTests`, `Cadence.ArchitectureTests` (`PermissionMatrix`), `TargetedSdvReviewAndCloseout.spec.ts`, `EcrfFormPanel.spec.ts`, and `./bin/cadence check` quality sentinels.
