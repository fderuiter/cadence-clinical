# ADR-2833: Hydrate Validation Finding Clinical Context and Persist FormQuery Rule-Pack Provenance

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** K-FIND-1, K-RULE-3, K-AUTH-4, K-XS-2, PX-20, PRD-EDC-003, ICH E6(R3) 4.2, 21 CFR 11.10(e)
- **Extends:** [ADR-2536](2026-11-22-unify-18-module-migrations-and-strip-schema-doc-counts.md), [ADR-2606](2026-11-22-clinical-canvas-casebook-unification-and-protocol-governance-endpoints.md), [ADR-2651](2026-11-22-shared-clinical-context-bar-and-url-scope-codec.md)

---

## 1. Context & Problem Statement

Validation findings (`ValidationFinding`) and auto-raised eCRF edit-check queries (`FormQuery`) previously had disconnected provenance and incomplete clinical scope hydration:

1. `KernelValidationResponses` serialized `TargetKind` as an untyped `string` while `ValidationSection.vue` checked for `"Record"` instead of `FindingTargetKind.DatasetRecord`, silently breaking deep links from dataset-record findings to eCRF collections.
2. `FindingTarget.Node` returned `null` for `DatasetRecord` targets referencing a `CollectionInstance` UUID, `CollectionValidationService` left `RecordValidationRun.Inputs` empty instead of recording versioned `CollectionInstance` and `Observation` `NodeRef`s (`LineageEdgeKind.ValidatedBy`), and `KernelValidationEndpoints` omitted hydrated clinical context (`Site`, `Participant`, `Visit`, `eCRF Form`, `Observation`, and linked `FormQuery`).
3. `FormQuery` in `Cadence.Modules.Execution` did not persist the originating `ValidationFindingId` or `RulePackRef` (`RulePackId`, `PackKey`, `RuleId`, `RuleVersion`), nor did `RaiseFormQueryHandler` or `MirrorFormQueryToKernel` record `DerivedFrom` (`validation_finding:<uuid>`) and `DefinedBy` (`library_item:<uuid>`) lineage edges so kernel read endpoints could resolve linked queries without violating module isolation (`K-XS-2`).

## 2. Decision Drivers & Constraints

- **Deterministic finding identity (`K-FIND-1`):** `FindingIdentity` hash inputs (`Study`, `Pack.Item`, `RuleId`, `TargetKind`, `TargetKey`) must remain unchanged so existing open findings reconcile across re-runs without duplication.
- **Module boundary isolation (`K-XS-2`):** `Cadence.Kernel.Api` must never reference `Cadence.Modules.Execution`; linked `FormQuery` context on kernel validation responses must be resolved strictly through kernel abstractions (`IKernelNavigator` and `IKernelLineage`).
- **Blinding and node-visibility enforcement (`K-AUTH-4`, `PX-20`):** Every hydrated `NodeRef` and label (`Site`, `Participant`, `Visit`, `eCRF Form`, `Observation`, `RulePack`, `FormQuery`) must pass `INodeVisibility.FilterAsync`; masked observations suppress observation-scoped findings per `K-AUTH-4`, and partially masked context nodes emit `"masked"` placeholders without leaking hidden references or treatment arms.
- **Post-commit validation (`K-RULE-3`) & additive schema evolution (`ADR-2536`):** Collection validation remains post-commit with deterministic versioned `NodeRef` inputs, and `FormQuery` provenance columns must be nullable so manual site/monitor queries require zero backfill.
- **Clinical Canvas scope preservation (`ADR-2651`):** Navigating between `ValidationSection.vue`, `QueriesSection.vue`, `QueryThreadCard.vue`, and `QueryContextLinks.vue` must preserve `Study`, `Site`, and `Participant` URL scope via shared `<EntityLink>` and `<NodeRefChip>` components with zero raw UUIDs.

## 3. Options Considered

### Option 1: Read-time kernel hydration via `IKernelNavigator` + `IKernelLineage` and nullable `FormQuery` provenance columns (Selected)

- **Overview:**
  1. Expose `FindingTargetKind` (`DatasetRecord`, `Observation`, `DesignElement`) as a strongly-typed enum schema on `KernelValidationFindingResponse` and regenerate `apps/web/src/api/generated/kernel.ts`.
  2. Resolve `FindingTarget.Node` to `collection_instance:<uuid>` for `DatasetRecord` targets whose key parses as a `CollectionInstance` UUID without changing `FindingIdentity` hash inputs, and populate `ValidationFindingView.Explain()` with `targetEvidence` and `NextAction.Target`.
  3. Populate deterministic versioned `CollectionInstance` and `Observation` `NodeRef`s on `RecordValidationRun.Inputs` in `CollectionValidationService` so `RecordValidationRunHandler` writes `LineageEdgeKind.ValidatedBy` edges.
  4. Persist nullable `ValidationFindingId`, `RulePackId`, `PackKey`, `RuleId`, and `RuleVersion` on `execution.form_queries` (with an index on `(TenantId, ValidationFindingId)`), record `DerivedFrom` (`validation_finding:<uuid>`) and `DefinedBy` (`library_item:<uuid>`) lineage edges in `RaiseFormQueryHandler`, and forward provenance through `MirrorFormQueryToKernel` / `RaiseQuery`.
  5. Enrich `KernelValidationFindingResponse` and `KernelRulePackPreviewViolationResponse` at read time in `KernelValidationEndpoints` using `IKernelCollectionReader`, `IKernelNavigator`, `IKernelLineage`, and `INodeVisibility`, and wire scope-preserving `<EntityLink>` and `<NodeRefChip>` links across web validation and query surfaces.
- **Pros:**
  - ✅ Preserves `K-FIND-1` deterministic finding hashes and `K-XS-2` strict kernel/module decoupling.
  - ✅ Enforces `INodeVisibility` and RTSM blinding uniformly across hydrated findings, `Explain()` provenance, and rule-pack preview violations.
  - ✅ Additive nullable migration on `execution.form_queries` keeps manual query creation backward-compatible while giving auto-raised edit-check queries full rule-pack and finding traceability.
- **Cons:**
  - ❌ Requires batched navigator/lineage lookups in `KernelValidationEndpoints` during finding list and preview responses.

### Option 2: Denormalize site, participant, visit, and query labels onto `kernel.validation_findings` at write time

- **Overview:** Store clinical labels and `FormQuery` references directly on kernel validation finding rows when rules execute or queries open.
- **Pros:**
  - ✅ Avoids read-time navigation lookups when listing findings.
- **Cons:**
  - ❌ Couples kernel validation persistence to execution query lifecycles (`K-XS-2` violation), risks stale labels after amendments, and complicates caller-specific `INodeVisibility` / RTSM blinding checks (`K-AUTH-4`).

### Option 3: Hydrate clinical context exclusively in the Vue SPA via N+1 client fetches

- **Overview:** Keep kernel validation responses minimal and resolve collection instances, enrollments, visits, and queries from `ValidationSection.vue`.
- **Pros:**
  - ✅ Minimal backend response changes.
- **Cons:**
  - ❌ Introduces N+1 client round-trips, leaves `Explain()` and API consumers with raw target keys, and duplicates visibility/scope resolution in the browser.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
- **Justification:** Option 1 closes the bidirectional provenance loop between `CollectionInstance` / `Observation`, `ValidationFinding`, `LibraryItem` (`RulePack`), and `FormQuery` while respecting `K-FIND-1`, `K-RULE-3`, `K-AUTH-4`, `K-XS-2`, and `ADR-2651` URL scope semantics.

## 5. Consequences & Trade-offs

- **Positive Impact:** Compile-time `FindingTargetKind` safety eliminates `"Record"` vs `"DatasetRecord"` drift; validation findings, preview violations, and `FormQuery` cards render human-readable, scope-preserving deep links across `Site`, `Participant`, `Visit`, `eCRF Form`, `Observation`, `Rule Finding`, `RulePack`, and `Query` with zero raw UUIDs.
- **Negative Impact / Technical Debt:** `KernelValidationEndpoints` performs batched read-time enrichment across `IKernelCollectionReader`, `IKernelNavigator`, `IKernelLineage`, and `INodeVisibility`.
- **Mitigation Strategy:** Candidate `NodeRef`s are deduplicated and filtered in a single batch via `INodeVisibility.FilterAsync` per request, backed by unit, blinding-regression, and Vitest component tests.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/kernel` (`Cadence.Kernel.Abstractions`, `Cadence.Kernel.Application`, `Cadence.Kernel.Persistence`, `Cadence.Kernel.Api`, `Cadence.Rules`), `apps/execution` (`Domain/FormQuery`, `Application/FormQuery*`, `Application/EvaluateEditChecksOnCollectionChanged`, `Application/MirrorFormQueryToKernel`, `Endpoints/FormQuery*`, `Infrastructure/ExecutionModuleDbContext` + migration `20261123201942_AddFormQueryValidationFindingProvenance`), `apps/web` (`EntityLink.vue`, `ValidationSection.vue`, `QueriesSection.vue`, `QueryThreadCard.vue`, `QueryContextLinks.vue`, `StudyCanvasView.vue`, `demoStore.js`, `demoHandlers.js`).
- **Verification Plan:**
  - `ValidationFindingTests`, `KernelValidationHydrationTests`, and `TreatmentBlindingHostRegressionTests` verify `FindingIdentity` stability (`K-FIND-1`), `ValidatedBy` lineage inputs, hydrated clinical `NodeRef`s, `INodeVisibility` masking/suppression (`K-AUTH-4`), and zero raw UUIDs in `Explain()`.
  - `ExecutionFormQueryTests` verifies `FormQuery` provenance persistence, `DerivedFrom` / `DefinedBy` lineage edges, and `MirrorFormQueryToKernel` forwarding.
  - `ValidationAndPreview.spec.ts` and `pnpm --filter web typecheck` verify `FindingTargetKind` compile-time safety, MSW demo parity, and scope-preserving `<EntityLink>` / `<NodeRefChip>` rendering.
