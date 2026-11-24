# ADR-2670: Share UI Roving Tab Focus, Landmark Hygiene, and Study-Centric Router Semantics

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015 (WCAG 2.2 Level A & AA SC 1.3.1 Info and Relationships, SC 2.4.2 Page Titled, SC 3.1.2 Language of Parts, SC 4.1.2 Name, Role, Value, and SC 4.1.3 Status Messages compliance), PRD-SYS-001 (21 CFR Part 11 regulated workflow integrity), UX-1 (study-centric language without product silo acronyms)

---

## 1. Context & Problem Statement

Across `packages/ui`, `apps/web`, and `apps/subject-portal`, screen-reader clinicians, keyboard-only monitors, and multilingual trial participants encountered five structural WCAG 2.2 Level A & AA accessibility gaps:

1. **Static `<title>` Elements & Silent Client-Side Route Transitions (WCAG 2.2 SC 2.4.2, SC 4.1.3, UX-1):** Both `apps/web/index.html` (`Cadence Clinical`) and `apps/subject-portal/index.html` (`Cadence Clinical — Participant Portal`) kept static `<title>` strings across all client-side route transitions, and neither SPA announced post-load route changes to polite status live regions.
2. **Mobile Navigation Drawer Missing Landmark & `aria-current="page"` (WCAG 2.2 SC 1.3.1, SC 4.1.2):** In `apps/web/src/components/AppShell.vue`, `#mobile-nav-menu` rendered as a `<div role="region">` instead of a `<nav>` landmark, remained exposed when closed unless hidden explicitly, and marked active links only via CSS `.active` rather than `aria-current="page"` (which desktop `#nav-*` links already exposed).
3. **Nested `<main>` Landmarks Inside `AppShell.vue` (WCAG 2.2 SC 1.3.1, ARIA11):** Twelve views and specialty consoles rendered inside `AppShell.vue` (`CellGeneTherapyCocConsole.vue`, `AggregateSafetyConsole.vue`, `DeviceInvestigationConsole.vue`, `LabGovernanceConsole.vue`, `TelehealthDtpConsole.vue`, `KnowledgeView.vue`, `DocumentManagerView.vue`, `DocumentsHubView.vue`, `ExportWizardView.vue`, `DataLockView.vue`, `ICFBuilderView.vue`, and `ImagingView.vue`) declared an inner `<main>` element inside `AppShell`'s `<main id="main-content">`, violating single-main landmark semantics.
4. **Fragmented Tab Bars Lacking WAI-ARIA `tablist` / Roving `tabindex` (WCAG 2.2 SC 4.1.2, G10):** While `ClinicalTabs.vue` (`packages/ui`) covered seven operational hub tab bars (ADR-2652), `useRovingTabindex` lived only inside `apps/web/src/composables/useRovingTabindex.js` without `Home` / `End` / vertical / disabled-tab support, and 35 additional tab switchers across `apps/web` views, consoles, and `v-if` drawers lacked standardized `role="tablist"`, `role="tab"`, `aria-selected`, roving `tabindex` (`0` / `-1`), or arrow/`Home`/`End` keyboard navigation.
5. **Missing `lang` Attributes on Localized eConsent Passages (WCAG 2.2 SC 3.1.2, H58):** In `apps/subject-portal/src/views/ConsentView.vue` and `apps/subject-portal/src/features/consent.js`, switching `#consent-lang-selector` to Spanish (`es`), Dutch (`nl`), or French (`fr`) rendered translated informed consent titles and clauses inside an `<html lang="en">` document without setting `lang` on `#consent-template-title` or `#consent-clauses-container`, causing assistive technologies to read non-English clinical risk disclosures with English phonetics.

## 2. Decision Drivers & Constraints

- **WCAG 2.2 Level A & AA Compliance (PRD-CRF-015):** Deliver descriptive study-centric `<title>` updates (`SC 2.4.2`), polite post-load route announcements (`SC 4.1.3`), single visible `<nav>` and `<main>` landmarks with `aria-current="page"` (`SC 1.3.1`, `SC 4.1.2`), WAI-ARIA Tabs APG roving `tabindex` across all 42 `apps/web` tablists (`SC 4.1.2`), and BCP 47 `lang` tags on localized eConsent passages (`SC 3.1.2`).
- **Study-Centric Copy & Zero PII Leakage (`UX-1`, Privacy):** Route titles and polite live announcements must never expose internal product silo acronyms (`EDC`, `CTMS`, `eTMF`, `RTSM`, `P21`, `QMS`, `Safety`) and must never interpolate participant PII (`participant`, `subjectId`, names, or emails).
- **Cold-Boot Bundle Budget Preservation (ADR-2537):** Adding `ROUTE_TITLES` and router semantics to `apps/web/src/router/availability.js` must keep `apps/web` cold-boot JS chunks strictly within the `167 kB` gzip ceiling enforced by `apps/web/tests/cold_boot_bundle.spec.ts`.

## 3. Options Considered

### Option 1: Promote Shared `useRovingTabindex` to `packages/ui`, Standardize All 42 Tablists, Enforce Single-Main & Mobile `<nav>` Landmarks, Wire Study-Centric Router Semantics, and Tag Localized eConsent Passages (Selected)

- **Overview:**
  1. Promote `useRovingTabindex` (and `syncRovingTabindex`) into `packages/ui/src/composables/useRovingTabindex.js` with horizontal/vertical orientation, `ArrowRight`/`ArrowLeft` (`ArrowDown`/`ArrowUp`), `Home`/`End`, `manual` focus-only mode, disabled-tab skipping, synchronous `onMounted` binding plus `flush: "post"` `v-if` drawer re-attachment and `MutationObserver` attribute sync; re-export from `packages/ui` and `apps/web/src/composables/useRovingTabindex.js`, add `controlsPanels` to `ClinicalTabs.vue`, and standardize all 42 tablists across `apps/web`.
  2. Extract pre-release `SCREEN_CATALOG` metadata into pure-annotated `apps/web/src/router/screenCatalog.js` (re-exported by `availability.js`) so Rollup keeps pre-release catalog strings inside lazy `UnavailableView`, add `UX-1`-compliant `ROUTE_TITLES` / `resolveRouteTitle` / `formatRouteAnnouncement` in `apps/web/src/router/availability.js` and `PORTAL_ROUTE_TITLES` / `resolvePortalRouteTitle` / `formatPortalRouteAnnouncement` in `apps/subject-portal/src/router/availability.js`, and wire `installRouterSemantics` (`apps/web`) and `getPortalRouter` + `showView` (`apps/subject-portal`) to update `document.title` on every navigation while announcing only post-load transitions politely via `.sr-only-status-alerts` and `#portal-route-announcer`.
  3. Convert `#mobile-nav-menu` in `apps/web/src/components/AppShell.vue` to `<nav aria-label="Mobile navigation">` with `:hidden="!isMobileMenuOpen ? true : undefined"`, `@media (min-width: 861px) { display: none; }`, automatic close above `860px`, and `:aria-current="... ? 'page' : undefined"`, and replace nested `<main>` elements with `<div role="region">` / `<div>` wrappers across all 12 affected views and consoles.
  4. Bind `:lang="consentLang"` in `apps/subject-portal/src/views/ConsentView.vue` and synchronize `lang` (`en`, `es`, `nl`, `fr`) via `resolveConsentLanguage` in `apps/subject-portal/src/features/consent.js` on `#consent-template-title` and `#consent-clauses-container` while keeping `<html lang="en">` and surrounding English portal chrome unmutated.
- **Pros:**
  - ✅ Eliminates bespoke per-view keyboard handlers by sharing one tested roving-focus composable across `ClinicalTabs`, custom tab bars, and conditionally mounted `v-if` drawers.
  - ✅ Satisfies WCAG 2.2 SC 1.3.1, SC 2.4.2, SC 3.1.2, SC 4.1.2, SC 4.1.3, and `UX-1` across both SPAs while keeping cold-boot gzip bundle size under budget.
- **Cons:**
  - ❌ Requires updating 40+ Vue views/drawers in `apps/web` and maintaining `ROUTE_TITLES` / `PORTAL_ROUTE_TITLES` when new routes are added.

### Option 2: Keep `useRovingTabindex` Local to `apps/web` and Rely on Per-View `document.title` Assignments

- **Overview:** Leave `useRovingTabindex` in `apps/web`, patch individual views to set `document.title` on mount, and leave `SCREEN_CATALOG` inline inside `availability.js`.
- **Pros:**
  - ✅ Smaller diff in `packages/ui`.
- **Cons:**
  - ❌ Cannot share roving-tab focus primitives with `packages/ui` (`ClinicalTabs`) or `apps/subject-portal`, risks missed routes or PII leaks in ad-hoc `document.title` strings, and pushes cold-boot JS chunks past the `167 kB` gzip budget.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Centralizing `useRovingTabindex` in `packages/ui` and route title/announcement resolvers in `availability.js` guarantees uniform WAI-ARIA keyboard behavior across all 42 `apps/web` tablists, enforces `UX-1` study-centric titles and zero-PII polite announcements from a single source of truth per app, preserves single `<main>` and `<nav>` landmark invariants, and gives screen readers accurate BCP 47 pronunciation on translated eConsent documents without breaching cold-boot bundle budgets.

## 5. Consequences & Trade-offs

- **Positive Impact:** Full WCAG 2.2 Level A & AA alignment across tablist keyboard navigation, document titles, polite route status announcements, mobile navigation `aria-current="page"` and landmark visibility, single `<main>` landmarks across all `apps/web` routes, and multilingual eConsent `lang` tagging (`en`, `es`, `nl`, `fr`).
- **Negative Impact / Technical Debt:** New workspace or participant portal routes must declare an entry in `ROUTE_TITLES` (`apps/web/src/router/availability.js`) or `PORTAL_ROUTE_TITLES` (`apps/subject-portal/src/router/availability.js`).
- **Mitigation Strategy:** Unit and E2E accessibility suites assert `UX-1` title coverage across registered routes, single `<main>` landmarks, roving `tabindex` keyboard navigation, and localized eConsent `lang` attributes.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`, `packages/ui/README.md`, `apps/web/README.md`, `apps/subject-portal/README.md`, `docs/adr/index.md`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm --filter ui build`, `pnpm --filter ui test`, `pnpm -r test`, `pnpm exec vitest run --config vitest.config.mjs`, `pnpm -r lint`, `pnpm exec eslint --max-warnings 0 .`, `pnpm build`, `pnpm build:demo`, `pnpm docs:build`, `pnpm cadence check adrs`, `pnpm cadence check markdown`, `pnpm cadence check readmes`, `pnpm --filter subject-portal test:e2e`, and `pnpm --filter web test:e2e`.
