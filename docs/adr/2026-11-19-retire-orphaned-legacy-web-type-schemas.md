# ADR-2816: Retire Orphaned Legacy Web Type Schemas

- **Status:** Accepted
- **Date:** 2026-11-19
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-053 (development environment guardrails and build hygiene)

---

## 1. Context & Problem Statement

Zero production files in `apps/web` import `apps/web/src/types/usdm.ts` (25,597 lines, 2,445 exported types) or `apps/web/src/types/db_schemas.ts` (674 lines, 38 exported interfaces), and the .NET 10 migration removed the legacy Python introspection generator that previously produced `db_schemas.ts`. Despite having zero callers, `apps/web/package.json` still invoked `apps/web/src/types/generate-types.js` on `prebuild`, `pretest`, and `pretypecheck`, causing TypeScript (`tsc`) and ESLint to parse over 26,000 lines of dead declarations and Vitest (`apps/web/tests/generate-types.test.js`) to force four full regenerations on disk. Meanwhile, active .NET OpenAPI client generation lives in `apps/web/scripts/generate-api-types.js` (`apps/web/src/api/generated/`), and remaining Python-era API clients import `paths` from `apps/web/src/api/types.ts`.

## 2. Decision Drivers & Constraints

- **Zero Dead Build Work (PRD-SYS-053):** Web builds, lints, typechecks, and unit tests should not generate or parse unreferenced type bundles or mutate tracked files during local checks.
- **Preserve Active API Type Contracts:** Keep `apps/web/scripts/generate-api-types.js`, `apps/web/src/api/types.ts`, `docs/openapi/legacy/cdisc_openapi.json`, and `packages/ui/src/types/cdash.ts` intact.
- **Preserve Historical Audit & Snapshot Records:** Leave historical ADRs in `docs/adr/` and committed GxP snapshots in `docs/SDLC/` untouched per ADR-2532.

## 3. Options Considered

### Option 1: Delete `apps/web/src/types/` and Its Legacy Pre-Script Hooks and Tests (Selected)

- **Overview:** Remove `apps/web/src/types/` (`usdm.ts`, `db_schemas.ts`, `generate-types.js`, `.usdm-hash.json`), delete `prebuild`/`pretest`/`pretypecheck` from `apps/web/package.json`, clean up obsolete `src/types/**` and `db_schemas.ts` exclusions in `apps/web/vitest.config.js` and Prettier ignore files, update web and OpenAPI READMEs and `docs/TESTING.md`, and remove `apps/web/tests/generate-types.test.js` and `OfflineSchemaDriftTests.TestCommittedTypescriptSchemaIsUpToDate`.
- **Pros:**
  - ✅ Eliminates 26,470 lines of unreferenced declarations and four redundant generator runs during tests.
  - ✅ Preserves active `.NET` OpenAPI client generation (`generate-api-types.js`), `apps/web/src/api/types.ts`, and `packages/ui` CDASH generation.
- **Cons:**
  - ❌ Any future consumer of USDM or service contracts must import from `packages/usdm-schemas` or `apps/web/src/api/generated/`, which is already the target architecture.

### Option 2: Keep `apps/web/src/types/` Excluded from TypeScript and ESLint

- **Overview:** Retain the files in `apps/web/src/types/` while adding compiler and linter exclusions.
- **Pros:**
  - ✅ Minimal file deletions.
- **Cons:**
  - ❌ Leaves 26,470 dead lines, a retired schema snapshot with no generator, and misleading build scripts in the repository.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Deleting the orphaned `apps/web/src/types/` bundle, its npm pre-hooks, and its dedicated generator checks aligns the web workspace with the active OpenAPI client pipeline (PRD-SYS-053) without affecting any production module or active API contract.

## 5. Consequences & Trade-offs

- **Positive Impact:** Faster `pnpm --filter web` builds, tests, lints, and typechecks, with no working-tree regeneration drift in `apps/web/src/types/`.
- **Negative Impact / Technical Debt:** Remaining Python-era clients in `apps/web/src/api/` still import `apps/web/src/api/types.ts` until their screens migrate onto `.NET` module endpoints.
- **Mitigation Strategy:** `apps/web/scripts/generate-api-types.js` and the `api-clients` quality gate enforce drift-free `.NET` OpenAPI types as screens migrate (`docs/web/API_MIGRATION.md`).

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/web`, `.prettierignore`, `docs/TESTING.md`, `docs/openapi/README.md`, `tests/Cadence.UnitTests/Validation/OfflineSchemaDriftTests.cs`.
- **Verification Plan:** Verified via `pnpm --filter web typecheck`, `pnpm --filter web lint`, `pnpm --filter web test`, `dotnet test`, and `pnpm cadence check`.
