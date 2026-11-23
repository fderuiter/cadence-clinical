# ADR-2656: Add ClinicalFormActionBar and CSS Variable Form Reflow

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (21 CFR Part 11 regulated UI consistency, attestation reason visibility, and audit traceability), PRD-CRF-015 (WCAG 2.2 Level AA SC 1.4.10 Reflow at 320px, SC 2.5.3 Label in Name, and two-profile 44px/48px touch targets), PRD-EDC-005 (Single-command eCRF collection instance entry and inline query/SDV review)

---

## 1. Context & Problem Statement

Clinical data entry and participant-facing form surfaces across `packages/ui`, `apps/web`, and `apps/subject-portal` relied on two brittle layout patterns:

1. **Inline `grid-column: span N` styles that resisted responsive collapse:** `ClinicalFieldLayout` (`packages/ui`), `CrcFormRenderer.vue` (inside `display: contents` fieldsets), and `AuditView.vue` bound raw inline `style="grid-column: span N"` attributes. Because inline element styles outrank stylesheet selectors unless `!important` overrides every direct child, multi-span fields inside `display: contents` wrappers failed to collapse cleanly below `1024px`, while `EcrfItemTable.vue`, `EcrfFormOpener.vue`, `ProtocolTrainingConsole.vue`, `CtmsGreenlightSection.vue`, `TicketCreateModal.vue`, and `DocumentGrid.vue` forced horizontal scrolling or cramped multi-column tracks on `320px–640px` viewports.
2. **Below-the-fold form submission and fragmented eCRF toolbars:** Static `.form-actions` footers in `EcrfFormPanel.vue`, `ObservationView.vue`, `CrcFormRenderer.vue`, `QuestionnaireView.vue`, `ConsentView.vue`, and `WalletView.vue` scrolled below the viewport fold on `768x1024` and `1024x768` tablets and `390x844` phones. In `EcrfFormPanel.vue`, the four primary clinical verbs (`Save page`, `Sign form`, `Verify`, and `Open query`) were split across the form footer, a separate SDV toolbar, per-row buttons, and a bottom signatures card, requiring vertical scrolling during bedside and monitoring workflows.

## 2. Decision Drivers & Constraints

- **WCAG 2.2 SC 1.4.10 Reflow & Mobile Progressive Cards (PRD-CRF-015):** Form grids must collapse multi-column field spans below `1024px` without `!important` specificity wars, and `EcrfItemTable.vue` must keep one semantic `<table>` DOM (preserving `ecrf-row-*` and `ecrf-item-*` identifiers) while pinning `th[scope="row"]` horizontally on `768px–1280px` tablets and presenting progressive single-column item cards on `320px–480px` viewports with `0px` horizontal wrapper overflow.
- **Consolidated Sticky Form Chrome (PRD-SYS-001, [ADR-2622](2026-10-19-ecrf-forms-are-collection-instances-saved-in-one-command.md)):** Collapsed `ClinicalFormActionBar` chrome must remain `<= 56px` tall on tablets, keep `Save page`, `Sign form`, `Verify`, and `Open query` visible without vertical scrolling, open compact inline reason trays for Part 11 change reasons, SDV verification, and discrepancy queries, and explain gated actions (such as locked or unsigned prerequisites) in plain language inline.
- **Two-Profile Touch Targets, Typography, & Zero-Emoji Iconography:** Clinician actions in `apps/web` require `>= 44px` hit targets (`--profile-clinician-touch-target-min`), participant actions in `apps/subject-portal` require `>= 48px` hit targets (`--profile-participant-touch-target-min`) offset cleanly above the fixed mobile `.nav-tabs` bar and `env(safe-area-inset-bottom)`, numeric and identifier metadata must preserve `Inter` `tabular-nums` and `JetBrains Mono` alignment ([ADR-2650](2026-11-22-consolidate-shared-ui-css-aliases-and-table-numerals.md)), and new UI primitives and templates must pair `lucide-vue-next` icons (`aria-hidden="true"`) with leading accessible text labels and zero Unicode emojis ([ADR-2822](2026-11-22-shared-a11y-primitives-and-label-in-name-ast-gates.md)).

## 3. Options Considered

### Option 1: Shared `ClinicalFormActionBar` Primitive + `--field-grid-span` & `--form-action-bar-bottom` CSS Custom Properties (Selected)

- **Overview:** Bind `:style="{ '--field-grid-span': String(gridSpan) }"` on `ClinicalFieldLayout` and `.field-grid-span` cells so `packages/ui/responsive.css` resolves `grid-column: span var(--field-grid-span, 12)` on desktop and resets descendant spans (`--field-grid-span: 1; grid-column: 1 / -1`) below `1024px` across `.responsive-grid` and `.grid-2-responsive` (including `display: contents` fieldsets). Export `ClinicalFormActionBar` from `packages/ui` (`position: sticky; bottom: var(--form-action-bar-bottom, 0px)`), consolidate `EcrfFormPanel.vue` verbs and inline reason trays into a single bar, adopt `ClinicalFormActionBar` across `ObservationView.vue`, `CrcFormRenderer.vue`, `QuestionnaireView.vue`, `ConsentView.vue`, and `WalletView.vue`, sync `--portal-mobile-nav-offset` from `TabBar.vue` in `apps/subject-portal`, and add sticky tablet row headers plus progressive `320px–480px` table-row cards in `EcrfItemTable.vue`.
- **Pros:**
  - Eliminates inline `grid-column` specificity overrides so nested `display: contents` fieldsets and multi-span cards collapse cleanly below `1024px`.
  - Keeps primary clinical and participant actions visible at all scroll positions while respecting fixed mobile `.nav-tabs` and device safe-area insets with `0px` overlap.
  - Preserves a single semantic `<table>` DOM in `EcrfItemTable.vue` so screen readers, keyboard navigation, and existing `#ecrf-row-*` / `#ecrf-item-*` selectors remain unbroken across mobile, tablet, and desktop breakpoints.
- **Cons:**
  - Requires scrollport containers (`main`, `.shell-main`, `.portal-main`) to avoid trapping `position: sticky` inside un-bounded `overflow-y: auto` wrappers.

### Option 2: Viewport-Specific Duplicate Mobile/Desktop Templates and Fixed-Position Footers

- **Overview:** Render separate `v-if="isMobile"` card lists alongside desktop `<table>` templates in `EcrfItemTable.vue` and use `position: fixed; bottom: 0` bars with hardcoded pixel offsets in each view.
- **Pros:**
  - Avoids CSS media-query overrides on table elements.
- **Cons:**
  - Duplicates reactive form inputs and IDs (`#ecrf-item-*`), breaks SSR/hydration determinism across viewports, and causes `position: fixed` bars from stacked cards (such as multi-step `ConsentView.vue`) to collide at the viewport bottom.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** CSS custom properties (`--field-grid-span` and `--form-action-bar-bottom`) combined with one shared `ClinicalFormActionBar` primitive and progressive CSS table-card reflow satisfy WCAG 2.2 AA reflow, two-profile touch-target sizing, and single-command eCRF ergonomics without duplicating DOM trees or breaking Part 11 traceability selectors.

## 5. Consequences & Trade-offs

- **Positive Impact:** All shared and app-level clinical forms collapse cleanly to single-column tracks on narrow viewports (`320x640` and `390x844`), tablet users (`768x1024` and `1024x768`) retain sticky `th[scope="row"]` labels and `<= 56px` consolidated action chrome without vertical scrolling, and participant forms keep `>= 48px` actions anchored cleanly above `.nav-tabs`.
- **Negative Impact / Technical Debt:** Legacy views that do not require sticky viewport anchoring may continue to use the static `.form-actions` utility retained in `packages/ui/tokens.css` for backward compatibility.
- **Mitigation Strategy:** `packages/ui/tests/ClinicalFormActionBar.test.js`, `packages/ui/tests/design_system_harmony.test.js`, and Playwright multi-viewport specs in `apps/web` and `apps/subject-portal` enforce zero horizontal overflow, single-column mobile reflow, sticky header/bar geometry, zero `.nav-tabs` overlap, and `axe-core` WCAG 2.2 AA accessibility.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`, `docs/components/index.md`, `docs/adr/`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm --filter ui test`, `pnpm --filter web test`, `pnpm --filter subject-portal test`, Playwright multi-viewport suites (`320x640`, `390x844`, `768x1024`, `1024x768`), `pnpm lint`, `pnpm build`, and `pnpm cadence check --base main`.
