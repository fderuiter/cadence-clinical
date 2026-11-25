# ADR-2465: Materialize Validation Finding States in a Kernel Projection

- **Status:** Accepted
- **Date:** 2026-11-25
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-004 (declarative rules and multi-layer edit checks), PRD-QRY-003 (cross-form edit check execution), PRD-CRF-013 (tenant data isolation), PRD-SYS-001 (clinical kernel performance and auditability)

---

## 1. Context & Problem Statement

[ADR-2462](2026-09-30-validation-findings-derived-from-immutable-runs-and-attested-decisions.md) (K022) established immutable `ValidationRun` and `ValidationDetection` truth tables alongside small `ValidationFinding` decision records, deriving each finding's lifecycle status (`Open`, `Investigating`, `Resolved`, `Accepted`, `Explained`) on read through `FindingLedger`. ADR-2462 §5 explicitly anticipated that as run history accumulated—especially once [ADR-2624](2026-11-21-outbox-collection-edit-checks-and-pre-commit-preview.md) triggered asynchronous `CollectionValidationService.ValidateStudyCollectionsAsync` runs after every committed eCRF save—a disposable materialized projection could be added behind `IValidationFindings` without altering the truth tables (`K-MOD-2`).

Under high-frequency collection evaluation, four read-amplification bottlenecks emerged:

1. **Historical Run Replay (`O(R × D)`):** `KernelValidationStore.LoadStudyAsync` replayed every `validation_runs` header, covered dataset, pack reference, light `validation_detections` row, and `validation_findings` decision across the study on every `FindAsync`, `ListAsync`, `LoadActiveErrorFindingTargetsAsync`, and `LoadActiveErrorFindingsAsync` call. At 500 runs × 200 detections (100,000 historical detection rows), a single Study Home load read 446,538 rows and allocated >320 MB.
2. **Unbounded Work-Queue, Scope, and Readiness Scans:** `KernelWorkQueues`, `KernelAuthorityStore`, and `KernelScopeGraph` scanned historical `validation_detections` rows to discover distinct findings, and `EvidenceReadiness` loaded study-wide findings instead of filtering to the specific collection instance and observation targets being signed.
3. **Pre-Run Full Finding Hydration on Form Save:** `CollectionValidationService.ValidateStudyCollectionsAsync` called `IValidationFindings.ListAsync` (hydrating full finding views and explanations) solely to preserve dataset coverage from prior collection findings before recording the next run.
4. **Duplicate Study Home Attention Requests:** `NeedsAttention.vue` issued two parallel `listStudyValidationFindings` requests (`status=Open` and `status=Investigating`) on fallback Study Home loads because the HTTP endpoint accepted only a single `status` value.

## 2. Decision Drivers & Constraints

- **K-TX-1 (Single-Aggregate Command Audit):** `RecordValidationRun` and the five finding decision commands (`InvestigateFinding`, `ClassifyFinding`, `AcceptFinding`, `ExplainFinding`, `ReopenFinding`) must continue to record exactly one aggregate version and one audit entry per command without per-finding sagas or event floods.
- **K-MOD-2 & KA001 (Disposable Read Projection over Insert-Only Truth):** `validation_runs` and `validation_detections` remain insert-only (`KA001`), and `validation_findings` remains the authoritative decision aggregate (`KS001`). The projection must be deterministically rebuildable from those truth tables at any time.
- **K-FIND-1 (Exact `FindingLedger` Parity):** Incremental projection transitions on covering runs and attested decisions—including out-of-order or equal-timestamp `recorded_at` tie-breaks (`(recorded_at, run_id)`) and decision invalidation when a resolved finding recurs—must match `FindingLedger.Derive` identically.
- **K-ID-1, K-ID-2 & K-AUTH-4 (Tenant Isolation and Blinding):** The projection table must enforce composite `(tenant_id, ...)` foreign keys, forced PostgreSQL Row-Level Security (`TenantIsolationSql.EnableTenantIsolation`), grant-scoped visibility (`INodeVisibility`), and treatment-blinding redaction (`IBlindingPolicy`).

## 3. Options Considered

### Option 1: Transactional Non-Aggregate `kernel.validation_finding_projections` Table + Lightweight Dataset Coverage Read + Multi-Status HTTP Query (Selected)

- **Overview:**
  - Introduce `kernel.validation_finding_projections` keyed by `(tenant_id, finding_id)` as a non-aggregate EF Core entity (`ValidationFindingProjectionRecord`) holding the finding's identity, latest detecting run/detection metadata, derived `status`, `is_blocking` flag, denormalized target `target_key`, covering/resolving run pointers, decision snapshot, and `projection_version` concurrency token.
  - Extend `FindingLedger` with pure `Project`, `ApplyCoveringRun`, and `ApplyDecision` functions, and add `IValidationProjectionStaging` on `KernelCommandTransaction` so `RecordValidationRunHandler` and `FindingDecisionHandler` stage projection upserts inside the same `SaveChangesAsync` transaction while `KernelCommandTransaction` audits only the single command aggregate (`K-TX-1`). When an incoming run sorts earlier than a projection's `last_covering_run` (`(recorded_at, run_id)`), recompute only the affected study/stage findings from truth tables inside the transaction.
  - Serve `KernelValidationStore` (`FindAsync`, `ListAsync`, `ListDatasetsAsync`, `LoadActiveErrorFindingTargetsAsync`, `LoadActiveErrorFindingsAsync`), `KernelWorkQueues`, `KernelAuthorityStore`, `KernelScopeGraph`, and target-scoped `EvidenceReadiness` directly from indexed `validation_finding_projections` rows, joining `validation_detections` on `(tenant_id, last_detection_id)` only for the returned page.
  - Add `RebuildValidationFindingProjections` (`validation_finding.rebuild_projection`), `IValidationFindingProjector`, and `POST /api/v1/kernel/studies/{studyRef}/validation/findings/rebuild` for deterministic operator recovery, plus SQL backfill in the EF Core migration.
  - Replace `CollectionValidationService`'s pre-run `ListAsync` with `IValidationFindings.ListDatasetsAsync`, extend `GET /api/v1/kernel/studies/{studyRef}/validation/findings` to accept repeatable or comma-separated `status` values, and merge `NeedsAttention.vue`'s `Open` + `Investigating` requests into a single call.
- **Pros:**
  - Reduces read complexity from `O(R × D)` to `O(DistinctFindings)` or `O(PageSize)` with zero staleness window after command commits.
  - Preserves `K-TX-1` single-aggregate command auditing and `KA001` truth-table immutability.
  - Deterministic SQL backfill and `IValidationFindingProjector` satisfy `K-MOD-2` disposability.
- **Cons:**
  - Adds write-time upsert maintenance during `RecordValidationRun` and finding decision commits, bounded by `O(DistinctFindingsInStage)`.

### Option 2: PostgreSQL Materialized View Refreshed Out-of-Band

- **Overview:** Create a database `MATERIALIZED VIEW` over `validation_runs`, `validation_detections`, and `validation_findings`, refreshed asynchronously after runs and decisions.
- **Pros:**
  - Requires no C# write-side staging logic.
- **Cons:**
  - Introduces read-after-write staleness on eCRF signing/lock gates (`EvidenceReadiness`) and finding workflow transitions (`InvestigateFinding` -> `AcceptFinding`), and `REFRESH MATERIALIZED VIEW CONCURRENTLY` still rescans all `O(R × D)` historical detection rows on every refresh.

### Option 3: Derived Two-Phase SQL Window Queries Without a Projection Table

- **Overview:** Push `ROW_NUMBER() OVER (PARTITION BY finding_id ORDER BY recorded_at DESC, run_id DESC)` into PostgreSQL CTEs on `validation_detections` and `validation_runs`.
- **Pros:**
  - Avoids a new table and migration.
- **Cons:**
  - Still scans and sorts all `O(R × D)` historical detection rows per study/stage on every Study Home load, work-queue evaluation, and form save.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Maintaining `kernel.validation_finding_projections` transactionally inside `KernelCommandTransaction` via `IValidationProjectionStaging` preserves every kernel invariant (`K-TX-1`, `K-MOD-2`, `K-FIND-1`, `K-ID-1`, `K-ID-2`, `K-AUTH-4`, `KA001`) while bounding reads to current finding state and immediately reflecting run completions and attested workflow decisions on readiness gates, work queues, and Study Home.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Release benchmarks on PostgreSQL 18 (`.NET 10.0 -c Release`, 200 forms, 25 / 100 / 500 runs × 200 detections = up to 100,000 historical detection rows) confirm flat `O(DistinctFindings)` / `O(PageSize)` scaling independent of run count `R`:

    | Operation (at `R = 500`, 100,000 detections)        | Total Rows Read | Detection Rows Read | Latency (ms) | Allocated (MB) |
    | --------------------------------------------------- | --------------: | ------------------: | -----------: | -------------: |
    | `ListAsync` (`Open+Investigating`, `Take = 20`)     |             165 |                  20 |        29.91 |           0.59 |
    | Study Home (`Attention` + `WorkQueues`, 200 active) |           2,808 |                 200 |       115.90 |          10.25 |
    | Study Home (`20 active / 180 resolved`)             |           1,188 |                  20 |        94.81 |           7.23 |
    | Form Signing Gate (`EvidenceReadiness`, 1 active)   |             221 |                   1 |        54.61 |           0.65 |
    | Form Signing Gate (`EvidenceReadiness`, resolved)   |               1 |                   0 |         3.66 |           0.07 |
    | `ValidateStudyCollectionsAsync` (post-save run)     |           3,014 |                 200 |       368.41 |          24.52 |

  - Eliminates 99.4% of database rows read and 96.8% of managed allocations on Study Home at 500 runs, and cuts `NeedsAttention.vue` fallback validation HTTP calls in half.
- **Negative Impact / Technical Debt:**
  - Schema changes to `FindingLedger` derivation rules require a corresponding migration or operator execution of `RebuildValidationFindingProjections`.
- **Mitigation Strategy:**
  - Generative property tests across 250 multi-pack, multi-dataset, out-of-order, and recurring histories verify 100% parity between `FindingLedger.Derive`, `FindingLedger.Project`, `ApplyCoveringRun`, and `ApplyDecision`, and PostgreSQL integration tests verify migration backfill, RLS isolation, concurrency conflict handling, and deterministic rebuild parity.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `packages/kernel/src/Cadence.Kernel.Domain/Validation/FindingLedger.cs`
  - `packages/kernel/src/Cadence.Kernel.Abstractions/Validation/` (`IValidationFindings.cs`, `ValidationCommands.cs`)
  - `packages/kernel/src/Cadence.Kernel.Application/Validation/ValidationCommandHandlers.cs` and `Evidence/EvidenceReadiness.cs`
  - `packages/kernel/src/Cadence.Kernel.Persistence/` (`ValidationFindingProjectionRecord.cs`, `ValidationConfigurations.cs`, `KernelValidationStore.cs`, `KernelCommandTransaction.cs`, `KernelWorkQueues.cs`, `KernelAuthorityStore.cs`, `KernelScopeGraph.cs`, `20261125181126_ValidationFindingProjections.cs`)
  - `packages/kernel/src/Cadence.Rules/Evaluation/CollectionValidationService.cs`
  - `packages/kernel/src/Cadence.Kernel.Api/` (`KernelValidationEndpoints.cs`, `KernelValidationResponses.cs`)
  - `apps/web/src/components/ai/NeedsAttention.vue`, `apps/web/src/api/generated/kernel.ts`, `apps/web/src/mocks/handlers/kernelStudyAndGovernanceHandlers.js`
  - `docs/schema/`, `docs/openapi/`, `docs/api/`, `packages/kernel/README.md`
- **Verification Plan:**
  - `ValidationFindingTests` (including 250-seed generative ledger parity property tests), `CollectionValidationServiceTests`, `KernelApiHostTests`, `TreatmentBlindingHostRegressionTests`, and `KernelDbContextModelTests` in `Cadence.UnitTests`.
  - `ValidationFindingPostgresTests`, `KernelSchemaPostgresTests`, `ReadinessGatesAndWorkQueuesHostPostgresTests`, and Release benchmark `ValidationFindingProjectionBenchmarkPostgresTests` in `Cadence.IntegrationTests`.
  - `StudyHomeSingleWaveBoot.spec.ts` and full Vitest / `cadence check` governance suites.
