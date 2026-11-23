# ADR-2593: Extend Focus Primitives with Inline Focus Handoff

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015 (WCAG 2.2 AA keyboard focus order, scrollable regions, and accessibility auditing), PRD-SYS-001 (21 CFR Part 11 electronic signature and audit ledger workflows)

---

## 1. Context & Problem Statement

ADR-2545 centralized modal and drawer focus containment (`useFocusTrap`, `ClinicalModal`, `ClinicalDrawer`) in `packages/ui` and explicitly exempted inline workspace regions from `Tab` trapping per WCAG 2.2 SC 2.1.2 (No Keyboard Trap, G21). However, inline `v-if` branch swaps across the 20 continuous-zoom workspace views in `apps/web/src/views/StudyCanvasView.vue` (UX-2), inline SDV/query disclosures in `EcrfFormPanel.vue` and `FormQueryList.vue`, ephemeral filter/import buttons in `ParticipantsSection.vue`, `AddParticipantForm.vue`, `QueriesSection.vue`, `ProposalReview.vue`, `ProtocolImportPanel.vue`, `DesignerSchemaPanel.vue`, `ValidationSection.vue`, and wallet claim reviews in `ParticipantWalletView.vue` unmounted active trigger elements without transferring focus, dropping `document.activeElement` onto `document.body` and violating WCAG 2.2 SC 2.4.3 (Focus Order, G59).

In `apps/subject-portal`, `showView` in `navigation.js`, questionnaire exit/sign flows (`questionnaire.js`, `signature.js`, `UnavailableView.vue`), notification acknowledgements (`inbox.js`), informed consent completion (`consent.js`), and receipt reimbursement/payout actions (`WalletView.vue`) similarly left focus on hidden `.portal-view` sections or unmounted controls, while `#inbox-container` remained hidden (`display: none`) after rendering notifications. Additionally, `sync-status.js` and `audit.js` overwrote `#sync-queue-list` and `#portal-ledger-timeline` `innerHTML` on 1000ms retry ticks and ledger appends (WCAG Failure F55), resetting keyboard focus on `.site-contact-inline-link` and `<details>/<summary>` controls, and `TicketsView.vue` and `KnowledgeView.vue` rendered non-focusable overflowing scroll regions and click-only cards with `vuejs-accessibility` rule suppressions.

## 2. Decision Drivers & Constraints

- **WCAG 2.2 AA Focus Order Without Inline Traps (PRD-CRF-015):** Inline workspace swaps, disclosures, and ephemeral button unmounts must place focus on a visible, non-`inert` incoming heading (`tabindex="-1"`), control, or saved trigger (SC 2.4.3, G59, avoiding F44) while keeping unmodified `Tab` free (SC 2.1.2, G21) and suppressing focus rings on pointer-triggered programmatic focus via `:focus-visible` (SC 2.4.7, C45).
- **Cross-Browser Visibility Timing & Fallbacks:** Chromium, Firefox, and WebKit reset `document.activeElement` to `document.body` at different phases of `display: none` or DOM detachment; focus handoff must await Vue `nextTick`, verify `getComputedStyle` visibility and ancestor `inert` state, and fall back to `#canvas-workspace` or `#main-content`.
- **Non-Destructive Portal Live Updates & GxP Auditability (PRD-SYS-001):** Active 1000ms offline sync retry countdowns and hash-chained Part 11 local audit ledger appends in `apps/subject-portal` must preserve keyboard focus and `<details>` open state without requiring a full Vue rewrite of the portal's DOM modules.
- **Zero Keyboard Suppressions or Unfocusable Scroll Regions:** Every card in `TicketsView.vue` and `KnowledgeView.vue` and every notification control in `#inbox-container` must activate via `Enter` and `Space` without `vuejs-accessibility` suppressions, and overflowing scroll containers must satisfy `axe-core` `scrollable-region-focusable`.

## 3. Options Considered

### Option 1: Shared `handoffFocus` and `useInlineFocusHandoff` Primitives in `packages/ui` + Keyed Portal DOM Reconciliation + Semantic Keyboard Cards (Selected)

- **Overview:** Extend `packages/ui/src/composables/` (`useInlineFocusHandoff.js` alongside `useFocusTrap.js`, re-exported via `packages/ui/index.js` and `packages/ui/index.d.ts`) with `handoffFocus(targets, options)` and `useInlineFocusHandoff(options)` that resolve ordered visible, enabled, non-`inert` targets after `nextTick`, scroll only off-screen disclosures when requested, and track trigger memory without invoking `trapTabFocus` or background `inert`. Coordinate all 20 `StudyCanvasView.vue` workspace branches (including async zoom-out casebook link restoration via `MutationObserver`) and inline disclosures across `apps/web`. Update `apps/subject-portal` view transitions (`navigation.js`, `TabBar.vue` roving tabindex preservation, `questionnaire.js`, `signature.js`, `UnavailableView.vue`), display `#inbox-container` and wire `inbox.js`, `consent.js`, and `WalletView.vue` confirmation headings, replace `innerHTML` overwrites in `sync-status.js` and `audit.js` with keyed DOM reconciliation, and upgrade `.kanban-board`, `.column-cards`, `.articles-stream`, `.reader-body-card`, `.ticket-card`, and `.article-card` in `TicketsView.vue` and `KnowledgeView.vue`.
- **Pros:**
  - ✅ Satisfies PRD-CRF-015 and PRD-SYS-001 across both Vue 3 (`apps/web`) and hybrid Vue/DOM (`apps/subject-portal`) surfaces from one tested `packages/ui` implementation.
  - ✅ Eliminates F55 focus resets on 1000ms sync timers and ledger appends without rewriting IndexedDB sync queues into Pinia.
  - ✅ Removes `vuejs-accessibility` suppressions on `.ticket-card` and `.article-card` and resolves `scrollable-region-focusable` violations.
- **Cons:**
  - ❌ Requires compiling `packages/ui` (`pnpm --filter ui build`) so downstream packages consume updated `dist/` exports.

### Option 2: Full Vue Component Rewrite of `apps/subject-portal` and Per-View Ad-Hoc `nextTick` Calls

- **Overview:** Rewrite `inbox.js`, `consent.js`, `sync-status.js`, and `audit.js` as reactive Vue SFCs and scatter manual `document.getElementById(...).focus()` calls across `apps/web` views.
- **Pros:**
  - ✅ Uses Vue virtual DOM diffing inside `apps/subject-portal`.
- **Cons:**
  - ❌ High regression risk across offline IndexedDB sync and Part 11 signature flows, while duplicating visibility/inert/fallback logic across dozens of components.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Extending ADR-2545 in `packages/ui` with `handoffFocus` and `useInlineFocusHandoff`, paired with keyed DOM patches in `apps/subject-portal` and semantic keyboard controls/scroll regions in `TicketsView.vue` and `KnowledgeView.vue`, resolves all `BODY` focus drops and keyboard barriers cleanly within existing architectural boundaries.

## 5. Consequences & Trade-offs

- **Positive Impact:** Continuous keyboard focus across all Clinical Canvas zoom levels, inline eCRF/query/wallet disclosures, and participant portal workflows; uninterrupted focus during live sync countdowns and audit block appends; zero `scrollable-region-focusable` violations.
- **Negative Impact / Technical Debt:** Heading targets require explicit `tabindex="-1"` attributes in section templates so `handoffFocus` can focus them programmatically without adding extra sequential `Tab` stops.
- **Mitigation Strategy:** Enforced via unit tests in `packages/ui/tests/useFocusTrap.test.js`, component tests in `apps/web/tests/` and `apps/subject-portal/tests/`, and Playwright + `axe-core` E2E verification.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`, `docs/components/modal.md`, `docs/adr/index.md`.
- **Verification Plan:** Verified via `pnpm --filter ui build`, `pnpm lint`, `pnpm --filter web typecheck`, `pnpm test`, Playwright + `axe-core` E2E suites (`apps/web` and `apps/subject-portal`), and `pnpm cadence check`.
