# ADR-2536: Split UI Vitest Accessibility Matcher and Enforce Staged Demo Bundle Budgets

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Amends:** [ADR-251](2026-08-02-centralized-accessibility-matcher-and-80-percent-coverage-gate.md)
- **Requirements:** PRD-SYS-001 (WCAG accessibility enforcement without shipping test harnesses or OIDC SDKs into public demo bundles)

---

## 1. Context & Problem Statement

Under [ADR-251](2026-08-02-centralized-accessibility-matcher-and-80-percent-coverage-gate.md), `packages/ui` centralized the `toBeAccessible` Vitest matcher by declaring `"axe-core"` in runtime `"dependencies"` and re-exporting `toBeAccessible` directly from the root `packages/ui/index.js` barrel. Because Vite's library build bundled `axe-core` into `packages/ui/dist/index.js`, every consumer importing `ui` at runtime (`apps/web` and `apps/subject-portal`) carried ~588 KB of minified `axe-core` audit rules inside production and co-hosted GitHub Pages demo bundles ([ADR-2550](2026-11-20-co-host-interactive-demos-on-github-pages-subpaths.md)).

In addition, two other build and verification gaps inflated cold-boot transfer on the public demo subpaths (`/cadence-clinical/demo/` and `/cadence-clinical/subject-portal/`):

1. Zero-argument `isDemoBuild()` calls were evaluated through dynamic `import.meta.env` helper functions rather than constant-folded during Rollup builds, keeping `keycloak-js` (`vendor-auth`, ~216 KB) in static demo bundles that never contact an OIDC identity provider, while `DocumentsHubView.vue` and `MediaPreviewModal.vue` eagerly preloaded `hls.js` (`vendor-media`, ~521 KB) even when no `.m3u8` stream was opened.
2. Neither `DependencyValidatorService` nor `PublicBundleValidatorService` blocked test libraries in runtime `"dependencies"` or enforced cold-boot asset budgets and `axe-core`/`keycloak-js` chunk absence on staged demo bundles, and Playwright E2E suites disabled service workers and Rollup code splitting on root `/` servers rather than exercising the staged `/cadence-clinical/demo/` and `/cadence-clinical/subject-portal/` subpaths.

## 2. Decision Drivers & Constraints

- **Zero test-harness contamination in runtime bundles:** `axe-core` and Vitest/DOM test libraries must remain available to unit and E2E suites (`PRD-SYS-001`) while never shipping in `packages/ui/dist` or application bundles.
- **Preservation of `ui/testing` boundary and PKCE S256 production auth:** `packages/ui/testing` must remain dedicated to `mockGateway.js`, and non-demo builds of `apps/web` and `apps/subject-portal` must preserve Keycloak authorization-code + PKCE `S256` flows ([ADR-2493](2026-09-30-keycloak-only-web-sign-in.md)) and `vi.stubEnv` unit-test overrides on `isDemoBuild(env)`.
- **Lazy media streaming:** `MediaPreviewModal.vue` and `hls.js` must load only on demand when a user previews a live `.m3u8` stream outside demo mode.
- **Fail-closed Native AOT CLI gates & staged subpath E2E parity:** `cadence check dependencies` and `cadence check public-bundle` must enforce the dependency and bundle-budget rules with zero reflection warnings, and Playwright must verify both staged subpaths with service workers and Rollup chunking active.

## 3. Options Considered

### Option 1: Dedicated `ui/vitest` Subpath, Build-Only `isDemoBuild()` Constant-Folding, Lazy `hls.js`, and CLI + Playwright Gates (Selected)

- **Overview:**
  - Move `"axe-core": "catalog:"` from `"dependencies"` to `"devDependencies"` in `packages/ui/package.json` ([ADR-2527](2026-11-21-enforce-dependency-alignment-with-msbuild-and-pnpm-catalogs.md)), externalize `"axe-core"` in `packages/ui/vite.config.js`, remove `toBeAccessible` from `packages/ui/index.js` and `index.d.ts`, and export `accessibility-matcher.js` + `accessibility-matcher.d.ts` exclusively from `./vitest` (`ui/vitest`).
  - Add a build-only Vite transform plugin (`createConstantFoldDemoModePlugin` in `scripts/vite-strip-demo-fixtures.mjs`, `apply: "build"`) that constant-folds zero-argument `isDemoBuild()` calls so Rollup eliminates `keycloak-js` in demo builds while leaving `isDemoBuild(env)` and Vitest `serve` runs untouched.
  - Convert `MediaPreviewModal` to `defineAsyncComponent` in `DocumentsHubView.vue` and dynamically `import("hls.js")` inside `MediaPreviewModal.vue` only for non-demo `.m3u8` streams.
  - Extend `DependencyValidatorService` to reject runtime test packages (`axe-core`, `@axe-core/playwright`, `vitest`, `@vue/test-utils`, `jsdom`, `fake-indexeddb`, `msw`) inside `"dependencies"`, and extend `PublicBundleValidatorService` to reject `axe-core`/`keycloak-js` chunks or payloads, reject forbidden `<link rel="modulepreload">` hints, cap `/cadence-clinical/demo/` cold-boot transfer at `< 1.35 MiB` raw / `< 450 KiB` gzipped, and cap `/cadence-clinical/subject-portal/assets/` at `< 920 KiB` raw.
  - Add Playwright staged-subpath suites with `serviceWorkers: "allow"` and multi-chunk Rollup output enabled for both `/cadence-clinical/demo/` and `/cadence-clinical/subject-portal/`.
- _**Pros:**_ Eliminates `axe-core`, `keycloak-js`, and eager `hls.js` from demo cold boot; keeps `ui/testing` focused on `mockGateway.js`; preserves Vitest `vi.stubEnv` ergonomics and non-demo PKCE auth; enforces regressions automatically in CI.
- _**Cons:**_ Test setup files importing `toBeAccessible` must import from `"ui/vitest"` instead of `"ui"`.

### Option 2: Keep `toBeAccessible` on the Root `ui` Export and Rely on Consumer Tree-Shaking

- **Overview:** Retain `toBeAccessible` in `packages/ui/index.js` and rely on application bundlers to tree-shake unused exports from `packages/ui/dist/index.js`.
- _**Pros:**_ Avoids updating test setup imports.
- _**Cons:**_ Requires `"axe-core"` to remain a runtime dependency of `packages/ui` or breaks unbundled/SSR resolution, and leaves `packages/ui` vulnerable to accidental barrel side-effect retention.

### Option 3: Merge `toBeAccessible` into `ui/testing`

- **Overview:** Re-export `toBeAccessible` alongside `mockGateway.js` from `packages/ui/testing`.
- _**Pros:**_ Avoids adding a `./vitest` export entry.
- _**Cons:**_ Couples browser/MSW gateway request-signing helpers (`mockGateway.js`) to `axe-core`, dragging `axe-core` into tests or MSW harnesses that only need gateway header validation.

## 4. Decision Outcome

- **Chosen Option:** Option 1 (Dedicated `ui/vitest` Subpath, Build-Only `isDemoBuild()` Constant-Folding, Lazy `hls.js`, and CLI + Playwright Gates).
- **Justification:** Separating `ui/vitest` cleanly isolates `axe-core` to `devDependencies` while preserving centralized WCAG matcher coverage (`PRD-SYS-001`), and pairing build-only `isDemoBuild()` constant-folding and lazy `hls.js` loading with `Cadence.ToolingCore` and Playwright subpath gates prevents future bundle bloat or subpath service-worker regressions.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Co-hosted `/cadence-clinical/demo/` cold-boot transfer drops below `1.20 MiB` raw / `380 KiB` gzipped (well within the `1.35 MiB` raw / `450 KiB` gzipped gate), and `/cadence-clinical/subject-portal/assets/` drops below `900 KiB` raw (within the `920 KiB` gate) with zero `axe-core`, `keycloak-js`, or `hls.js` chunks on initial load.
  - `cadence check dependencies` and `cadence check public-bundle` fail closed if test libraries re-enter runtime `"dependencies"` or if `axe-core`/`keycloak-js` chunks reappear in staged demo bundles.
  - Playwright continuously exercises MSW/PWA service workers (`serviceWorkers: "allow"`), Rollup code-split chunks, and deep-link hash reloads under `/cadence-clinical/demo/` and `/cadence-clinical/subject-portal/`.
- **Negative Impact & Mitigations:**
  - Workspace Vitest setup files must import `toBeAccessible` from `"ui/vitest"`, mitigated by dedicated TypeScript declarations (`packages/ui/accessibility-matcher.d.ts`) and Vite test-alias resolution.

## 6. Implementation & Verification

- **Affected Files:**
  - `packages/ui/package.json`, `packages/ui/vite.config.js`, `packages/ui/index.js`, `packages/ui/index.d.ts`, `packages/ui/accessibility-matcher.d.ts`, `packages/ui/tests/accessibility_matcher.test.js`
  - `scripts/vite-strip-demo-fixtures.mjs`
  - `apps/web/vite.config.js`, `apps/web/src/main.js`, `apps/web/src/views/DocumentsHubView.vue`, `apps/web/src/components/fileshare/MediaPreviewModal.vue`, `apps/web/playwright.config.ts`, `apps/web/package.json`, `apps/web/tests/e2e/staged-demo-subpath.spec.ts`
  - `apps/subject-portal/vite.config.js`, `apps/subject-portal/src/features/auth.js`, `apps/subject-portal/playwright.config.js`, `apps/subject-portal/tests/e2e/staged-subpath.spec.js`
  - `packages/tooling_core/src/Services/DependencyValidatorService.cs`, `packages/tooling_core/src/Services/PublicBundleValidatorService.cs`, `packages/cli/src/Services/QualityGates.cs`
  - `tests/Cadence.UnitTests/Testing/ContainerImagePinTests.cs`, `tests/Cadence.UnitTests/Packages/PublicBundleValidatorServiceTests.cs`
- **Verification:**
  - Vitest unit suites across `packages/ui`, `apps/web`, and `apps/subject-portal` verify `ui/vitest` matcher exports, dynamic `hls.js` loading only for non-demo `.m3u8` manifests, and preserved `isDemoBuild(env)` overrides.
  - `Cadence.UnitTests` and `cadence check dependencies` / `cadence check public-bundle` verify fail-closed rejection of runtime test dependencies, `axe-core`/`keycloak-js` bundle contamination, forbidden preload tags, and byte budget violations.
  - Playwright `staged-demo-subpath` (`apps/web`) and `staged-subpath.spec.js` (`apps/subject-portal`) verify active service-worker control, multi-chunk Rollup execution, zero `hls.js` requests on `#/documents`, and zero HTTP `>= 400` or off-origin requests.
