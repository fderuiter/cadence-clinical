# ADR-2552: Centralize UI State, Focus, and Target CSS Rules

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015 (WCAG 2.2 Level AA focus appearance, disabled control feedback, pointer target sizing, and guided tour focus containment)

---

## 1. Context & Problem Statement

Across `packages/ui`, `apps/web`, and `apps/subject-portal`, interactive state styling, keyboard focus indicators, compact pointer target sizes, and guided tour focus transitions had drifted across app-level stylesheets:

1. `apps/subject-portal/src/style.css` imported `tokens.css` and `responsive.css` from `packages/ui` without importing pre-compiled shared component styles (`packages/ui/dist/index.css`), unlike `apps/web/src/style.css`.
2. Shared `ClinicalModal.vue` buttons and app-level `.btn` / `.btn-close` rules in `apps/web/src/style.css` and `apps/subject-portal/src/style.css` lacked `:disabled` / `[aria-disabled="true"]` cursor/opacity feedback and `:not(:disabled):not([aria-disabled="true"])` hover guards, allowing disabled buttons to display `cursor: pointer` and hover fill changes.
3. Focus-visible outlines hard-coded `var(--semantic-color-accent)` (`#4338ca` in light mode), falling below WCAG 2.2 SC 1.4.11 / SC 2.4.11 `3:1` non-text contrast when rendered over dark or inverse chrome surfaces in light mode (such as `OverviewView` `.overview-header-card` and `subject-portal` `.portal-footer`), while global `[tabindex]:focus-visible` selectors matched programmatic `tabindex="-1"` landmarks (`main#main-content`) and dialogs on skip-link activation.
4. Dense controls in `responsive.css`, `ParticipantsSection.vue`, `CanvasInspector.vue`, `CtmsView.vue`, `AppShell.vue`, and `OnboardingTour.vue` rendered below WCAG 2.2 SC 2.5.8 (`24×24` CSS px) on desktop or lacked `@media (pointer: coarse)` (`48×48` CSS px) touch scaling.
5. `OnboardingTour.vue` bound `useFocusTrap(popoverRef, { isOpen: isModalStep })`; when advancing from modal Step 1 to anchored Step 2 (`isModalStep` becoming `false` while `isTourOpen` stayed `true`), `useFocusTrap.deactivate()` restored focus to the background trigger instead of keeping keyboard focus inside `popoverRef`.
6. `packages/ui/accessibility-matcher.js` omitted `"wcag22aa"` from its default `axe-core` tag set.

## 2. Decision Drivers & Constraints

- **WCAG 2.2 Level AA Compliance (PRD-CRF-015):** Enforce `>= 3:1` focus-ring contrast across light, dark, and inverse chrome surfaces, suppress focus outlines on programmatic `tabindex="-1"` landmarks, guarantee `>= 24px` desktop targets and `>= 48px` coarse-pointer targets while preserving existing `48px` subject-portal touch targets, and run `wcag22aa` rules in shared and E2E accessibility suites.
- **Consistent Disabled Affordances Without Template Churn:** Centralize disabled cursor (`cursor: not-allowed`) and hover suppression across `packages/ui` and both app stylesheets without replacing native `<button>`, `<input>`, and `<table>` elements across all Vue templates or altering the 50 migrated overlays enforced by `custom-vue/no-raw-overlays` (ADR-2821).
- **Continuous Keyboard Focus Across Guided Tour Steps:** Retain keyboard focus inside `OnboardingTour` across modal-to-anchored step transitions and restore focus to the tour trigger only when the tour closes.

## 3. Options Considered

### Option 1: Centralize Tokens, Disabled Guards, Target Floors, and Reactive `restoreFocus` in `packages/ui` with App Stylesheet Alignment (Selected)

- **Overview:** Import `packages/ui/dist/index.css` in `apps/subject-portal/src/style.css`; define `--target-size-min: 24px`, `--semantic-target-size-min`, `--semantic-opacity-disabled`, and surface-aware `--semantic-color-focus-ring` (`#4338ca` on light surfaces; `var(--semantic-color-accent-inverse)` `#38bdf8` in dark mode and on `[data-surface="inverse"]`, `.surface-inverse`, `.overview-header-card`, and `.portal-footer`) in `packages/ui/tokens.css`; centralize disabled cursor/opacity rules and `:not(:disabled):not([aria-disabled="true"])` hover guards across `tokens.css`, `ClinicalModal.vue`, `apps/web/src/style.css`, and `apps/subject-portal/src/style.css`; narrow `[tabindex]:focus-visible` to `[tabindex]:not([tabindex="-1"]):focus-visible`; enforce `24px` desktop and `48px` `@media (pointer: coarse)` target floors in `responsive.css` and dense workspace controls; extend `useFocusTrap` `restoreFocus` to resolve boolean getters/refs so `OnboardingTour.vue` skips background focus restoration while `isTourOpen` remains `true`; and add `"wcag22aa"` to `packages/ui/accessibility-matcher.js`.
- **Pros:**
  - ✅ Satisfies PRD-CRF-015 across both `apps/web` and `apps/subject-portal` from a single semantic token and CSS contract.
  - ✅ Preserves all existing Vue templates, migrated clinical overlays, and `48px` subject-portal touch controls.
- **Cons:**
  - ❌ Requires rebuilding `packages/ui` (`pnpm --filter ui build`) so `packages/ui/dist/index.css` reflects `ClinicalModal.vue` updates before downstream app builds and E2E runs.

### Option 2: Patch Individual Vue Views With Per-Component Focus, Disabled, and Target Overrides

- **Overview:** Leave `packages/ui` tokens and `useFocusTrap` unchanged and patch individual views where contrast, cursor, or target-size defects appear.
- **Pros:**
  - ✅ Avoids rebuilding `packages/ui`.
- **Cons:**
  - ❌ Perpetuates stylesheet drift between `apps/web` and `apps/subject-portal` and leaves `accessibility-matcher.js` on WCAG 2.1 tags.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Centralizing `--semantic-color-focus-ring`, `--semantic-target-size-min`, disabled button/control state rules, reactive `useFocusTrap` `restoreFocus` suppression, and `wcag22aa` matcher defaults in `packages/ui` eliminates cross-app CSS drift and enforces uniform WCAG 2.2 AA behavior across clinical and participant surfaces.

## 5. Consequences & Trade-offs

- **Positive Impact:** Consistent `cursor: not-allowed` and suppressed hover transitions on disabled controls; `>= 3:1` focus-ring contrast (`7.54:1` `#4338ca` on `#ffffff`, `8.29:1` `#38bdf8` on `#0f172a`, `6.45:1` on `#1e293b`) across light, dark, and inverse surfaces; clean skip-link landmark navigation without spurious `main#main-content` outlines; `>= 24px` desktop and `>= 48px` coarse-pointer interactive targets; and uninterrupted keyboard navigation across `OnboardingTour` steps.
- **Negative Impact / Technical Debt:** New inverse chrome containers introduced in light mode should apply `data-surface="inverse"`, `.surface-inverse`, or override `--semantic-color-focus-ring: var(--semantic-color-accent-inverse)`.
- **Mitigation Strategy:** Enforced continuously via `packages/ui/tests/design_system_harmony.test.js`, `packages/ui/tests/useFocusTrap.test.js`, `apps/web/tests/components/OnboardingTour.spec.js`, and Playwright `wcag22aa` E2E suites in both apps.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`, `docs/components/`, `docs/adr/index.md`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm --filter ui build`, `pnpm -r lint`, `pnpm build`, `pnpm -r test`, `pnpm --filter subject-portal test:e2e`, `pnpm --filter web test:e2e`, and `pnpm cadence check`.
