# ADR-2678: Retire Legacy OpenAPI Snapshot, Isolate Legacy Web Client, and Prune Uncalled Web Types

- **Status:** Accepted
- **Date:** 2026-11-24
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-053 (development environment guardrails, contract drift prevention, and build hygiene)

---

## 1. Context & Problem Statement

Following the retirement of per-service Python OpenAPI snapshots in [ADR-2816](2026-11-19-retire-orphaned-legacy-web-type-schemas.md), four residual legacy contract liabilities remained in the workspace:

1. **Orphaned 552 KB Python OpenAPI Snapshot (`legacy/cdisc_openapi.json` under `docs/openapi/`):** No build script, runtime host, test, or CLI command read `legacy/cdisc_openapi.json`, yet `OpenApiSyncService.cs`, `ApiClientValidatorService.cs`, `ApiDocsGeneratorService.cs`, `generate-api-types.js`, `.gitattributes`, `OpenApiDriftTests.cs`, `PreCommitGuardrailsTests.cs`, and `generated_types.test.js` still carried exclusions and prose treating the `legacy/` subdirectory under `docs/openapi/` as an active contract directory.
2. **Legacy `types.ts` Coupled into Shared `client.ts`:** `apps/web/src/api/client.ts` imported `type { paths } from "./types"` and exported `openApiClient = createOpenApiClient<paths>()`, forcing all 20 `.NET`-migrated API client and workspace modules that import `createOpenApiClient`, `unwrapResponse`, or `ApiError` from `client.ts` to pull the Python-era `types.ts` graph into TypeScript compilation.
3. **Unmigrated eSource Reconciliation Calls & Stale `snake_case` Shapes in `interop.ts`:** All 6 `/api/v1/interop/esource/*` endpoints (`listEhrCandidates`, `acceptEhrCandidate`, `batchAcceptEhrCandidates`, `rejectEhrCandidate`, `batchRejectEhrCandidates`, `raiseEhrClinicalQuery`) are served by `.NET` `EsourceEndpoints.cs` (`docs/openapi/interop_openapi.json` / `generated/interop.ts`), where request and response DTOs serialize in `camelCase` (`candidateId`, `candidateIds`, `commitReason`, `rejectionReason`, `queryExplanation`, `totalCount`, `actionCount`) and `ListCandidates` requires `skip` and `limit` query parameters. Leaving `interop.ts` bound to Python-era `snake_case` `types.ts` masked wire mismatches against the `.NET` service.
4. **Uncalled Operations, Unreachable Schemas, & Missing Linter/GitAttributes Parity:** `apps/web/src/api/types.ts` still carried 26 uncalled operations (including 3 `GET /api/v1/knowledge/*` routes already migrated onto `generated/knowledge.ts`), 6 eSource operations replaceable by `generated/interop.ts`, and 19 unreachable schemas, while `eslint.config.mjs` and `.gitattributes` lacked the `apps/web/src/api/types.ts` generated-file rules already configured in `apps/web/vitest.config.js` and `DuplicationDetectorService.cs`. In addition, `docs/web/API_MIGRATION.md` and `docs/openapi/README.md` duplicated hardcoded `.NET` operation totals that belong in generated `docs/api/index.md`.

## 2. Decision Drivers & Constraints

- **Supersede the `legacy/cdisc_openapi.json` Retention Clause in ADR-2816 (PRD-SYS-053):** Retire `legacy/cdisc_openapi.json` under `docs/openapi/` and its tooling exclusions now that `apps/web/src/api/types.ts` is maintained solely as a shrinking, AST-verified legacy residual, while leaving historical ADRs in `docs/adr/`, `docs/BACKLOG_INSIGHTS_ARCHIVE.md`, and committed GxP snapshots in `docs/SDLC/` untouched per [ADR-2532](2026-10-16-feature-branches-never-commit-the-gxp-snapshot.md).
- **Zero Cold-Boot & `.NET` Module Coupling:** `apps/web/src/api/client.ts` and domain-sliced `.NET` clients such as `apps/web/src/api/interop/esource.ts` must have zero imports from `./types` or `./legacyClient`, and lazy Rollup chunk boundaries enforced by `chunkSplitAndBoot.spec.ts` and `cold_boot_bundle.spec.ts` must keep `legacyClient.ts` out of cold boot.
- **Backward-Compatible eSource Runtime Contract:** Extracting `.NET` eSource methods into `apps/web/src/api/interop/esource.ts` (re-exported via `interopService` in `apps/web/src/api/interop.ts`) must preserve public method signatures, accept both `snake_case` and `camelCase` caller inputs, send default `skip=0` and `limit=50` query parameters, and normalize `.NET` `camelCase` and MSW demo envelopes so `EhrReconciliationDrawer.vue` requires zero template or layout edits.
- **Keep Ratchet Baselines Stable:** Retain `apps/web/src/api/types.ts` at its existing path so `baselines/deid/apps/web.baseline.txt` and `DuplicationDetectorService.cs` require zero baseline growth.

## 3. Options Considered

### Option 1: Retire `legacy/cdisc_openapi.json`, Isolate `legacyClient.ts`, Domain-Slice eSource onto `generated/interop.ts`, and Prune `types.ts` to Exact Reachability (Selected)

- **Overview:** Delete `legacy/cdisc_openapi.json` from `docs/openapi/` and remove `legacy/` exclusions/prose across C# tooling, `.gitattributes`, and JS type generators; extract `openApiClient` into `apps/web/src/api/legacyClient.ts` and update the 12 unmigrated callers; extract all 6 `/api/v1/interop/esource/*` calls into `apps/web/src/api/interop/esource.ts` against `createOpenApiClient<InteropPaths>()` with dual-case input mapping and response normalization re-exported through `interopService`; prune `apps/web/src/api/types.ts` from 69 paths, 101 operations, and 175 schemas to 63 paths, 69 called operations, and 156 reachable schemas; add `apps/web/src/api/types.ts` to `eslint.config.mjs` global `ignores` and `.gitattributes` `linguist-generated=true`; and replace hardcoded `.NET` operation counts in migration/OpenAPI docs with links to `docs/api/index.md`.
- **Pros:**
  - Removes 552 KB of dead OpenAPI JSON and eliminates special-case `legacy/` exclusions in `OpenApiSyncService` and `ApiClientValidatorService`.
  - Decouples all `.NET` client/workspace modules importing `client.ts` from legacy `types.ts` without altering lazy cold-boot chunk boundaries, and keeps `.NET` eSource code cleanly separated from unmigrated Sensor/DCT calls in `interop.ts`.
  - Aligns all 6 eSource reconciliation calls with the real `.NET` `interop_openapi.json` wire contract while preserving `EhrReconciliationDrawer.vue` runtime behavior across live and MSW demo modes.
  - Enforces exact zero-dead-code invariants on `types.ts` (`63` paths, `69` called operations, `156` reachable schemas) via automated Vitest AST tests.
- **Cons:**
  - `apps/web/src/api/legacyClient.ts` and `apps/web/src/api/types.ts` remain in tree until the remaining 69 Python-era operations migrate or retire under epic `#4833`.

### Option 2: Retain `legacy/cdisc_openapi.json` and Keep `openApiClient` in `client.ts`

- **Overview:** Leave the Python-era snapshot, tooling exclusions, and `client.ts` exports in place until every hidden screen in `apps/web` migrates.
- **Pros:**
  - No caller import changes across the 12 legacy API modules.
- **Cons:**
  - Keeps 552 KB of unvalidated Python-era JSON in `docs/openapi/`, continues compiling `types.ts` across `.NET`-migrated modules, leaves `interopService` eSource calls sending incompatible `snake_case` request payloads to `.NET`, and leaves 32 dead operations and 19 unreachable schemas in `types.ts`.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Superseding the `legacy/cdisc_openapi.json` retention clause in [ADR-2816](2026-11-19-retire-orphaned-legacy-web-type-schemas.md) removes an unreferenced 552 KB artifact, cleanly separates `.NET` `client.ts` and `interop/esource.ts` infrastructure from `legacyClient.ts`, fixes real `camelCase` wire serialization on `.NET` eSource endpoints, and locks `types.ts` to strictly reachable legacy operations and schemas under `PRD-SYS-053`.

## 5. Consequences & Trade-offs

- **Positive Impact:** `docs/openapi/` now holds exclusively live `.NET`-exported OpenAPI specifications; `client.ts` and `interop/esource.ts` have zero legacy type imports; `interopService` eSource reconciliation calls compile against `generated/interop.ts` and normalize `.NET` `camelCase` responses; `types.ts` drops 32 operations and 19 unreachable schemas and is marked generated in ESLint and `.gitattributes` consistently with Vitest coverage and duplication gates; and `docs/api/index.md` serves as the single source of truth for `.NET` operation counts.
- **Negative Impact / Technical Debt:** Dual-case normalization helpers remain in `apps/web/src/api/interop/esource.ts` while `EhrReconciliationDrawer.vue` reads `snake_case` candidate fields.
- **Mitigation Strategy:** `apps/web/tests/api/interop_esource_and_legacy_types.spec.ts` continuously verifies `.NET` wire serialization, response normalization, `client.ts` and `interop/esource.ts` isolation, ESLint and `.gitattributes` parity, and zero uncalled operations or unreachable schemas in `types.ts`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `.gitattributes`, `packages/tooling_core`, `tests/Cadence.UnitTests`, `apps/web`, `eslint.config.mjs`, `docs/api/`, `docs/openapi/`, `docs/web/`, `docs/cli.md`, `docs/adr/`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `dotnet build Cadence.sln`, `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj`, `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`, `pnpm -r typecheck`, `pnpm lint`, `pnpm -r test` (including `interop_esource_and_legacy_types.spec.ts`, `EhrReconciliationDrawer.spec.ts`, `drawer_responsiveness.spec.js`, `chunkSplitAndBoot.spec.ts`, `cold_boot_bundle.spec.ts`, and `ui_consumer_reachability.test.js`), `pnpm --filter web build`, and `pnpm cadence check` (`openapi`, `api-clients`, `adrs`, `validate-doc-index`, `markdown`, `cli-docs`, `deid`, `duplication`).
