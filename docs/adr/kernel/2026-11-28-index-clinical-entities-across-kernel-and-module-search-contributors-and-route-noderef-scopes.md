# ADR-KERNEL-20261128-index-clinical-entities-across-kernel-and-module-search-contributors-and-route-noderef-scopes: Index clinical entities across kernel and module search contributors and route NodeRef scopes

- **Status:** Accepted
- **Date:** 2026-11-28
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

ADR-2484 established tenant-scoped PostgreSQL full-text search (`to_tsvector('simple', ...)` with GIN expression indexes) directly over kernel source tables for eight initial reference and study `NodeKinds` (`organization`, `site`, `study`, `concept`, `code_system`, `code_system_version`, `standard_version`, `library_item`). As clinical execution, validation, governance, and proposals landed in `kernel` and `execution` schemas, operational entities (`study_site`, `enrollment`, `protocol_version`, `collection_instance`, `observation`, `form_query`, `validation_finding`, `qualification`, `artifact`, `proposal`) remained unindexed in `IKernelSearch`, `SearchKernelTool`, MSW demo handlers, and the global command palette (`CanvasPalette.vue`). In addition, `CanvasPalette.vue` restricted `/api/v1/kernel/search` calls to `kinds: ['study']`, synthesized broken `Open study` links for clinical queries, and did not resolve `NodeRef` zoom paths to synchronize `clinicalScope` and `etmf` site/participant stores across intra-study and cross-study jumps (PRD-CRF-013, K-AUTH-4, K-IDN-1).

## 2. Decision Drivers & Constraints

- **Source-table search under RLS (ADR-2484, PRD-CRF-013):** Search queries source tables directly through tenant-scoped DbContexts with GIN expression indexes rather than maintaining a duplicate search projection table.
- **Module schema isolation (K-XS-1, K-XS-2):** `Cadence.Kernel` cannot query `execution.form_queries` directly, and `Cadence.Modules.Execution` references only `Cadence.Kernel.Abstractions` and `Cadence.Kernel.Modules`.
- **Non-confirming visibility & treatment blinding (K-AUTH-4, K-IDN-1):** `IdentityVault` and `people` rows remain excluded from search; restricted sites, blinded observations, blinded form queries, and blinded or target-restricted validation findings must return zero hits without confirming existence.
- **Cold-boot bundle & synchronous scope coordination (`apps/web`):** `palette.ts` is imported on cold boot (`AppShell.vue`) and must stay within the 250 kB raw JS cold-boot budget while synchronizing `clinicalScope` and `etmf` stores cleanly across `/studies`, `/ctms`, and `/ecrf`.

## 3. Options Considered

### Option 1: Extend `KernelSearchStore` to 17 kernel kinds, add `IKernelSearchContributor` for module schemas, and route `NodeRef` zoom paths in `palette.ts` (Selected)

- **Overview:** Add GIN-backed `KernelSearchSource` definitions and migration `ClinicalEntitySearchIndexes` for the nine remaining kernel `NodeKinds`, introduce `IKernelSearchContributor` in `Cadence.Kernel.Abstractions.Search` implemented by `ExecutionSearchContributor` (`FormQuerySearchIndexes`) in `Cadence.Modules.Execution`, filter `validation_finding` hits through `IsFindingVisibleAsync` in both `KernelReadEndpoints` and `SearchKernelTool`, and expand `CanvasPalette.vue` + `palette.ts` to query all clinical kinds with category chips and lazy-loaded `getZoomPath` scope routing.
- **Pros:**
  - ✅ Preserves ADR-2484 zero-copy RLS isolation and `EXPLAIN`-verified GIN index plans across all 18 `NodeKinds` (PRD-CRF-013).
  - ✅ Keeps `K-XS-1`/`K-XS-2` architectural boundaries intact while unifying `form_query` into `/api/v1/kernel/search` and `SearchKernelTool`.
  - ✅ Enforces `INodeVisibility` and `K-AUTH-4` blinding identically across HTTP and MCP tool surfaces.
  - ✅ Routes participant hits directly to casebooks (`?stage=conduct&site=<site>&participant=<ref>`) and synchronizes `clinicalScope` and `etmf` stores after cross-route navigation.
- **Cons:**
  - ❌ Adds GIN expression indexes across kernel and execution tables and fans out contributor queries in `KernelSearch`.

### Option 2: Dedicated cross-schema search projection table

- **Overview:** Project searchable documents from `kernel` and `execution` domain events into a shared `search_documents` table.
- **Pros:**
  - ✅ Single-table `tsvector` scan at read time.
- **Cons:**
  - ❌ Duplicates identifiers outside source tables, lags behind writes, and complicates crypto-shredding, bitemporal supersession, and `K-AUTH-4` blinding enforcement (rejected in ADR-2484).

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Extending source-table GIN search across the 17 kernel kinds and introducing `IKernelSearchContributor` for `execution.form_queries` delivers immediate post-write searchability across all 18 clinical `NodeKinds` under PostgreSQL Row-Level Security without violating module boundaries or leaking blinded or unentitled records.

## 5. Consequences & Trade-offs

- **Positive Impact:** Clinicians, monitors, data managers, and bounded AI tools can locate study sites, enrollments, protocol versions, forms, observations, form queries, validation findings, qualifications, artifacts, and proposals from one unified search contract and jump directly to scoped workspaces with synchronized `clinicalScope` and `etmf` state.
- **Negative Impact / Technical Debt:** Each new searchable source requires a matching GIN expression index migration.
- **Mitigation Strategy:** `KernelSearchPostgresTests` and `ExecutionFormQueryHostPostgresTests` run `EXPLAIN` with `SET enable_seqscan = off` against every kernel and module search expression so unindexed sources fail CI immediately.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/kernel` (`Cadence.Kernel.Abstractions`, `Cadence.Kernel.Application`, `Cadence.Kernel.Persistence`, `Cadence.Kernel.Api`), `apps/execution/module` (`Cadence.Modules.Execution`), `apps/intelligence` (`Cadence.Modules.Intelligence`), and `apps/web`.
- **Verification Plan:** Verified via `KernelSearchTests`, `AgentToolTests`, `KernelLayeringTests`, PostgreSQL `KernelSearchPostgresTests` and `ExecutionFormQueryHostPostgresTests` (`EXPLAIN` GIN index plans + RLS + `K-AUTH-4` blinding), Vitest (`CommandPaletteOverlay.spec.js`, `clinical_context_bar_route_scope.spec.ts`, `cold_boot_bundle.spec.ts`), and Playwright E2E accessibility and routing tests (`web-accessibility.spec.ts`).
