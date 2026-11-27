# ADR-2692: Strip Unreachable Non-Demo Route Modules, Remove hls.js, and Tighten Worker CSP

- **Status:** Accepted
- **Date:** 2026-11-27
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Amends:** [ADR-2498](2026-11-22-hoist-csp-delegate-widgets-and-mode-aware-vite-profiles.md), [ADR-2537](2026-11-22-split-ui-vitest-matcher-and-enforce-demo-bundle-budgets.md)
- **Requirements:** PRD-SYS-001 (Frontend bundle hygiene, dependency minimization, and strict Content Security Policy enforcement)

---

## 1. Context & Problem Statement

Under [ADR-2819](2026-11-20-focus-active-ecrf-view-on-backed-kernel-forms-outside-demo-mode.md), non-demo production builds of `apps/web` and `apps/subject-portal` expose only workflows backed by live `.NET` services, redirecting unbacked `DEMO_AVAILABLE_SCREENS` routes to `UnavailableView.vue` and legacy routes (`ecrf`, `data-lock`, `exports`, `amendment-diff`) to `StudyCanvasView.vue`. However, `createDemoFixturesPlugin` in `scripts/vite-strip-demo-fixtures.mjs` previously stubbed only `src/views/demo/*` and `src/stores/demo/*`. Top-level unbacked and redirected view files in `apps/web/src/views/` and `apps/subject-portal/src/views/` remained in the Rollup module graph during non-demo builds:

1. In `apps/web`, unbacked `MdrView.vue` pulled `usdm-schemas` (`zod`) and `CrfAuthoringCanvas.vue` (`sortablejs` and `vuedraggable`) into production bundles, while `apps/web/src/router/index.js` kept a dynamic `import("../i18n/index.js")` (`vue-i18n` and `@intlify`) reachable even though every `requiresI18n` route (`icf-builder`, `econsent-authoring`, and `ecrf`) is demo-gated or redirected before reaching the `ensureI18nInstalled` guard in production.
2. In `apps/subject-portal`, `src/router/routes.js` statically imported all six `DEMO_AVAILABLE_SCREENS` views and five demo-only `lucide-vue-next` tab icons even though non-demo builds expose only `WalletView.vue` and `UnavailableView.vue`.
3. `MediaPreviewModal.vue` (imported solely by demo-only `DocumentsHubView.vue`) dynamically imported `hls.js` only when `!isDemoBuild()`, while `apps/fileshare/src/Program.cs` mounts only health and status probes with zero HLS streaming endpoints. Retaining `hls.js` shipped ~1,080.72 KiB of unreachable vendor JavaScript and required `worker-src 'self' blob:` in `apps/web/vite.config.js` ([ADR-2498](2026-11-22-hoist-csp-delegate-widgets-and-mode-aware-vite-profiles.md)).

## 2. Decision Drivers & Constraints

- **Zero unreachable third-party vendor chunks in production:** Non-demo production builds of `apps/web` must ship zero bytes of `hls.js`, `zod`, `sortablejs`, `vuedraggable`, and `vue-i18n`, and non-demo production builds of `apps/subject-portal` must exclude the six demo views and five unused demo tab icons.
- **Single declarative route tables and demo/test parity:** `build:demo` and Vitest (`PRD-SYS-001`) must continue to exercise all `DEMO_AVAILABLE_SCREENS` routes, `vendor-vuedraggable` chunk isolation (`apps/web/tests/api/chunkSplitAndBoot.spec.ts`), `vue-i18n` locale loading, and all seven `apps/subject-portal` `viewRoutes` / six `tabs` (`apps/subject-portal/tests/portal-router.test.js`).
- **Tighter Content Security Policy:** Removing `hls.js` eliminates the sole justification for `blob:` Web Workers in `apps/web`, allowing hoisted CSP profiles across both demo and production builds to enforce `worker-src 'self'` while retaining `media-src 'self' blob:` and `frame-src 'self' blob:` for regulated video and PDF object URLs.
- **Compliance with public-bundle and dependency gates:** Changes must satisfy `PublicBundleValidatorService` and `DependencyValidatorService` across both `pnpm build` and staged demo builds ([ADR-2537](2026-11-22-split-ui-vitest-matcher-and-enforce-demo-bundle-budgets.md)).

## 3. Options Considered

### Option 1: Extend Build-Time View Stubbing + Constant-Folded Guards, Remove `hls.js`, and Enforce `worker-src 'self'` (Selected)

- **Overview:**
  - Remove `"hls.js"` from `apps/web/package.json` and `pnpm-lock.yaml`, simplify `MediaPreviewModal.vue` (`setupVideoPlayer`) to native HTML5 `<video>` playback, delete the `vendor-media` manual chunk, and set `allowBlobWorker: false` in `apps/web/vite.config.js` (plus static fallback HTML meta tags) so `worker-src 'self'` is emitted everywhere.
  - Extend `createDemoFixturesPlugin` in `scripts/vite-strip-demo-fixtures.mjs` during non-demo builds (`command === "build" && !isDemo`) to stub the unbacked `DEMO_AVAILABLE_SCREENS` views and redirected legacy views (`AmendmentDiffView.vue`, `DataLockView.vue`, `EcrfView.vue`, `ExportWizardView.vue`) in `apps/web/src/views/`, plus the six `DEMO_AVAILABLE_SCREENS` views in `apps/subject-portal/src/views/`.
  - Gate `ensureI18nInstalled` in `apps/web/src/router/index.js` behind `isDemoBuild()` and guard the five demo tab icons in `apps/subject-portal/src/router/routes.js` with `isDemoBuild() || import.meta.env?.MODE === "test"` so `createConstantFoldDemoModePlugin` folds `isDemoBuild()` to `(false)` in production builds while preserving `build:demo` and Vitest.
- _**Pros:**_ Eliminates ~1.49 MiB of unreachable vendor JavaScript in non-demo builds and `hls.js` across the workspace; tightens `worker-src` to `'self'`; preserves single route tables, `vuedraggable`/`vue-i18n`/`usdm-schemas` demo support, and all `subject-portal` exports and unit tests.
- _**Cons:**_ Requires route-parity tests in `vite_strip_demo_fixtures.spec.ts` so newly promoted live screens are removed from the non-demo stub list when backed by `.NET` endpoints.

### Option 2: Maintain Separate Production and Demo Router Files

- **Overview:** Split `apps/web/src/router/index.js` and `apps/subject-portal/src/router/routes.js` into separate production and demo entry modules selected via Vite aliases.
- _**Pros:**_ Avoids build-time `resolveId` stubbing of top-level view imports.
- _**Cons:**_ Duplicates route metadata, redirect rules, and navigation guards across two router definitions per app, risking silent routing drift between Vitest, `build:demo`, and production builds.

### Option 3: Keep `hls.js` Installed for Demo Builds Only

- **Overview:** Retain `hls.js` and `worker-src 'self' blob:` for static demo video previews while stripping `DocumentsHubView.vue` in non-demo builds.
- _**Pros:**_ Preserves synthetic multi-bitrate selector simulation in demo mode.
- _**Cons:**_ Keeps an unused ~1 MiB third-party media engine in `package.json` with zero backend HLS stream endpoints in `apps/fileshare` and leaves `worker-src` unnecessarily permissive (`blob:`).

## 4. Decision Outcome

- **Chosen Option:** Option 1 (Extend Build-Time View Stubbing + Constant-Folded Guards, Remove `hls.js`, and Enforce `worker-src 'self'`).
- **Justification:** Extending `createDemoFixturesPlugin` and `isDemoBuild()` constant-folding keeps a single canonical route table per application while ensuring non-demo production bundles ship only live clinical and participant code paths, and removing `hls.js` closes the `worker-src` `blob:` exception documented in [ADR-2498](2026-11-22-hoist-csp-delegate-widgets-and-mode-aware-vite-profiles.md).

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Non-demo production builds of `apps/web` ship zero chunks or modules for `hls.js`, `vendor-media`, `zod`, `sortablejs`, `vuedraggable`, and `vue-i18n`.
  - Non-demo production builds of `apps/subject-portal` replace the six unbacked demo views with inert `DemoWorkspaceProdStub` components and tree-shake the five demo tab icons while keeping `WalletView.vue`, `UnavailableView.vue`, and the `Wallet` tab icon intact.
  - Both demo and production HTML entrypoints emit `worker-src 'self'` with zero CSP violations during regulated PDF and native `<video>` previews.
- **Negative Impact & Mitigations:**
  - Promoting a screen from `DEMO_AVAILABLE_SCREENS` to `AVAILABLE_SCREENS` requires removing its view filename from `WEB_NON_DEMO_STUBBED_VIEWS` or `SUBJECT_PORTAL_NON_DEMO_STUBBED_VIEWS` in `scripts/vite-strip-demo-fixtures.mjs`; automated parity assertions in `apps/web/tests/vite_strip_demo_fixtures.spec.ts` fail immediately if availability sets, router redirects, and stub lists diverge.

## 6. Implementation & Verification

- **Affected Files:**
  - `apps/web/package.json`, `pnpm-lock.yaml`, `apps/web/vite.config.js`, `apps/web/index.html`, `apps/web/public/silent-check-sso.html`, `apps/web/README.md`
  - `apps/web/src/components/fileshare/MediaPreviewModal.vue`, `apps/web/src/router/index.js`
  - `apps/subject-portal/src/router/routes.js`, `apps/subject-portal/src/components/TabBar.vue`
  - `scripts/vite-strip-demo-fixtures.mjs`
  - `apps/web/tests/cold_boot_bundle.spec.ts`, `apps/web/tests/vite_strip_demo_fixtures.spec.ts`, `apps/web/tests/vite_csp_profiles.test.js`, `apps/web/tests/media_preview_hls.test.js`, `apps/web/tests/e2e/web-accessibility.spec.ts`
- **Verification:**
  - `apps/web/tests/cold_boot_bundle.spec.ts` asserts zero `vendor-media`, `hls.js`, `zod`, `sortablejs`, `vuedraggable`, and `vue-i18n` chunks/modules in non-demo production builds.
  - `apps/web/tests/vite_strip_demo_fixtures.spec.ts` verifies exact parity between router availability/redirect definitions and non-demo view stubbing across both `apps/web` and `apps/subject-portal`.
  - `apps/web` and `apps/subject-portal` Vitest suites, Playwright accessibility/CSP verification, `cadence check adrs`, and `cadence check public-bundle` pass with zero errors.
