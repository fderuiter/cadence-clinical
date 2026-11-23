# ADR-2652: Prune Global Web CSS Overrides, Standardize GxP Status Badges, and Gate AI Token Exclusivity

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (21 CFR Part 11 GxP record state legibility and AI provenance clarity), PRD-CRF-015 (WCAG 2.2 Level AA contrast, focus indicators, and clinical typography)

---

## 1. Context & Problem Statement

`apps/web/src/style.css` retained pre-component-library global selectors (`.app-header`, `.modal*`, `.btn-close`, `.badge`, `.badge.gxp`, `.authoring-canvas-layout`, and `!important` declarations) that collided with scoped rules in `AppShell.vue` and shared `ClinicalModal.vue` / `ClinicalDrawer.vue` primitives from `packages/ui`. At the same time, `.clinical-badge` in `packages/ui/tokens.css` used an `11px` (`0.6875rem`) font size without GxP lifecycle or readiness modifiers, forcing views (`CtmsDemoWorkspace.vue`, `EcrfLiveWorkspace.vue`, `ParticipantFormsPanel.vue`, `LocksSection.vue`, `EcrfItemTable.vue`) to hand-roll ad-hoc status pills. Finally, `--semantic-color-ai*` tokens reserved by [ADR-2635](2026-10-20-ai-in-the-canvas-is-labelled-scoped-and-ends-in-a-proposal.md) (UX-8) solely for governed AI output had leaked into `.lookup-candidate` in `apps/web/src/style.css` and 20 non-AI Vue components across safety clocks, EHR reconciliation queries, telemetry bands, ticket statuses, regulatory tags, and DICOM timepoints.

## 2. Decision Drivers & Constraints

- **Eliminate Global CSS Collisions & `!important` Escalation:** `apps/web/src/style.css` must never override `AppShell.vue`, `ClinicalModal.vue`, or `ClinicalDrawer.vue` styles or require `!important` specificity hacks, while `.skip-link:focus-visible` must remain keyboard-focusable above sticky headers via clean selector specificity.
- **Single Shared GxP Status & Readiness Badge Contract (PRD-SYS-001, PRD-CRF-015):** Clinicians inspecting 21 CFR Part 11 record lifecycle states (`draft`, `verified`, `queried`, `frozen`, `signed`, `locked`) and operational readiness states (`warning`, `error`) need a `0.75rem` (`12px`) minimum badge floor with `font-variant-numeric: tabular-nums`, Lucide iconography paired with text labels, and `>= 4.5:1` WCAG 2.2 AA contrast in both light and dark themes.
- **Strict AI Provenance Colour Exclusivity (UX-8 / ADR-2635):** `--semantic-color-ai*` must only appear in `packages/ui/tokens.css` definitions and governed AI components (`apps/web/src/components/ai/**` and `packages/ui/src/components/clinical/Ai*.vue`), enforced statically by ESLint and Vitest across `.vue`, `.css`, `.js`, and `.ts` files.

## 3. Options Considered

### Option 1: Prune Global Web Overrides, Hoist Shared Matrix/Lookup Rules, Standardize `.clinical-badge--*`, and Enforce `custom-vue/no-misused-ai-tokens` (Selected)

- **Overview:** Raise `.clinical-badge` in `packages/ui/tokens.css` to `0.75rem` with `tabular-nums` and add paired light/dark modifiers (`.clinical-badge--draft`, `--verified`, `--queried`, `--frozen`, `--signed`, `--locked`, `--warning`, `--error`); hoist shared `.clinical-visit-matrix`, `.clinical-soa-matrix`, and `.clinical-lookup-container` rules into `packages/ui/tokens.css`; scope CRF designer rules into `CrfAuthoringCanvas.vue`, `FormSectionContainer.vue`, and `CanvasFieldWidget.vue`; delete `.app-header`, `.modal*`, `.btn-close`, `.badge`, and all `!important` declarations from `apps/web/src/style.css`; migrate all non-AI `--semantic-color-ai*` usages to semantic error, warning, info, primary, or categorical tokens (pairing `RulesView.vue` `AI_ASSISTED` queries with `AiLabel.vue`); and enforce `custom-vue/no-misused-ai-tokens` in `eslint.config.mjs` plus `design_system_harmony.test.js`.
- **Pros:**
  - Stops global CSS leaks onto shared modal, drawer, and shell components while keeping the 23 Vue component exports in `packages/ui` and `apps/subject-portal` untouched.
  - Guarantees `>= 4.5:1` light and dark contrast across every GxP status badge and migrated clinical view, and blocks future `--semantic-color-ai*` drift at lint and test time.
- **Cons:**
  - Remaining legacy non-`ClinicalModal` Vue dialogs that previously relied on global `.modal` rules declare explicit `<style scoped>` modal rules until migrated onto `ClinicalModal`.

### Option 2: Add Higher-Specificity Scoped Overrides Inside `ClinicalModal.vue` and `AppShell.vue`

- **Overview:** Keep legacy selectors in `apps/web/src/style.css` and increase selector specificity in shared primitives to outrank them.
- **Pros:**
  - Smaller diff in `apps/web/src/style.css`.
- **Cons:**
  - Compounds CSS specificity debt and leaves `!important` overrides, undersized ad-hoc badges, and non-AI `--semantic-color-ai*` misuses unresolved.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Pruning colliding selectors and `!important` declarations at the source, centralizing GxP `.clinical-badge--*` modifiers and matrix/lookup styles in `packages/ui/tokens.css`, and gating `--semantic-color-ai*` via ESLint and Vitest restores clean CSS cascade boundaries and preserves UX-8 AI provenance trust.

## 5. Consequences & Trade-offs

- **Positive Impact:** `apps/web/src/style.css` contains zero `!important` rules and zero shell/modal/badge collisions; GxP lifecycle and readiness badges share a `0.75rem` tabular-numeral contract with `>= 4.5:1` light/dark contrast; and `--semantic-color-ai*` is strictly reserved for governed AI output.
- **Negative Impact / Technical Debt:** Non-AI categorical purple accents (such as regulatory tags, R language pills, and RTSM badges) must explicitly use `--semantic-color-purple-*` (or `--semantic-color-purple-inverse-text` on dark imaging/code surfaces) rather than `--semantic-color-ai*`.
- **Mitigation Strategy:** `custom-vue/no-misused-ai-tokens` in `eslint.config.mjs` and Requirement 9 in `packages/ui/tests/design_system_harmony.test.js` fail lint and unit test runs on any unauthorized `--semantic-color-ai*` usage, `!important` reintroduction, colliding global selector, or badge contrast regression.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `eslint.config.mjs`, `docs/adr/`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm --filter ui test`, `pnpm --filter web test`, `pnpm --filter subject-portal test`, `pnpm exec eslint --max-warnings 0 .`, `pnpm build`, `pnpm cadence check adrs`, `pnpm cadence check markdown`, and `pnpm cadence check deid`.
