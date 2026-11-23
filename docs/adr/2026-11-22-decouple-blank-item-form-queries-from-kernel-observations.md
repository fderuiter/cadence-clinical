# ADR-2824: Decouple blank-item form queries from kernel observations

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001, K-SCOPE-1, 21 CFR 11.10(e) and 11.70
- **Amends:** extends ADR-2600, ADR-2620, ADR-2621, and ADR-2622

---

## 1. Context & Problem Statement

Raising a manual or automated edit-check query against an unrecorded item on an eCRF form (`CollectionInstance`) previously dispatched two separate commands: `RecordUnaskedCollectionItem` in `Cadence.Kernel` to create a synthetic `NullFlavor.NotAsked` `Observation`, followed by `RaiseFormQuery` in `Cadence.Modules.Execution` targeting that observation.

That two-command path caused three GxP and architectural defects:

1. **Non-atomic `422` orphans (ADR-2620, PRD-SYS-001):** `FormQueryEndpoints.RaiseAsync` committed `RecordUnaskedCollectionItem` before `RaiseFormQueryValidator` checked query text, leaving orphan `NotAsked` observations in bitemporal exports when `RaiseFormQuery` returned `422`.
2. **Signature superseding under `Frozen` locks (§10.5, ADR-2622, 21 CFR 11.70):** Joining a synthetic `NotAsked` observation to a `CollectionInstance` changed `CollectionInstance.ContentHashOf`, superseding investigator Part 11 signatures and mutating `Frozen` forms during review or study-wide automated edit checks.
3. **EDC workflow leakage into kernel facts (K-SCOPE-1):** Missing-data queries belong to EDC query workflow in `Cadence.Modules.Execution`, not synthetic clinical observations in `Cadence.Kernel`.

Additionally, `StudyCanvasView` bound `CanvasInspector` to Execution `participant:<id>` identifiers instead of canonical kernel `enrollment:<id>`, `collection_instance:<id>`, and `observation:<id>` references, while `KernelScopeGraph`, `KernelAuthorityStore`, and `KernelCommandCatalog` lacked summaries, scope paths, and offered commands on `CollectionInstance` and `Observation` nodes.

## 2. Decision Drivers & Constraints

- **K-SCOPE-1 and ADR-2622:** EDC query workflow belongs to `Cadence.Modules.Execution`; kernel `Observation` aggregates record clinical facts and reasoned missingness entered by site staff.
- **ADR-2600 and ADR-2620:** Every module write executes atomically in a single command transaction after pre-handler FluentValidation and shared scope lock checks.
- **Spec §10.5 and ADR-2621:** `Frozen` denies observation writes while permitting query review without superseding Part 11 signatures; `Locked` denies both observation writes and query transitions (`423 K.Assertion.Locked`).
- **Clinical Canvas UX-3 and UX-5:** Every inspected participant, form, and observation binds its canonical `NodeRef` across `Trace`, `Impact`, `Explain`, and `Act`.

## 3. Options Considered

### Option 1: Store blank-item `FormQuery` rows atomically in `Cadence.Modules.Execution` with nullable `ObservationId` and retire `RecordUnaskedCollectionItem` (Selected)

- **Overview:** Retire `RecordUnaskedCollectionItem` and `collection.record_unasked`. Make `FormQuery.ObservationId` and `FormQuery.KernelQueryId` nullable (`Revision = 0` when blank), validate `ItemId` and `Text` in `RaiseFormQueryValidator`, acquire widest-first shared scope locks in `RaiseFormQueryHandler`, and compute `CurrentRevision` and `ValueChanged` by `ItemId`. Expose `CollectionInstance` and `Observation` summaries, scope paths, and `OfferedOn` commands in `Cadence.Kernel`, and bind `enrollment`, `collection_instance`, and `observation` references in `StudyCanvasView`.
- **Pros:**
  - Satisfies PRD-SYS-001, K-SCOPE-1, ADR-2600, ADR-2620, ADR-2621, and ADR-2622 in one atomic transaction.
  - Preserves `CollectionInstance.ContentHashOf`, Part 11 signatures, and clean bitemporal exports on `Frozen` forms and rejected `422` requests.
  - Preserves kernel `Queried` and `QueryAnswered` assertion mirroring for queries raised on recorded observations.
- **Cons:**
  - Requires an EF Core migration on `execution.form_queries` and nullable `observation_id` support in OpenAPI and web clients.

### Option 2: Keep `RecordUnaskedCollectionItem` and wrap both commands in a host coordinator

- **Overview:** Keep synthetic `NotAsked` observations in `Cadence.Kernel`, pre-validate query text in the endpoint, and exclude `NotAsked` from `ContentHashOf`.
- **Pros:**
  - Keeps `FormQuery.ObservationId` non-nullable.
- **Cons:**
  - Continues violating K-SCOPE-1 and §10.5 by writing synthetic kernel observations during `Frozen` review and automated edit checks, and leaks `NotAsked` rows into bitemporal exports.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Retiring `RecordUnaskedCollectionItem` eliminates synthetic kernel observations at the root cause, keeps blank-item queries atomic inside `Cadence.Modules.Execution`, preserves Part 11 form signatures and `Frozen`/`Locked` semantics, and aligns Clinical Canvas Inspector verbs with canonical kernel `NodeRef` targets.

## 5. Consequences & Trade-offs

- **Positive Impact:** Raising or transitioning blank-item queries never mutates `CollectionInstance` membership, `ContentHashOf`, or bitemporal observation exports; concurrent saves before a blank-item query commits return `409 M.Execution.Query.RevisionStale`; subsequent observation-level `Locked` locks on a newly recorded value for `ItemId` are honored by `FormQueryScopes.StepAsync` (`423 K.Assertion.Locked`).
- **Negative Impact / Technical Debt:** `FormQueryResponse.observation_id` and `current_revision` are nullable while the target item remains blank.
- **Mitigation Strategy:** OpenAPI schemas, generated TypeScript types (`kernel.ts`), eCRF query components, and demo MSW handlers explicitly type and render nullable `observation_id` and zero `revision`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `Cadence.Kernel.Abstractions`, `Cadence.Kernel.Application`, `Cadence.Kernel.Persistence`, `Cadence.Modules.Execution`, `Cadence.Kernel.Host`, `packages/demo-fixtures`, and `apps/web`.
- **Verification Plan:** Unit tests (`ExecutionFormQueryTests`, `KernelAuthorizationTests`), architecture boundary tests (`KernelBoundaryRules`), PostgreSQL integration suites (`ExecutionFormQueryPostgresTests`, `ExecutionFormQueryHostPostgresTests`, `ExecutionOutboxEditChecksPostgresTests`, `KernelNavigationPostgresTests`, `KernelAuthorityPostgresTests`, `KernelBatchVisibilityPostgresTests`, `KernelGrantVisibilityPostgresTests`), and Vitest suites (`StudyCanvasRecord.spec.js`, `StudyCanvasKernelVerbs.spec.js`, `EcrfFormPanel.spec.ts`).
