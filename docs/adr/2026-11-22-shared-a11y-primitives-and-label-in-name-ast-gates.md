# ADR-2822: Shared Accessibility Primitives (`ClinicalIconButton`, `ClinicalDropzone`) and Label-in-Name AST Gates

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015 (WCAG 2.2 Level A & AA SC 2.5.3 Label in Name, SC 4.1.2 Name/Role/Value, and SC 2.4.6 Headings and Labels compliance), PRD-SYS-001 (21 CFR Part 11 regulated workflow integrity)

---

## 1. Context & Problem Statement

Voice-control clinicians (Dragon NaturallySpeaking, Apple Voice Control), screen-reader monitors (NVDA, JAWS), and mobile trial participants (iOS VoiceOver, Android TalkBack) encountered three systemic accessibility defects across the shared UI library, clinician web workspace, subject portal, and documentation theme:

1. **Label-in-Name Mismatches & Nested Interactive Dropzones (WCAG 2.2 SC 2.5.3 Failure F96, SC 4.1.2 Failure F20):** Six upload dropzones (`FileUploadModal.vue`, `BatchUploadDrawer.vue`, `DocumentGrid.vue`, `SynonymImportDrawer.vue`, `CellGeneTherapyCocConsole.vue`, and `WalletView.vue`) wrapped interactive children inside `<div role="button">` containers or hid selected filename status with `aria-hidden`, while buttons in `PersonaSwitcher.vue`, `AiActionButton.vue`, `TmfBinderTree.vue`, `DocumentGrid.vue`, and `AuditView.vue` overrode visible text with mismatched `aria-label` strings.
2. **Unlabeled Glyph Controls (WCAG 2.2 SC 4.1.2 Failure F68):** Glyph-only `<button>` elements across `apps/web` and `docs/.vitepress/theme/index.js` relied on `title` attributes (which mobile Safari VoiceOver and Android TalkBack ignore on touch controls) or omitted programmatic names entirely.
3. **Ambiguous Repeated Row & Card Buttons (WCAG 2.2 SC 2.4.6):** Repeated table and card buttons across eCRF, Clinical Canvas, eTMF QC, CTMS, Overview, and Subject Portal views announced generic verbs (`Verify`, `Release`, `Complete`, `Start Survey`, `Mark as Read`) without identifying the target record, while no static ESLint AST rule blocked unlabeled glyph buttons or static `aria-label` overrides from re-entering the repository.

## 2. Decision Drivers & Constraints

- **WCAG 2.2 Level A & AA Compliance (PRD-CRF-015):** Controls with visible text must begin their programmatic accessible name with that visible text (Techniques G208 / G211), glyph-only buttons must expose explicit `aria-label` names (Technique ARIA14) while hiding decorative glyphs (`aria-hidden="true"`), upload dropzones must trigger file selection via native `<button type="button">` controls (Technique G108) with zero nested buttons inside `role="button"`, and repeated row/card actions must disambiguate targets without widening compact 1280px clinical table cells or 375px mobile cards.
- **21 CFR Part 11 Compliance Boundary (PRD-SYS-001):** All `X-Change-Reason` inputs, electronic signature modals, and `data-testid` selectors must remain intact without modifying backend APIs.
- **Millisecond Pre-Commit Enforcement:** Static template and DOM `createElement("button")` defects must fail `pnpm lint` / `pnpm check` immediately, paired with Playwright `axe-core` WCAG 2.2 AA audits for dynamic labels.

## 3. Options Considered

### Option 1: Centralize `ClinicalIconButton`, `ClinicalDropzone`, Global `.sr-only`, and `createSrOnlyActionButton` in `packages/ui` Paired with Custom ESLint AST Rules (Selected)

- **Overview:** Add `ClinicalIconButton.vue` (requiring a non-empty `label` prop bound to `aria-label` and wrapping glyph slots in `aria-hidden="true"`), `ClinicalDropzone.vue` (rendering a non-`role="button"` drop container, native `<button type="button">` browse trigger, live `#selected` status slot without `aria-hidden`, and sibling remove `<button type="button">`), a global `.sr-only` utility in `packages/ui/tokens.css`, and `createSrOnlyActionButton` in `packages/ui/index.js`. Remediate shared controls (`PersonaSwitcher`, `AiActionButton`, `ClinicalQueryFlag`), adopt `ClinicalIconButton` across glyph buttons in `apps/web` and `aria-label` on `docs/.vitepress/theme/index.js`, append `.sr-only` row/card context across eCRF, Canvas, eTMF, CTMS, Overview, and Subject Portal views, enforce `custom-vue/no-unlabeled-glyph-button` and `custom-vue/no-aria-label-mismatch` in `eslint.config.mjs` (including `docs/.vitepress/theme/**/*.js`), and expand Playwright `axe-core` E2E suites.
- **Pros:**
  - ✅ Eliminates SC 2.5.3, SC 4.1.2, and SC 2.4.6 violations uniformly while keeping compact button geometry intact via `.sr-only` suffixes.
  - ✅ Blocks regressions in both Vue templates and imperative `document.createElement("button")` scripts during `pnpm lint` and `pnpm check`.
- **Cons:**
  - ❌ Requires rebuilding `packages/ui/dist` and maintaining TypeScript definitions in `packages/ui/index.d.ts` for the two new components and helper function.

### Option 2: Add Ad-Hoc `aria-label` Attributes Inline Without Shared Primitives or ESLint AST Rules

- **Overview:** Patch individual templates with inline `aria-label` strings while leaving `<div role="button">` dropzones and ESLint rules unchanged.
- **Pros:**
  - ✅ Avoids introducing new exports in `packages/ui`.
- **Cons:**
  - ❌ Leaves nested `<button>` controls inside `<div role="button">` dropzones, risks new SC 2.5.3 Failure F96 mismatches whenever visible button copy changes, and provides no pre-commit AST gate.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Providing `ClinicalIconButton`, `ClinicalDropzone`, `.sr-only`, and `createSrOnlyActionButton` from `packages/ui` alongside `custom-vue/no-unlabeled-glyph-button` and `custom-vue/no-aria-label-mismatch` AST lint gates fixes root-cause voice-control and screen-reader barriers and prevents regressions automatically.

## 5. Consequences & Trade-offs

- **Positive Impact:** Reliable Dragon NaturallySpeaking / Voice Control activation via leading visible verbs, unambiguous NVDA / JAWS / VoiceOver / TalkBack announcements on glyph buttons, dropzones, and repeated table/card actions, and zero WCAG 2.2 Level A/AA `axe-core` violations across `/etmf`, `/training`, `/ssu`, upload drawers/modals, and Subject Portal `#view-wallet`.
- **Negative Impact / Technical Debt:** Appending `.sr-only` context lengthens full DOM `.textContent` on repeated row/card buttons (requiring unit tests that assert exact `.textContent` rather than visible layout to include the `.sr-only` suffix) and slightly lengthens screen-reader rotor announcements.
- **Mitigation Strategy:** Keep leading verbs concise and place disambiguating record identifiers after the visible verb inside `.sr-only` spans per WCAG Technique G208.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`, `docs/.vitepress/theme`, `eslint.config.mjs`, `package.json`, `docs/components/index.md`, `docs/adr/index.md`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm lint`, `pnpm --filter web typecheck`, `pnpm build`, `pnpm build:demo`, `pnpm test`, `pnpm --filter web test:e2e`, `pnpm --filter subject-portal test:e2e`, `pnpm docs:build`, and `pnpm check`.
