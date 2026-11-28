# ADR-FRONTEND-20261128-shared-governance-banners-and-single-surface-new-form-entry: Shared Governance Banners, Blocker Drawers, and Single-Surface New-Form Entry

- **Status:** Accepted
- **Date:** 2026-11-28
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015, PRD-SYS-001

---

## 1. Context & Problem Statement

Across Clinical Canvas Conduct, Data, and Observation workflows (`ParticipantRecord.vue`, `EcrfSdvToolbar.vue`, `ObservationView.vue` via `ScopeLockControls.vue`, `LocksSection.vue` via `LocksApplyForm.vue`, and `CanvasFormWorkspace.vue`), stacked inline governance banners, multi-row casebook export forms, inline lock readiness blocker lists, and duplicated casebook matrices consumed critical above-the-fold vertical height on `768x1024` and `1024x768` clinical tablets:

1. **Above-the-Fold Governance Displacement:** Rendering held lock summaries, six-gate readiness counts, itemized blocker lists, lock reason inputs, and bitemporal casebook export controls inline pushed primary clinical work surfaces (`#casebook-matrix`, eCRF items, `#observation-new-value`, and `#btn-apply-study-lock`) below the viewport fold on standard tablet resolutions (`PRD-CRF-015`).
2. **Duplicated Schedule Matrix on Preselected `form=new` Routes:** Navigating from a scheduled or unscheduled cell into `?form=new&spec=...` mounted `ParticipantFormsPanel` (`#casebook-matrix`) inline above `EcrfFormPanel` (`#ecrf-form-new`), forcing coordinators to scroll past the matrix they had just clicked to reach `#ecrf-collected-at`.
3. **Scroll Restoration & Sticky Chrome Occlusion on Cell Transitions:** `apps/web/src/router/index.js` omitted a Vue Router `scrollBehavior` contract, while `ensureFocusedElementClearsStickyChrome` skipped programmatic focus alignment within `150ms` of pointer clicks and `useInlineFocusHandoff` scrolled without accounting for `--sticky-chrome-offset`.

## 2. Decision Drivers & Constraints

- **Tablet-First Clinical Ergonomics (`PRD-CRF-015`):** Governance chrome must remain a compact single-row status strip (`<= 52px` height on `768px`-`1279px` viewports) so primary clinical tables, inputs, and signing actions stay visible above the fold without vertical scrolling.
- **WCAG 2.2 AA & LIFO Focus Containment (`ADR-2650`, `ADR-2834`, `PRD-SYS-001`):** Slide-over and mobile bottom-sheet (`<= 480px`, `90dvh`) drawers must preserve `>= 44x44px` touch targets, Escape dismissal, trigger focus restoration, and LIFO modal focus containment when nested `EntityPeekDrawerHost` or `SignatureCaptureModal` overlays open from inside a governance drawer.
- **Zero-Emoji Lucide Iconography & Tabular Numerals:** All status indicators and gate counts must use `lucide-vue-next` SVGs with visible text labels and `font-variant-numeric: tabular-nums`.

## 3. Options Considered

### Option 1: Shared `ClinicalGovernanceBanner` + `ClinicalDrawer` Progressive Disclosure, Single-Surface `form=new` with Visit Switcher Drawer, and Sticky-Aware Router/Focus Scroll Coordination (Selected)

- **Overview:** Extract `ClinicalGovernanceBanner.vue` in `packages/ui` pairing a single-row status strip (`unlocked`, `frozen`, `locked`, `ready`, `blocked`, `loading`) and tabular gate chips with a bounded `ClinicalDrawer` (`mobile-bottom-sheet`) for itemized blockers (`LockBlockerList.vue`), lock mutations (`ScopeLockControls.vue`), and casebook exports (`ParticipantRecord.vue`). Omit inline `ParticipantFormsPanel` in `CanvasFormWorkspace.vue` when `collectionSpecId` is preselected on `form=new`, exposing a `Switch visit or form` header button backed by `ClinicalDrawer`. Add `appScrollBehavior` in `apps/web/src/router/index.js`, honor `--sticky-chrome-offset` and `scrollTarget` in `useInlineFocusHandoff.js`, bypass the `150ms` pointer guard for programmatic focus handoffs in `stickyChromeOffset.ts`, and promote `EntityPeekDrawerHost` to modal LIFO focus containment when triggered from inside an active modal focus trap.
- **Pros:**
  - Keeps all four governance surfaces within `<= 52px` on tablet viewports while preserving one-click access to itemized blockers, lock actions, and bitemporal exports.
  - Eliminates duplicate inline `#casebook-matrix` rendering on preselected `form=new` routes and aligns `#ecrf-form-new` clear of sticky chrome with focus on `#ecrf-collected-at`.
  - Preserves LIFO focus containment and clean `inert` teardown across `ClinicalDrawer`, `EntityPeekDrawerHost`, and `SignatureCaptureModal`.
- **Cons:**
  - Interactive lock reason inputs and casebook export controls require opening the governance or export drawer sheet before submission.

### Option 2: Collapsible Inline `<details>` Accordions Within Each View

- **Overview:** Wrap each view's existing inline blocker list, lock form, and casebook export bar in view-local `<details>` disclosures and collapse `#casebook-matrix` behind an inline `<details>` element on `form=new`.
- **Pros:**
  - Requires no shared `packages/ui` component extraction.
- **Cons:**
  - Leaves duplicated governance markup across four views, shifts vertical document layout whenever expanded on tablets, and lacks `90dvh` mobile bottom-sheet ergonomics and focus-trapped Escape restoration.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Centralizing governance status strips and progressive-disclosure drawers in `ClinicalGovernanceBanner.vue`, deduplicating blocker rendering in `LockBlockerList.vue`, switching preselected `form=new` routes to single-surface entry with a drawer-mounted visit switcher, and coordinating router and programmatic focus scroll offsets satisfies `PRD-CRF-015` and `PRD-SYS-001` across desktop, tablet, and `320px` mobile viewports.

## 5. Consequences & Trade-offs

- **Positive Impact:** `#casebook-matrix`, eCRF item rows, `#observation-new-value`, and `#btn-apply-study-lock` appear above the fold on `768x1024` and `1024x768` viewports; preselected `form=new` routes mount zero inline `ParticipantFormsPanel` instances while keeping scheduled cell switching one click away; and programmatic focus handoffs clear sticky headers reliably even immediately after pointer clicks.
- **Negative Impact / Technical Debt:** Integration and E2E tests exercising participant casebook exports, scope lock mutations, or itemized lock readiness blockers click the corresponding strip trigger (`#btn-open-casebook-drawer`, `#*-manage`, or `#*-review-blockers`) before interacting with drawer controls.
- **Mitigation Strategy:** Deterministic `id` and `data-testid` contracts on strip triggers and drawer sheets keep keyboard navigation and automated test selectors explicit and accessible.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `docs/components/index.md`, `docs/adr/frontend/`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm --filter ui build`, `pnpm --filter ui test` (`ClinicalGovernanceBanner.spec.js`, `design_system_harmony.test.js`, `ui_consumer_reachability.test.js`), `pnpm typecheck`, `pnpm lint`, `apps/web` Vitest suites, multi-viewport headless Chromium geometry verification across `768x1024`, `1024x768`, `1280x800`, `1440x900`, `390x844`, and `320x568`, and `pnpm cadence check`.
