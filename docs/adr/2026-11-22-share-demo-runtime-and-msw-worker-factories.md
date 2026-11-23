# ADR-2537: Share Demo Runtime, Unbacked-Action Guards, and MSW Startup Factories

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (21 CFR Part 11 regulated workflow integrity, deterministic offline demo isolation, and production bundle hygiene)

---

## 1. Context & Problem Statement

Both interactive clinical applications (`apps/web` and `apps/subject-portal`) maintained hand-rolled copies of three cross-application infrastructure modules:

1. **Deterministic Demo Runtime (`demoMode.js`):** Both apps duplicated Mulberry32 PRNG seeding, `demoNowMs` / `demoNowIso` fixed-epoch clocks, `demoRandom`, `demoRandomHex`, `demoRandomId`, `demoUuid`, `demoHashInt`, and `demoSubtleDigest` Web Crypto fallbacks, leading to 4 normalized duplicate blocks across `apps/web/src/utils/demoMode.js` and `apps/subject-portal/src/utils/demoMode.js`.
2. **Unbacked Demo Action Guards (`unbackedActions.js`):** Both apps duplicated `guardUnbackedAction` and `requireLiveBackendAction` toast guards, diverging only on toast copy (`"No Cadence service..."` vs `"No live Cadence service..."`) and return value (`undefined` vs formatted `message`).
3. **MSW Browser Worker Startup (`src/mocks/browser.js`):** Both apps duplicated base-path normalization, Service Worker URL/scope resolution, `navigator.serviceWorker.controller` attachment synchronization (`ensureServiceWorkerController`), and singleton `startPromise` memoization, while `DuplicationDetectorService` only failed on `.cs` duplicates and allowed `.js` copy-paste drift to pass `cadence check duplication`.

## 2. Decision Drivers & Constraints

- **Zero Production Demo Manifest Leakage (`PRD-SYS-001`):** `scripts/vite-strip-demo-fixtures.mjs` strips bare `"demo-fixtures"` imports during non-demo production builds so `CADENCE-101` synthetic study strings never ship in `apps/web/dist` or `apps/subject-portal/dist`. Any shared runtime subpath must avoid relative `./index.js` or `./manifest.js` imports.
- **Zero Production MSW Leakage (`PRD-SYS-001`):** `packages/ui` (`ui/testing`) must never import `msw` or `msw/browser`, and `isDemoBuild()` must remain statically foldable via `import.meta.env` so Rollup and `scripts/vite-strip-mock-worker.mjs` strip `msw/browser` and `mockServiceWorker.js` from production builds.
- **Stable Caller Contracts:** All 80 caller files across `apps/web` and `apps/subject-portal` must keep their existing import paths, default token lengths (`7` in web, `13` in portal), toast copy, return values, web `installInProcessMswFallback`, and portal `sw.js?msw=1` service worker URL.
- **Automated Regression Guardrail:** `DuplicationDetectorService` must reject 15+ normalized line duplicates involving `.js` files in addition to `.cs` files.

## 3. Options Considered

### Option 1: Export Manifest-Free Factories from `demo-fixtures/runtime` and `ui/testing`, and Enforce `.js` Duplication Gates (Selected)

- **Overview:** Add `./runtime` (`src/runtime.js` and `src/runtime.d.ts`) to `packages/demo-fixtures` exporting `isDemoBuild`, `createDemoRuntime`, and `createUnbackedActionGuard` with zero `./index.js` or `./manifest.js` imports, receiving `DEMO_EPOCH_ISO` and `DEMO_EPOCH_MS` from app wrappers via bare `"demo-fixtures"` imports and guarding `globalThis.crypto.getRandomValues` with a `0.5` fallback. Add `resolveMswServiceWorkerOptions`, `ensureServiceWorkerController`, and `createMswWorkerStarter` to `packages/ui/mockGateway.js` (`ui/testing`) without `msw` imports and out of `packages/ui/index.js`. Refactor `apps/web` and `apps/subject-portal` wrappers to delegate to these factories while preserving app-specific defaults, and update `DuplicationDetectorService.cs` + `QualityGates.cs` to fail on `.cs` or `.js` duplicates.
- **Pros:**
  - ✅ Eliminates all cross-app `.js` duplication while keeping `scripts/vite-strip-demo-fixtures.mjs` and `scripts/vite-strip-mock-worker.mjs` production stripping intact.
  - ✅ Preserves every existing caller signature, toast message, return value, web in-process fallback, and portal `sw.js?msw=1` cooperative worker hook.
  - ✅ Permanently blocks `.js` copy-paste regressions in `cadence check duplication` and pre-commit hooks.
- **Cons:**
  - ❌ Requires explicit bare `"demo-fixtures"` epoch pass-through in each app's `demoMode.js` wrapper rather than importing epochs directly inside `demo-fixtures/runtime`.

### Option 2: Import `DEMO_EPOCH_*` Directly Inside `packages/demo-fixtures/src/runtime.js` and Bundle `msw/browser` in `packages/ui`

- **Overview:** Have `demo-fixtures/runtime` import `./manifest.js` directly and move `setupWorker` instantiation into `packages/ui/mockGateway.js`.
- **Pros:**
  - ✅ Slightly shorter app wrapper files.
- **Cons:**
  - ❌ Relative `./manifest.js` imports bypass `scripts/vite-strip-demo-fixtures.mjs` (which matches bare `"demo-fixtures"` specifiers), leaking `CADENCE-101` fixture data into production bundles, and importing `msw/browser` in `packages/ui` couples the shared UI library to MSW.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Keeping `demo-fixtures/runtime` free of manifest imports and `ui/testing` free of `msw` imports deduplicates the deterministic runtime, unbacked-action guards, and MSW worker startup lifecycle without compromising production bundle stripping or caller contracts, while `.js` enforcement in `DuplicationDetectorService` prevents future drift.

## 5. Consequences & Trade-offs

- **Positive Impact:** Single audited implementation for Mulberry32 deterministic seeding, fixed-epoch clocks, Web Crypto fallbacks, unbacked-action toast guards, and MSW controller readiness across both clinical apps, backed by unit tests and `.js` duplication enforcement.
- **Negative Impact / Technical Debt:** Future `.js` modules under `apps/` and `packages/` cannot share 15+ normalized lines of boilerplate without extracting a shared helper into `packages/ui` or `packages/demo-fixtures`.
- **Mitigation Strategy:** Extract shared cross-app browser utilities into `packages/ui` or `packages/demo-fixtures` subpaths with clear dependency boundaries.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/demo-fixtures`, `packages/ui`, `apps/web`, `apps/subject-portal`, `packages/tooling_core`, `packages/cli`, `tests/Cadence.UnitTests`, `docs/cli.md`, `docs/CI.md`, `docs/adr/index.md`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm --filter ui test`, `apps/web` and `apps/subject-portal` Vitest suites (`demo_manifest_determinism`, `demo_isolation_stateful_msw`, `demo_isolation_portal`, `msw_mock_gateway`, `unbacked_actions`), `pnpm --filter web build` and `pnpm --filter subject-portal build` zero-leak bundle checks, `dotnet test` (`DomainEngineUnitTests`, `QualityGateRegistryTests`), and `cadence check` (`duplication`, `adrs`, `cli-docs`, `doc-index`).
