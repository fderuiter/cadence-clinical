# ADR-2544: Shared Reactive Focus Containment and Global Reduced-Motion Tokens

- **Status:** Accepted
- **Date:** 2026-11-19
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015 (WCAG 2.1 AA keyboard focus containment and accessibility auditing)

---

## 1. Context & Problem Statement

Modal dialogs, slide-over drawers, and tab bars across the clinician workspace (`apps/web`), the participant portal (`apps/subject-portal`), and the shared design system (`packages/ui`) previously relied on fragmented focus-management helpers: a partial Vue composable in `apps/web/src/composables/useFocusTrap.js`, an imperative helper in `apps/subject-portal/src/components/focus-trap.js`, and inline `keydown` listeners in individual components. As a result, several dialogs and drawers lacked `Tab`/`Shift+Tab` focus wrapping or trigger restoration, offscreen persistent drawers remained reachable in the tab order when closed, participant portal tab navigation tracked only four of its six views imperatively, and continuous pulse/slide animations lacked a shared `prefers-reduced-motion: reduce` baseline.

## 2. Decision Drivers & Constraints

- **WCAG 2.1 AA Compliance:** Keyboard users and assistive technologies require deterministic focus containment inside active `role="dialog"` / `role="alertdialog"` overlays, focus restoration on dismissal, inert offscreen drawers, and roving-tabindex navigation across all six participant portal tabs.
- **21 CFR Part 11 & Mandatory Lock Guardrails:** Mandatory security PIN lock dialogs (`PinSetupModal`, `PinUnlockModal`) must contain focus while blocking `Escape` dismissal, whereas electronic signature and step-up re-authentication modals must immediately wipe typed credentials when dismissed via `Escape`.
- **Single Shared UI Source:** Per repository architecture guidelines, shared UI primitives, composables, and design tokens belong in `packages/ui` rather than duplicated per-application copies.
- **Vestibular Accessibility:** Users with `prefers-reduced-motion: reduce` enabled must have decorative infinite pulse animations halted and slide/fade transitions collapsed without hiding semantic status indicators.

## 3. Options Considered

### Option 1: Rewrite All Overlays onto Native HTML `<dialog>` Elements

- **Overview:** Replace every existing `.vue` modal and drawer layout across `packages/ui`, `apps/web`, and `apps/subject-portal` with `<dialog>` and `showModal()`.
- **Pros:**
  - ✅ Browser-managed top-layer rendering.
- **Cons:**
  - ❌ High visual regression blast radius across 46 clinical modals, multi-step consoles, and slide-over drawers, plus inconsistent `jsdom` behavior in existing synchronous unit test suites.

### Option 2: Centralize Reactive `useFocusTrap` and Reduced-Motion Rules in `packages/ui` (Selected)

- **Overview:** Implement a shared reactive `useFocusTrap` (with stacked-overlay coordination, `escapeDeactivates`, `onEscape`, `initialFocus`, and `restoreFocus`) and `useEscapeClose` in `packages/ui`, wire all modal/drawer views across `packages/ui`, `apps/subject-portal`, and `apps/web` to it, drive all six participant portal tabs reactively from `routes.js` + `ui.currentView`, and add a global `@media (prefers-reduced-motion: reduce)` rule in `packages/ui/tokens.css`.
- **Pros:**
  - ✅ Eliminates duplicated focus-trap utilities across applications while preserving existing layout markup and styling.
  - ✅ Handles nested modal/drawer stacks deterministically via a shared active trap stack and excludes `[inert]` / hidden subtrees.
  - ✅ Enforces reduced-motion rules workspace-wide from the shared design-token stylesheet.
- **Cons:**
  - ❌ Persistent offscreen drawers that stay mounted in the DOM must explicitly bind `inert` and `visibility: hidden` when closed.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Centralizing `useFocusTrap`, `useEscapeClose`, and reduced-motion token rules in `packages/ui` achieves consistent WCAG 2.1 AA keyboard containment, focus restoration, and motion accessibility across all 46 overlays with minimal layout risk and full compatibility with 21 CFR Part 11 credential-clearing and PIN lock requirements.

## 5. Consequences & Trade-offs

- **Positive Impact:** One tested focus-containment implementation in `packages/ui` backs `packages/ui`, `apps/subject-portal`, and `apps/web`; `apps/subject-portal/src/components/focus-trap.js` is retired; all six participant portal tabs share a single reactive state source; and `@media (prefers-reduced-motion: reduce)` applies uniformly wherever `packages/ui/tokens.css` is imported.
- **Negative Impact / Technical Debt:** `apps/web` resolves `"ui"` against the built `packages/ui/dist` bundle, so changes to `packages/ui` composables require running `pnpm --filter ui build` before downstream `apps/web` builds or tests.
- **Mitigation Strategy:** Root `pnpm build` builds `ui` before `web`, and unit/E2E accessibility suites in `packages/ui`, `apps/subject-portal`, and `apps/web` verify focus containment, `inert` closed drawers, six-tab roving tabindex, and reduced-motion rules.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/subject-portal`, `apps/web`, `docs/components/modal.md`.
- **Verification Plan:** Verified via Vitest unit/component suites (`packages/ui/tests/useFocusTrap.test.js`, `apps/subject-portal/tests/accessibility-focus-tabs.test.js`, `apps/web/tests/accessibility.test.js`), Playwright + axe-core WCAG 2.1 AA E2E tests (`apps/subject-portal/tests/e2e/portal-accessibility.spec.js`), and `pnpm cadence check`.
