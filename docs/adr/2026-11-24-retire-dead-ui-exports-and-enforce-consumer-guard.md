# ADR-2677: Retire Unreferenced Shared UI and Web Exports and Enforce Live Consumer Reachability

- **Status:** Accepted
- **Date:** 2026-11-24
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001, PRD-CRF-015

---

## 1. Context & Problem Statement

Following the interactive Vue component migrations in ADR-2432 and subsequent Clinical Canvas workspace consolidations, `packages/ui` and `apps/web/src` retained 3,009 lines of unreferenced legacy code across vanilla HTML matrix and lookup builders in `packages/ui/src/builders.js`, six superseded Vue single-file components and an unused retry composable in `packages/ui/src/index.js`, uncalled signature/token utilities in `packages/ui/signing.js` and `packages/ui/mockGateway.js`, an unmounted copilot composable in `apps/web/src/composables/`, and thirteen uncalled API and canvas helper exports in `apps/web/src/api/` and `apps/web/src/canvas/`.

Because `EXPECTED_SHARED_COMPONENTS` in `packages/ui/tests/design_system_harmony.test.js` locked every file under `packages/ui/src/components` into the root barrel without checking whether any live application imported it, repository-wide accessibility and CSS token sweeps (including ADR-2822 and ADR-2646) repeatedly incurred maintenance churn across dead files, kept three oversized modules in `packages/ui`, and preserved stale de-identification baseline entries in `baselines/deid/packages/ui.baseline.txt` and broad `Ai*.vue` lint allowlists in `eslint.config.mjs`.

## 2. Decision Drivers & Constraints

- **Eliminate Dead-File Sweep Churn & Oversized Modules:** Shared UI sweeps for WCAG 2.2 Level AA compliance (`PRD-CRF-015`) and 21 CFR Part 11 audit workflows (`PRD-SYS-001`) must operate exclusively on active or explicitly staged primitives, shrinking `packages/ui/src/builders.js` to 400 non-empty lines and clearing `baselines/deid/packages/ui.baseline.txt` to zero entries.
- **Support Explicit Foundation Staging (`#6042` & `#6148`):** Newly landed foundation primitives (`ClinicalExplanation` and `ClinicalGatedAction`, plus `#6042` drawer/shared helpers and `#6148` simulation report persistence) must remain intact ahead of route-level adoption via a narrow, explicit `STAGED_UI_PRIMITIVES` allowlist.
- **Zero User-Visible or CI Workflow Drift:** Pruning dead exports and tightening `eslint.config.mjs` AI-token allowlists must introduce zero layout, styling, or functional changes in `apps/web` and `apps/subject-portal` while executing inside the existing `packages/ui` Vitest suite without altering `.github/workflows`.

## 3. Options Considered

### Option 1: Delete Unreferenced UI/Web Exports and Enforce Live Consumer Reachability with `STAGED_UI_PRIMITIVES` (Selected)

- **Overview:** Remove all verified unreferenced `packages/ui` HTML builders, six dead Vue SFCs, the unused retry composable, dead `signing.js` / `mockGateway.js` helpers, stale component docs, and uncalled `apps/web/src` copilot, API, and canvas exports (demoting internal-only ticket helpers to module-private scope); synchronize `packages/ui/index.js`, `packages/ui/src/index.js`, `packages/ui/index.d.ts`, `packages/ui/README.md`, `docs/components/index.md`, `baselines/deid/packages/ui.baseline.txt`, and `eslint.config.mjs`; and add a Vitest consumer reachability guard in `packages/ui/tests/design_system_harmony.test.js` scanning `apps/web/src` and `apps/subject-portal/src` with `STAGED_UI_PRIMITIVES` restricted to `ClinicalExplanation` and `ClinicalGatedAction`.
- **Pros:**
  - Removes 3,009 dead lines, shrinks `packages/ui/src/builders.js` to 400 non-empty lines, and eliminates both `baselines/deid/packages/ui.baseline.txt` entries.
  - Prevents orphan `packages/ui` components, composables, and HTML builders from returning silently while giving staged primitives an explicit, self-documenting contract.
  - Restricts `--semantic-color-ai*` token usage to governed `apps/web/src/components/ai/` surfaces (`AiLabel.vue`).
- **Cons:**
  - Authors landing a shared `packages/ui` primitive in a foundation PR prior to consumer wiring must add its name to `STAGED_UI_PRIMITIVES` until an app route imports it.

### Option 2: Retain Legacy Barrel Exports Behind `@deprecated` JSDoc Annotations Without Deleting Source Files

- **Overview:** Mark unreferenced `packages/ui` and `apps/web` symbols `@deprecated` in `packages/ui/index.d.ts` and TypeScript modules while leaving existing SFCs, builders, and tests in place.
- **Pros:**
  - Avoids updating barrel exports or shared component inventory counts.
- **Cons:**
  - Fails to eliminate oversized files, leaves dead-file churn in future accessibility and CSS token sweeps, retains `baselines/deid/packages/ui.baseline.txt` debt, and provides no automated gate against new unreferenced exports.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Deleting verified dead exports across `packages/ui` and `apps/web/src` while pairing the 24-component barrel inventory with a transitive consumer reachability check in `design_system_harmony.test.js` permanently eliminates dead-sweep maintenance overhead, clears the `packages/ui` de-identification baseline, and preserves a clean staging path for `#6042` primitives.

## 5. Consequences & Trade-offs

- **Positive Impact:** `packages/ui` exposes 24 active or staged Vue components, 5 live composable modules, 5 active HTML/tree builders (`packages/ui/src/builders.js` at 400 non-empty lines), and active gateway/canonical signing helpers (`verifyGatewaySignature`, `generateGatewaySignature`, `generateCanonicalSignature`); `baselines/deid/packages/ui.baseline.txt` drops to zero entries; `eslint.config.mjs` rejects `--semantic-color-ai*` tokens in `packages/ui` components; and internal ticket permission/simulation helpers in `apps/web/src` are encapsulated within their modules.
- **Negative Impact / Technical Debt:** `STAGED_UI_PRIMITIVES` temporarily allowlists `ClinicalExplanation` and `ClinicalGatedAction` until `#6042` route integration mounts them in `apps/web`.
- **Mitigation Strategy:** `packages/ui/tests/design_system_harmony.test.js` verifies both live consumer reachability across `apps/web/src` and `apps/subject-portal/src` and negative detection of synthetic unreferenced components, composables, and chained builders, while `packages/ui/vite.config.js` enforces V8 coverage thresholds across the retained surface.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `baselines/deid/packages/ui.baseline.txt`, `eslint.config.mjs`, `docs/components/`, `docs/adr/`, `docs/DOCUMENTATION_INDEX.md`, `CONTEXT.md`.
- **Verification Plan:** Verified via `pnpm --filter "./packages/*" build`, `pnpm --filter ui test`, `pnpm --filter ui typecheck`, `pnpm --filter web typecheck`, `pnpm --filter subject-portal typecheck`, `pnpm --filter web test`, `pnpm --filter subject-portal test`, `pnpm lint`, and Cadence CLI documentation, ADR, README, and de-identification baseline checks.
