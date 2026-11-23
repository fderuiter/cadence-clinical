# ADR-2650: Consolidate Shared UI CSS Aliases, Focus Resets, and Table Numerals

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (21 CFR Part 11 regulated UI consistency and validation cues), PRD-CRF-015 (WCAG 2.2 Level AA keyboard focus indicators, 48px touch targets, and tabular numeral alignment)

---

## 1. Context & Problem Statement

Both frontend applications (`apps/web/src/style.css` and `apps/subject-portal/src/style.css`) already import `packages/ui/tokens.css` and `packages/ui/responsive.css`, yet each stylesheet separately maintained copies of the 23 `:root` semantic custom-property aliases (`--primary` through `--font`, including `--border-control`), the universal `*` `box-sizing` reset, keyboard `:focus-visible` outlines, `.form-actions` bar spacing, `.btn-primary` brand colors, and `.touch-target` / `.min-touch-target` / `.touch-target-interactive` sizing selectors. In addition, Vue single-file components across `apps/web/src` and `apps/subject-portal/src` repeated `<style scoped>` `font-variant-numeric: tabular-nums` rules on `<table>` elements even though `packages/ui/tokens.css` already applies `font-variant-numeric: tabular-nums` globally to `table`, `th`, `td`, `.clinical-table`, `.clinical-visit-matrix`, and `.data-table` under [ADR-2166](2026-08-08-standardize-semantic-token-layer-across-platforms.md).

## 2. Decision Drivers & Constraints

- **Single Shared Stylesheet Source (PRD-SYS-001):** Updating a shared semantic alias, `.btn-primary` brand rule, `.form-actions` layout, or keyboard focus ring must happen once in `packages/ui` without adding new stylesheet package exports or manually copying rules across two application stylesheets.
- **WCAG 2.2 AA Specificity & Contrast Guarantees (PRD-CRF-015):** Global `:focus-visible` selectors must stay strictly below `[aria-invalid="true"]:focus-visible`, `.skip-link:focus-visible`, and `.form-group input:focus-visible` in CSS specificity so validation-error and skip-link focus rings always win, while dark-theme scopes (`[data-theme="dark"]`, `.dark`, and `:root[data-theme="auto"]` under `prefers-color-scheme: dark`) must recompute color aliases on both root and subtree containers.
- **Zero Computed-Style Drift:** App-specific `.btn` padding and border styles, `.card`, `.modal`, `.skip-link`, `.btn-danger-solid`, portal button target lists, shared component styles in `packages/ui/src/components/`, and non-table numeric rules (`.numeric`, `.activity-time`, `.pulse-value`) must remain intact.

## 3. Options Considered

### Option 1: Hoist Shared Aliases, Resets, Focus Outlines, and Utilities into `packages/ui/tokens.css` and `packages/ui/responsive.css` (Selected)

- **Overview:** Move the 23 `:root` semantic aliases (with dark-mode color alias mirrors), universal `*` reset, `:where([role="button"], [tabindex]):focus-visible` low-specificity focus rules, `.form-actions`, and `.btn-primary` brand colors into `packages/ui/tokens.css`; consolidate 48px `.touch-target`, `.min-touch-target`, and `.touch-target-interactive` helpers in `packages/ui/responsive.css`; delete the duplicate rules from `apps/web/src/style.css` and `apps/subject-portal/src/style.css`; remove redundant `<style scoped>` table `tabular-nums` blocks (and resulting empty `<style scoped>` tags) from application `.vue` files; and update `packages/ui/tests/design_system_harmony.test.js` to enforce the centralized contract.
- **Pros:**
  - ✅ Eliminates duplicated CSS maintenance across clinician web and participant portal stylesheets without adding new `package.json` stylesheet exports.
  - ✅ Preserves computed styles, WCAG 2.2 AA `>= 4.5:1` light/dark contrast, `48px` touch targets, and global `tabular-nums` table alignment while freeing Vue components from boilerplate `<style scoped>` table blocks.
- **Cons:**
  - ❌ App-specific button geometry (`.btn` padding/border) remains locally scoped in each app stylesheet and therefore pairs with `.btn.btn-primary` specificity in `tokens.css` so primary borders win over `.btn` defaults.

### Option 2: Introduce a Third Shared Base Stylesheet Export (`ui/base.css`)

- **Overview:** Keep `tokens.css` strictly limited to custom properties and export a new `ui/base.css` stylesheet imported by both apps.
- **Pros:**
  - ✅ Separates custom-property declarations from reset and element selectors.
- **Cons:**
  - ❌ Expands `packages/ui/package.json` exports unnecessarily when `packages/ui/tokens.css` already houses global `tabular-nums`, `.clinical-input`, `.sr-only`, and reduced-motion rules.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Hoisting shared aliases, resets, focus indicators, `.form-actions`, and `.btn-primary` into `packages/ui/tokens.css` and consolidating touch-target helpers in `packages/ui/responsive.css` fulfills ADR-2166's single-source mandate with zero new package exports and zero computed-style regressions.

## 5. Consequences & Trade-offs

- **Positive Impact:** One source of truth in `packages/ui` governs shared aliases, focus outlines, form actions, primary button colors, 48px touch-target utilities, and global table `tabular-nums` alignment across both frontend applications.
- **Negative Impact / Technical Debt:** Application `.vue` files that format numerals outside `<table>` elements (such as `.numeric`, `.activity-time`, and `.pulse-value`) continue to declare `font-variant-numeric: tabular-nums` locally on their non-table selectors.
- **Mitigation Strategy:** `packages/ui/tests/design_system_harmony.test.js` verifies centralized `:root` and dark-theme aliases, `.btn-primary` light/dark contrast, global `tokens.css` table coverage, and the absence of redundant `<style scoped>` table `tabular-nums` blocks or empty `<style>` tags.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`, `docs/adr/`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm --filter ui test`, `pnpm --filter web test`, `pnpm --filter subject-portal test`, `pnpm lint`, `pnpm build`, `pnpm cadence check adrs`, `pnpm cadence check markdown`, and `pnpm cadence check deid`.
