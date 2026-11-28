# ADR-FRONTEND-20261128-standardize-focus-out-dismissal-ancestor-fallback-and-portal-modal-primitives: Standardize Focus Out Dismissal Ancestor Fallback and Portal Modal Primitives

- **Status:** Accepted
- **Date:** 2026-11-28
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015, PRD-SYS-001

---

## 1. Context & Problem Statement

Non-modal header disclosures, autocomplete popovers, row-mutating clinical dialogs, and participant portal modals across `packages/ui`, `apps/web`, and `apps/subject-portal` exhibited five focus-management and WCAG 2.2 AA operability gaps:

1. Tabbing out of non-modal popovers (`ClinicalPopover`, `#mobile-nav-menu`, `#header-secondary-menu`) left floating panels open over `<main>` controls (WCAG 2.4.11 Focus Not Obscured), while pressing `Escape` while focus already sat inside `<main>` stole focus back to the header toggle.
2. When a dialog trigger unmounted during an action (such as opening `SignOutConfirmModal` from `#header-secondary-menu` or resolving a row in a governance queue), `useFocusTrap`'s fallback order jumped across the page to `#btn-open-palette` instead of a nearby `[aria-controls]` toggle or `<main>` landmark.
3. `BatchDicomDropzoneModal` relied on a removed `escapeLock` option instead of `escapeDeactivates`, allowing premature dismissal during active 21 CFR Part 11 / DICOM PS 3.15 uploads.
4. `CanvasPalette` referenced `#canvas-palette-list` via `aria-controls` even when zero results rendered, and `ShareDrawer` used a raw suggestion dropdown whose `Escape` key bubbled to close the parent `ClinicalDrawer`.
5. `ClinicalModal` rendered a Unicode close glyph rather than a `1.75px` stroke vector icon, and the six `apps/subject-portal` modals maintained parallel raw focus-trap overlays with inconsistent heading levels and stacked PIN sign-out focus restoration.

## 2. Decision Drivers & Constraints

- **WCAG 2.2 AA Operability:** Satisfy SC 2.1.1 (Keyboard), SC 2.1.2 (No Keyboard Trap), SC 2.4.3 (Focus Order), SC 2.4.11 (Focus Not Obscured), SC 2.5.8 / AAA-aligned 48px participant touch targets, and ARIA 1.2 combobox/dialog contracts.
- **Zero New Dependencies & Bundle Budget:** Keep `packages/ui` free of third-party positioning or focus-trap libraries and preserve the `<= 250 kB` raw JS cold-boot budget in `apps/web` (`ADR-2689`).
- **21 CFR Part 11 & Participant Safety:** Preserve `X-Sig-Token` signature payloads, PIN lockout counters, 6th-grade participant copy, and existing DOM IDs across `apps/subject-portal`.
- **Zero-Emoji Vector Iconography:** Enforce `1.75px` stroke Lucide vector icons with zero Unicode emojis across components, tests, and documentation.

## 3. Options Considered

### Option 1: Centralize Primitives in `packages/ui` and Adopt Across `apps/web` and `apps/subject-portal` (Selected)

- **Overview:** Add `isTargetWithinElements` / `didFocusExitElements` and `closeOnFocusOut` to `packages/ui` (`useFloatingPosition` and `ClinicalPopover`), snapshot trigger ancestor IDs/landmarks in `useFocusTrap`, replace the `ClinicalModal` close glyph with a `1.75px` Lucide `<X>` icon (plus `keepMounted` support for persistent DOM modals), wire `fallbackFocus` across row-mutating web dialogs, migrate `ShareDrawer` suggestions onto `ClinicalPopover`, and migrate all six `apps/subject-portal` modals onto `ClinicalModal`.
- **Pros:**
  - Eliminates duplicated ad-hoc focus traps and floating dismissal listeners across both applications.
  - Preserves contextual focus order when triggers unmount without adding runtime bundle weight.
  - Standardizes `<h2>` dialog headings, `1.75px` vector close buttons, and LIFO stacked modal Escape handling across participant and clinical workspaces.
- **Cons:**
  - Requires careful coordination between `SignatureModal`'s synchronous imperative helpers (`openSignatureModal` / `closeSignatureModal`) and `ClinicalModal`'s reactive `inert` cleanup.

### Option 2: Patch Individual Views In Place Without Updating `packages/ui`

- **Overview:** Add one-off `focusout` listeners, custom `Escape` stop-propagation handlers, and bespoke heading/focus fixes inside each affected component in `apps/web` and `apps/subject-portal`.
- **Pros:**
  - Avoids touching shared `packages/ui` primitives.
- **Cons:**
  - Leaves six duplicate modal implementations in `apps/subject-portal` and repeats `focusout` / fallback restoration bugs in future popovers and dialogs.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Centralizing outside-`focusout` dismissal, ancestor-aware `useFocusTrap` fallback resolution, and vector-icon `ClinicalModal` primitives in `packages/ui` resolves all five accessibility and focus-order defects at their root while keeping cold-boot JS within budget and eliminating six bespoke modal shells in `apps/subject-portal`.

## 5. Consequences & Trade-offs

- **Positive Impact:** Non-modal menus and popovers close cleanly when focus moves into `<main>`, stacked drawers/popovers and PIN confirmation modals unwind strictly LIFO on `Escape`, unmounted row triggers restore focus to workspace controls or `<main>`, and all six participant modals share `ClinicalModal` semantics and `48px` touch targets.
- **Negative Impact / Technical Debt:** Legacy raw modals in `apps/web` tracked by `LEGACY_WEB_RAW_MODAL_OVERLAY_ALLOWLIST` remain scheduled for separate incremental migration.
- **Mitigation Strategy:** Automated Vitest unit suites in `packages/ui`, `apps/web`, and `apps/subject-portal` plus Playwright headless Chromium accessibility and viewport reflow checks guard against regressions.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`, and `docs/adr/frontend/`.
- **Verification Plan:** Verified via `pnpm --filter ui test`, `pnpm --filter subject-portal test`, `pnpm --filter web exec vitest run` (including `cold_boot_bundle.spec.ts`), `pnpm build`, `pnpm build:demo`, `pnpm -r typecheck`, `pnpm lint`, `cadence check`, and Playwright headless Chromium E2E suites across `390x844`, `1024x768`, and `1280x800` viewports.
