# ADR-2677: Retire Unreferenced Shared UI and Web Exports and Enforce Live Consumer Reachability

- **Status:** Accepted
- **Date:** 2026-11-24
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001, PRD-CRF-015

---

## 1. Context & Problem Statement

Following the interactive Vue component migrations in ADR-2432, the Clinical Canvas workspace consolidations in ADR-2606 and ADR-2635, canonical scoped navigation in ADR-2651, and the bounded `ClinicalDrawer` migration in ADR-2674, `packages/ui` and `apps/web/src` retained unreferenced legacy exports across imperative HTML matrix and lookup builders in `packages/ui/src/builders.js`, six superseded Vue single-file components and an unused retry composable in `packages/ui/src/index.js`, uncalled signature and JWT utilities in `packages/ui/signing.js` and `packages/ui/mockGateway.js`, an unmounted copilot composable in `apps/web/src/composables/useWebCopilot.ts`, and twelve uncalled API and canvas helper exports in `apps/web/src/api/` and `apps/web/src/canvas/`.

Because `EXPECTED_SHARED_COMPONENTS` in `packages/ui/tests/design_system_harmony.test.js` locked every file under `packages/ui/src/components` into the root barrel without verifying whether any live application imported it, cross-cutting accessibility, focus, and drawer sweeps (including ADR-2646, ADR-2674, and ADR-2822) repeatedly incurred maintenance churn and merge conflicts across dead components such as `AiSuggestionDrawer.vue`, preserved stale de-identification baseline entries in `baselines/deid/packages/ui.baseline.txt`, and kept broad `Ai*.vue` lint allowlists in `eslint.config.mjs`.

## 2. Decision Drivers & Constraints

- **Eliminate Dead-File Sweep Churn & Oversized Modules:** Shared UI sweeps for WCAG 2.2 Level AA compliance (`PRD-CRF-015`) and 21 CFR Part 11 audit workflows (`PRD-SYS-001`) must operate exclusively on active or explicitly staged primitives, shrinking `packages/ui/src/builders.js` to 400 non-empty lines and clearing `baselines/deid/packages/ui.baseline.txt` to zero entries.
- **Preserve Active Upstream Contracts & Foundation Staging (`#6042`, `#6148`, `#6229`, `#6230`, `#6236`):** Active capabilities—including `buildScopedWorkflowLocation` consumed by `useScopedNavigation.ts` (`#6229`), CGT step-up binding helpers `formatCgtBedsideBindingId` and `formatCgtDestructionBindingId` (`#6230` / ADR-2666), bounded `ClinicalDrawer` chrome (`#6236` / ADR-2674), `saveSimulationReport` (`#6148`), and staged `#6042` primitives (`ClinicalExplanation` and `ClinicalGatedAction`)—must remain intact via a narrow, explicit `STAGED_UI_PRIMITIVES` allowlist.
- **Decouple Guardrails From High-Churn Monoliths:** Consumer reachability enforcement must live in an isolated Vitest module (`packages/ui/tests/ui_consumer_reachability.test.js`) with comment-safe scanning rather than expanding the monolithic `design_system_harmony.test.js` suite.

## 3. Options Considered

### Option 1: Delete Unreferenced UI/Web Exports and Enforce Isolated Comment-Safe Consumer Reachability with `STAGED_UI_PRIMITIVES` (Selected)

- **Overview:** Remove all verified unreferenced `packages/ui` HTML builders (`createClinicalLookupInput`, `createSoaBuilderMatrix`, `createClinicalVisitMatrix`), six dead Vue SFCs (`AiActionButton.vue`, `AiSuggestionDrawer.vue`, `AuditLogViewer.vue`, `ConfidenceBadge.vue`, `PersonaSwitcher.vue`, `FileUploadModal.vue`), `useAsyncRetry.js`, dead `signing.js` / `mockGateway.js` helpers, stale component docs, and uncalled `apps/web/src` copilot, API, and canvas exports (preserving `buildScopedWorkflowLocation` and demoting internal-only ticket helpers to module-private scope); synchronize `packages/ui/index.js`, `packages/ui/src/index.js`, `packages/ui/index.d.ts`, `packages/ui/README.md`, `docs/components/index.md`, `baselines/deid/packages/ui.baseline.txt`, and `eslint.config.mjs`; and add a dedicated Vitest consumer reachability guard in `packages/ui/tests/ui_consumer_reachability.test.js` scanning stripped non-comment source across `apps/web/src` and `apps/subject-portal/src` with `STAGED_UI_PRIMITIVES` restricted to `ClinicalExplanation` and `ClinicalGatedAction`.
- **Pros:**
  - Shrinks `packages/ui` from 30 to 24 shared Vue components, reduces `packages/ui/src/builders.js` to 400 non-empty lines, and eliminates both `baselines/deid/packages/ui.baseline.txt` entries.
  - Prevents orphan `packages/ui` components, composables, and chained HTML builders (or stale migration comments) from passing silently, and flags graduated `STAGED_UI_PRIMITIVES` entries once adopted in production.
  - Isolates reachability checks in `ui_consumer_reachability.test.js` and structures `packages/ui/README.md` component entries to reduce merge contention across parallel UI PRs.
- **Cons:**
  - Authors landing a shared `packages/ui` primitive in a foundation PR prior to consumer wiring must list it in `STAGED_UI_PRIMITIVES` and remove it when an app route adopts it.

### Option 2: Retain Legacy Barrel Exports Behind `@deprecated` JSDoc Annotations Without Deleting Source Files

- **Overview:** Mark unreferenced `packages/ui` and `apps/web` symbols `@deprecated` in `packages/ui/index.d.ts` and TypeScript modules while leaving existing SFCs, builders, and tests in place.
- **Pros:**
  - Avoids updating barrel exports or shared component inventory counts.
- **Cons:**
  - Leaves dead-file churn and merge conflicts in future accessibility and CSS token sweeps, retains `baselines/deid/packages/ui.baseline.txt` debt, and provides no automated gate against new unreferenced exports.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Deleting verified dead exports across `packages/ui` and `apps/web/src` while pairing the 24-component barrel inventory with an isolated, comment-safe transitive consumer reachability check in `packages/ui/tests/ui_consumer_reachability.test.js` permanently eliminates dead-sweep maintenance overhead, clears the `packages/ui` de-identification baseline, and preserves a clean staging path for `#6042` primitives.

## 5. Consequences & Trade-offs

- **Positive Impact:** `packages/ui` exposes 24 active or staged Vue components, 4 live composable modules (`useEscapeClose`, `useFocusTrap`, `useInlineFocusHandoff`, `useSignOutGuard`), 5 active HTML/tree builders (`packages/ui/src/builders.js` at 400 non-empty lines), and active gateway/canonical/CGT signing helpers; `baselines/deid/packages/ui.baseline.txt` drops to zero entries; `eslint.config.mjs` restricts `--semantic-color-ai*` tokens to `packages/ui/tokens.css` and `apps/web/src/components/ai/`; and internal ticket permission/simulation helpers in `apps/web/src` are encapsulated within their modules.
- **Negative Impact / Technical Debt:** `STAGED_UI_PRIMITIVES` temporarily allowlists `ClinicalExplanation` and `ClinicalGatedAction` until `#6042` route integration mounts them in `apps/web`.
- **Mitigation Strategy:** `packages/ui/tests/ui_consumer_reachability.test.js` verifies live consumer reachability across `apps/web/src` and `apps/subject-portal/src`, rejects synthetic unreferenced components, composables, comment-only mentions, and chained dead builders, and fails when a staged primitive is adopted without graduating out of `STAGED_UI_PRIMITIVES`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `baselines/deid/packages/ui.baseline.txt`, `eslint.config.mjs`, `docs/components/`, `docs/adr/`, `docs/DOCUMENTATION_INDEX.md`, `CONTEXT.md`.
- **Verification Plan:** Verified via `pnpm build`, `pnpm --filter ui test`, `pnpm typecheck`, `pnpm --filter web exec vitest run --shard=1/2`, `pnpm --filter web exec vitest run --shard=2/2`, `pnpm --filter subject-portal test`, `pnpm exec vitest run --config vitest.config.mjs`, `pnpm lint`, and Cadence CLI ADR, documentation index, README, de-identification, and architecture checks.
