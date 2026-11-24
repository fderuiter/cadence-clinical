# ADR-2824: Standardize Clinical Typography Utilities, 12px Floor, and AST Lints

- **Status:** Accepted
- **Date:** 2026-11-24
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-UI-001, PRD-A11Y-001

---

## 1. Context & Problem Statement

Clinical trial operations and participant ePRO surfaces require zero-ambiguity glyph distinction (`I`/`l`/`1`, `O`/`0`) on structured clinical identifiers, equal-width numeral alignment (`tabular-nums`) on KPIs and balances, and a minimum `12px` (`0.75rem`) caption floor for legibility under clinical lighting and WCAG 2.2 AA `320px` reflow (`CLINICAL_CANVAS.md` §2.13). Prior to this change:

1. `--font-mono` in `packages/ui/tokens.css` placed the generic `monospace` keyword first (`monospace, "JetBrains Mono", "Fira Code"`), causing browsers to stop at the OS generic monospace font and never reach `"JetBrains Mono"` or `ui-monospace`, and no semantic `--semantic-font-mono` alias or `.font-mono` / `.numeric` utility classes were exposed.
2. Hundreds of scoped and inline style declarations across `packages/ui`, `apps/web`, and `apps/subject-portal` hardcoded sub-`12px` font sizes (`9px`–`11px`, `0.6rem`–`0.74rem`) and bespoke `font-family` stacks (`monospace`, `ui-monospace, ...`, `Arial, sans-serif`, `system-ui, ...`).
3. Structured clinical identifiers (study/site/subject codes, CDISC `VSTESTCD` concepts, observation values, revision badges, SHA-256 hashes, eCRF form/item IDs, and masked ACH/card digits) lacked consistent `--semantic-font-mono` styling, and `apps/subject-portal/src/style.css` diverged from `apps/web/src/style.css` on Inter character-variant OpenType features (`"cv05" 1, "cv08" 1`).
4. ESLint enforced tabular numerals (`ADR-2670`) but had no AST/CSS lint rules preventing new sub-`12px` `font-size` declarations or custom `font-family` stacks from regressing into `.vue`, `.css`, or `.js`/`.ts` files.

## 2. Decision Drivers & Constraints

- **Glyph Disambiguation (`CLINICAL_CANVAS.md` §2.13):** Platform-first `--font-mono` (`"JetBrains Mono", ui-monospace, "SFMono-Regular", "Cascadia Mono", "Fira Code", Menlo, Consolas, monospace`) and Inter character variants (`"cv05" 1, "cv08" 1`) eliminate lookalike character misreads across clinician and participant surfaces.
- **12px Clinical Legibility Floor & WCAG 2.2 AA Reflow:** Every caption, badge, and timestamp must render at `>= 12px` (`--font-size-2xs: 12px`) without horizontal overflow at `320px` CSS width, while preserving `>= 16px` (`1rem`) on `@media (pointer: coarse)` form controls and `48px` (`--semantic-touch-target-min`) touch targets.
- **Compatibility with Upstream Tabular-Nums Architecture ([ADR-2670](2026-11-22-enforce-tabular-numerals-via-css-tokens-and-lint.md)):** Preserve proportional `<textarea>` inputs, retain fixed DOM IDs and HTML tag names in `apps/subject-portal`, and integrate cleanly with `custom-vue/require-tabular-nums`.
- **Automated Regression Prevention:** Enforce the `12px` floor and tokenized font stacks statically in ESLint across `.vue`, `.css`, and `.js`/`.ts` files with zero suppressions.

## 3. Options Considered

### Option 1: Shared Token & Utility Standardization + Codebase Sweep + ESLint AST/CSS Rules (Selected)

- **Overview:** Fix `--font-mono` ordering in `packages/ui/tokens.css`, add `--semantic-font-mono`, `--font-size-2xs: 12px`, `--semantic-font-size-2xs`, `.font-mono`, and `.numeric`, align `body` OpenType features across `apps/web` and `apps/subject-portal`, migrate all sub-`12px` `font-size` and custom `font-family` declarations across `packages/ui`, `apps/web`, and `apps/subject-portal` to shared tokens while applying `--semantic-font-mono` to clinical identifiers, and enforce `custom-vue/no-sub12px-font-size` and `custom-vue/no-custom-font-stack` alongside `custom-vue/require-tabular-nums` in `eslint.config.mjs`.
- **Pros:**
  - Single source of truth in `packages/ui/tokens.css` consumed by both clinician and subject-portal profiles.
  - Immediate, deterministic AST/CSS lint feedback in `pnpm lint` and `pnpm cadence check` blocking sub-`12px` sizes, non-tokenized `font-family` stacks, and generic-first `--font-mono` ordering.
  - Preserves all fixed DOM IDs, HTML tag names, proportional `<textarea>` rules, and coarse-pointer `>= 16px` sizing.
- **Cons:**
  - Broad mechanical touch across Vue `<style>` and inline style declarations to eliminate legacy sub-`12px` and raw `font-family` literals.

### Option 2: Global CSS `!important` Overrides Without AST Lint Rules

- **Overview:** Attempt to clamp font sizes or override `font-family` globally via CSS selectors without migrating component styles or adding ESLint rules.
- **Pros:**
  - Smaller initial diff.
- **Cons:**
  - CSS cannot declaratively clamp arbitrary component `font-size` declarations to `>= 12px`, `!important` overrides break specificity contracts, and regressions silently accumulate without static lint gates.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Tokenizing `--font-mono` and `--font-size-2xs` in `packages/ui/tokens.css`, sweeping legacy sub-`12px` and raw font-stack declarations, styling clinical identifiers with `--semantic-font-mono`, and enforcing static ESLint rules guarantees durable compliance with `CLINICAL_CANVAS.md` §2.13 and WCAG 2.2 AA.

## 5. Consequences & Trade-offs

- **Positive Impact:** Zero sub-`12px` font sizes and zero custom `font-family` stacks remain across `packages/ui`, `apps/web`, and `apps/subject-portal`; clinical identifiers consistently render in `JetBrains Mono` / platform monospace, and regressions fail fast in `pnpm lint` and `packages/ui/tests/design_system_harmony.test.js`.
- **Negative Impact / Technical Debt:** Slightly wider badge text at `12px` (`0.75rem`) compared to legacy `10px`/`11px` rules.
- **Mitigation Strategy:** Verified `320px` mobile reflow (`scrollWidth <= innerWidth`) and wrapping flex/grid layouts across Clinical Canvas, eCRF, CTMS, and Subject Portal views.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`, `eslint.config.mjs`, `docs/adr`.
- **Verification Plan:** Verified via `pnpm lint`, `pnpm -r typecheck`, `pnpm build`, `pnpm --filter ui test` (`design_system_harmony.test.js`), `pnpm --filter subject-portal test`, `pnpm --filter web test`, `320px` viewport reflow checks, and `pnpm cadence check --base origin/main`.
