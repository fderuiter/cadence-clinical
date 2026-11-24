# ADR-2671: Composable Dirty Navigation Guards, Inline eCRF Draft Recovery Banners, and Edited-Row Contrast Tokens

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (21 CFR Part 11 GxP record truth vs. uncommitted input provenance and explicit draft state legibility), PRD-CRF-015 (WCAG 2.2 Level AA `4.5:1` text and `3:1` non-text contrast across light and dark themes, accessible modal confirmations)

---

## 1. Context & Problem Statement

Operators correcting a committed observation or completing a multi-item eCRF form faced silent state transitions and low-contrast visual cues across clinical workspaces:

1. **Unprotected Observation Edits:** While `CanvasFormWorkspace.vue` emitted `@dirty-change` to guard dirty eCRF forms inside `StudyCanvasView.vue`, `ObservationView.vue` held unsaved `newValue` corrections in local memory without route-leave, canvas-zoom, `beforeunload`, or sign-out confirmation guards. Clicking back, switching lifecycle stages, changing routes, or signing out discarded an unsubmitted correction without warning.
2. **Silent eCRF Draft Overwrites and Drops:** `useEcrfForm.ts` (`restoreDraft`) silently overwrote committed server entries from account-scoped `localStorage` without informing the operator, silently discarded stale draft items when committed server revisions advanced without naming skipped fields, and immediately deleted stored drafts when opening a `Frozen` or `Locked` form without showing the unapplied values.
3. **Color-Only Low-Contrast Edited Rows:** `.ecrf-item-edited` in `EcrfItemTable.vue` relied solely on `var(--semantic-color-warning-bg-soft)` (`#fffbeb` on `#ffffff`, `1.04:1` non-text contrast) with no non-color `Unsaved` label beside `Verified (rev N)` SDV badges, making uncommitted edits nearly indistinguishable from committed source-verified values.

## 2. Decision Drivers & Constraints

- **Preserve GxP Truth vs. Unsaved Input Boundaries (ADR-2620, ADR-2621, ADR-2622):** Observation corrections must remain strictly in memory with zero browser draft storage and never claim uncommitted text is saved; eCRF drafts must stay in account-scoped browser storage (`cadence_user:<tenant>:<sub>:ecrf_form_draft:<formId>`) with zero backend draft endpoints.
- **Accessible Modal Confirmations & Non-Disruptive Inspector Navigation (PRD-CRF-015, ADR-2821):** Workspace and standalone exit prompts must use accessible `role="alertdialog"` modals (`ConfirmationDialogHost.vue` / `SignOutConfirmModal.vue`) rather than native `window.confirm()`, while allowing inspector toggles (`?node=`, `?ask=`, `#verb-details`), clean observation auto-redirects into Clinical Canvas, and expired-session or confirmed sign-out transitions without duplicate prompts.
- **WCAG 2.2 Level AA Non-Color and Luminance Contrast (PRD-CRF-015, ADR-2820):** Every edited eCRF row must pair a visible `Unsaved` badge beside verification metadata with calibrated light and dark semantic surface and accent-border tokens achieving `>= 3:1` non-text boundary contrast (SC 1.4.11) and `>= 4.5:1` text contrast (SC 1.4.3).

## 3. Options Considered

### Option 1: Composable Dirty Guard (`useUnsavedNavigationGuard`), Inline eCRF Draft Recovery Banners, and Calibrated Edited-Row Semantic Tokens (Selected)

- **Overview:** Introduce `useUnsavedNavigationGuard.ts` in `apps/web` to unify `beforeunload`, `onBeforeRouteLeave`, `onBeforeRouteUpdate`, `useSignOutGuard` (`registerDraftSource`), and `confirmDialog` prompts across standalone `ObservationView.vue` and embedded workspaces in `StudyCanvasView.vue` (keeping observation `newValue` strictly in memory); extend `useEcrfForm.ts` and `EcrfFormPanel.vue` with inline non-blocking recovery (`role="status"` with inline baseline comparison toggle and draft discard), stale-revision (`role="alert"` naming skipped items), and read-only locked/frozen unapplied draft banners; add `--semantic-color-edited-row-bg`, `--semantic-color-edited-row-border`, and `.clinical-badge--unsaved` (`--semantic-badge-unsaved-*`) across light, dark, and `auto` dark blocks in `packages/ui/tokens.css`; and render `Unsaved` badges plus `4px` left accent borders on `.ecrf-item-edited` in `EcrfItemTable.vue`, gated by `packages/ui/tests/assets.test.js`.
- **Pros:**
  - Protects both eCRF form drafts and in-memory observation corrections across back links, Clinical Canvas zoom/stage/section transitions, standalone routes, browser unload, and sign-out without writing observation drafts to storage or double-prompting on session expiry.
  - Gives CRCs and CRAs immediate visibility into restored browser drafts, inline committed baselines, skipped stale fields, and unapplied drafts on locked/frozen forms without extra modal overlays.
  - Guarantees `>= 3:1` border contrast and `>= 4.5:1` text contrast in both light and dark modes alongside explicit `Unsaved` text badges.
- **Cons:**
  - Locked or frozen forms retain unapplied browser draft keys in account-scoped `localStorage` until the operator clicks the explicit clear action (or signs out).

### Option 2: Persist Observation Drafts in `localStorage` and Open Blocking Modal Dialogs on eCRF Draft Restore

- **Overview:** Save partial observation corrections into `userScopedStorage` and open a blocking confirmation modal every time an eCRF form loads with a saved draft.
- **Pros:**
  - Reuses the same `localStorage` persistence path for observations and forms.
- **Cons:**
  - Violates ADR-2620 and ADR-2621 (which forbid persisting uncommitted single-observation corrections to browser storage) and interrupts clinical chart review with blocking modals instead of inline status banners.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Combining a reusable in-memory/workspace navigation guard composable with inline eCRF draft recovery banners and WCAG 2.2 AA edited-row tokens eliminates silent edit loss and draft ambiguity while honoring ADR-2620, ADR-2621, ADR-2622, and clinical accessibility guardrails.

## 5. Consequences & Trade-offs

- **Positive Impact:** Operators receive consistent accessible modal confirmation before losing unsaved observation or form edits, immediate inline baseline comparison and discard controls when eCRF browser drafts restore, explicit field names when server revisions advance past saved draft items, read-only recovery visibility when a form was frozen or locked after drafting, and unmistakable `Unsaved` row cues in light and dark modes.
- **Negative Impact / Technical Debt:** Account-scoped draft keys for locked/frozen forms remain in `localStorage` when unapplied differences exist until explicitly cleared by the user or purged on sign-out.
- **Mitigation Strategy:** The locked-form banner provides a single-click `#ecrf-draft-locked-clear` control that purges the scoped `localStorage` key immediately, and `purgeAllUserScopedDrafts` continues to wipe all scoped keys on sign-out.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `docs/adr/`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via WCAG 2.2 AA luminance gates in `packages/ui/tests/assets.test.js` (`pnpm --filter ui test`), Vitest unit and `axe-core` accessibility suites in `apps/web/tests/views/ObservationView.spec.ts`, `apps/web/tests/views/StudyCanvasRecord.spec.js`, and `apps/web/tests/components/EcrfFormPanel.spec.ts` (`pnpm --filter web test`), ESLint and TypeScript checks, headless Chromium verification across light and dark themes, and `pnpm cadence check`.
