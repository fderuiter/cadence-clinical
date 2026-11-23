# ADR-2652: Shared `ClinicalTabs`, Stack-Aware Focus Fallbacks, and Hub Accessibility AST Gates

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015 (WCAG 2.2 Level A & AA SC 1.3.1 Info and Relationships, SC 2.1.1 Keyboard, SC 2.4.3 Focus Order, SC 2.5.8 Target Size Minimum, and SC 4.1.2 Name, Role, Value compliance), PRD-SYS-001 (21 CFR Part 11 regulated workflow integrity)

---

## 1. Context & Problem Statement

Across the six live operational hubs in `apps/web` (`/etmf`, `/tickets`, `/knowledge`, `/ctms`, `/ssu`, and `/training`) and `packages/ui`, keyboard-only clinicians, screen-reader monitors, and automated accessibility gates encountered six systemic WCAG 2.2 Level A & AA barriers:

1. **Non-Standard Tab Bars (WCAG 2.2 SC 2.1.1, SC 4.1.2):** Seven operational tab switchers (`CtmsLiveWorkspace.vue` / `CtmsDemoWorkspace.vue`, `DocumentManagerView.vue`, `TicketsView.vue`, `TicketDetailDrawer.vue`, `KnowledgeView.vue`, `ProtocolTrainingConsole.vue`, and `SsuGreenlightConsole.vue`) either omitted `role="tablist"` / `role="tab"` / `role="tabpanel"` semantics entirely or lacked WAI-ARIA roving `tabindex` (`0` / `-1`) and `ArrowLeft`, `ArrowRight`, `ArrowUp`, `ArrowDown`, `Home`, and `End` keyboard navigation.
2. **Focus Loss to `<body>` When Trigger Controls Unmount (WCAG 2.2 SC 2.4.3):** `useFocusTrap` restored focus only to `previousActiveElement` on deactivation. When completing an eTMF technical QC adjudication (`CraQcQueue.vue`) removed the inspected row or completing a 21 CFR Part 11 electronic signature (`TicketSignModal.vue` stacked inside `TicketDetailDrawer.vue`) unmounted `#btn-open-part11-sign`, `previousActiveElement.isConnected` evaluated to `false` and keyboard focus dropped to `document.body`.
3. **Interactive Tree & 2D Grid Regressions in `/etmf` (WCAG 2.2 SC 2.1.1, SC 4.1.2):** `TmfBinderTree.vue` nested `<button class="btn-node-export">` inside `role="treeitem"`, hardcoded `aria-selected="false"` on artifact treeitems, lacked `Home`/`End` navigation, and lost roving `tabindex="0"` when filtering hid the active node. `DocumentGrid.vue` and `use2DGrid.js` clamped `activeRowIndex` to `-1` while `rowCount` started at `0` (leaving zero `tabindex="0"` gridcells after async document loads), declared `aria-colcount="7"` on an 8-column `<table>`, and called `event.preventDefault()` on `Enter` / `Space` before child `Inspect` and `Preview` buttons could activate natively.
4. **Click-Only `<div>` Cards & Module Items (WCAG 2.2 SC 2.1.1, SC 4.1.2):** Interactive KRI filter cards, Kanban ticket cards, SOP cards, and protocol training module items in `TicketsView.vue`, `KnowledgeView.vue`, and `ProtocolTrainingConsole.vue` used click-only `<div>` elements (several masked with `eslint-disable vuejs-accessibility/click-events-have-key-events` comments).
5. **Unlabeled Form Controls & Undersized `.tag-chip` Targets (WCAG 2.2 SC 1.3.1, SC 2.5.8, SC 4.1.2):** Search inputs, filter `<select>` controls, checkboxes, and form groups across `TicketsView.vue`, `KnowledgeView.vue`, `CtmsDemoWorkspace.vue`, and `SsuGreenlightConsole.vue` lacked programmatic accessible names (masked by `eslint-disable vuejs-accessibility/label-has-associated-control` directives), and `.tag-chip` filter pills in `KnowledgeView.vue` rendered below the `24x24` CSS pixel minimum target size.
6. **Missing Static Suppression Gate & Incomplete E2E Route Coverage:** No ESLint rule prevented `eslint-disable ... vuejs-accessibility/*` directives in `packages/ui` (`MediaPlayer.vue`) or live operational hubs, and `apps/web/tests/e2e/web-accessibility.spec.ts` did not run `axe-core` WCAG 2.2 Level A & AA assertions across `/tickets`, `/knowledge`, `/ctms`, or `/onboarding/trial`.

## 2. Decision Drivers & Constraints

- **WCAG 2.2 Level A & AA Compliance (PRD-CRF-015):** Standardize WAI-ARIA Tabs APG semantics, deterministic focus restoration when triggers unmount, non-nested ARIA treeitems, 2D roving `tabindex` grid navigation, native `<button type="button">` interactive cards, associated form labels, and `>= 24x24` CSS pixel pointer targets.
- **21 CFR Part 11 & Demo-Boundary Preservation (PRD-SYS-001, ADR-2645, ADR-2822):** Keep all step-up signature payloads, `X-Change-Reason` headers, leading visible button verbs with `.sr-only` suffixes, and `isDemoBuild()` gates for unbacked demo workflows intact without backend contract changes.
- **Automated Regression Prevention:** Block `vuejs-accessibility` lint suppressions statically in `eslint.config.mjs` across `packages/ui`, `apps/subject-portal`, and live `apps/web` hubs while gating all live operational routes in Playwright `axe-core` E2E tests.

## 3. Options Considered

### Option 1: Centralize `ClinicalTabs` and Stack-Aware `useFocusTrap` Fallbacks in `packages/ui`, Remediate Live Hubs, and Enforce AST + Playwright Gates (Selected)

- **Overview:**
  1. Add `ClinicalTabs.vue` to `packages/ui` implementing `role="tablist"`, `role="tab"`, `aria-selected`, `aria-controls`, roving `tabindex` (`0` / `-1`), optional `#panel` slot (`role="tabpanel"`, `aria-labelledby`), and `ArrowLeft`, `ArrowRight`, `ArrowUp`, `ArrowDown`, `Home`, and `End` navigation skipping disabled tabs, and adopt it across all seven operational tab bars.
  2. Extend `useFocusTrap` (and `OVERLAY_SHARED_PROPS` on `ClinicalModal` / `ClinicalDrawer`) with `options.fallbackFocus` (element, Vue ref, CSS selector, or getter) and automatic fallback to the active parent trap container in `activeTrapStack` when `previousActiveElement` has disconnected from the DOM; wire `fallbackFocus` in `CraQcQueue.vue`, `TicketDetailDrawer.vue`, and `TicketSignModal.vue`.
  3. Fix `use2DGrid.js` (`0, 0` recovery when `rowCount` transitions from `0` to `> 0`, skipping grid `Enter`/`Space` interception when `event.target` is an interactive descendant), `DocumentGrid.vue` (`aria-colcount="8"`), and `TmfBinderTree.vue` (scoped `.btn-node-export` buttons moved outside `role="tree"`, dynamic `aria-selected`, `Home`/`End` keys, and filter clamping of `activeFocusedNodeId`).
  4. Convert click-only cards and module items to native `<button type="button">`, associate programmatic labels across all hub inputs/selects/checkboxes/fieldsets, raise `.tag-chip` in `KnowledgeView.vue` to `min-height: 24px; min-width: 24px`, remove the `vuejs-accessibility` suppression in `MediaPlayer.vue`, enforce `custom-vue/no-vuejs-a11y-suppression` in `eslint.config.mjs`, and expand `apps/web/tests/e2e/web-accessibility.spec.ts` across `/etmf`, `/tickets`, `/knowledge`, `/ctms`, `/ssu`, `/training`, and `/onboarding/trial`.
- **Pros:**
  - ✅ Eliminates duplicated tab keyboard wiring across seven views through a single tested `packages/ui` primitive.
  - ✅ Guarantees focus never falls to `document.body` after single-item QC adjudication or nested 21 CFR Part 11 sign-off.
  - ✅ Enforces zero `vuejs-accessibility` suppressions in `packages/ui` and live hubs at lint time and full WCAG 2.2 AA compliance in Playwright E2E.
- **Cons:**
  - ❌ Increases the shared `packages/ui` component catalog from 23 to 24 components and requires keeping unmigrated demo-only screens scoped outside the new suppression-ban glob until migrated.

### Option 2: Patch Individual Hub Views Inline Without Shared `ClinicalTabs` or `useFocusTrap` Stack Fallbacks

- **Overview:** Hand-roll `role="tablist"` key handlers and `nextTick()` focus workarounds directly inside each `apps/web` view without modifying `packages/ui` or `eslint.config.mjs`.
- **Pros:**
  - ✅ Avoids rebuilding `packages/ui/dist` and updating `packages/ui/index.d.ts`.
- **Cons:**
  - ❌ Duplicates roving `tabindex` and disabled-tab skip logic across seven components, leaves `ClinicalModal` / `ClinicalDrawer` vulnerable to `document.body` focus drops when triggers unmount, and allows `eslint-disable vuejs-accessibility/*` comments to re-enter live clinical routes.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Centralizing `ClinicalTabs` and stack-aware `useFocusTrap` fallback restoration in `packages/ui` eliminates duplicated ARIA keyboard state machines, resolves nested overlay focus-drop bugs structurally, and pairs fast ESLint AST enforcement with end-to-end Playwright `axe-core` verification across every live operational hub.

## 5. Consequences & Trade-offs

- **Positive Impact:** Consistent keyboard and screen-reader behavior across all seven operational tab bars, deterministic focus retention during eTMF QC and Ticket Part 11 sign-off workflows, valid ARIA tree and 2D grid semantics in `/etmf`, native `<button>` activation on interactive cards, `>= 24px` tag filter targets, zero `vuejs-accessibility` suppressions across `packages/ui` and live hubs, and automated WCAG 2.2 Level A & AA E2E coverage across `/etmf`, `/tickets`, `/knowledge`, `/ctms`, `/ssu`, `/training`, and `/onboarding/trial`.
- **Negative Impact / Technical Debt:** Unmigrated demo-only views outside `AVAILABLE_SCREENS` / `ALWAYS_AVAILABLE` remain excluded from `custom-vue/no-vuejs-a11y-suppression` until promoted to live backend-wired routes.
- **Mitigation Strategy:** Expand the live hub file list in `eslint.config.mjs` whenever a view is promoted into `AVAILABLE_SCREENS` (`apps/web/src/router/availability.js`).

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `eslint.config.mjs`, `docs/components/index.md`, `packages/ui/README.md`, `docs/adr/index.md`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm --filter ui build`, `pnpm --filter ui test`, `pnpm lint`, `pnpm exec eslint --max-warnings 0 .`, `pnpm test`, `pnpm build`, `pnpm build:demo`, `pnpm docs:build`, and `PLAYWRIGHT_CHROME_EXECUTABLE=/usr/bin/google-chrome pnpm --filter web exec playwright test --project=accessibility`.
