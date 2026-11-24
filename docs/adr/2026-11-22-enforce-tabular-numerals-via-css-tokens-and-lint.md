# ADR-2670: Enforce Tabular Numerals via Shared CSS Tokens and AST Lint

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (21 CFR Part 11 regulated UI consistency and transcription clarity), PRD-CRF-015 (WCAG 2.2 Level AA form usability, coarse-pointer input sizing, and tabular numeral alignment)

---

## 1. Context & Problem Statement

Under [ADR-2166](2026-08-08-standardize-semantic-token-layer-across-platforms.md) and [ADR-2650](2026-11-22-consolidate-shared-ui-css-aliases-and-table-numerals.md), `packages/ui/tokens.css` enforced `font-variant-numeric: tabular-nums` globally on `table`, `th`, `td`, `.clinical-table`, `.clinical-visit-matrix`, and `.data-table`. However, three typography gaps remained across `packages/ui`, `apps/web`, and `apps/subject-portal`:

1. **Browser User-Agent `font` Resets on Form Controls:** User-agent stylesheets reset shorthand `font` on `<input>` and `<select>`, discarding inherited `font-variant-numeric: tabular-nums` even inside `<td>` cells (`EcrfItemTable.vue`), `ClinicalInput.vue`, and numeric/date inputs (`ObservationView.vue`, `clinical-fields.js`).
2. **Non-Table Clinical Readouts & Dynamic Timers:** KPI and telemetry cards (`LabGovernanceConsole.vue`, `SensorTelemetryConsole.vue`), participant stipends and wallet balances (`WalletView.vue`, `ComplianceView.vue`), VAS pain slider badges (`clinical-fields.js`), live SLA/sync/trial countdowns (`OverviewView.vue`, `TicketsView.vue`, `SyncDrawer.vue`), `<time>` elements, and count pills rendered proportional Inter digits, causing horizontal layout jitter during live updates and misaligning stacked decimal columns.
3. **Lack of Static Analysis Guardrails:** `design_system_harmony.test.js` only inspected `.vue` files matching `/<table[\s>]/`, and `eslint.config.mjs` had no AST rule to prevent unaligned numeric controls, countdowns, badges, KPI readouts, or `font-variant-numeric: normal` overrides.

## 2. Decision Drivers & Constraints

- **Single Shared CSS Contract (PRD-SYS-001):** Both clinician (`apps/web`) and participant (`apps/subject-portal`) profiles share `packages/ui/tokens.css`; tabular numeral tokens, utility selectors, and UA form-control overrides must live once in `packages/ui/tokens.css` with zero local token overrides or DOM structure mutations.
- **Typographic Separation of Concerns:** Inter with `font-variant-numeric: tabular-nums` governs quantitative clinical values, dates, times, timers, counts, and currency readouts; `--font-mono` (`JetBrains Mono`) remains strictly reserved for clinical identifiers, protocol versions, `NodeRef`s, CDISC variable names, and Part 11 signature hashes; narrative `<textarea>` controls and multi-sentence prose paragraphs retain proportional digits.
- **Touch Viewport Safety (PRD-CRF-015):** Form controls (`<input>`, `<select>`, `<textarea>`) on coarse-pointer viewports (`@media (pointer: coarse)`) must preserve `font-size >= 16px` (`1rem`) so iOS Safari never auto-zooms during bedside or ePRO data entry.
- **Zero-Regression CI Gate:** New Vue templates, CSS stylesheets, and imperative widget helpers must fail `pnpm lint` and `pnpm --filter ui test` immediately if numeric surfaces omit tabular numeral alignment or override `font-variant-numeric` to `normal`.

## 3. Options Considered

### Option 1: Extend `packages/ui/tokens.css`, `ClinicalInput` / `ClinicalFormField`, Harmony Tests, and `eslint.config.mjs` with `custom-vue/require-tabular-nums` (Selected)

- **Overview:** Define `--font-variant-numeric-tabular: tabular-nums` and `--semantic-font-variant-numeric-tabular` in `packages/ui/tokens.css`; extend global selectors to `time`, `.tabular-nums`, `[data-tabular-nums]:not([data-tabular-nums="false"])`, and `.clinical-badge`; override browser UA `font` resets on numeric/date `<input>` types, `inputmode="numeric" | "decimal"`, `td input`, `td select`, and opted-in `.clinical-input-control` elements while excluding `<textarea>` and enforcing `font-size: max(1rem, var(--font-size-md, 16px))` under `@media (pointer: coarse)`; propagate `data-tabular-nums` in `ClinicalInput.vue` and `ClinicalFormField.vue`; adopt `.tabular-nums` / `data-tabular-nums` across clinician web and participant portal views; expand `Requirement 6` in `packages/ui/tests/design_system_harmony.test.js`; and enforce `custom-vue/require-tabular-nums` in `eslint.config.mjs`.
- **Pros:**
  - Fixes browser UA `font` resets across all table cells and numeric/date inputs from a single shared CSS rule without changing DOM hierarchy.
  - Eliminates horizontal layout jitter in live timers, VAS slider badges, count pills, and stacked KPI/wallet readouts while keeping `<textarea>` narratives and multi-sentence prose proportional.
  - Prevents regressions at author time via AST lint checks across `.vue`, `.css`, and `.js`/`.ts` UI modules.
- **Cons:**
  - `type="text"` inputs bound to multi-kind models (such as `ObservationView.vue` and `EcrfItemTable.vue`) require dynamic `:data-tabular-nums` bindings so free-text observations remain proportional while numeric/date observations align.

### Option 2: Apply `font-family: var(--font-mono)` to All Numeric Inputs and KPI Readouts

- **Overview:** Style numeric inputs, badges, timers, and KPI cards with `JetBrains Mono` instead of Inter OpenType `tabular-nums`.
- **Pros:**
  - Monospace glyphs naturally occupy fixed horizontal advance widths.
- **Cons:**
  - Violates the Cadence Clinical typography hierarchy by blurring machine identifiers, `NodeRef`s, and cryptographic hashes (`JetBrains Mono`) with human-readable vitals, lab values, and participant stipends (`Inter` with `tabular-nums`).

### Option 3: Rely Exclusively on Per-Component `<style scoped>` Rules Without ESLint Enforcement

- **Overview:** Add `font-variant-numeric: tabular-nums` locally inside individual `.vue` `<style scoped>` blocks as issues are noticed.
- **Pros:**
  - Requires no changes to `eslint.config.mjs` or `packages/ui/tokens.css`.
- **Cons:**
  - Reintroduces duplicated component CSS abolished in ADR-2650 and provides no automated protection against future unaligned inputs, timers, or KPIs.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Extending `packages/ui/tokens.css` with `--font-variant-numeric-tabular`, UA form-control overrides, and `.tabular-nums` / `[data-tabular-nums]` opt-in hooks, paired with `ClinicalInput` / `ClinicalFormField` attribute propagation, full-codebase harmony assertions, and `custom-vue/require-tabular-nums` AST linting, delivers uniform tabular numeral alignment across both frontend applications without sacrificing proportional prose legibility, `--font-mono` identifier semantics, or `>= 16px` touch-viewport ergonomics.

## 5. Consequences & Trade-offs

- **Positive Impact:** Quantitative clinical values, eCRF table controls, VAS slider badges, live countdowns, unread/sync badges, and participant wallet figures render with fixed-width Inter numerals across `packages/ui`, `apps/web`, and `apps/subject-portal`, backed by deterministic Vitest and ESLint gates.
- **Negative Impact / Technical Debt:** Custom `<input type="text">` or `<select>` controls bound to numeric models via `v-model.number` must include `inputmode="numeric" | "decimal"`, `class="tabular-nums"`, or `data-tabular-nums="true"` to satisfy `custom-vue/require-tabular-nums`.
- **Mitigation Strategy:** `ClinicalInput` and `ClinicalFormField` automatically set `data-tabular-nums="true"` for numeric/date types and `inputmode` props, and `custom-vue/require-tabular-nums` emits actionable error messages naming the exact attribute or class to add.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`, `eslint.config.mjs`, `docs/components/index.md`, `docs/adr/`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm --filter ui test`, `pnpm --filter web test`, `pnpm --filter subject-portal test`, `pnpm lint`, `pnpm build`, `pnpm cadence check eslint`, `pnpm cadence check prettier`, `pnpm cadence check adrs`, `pnpm cadence check markdown`, `pnpm cadence check readmes`, and `pnpm cadence check deid`.
