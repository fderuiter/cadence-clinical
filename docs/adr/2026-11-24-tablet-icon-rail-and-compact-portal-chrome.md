# ADR-2685: Add Tablet Icon Rail and Compact Participant Portal Chrome

- **Status:** Accepted
- **Date:** 2026-11-24
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015 (WCAG 2.2 Level AA focus not obscured, orientation breadcrumbs, zero-emoji iconography, and responsive touch targets), PRD-DCT-001 (BYOD tablet and mobile participant portal ergonomics), PRD-SYS-001 (21 CFR Part 11 signature modal footer visibility), PRD-SYS-179 (protocol amendment re-consent redline review before credential entry), PRD-SYS-182 (participant wallet payout modal and peek inspection)

---

## 1. Context & Problem Statement

Across `768px–1279px` tablet viewports, `apps/subject-portal` kept a static `260px` inline `.portal-navigation` column alongside an inline `ParticipantCard` and multi-pill `.portal-header`, squeezing `#main-content` on `768x1024` portrait tablets and pushing primary tasks below chrome on `< 768px` phones. On short landscape viewports (`max-height: 480px`, such as `1024x416` tablets with an active on-screen keyboard), both `packages/ui/src/components/clinical/ClinicalModal.vue` and `apps/subject-portal/src/style.css` switched modal dialogs to `display: block` with `overflow-y: visible` on `.modal-body`, unpinning `.modal-footer` off-screen while `ReconsentModal.vue` and `features/reconsent.js` auto-focused `#reconsent-sign-password` at the bottom of the dialog, scrolling past `#reconsent-change-summary` and `#reconsent-clause-redlines` on open. Additionally, `PortalHeader.vue` and `TasksView.vue` still rendered raw Unicode emojis (`⚠️`, `🔄`), `ClinicalDrawer` switched `mobileBottomSheet` at `640px` rather than `767px` (`--breakpoint-tablet`), and `PortalPeekSheet.vue` statically forced `position="bottom"` on tablet and desktop screens.

## 2. Decision Drivers & Constraints

- **Tablet & Mobile Content Density Without Losing 48px Touch Targets (PRD-CRF-015, PRD-DCT-001):** Portrait and landscape tablets (`768px–1279px`) need a `60px` icon rail so `#main-content` retains `>= 636px` width across all 7 routes, while phones (`320x640`, `390x844`) need a single-row `<= 52px` header, compact collapsible banners, and conditional single-row breadcrumbs so the first interactive card starts above the bottom navigation bar (`< 591px` at `320x640`).
- **Pinned Short-Landscape Modal Footers & Top-Summary Re-Consent Focus (PRD-CRF-015, PRD-SYS-001, PRD-SYS-179, PRD-SYS-182):** At `max-height: 480px`, modal headers and footers must stay pinned inside `calc(100dvh - 16px)` while `.modal-body` scrolls independently, and opening `ReconsentModal` must land focus on `#reconsent-change-summary` at `scrollTop` `0` so amended clause redlines remain visible before password entry (WCAG 2.2 SC 2.4.11).
- **Shared Breakpoint Alignment & Superseding Static Peek Sheet Orientation (PRD-CRF-015, PRD-DCT-001):** `ClinicalDrawer` mobile bottom-sheet switching must align with `--breakpoint-tablet` (`max-width: 767px`), and `PortalPeekSheet.vue` must supersede the static bottom-sheet wiring noted in [ADR-2657](2026-11-23-share-ui-primitives-and-query-synced-portal-routes.md) by rendering `position="bottom"` below `768px` and `position="right"` (`size="md"`) at `>= 768px`.

## 3. Options Considered

### Option 1: Extend Shared `packages/ui` Responsive Rules and Layer a `60px` Tablet Icon Rail, Session `ClinicalDrawer`, and Pinned Short-Landscape Modals in `apps/subject-portal` (Selected)

- **Overview:** Keep flex-column layout (`max-height: calc(100dvh - 16px)`) with scrollable `.modal-body` at `@media (max-height: 480px)` in `ClinicalModal.vue` and `apps/subject-portal/src/style.css`; align `mobileBottomSheet` switching to `max-width: 767px` in `ClinicalDrawer.vue` and `packages/ui/responsive.css`; add `compactMobile` to `ClinicalBreadcrumbs.vue` and `"user"` to `ClinicalIcon.vue`; convert `.portal-navigation` on `768px–1279px` into a `60px` `lucide-vue-next` icon rail with an overlay expand toggle (`260px` inline only at `>= 1280px`); host `ParticipantCard` below `1280px` inside a `ClinicalDrawer` session sheet (`position="right"` on `768px–1279px`, `position="bottom"` below `768px`) opened from `#btn-mobile-session-toggle` in `PortalHeader.vue` while keeping expired-session alerts inline; compact `PortalHeader.vue` (`<= 52px`), `DemoBanner.vue`, and `ReconsentBanner.vue`; hide mobile breadcrumbs below `640px` unless a sub-record is active; focus `#reconsent-change-summary` at `scrollTop` `0` on `ReconsentModal` open; and bind responsive `position` (`"bottom"` `< 768px`, `"right"` `size="md"` `>= 768px`) on `PortalPeekSheet.vue`.
- **Pros:**
  - ✅ Reclaims horizontal width on `768x1024` / `1024x768` tablets (`>= 640px` main column) and above-the-fold height on `320x640` phones while preserving every canonical DOM ID (`#btn-mobile-session-toggle`, `#participant-secondary-details`, `#portal-peek-drawer`, `#reconsent-sign-password`, `#portal-sign-modal`).
  - ✅ Keeps `.modal-footer` pinned at `1024x416` across all five portal modals without rewriting custom `.modal-overlay` forms in a single high-risk pass.
- **Cons:**
  - ❌ Retains existing custom `.modal-overlay` markup in `apps/subject-portal` alongside shared `ClinicalModal` until dedicated dialog migrations are scheduled.

### Option 2: Introduce a New Cross-App Shell Component in `packages/ui` and Rewrite Every Portal Dialog onto `ClinicalModal`

- **Overview:** Build a unified application chrome wrapper in `packages/ui` shared by `apps/web` and `apps/subject-portal`, and migrate all five participant signature, PIN, re-consent, and disbursement dialogs to `ClinicalModal` simultaneously.
- **Pros:**
  - ✅ Consolidates modal markup across both frontend applications.
- **Cons:**
  - ❌ Expands blast radius across clinician `apps/web` layouts and validated 21 CFR Part 11 / offline PIN DOM hooks unnecessarily.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Targeted updates to `packages/ui` (`ClinicalModal`, `ClinicalDrawer`, `ClinicalBreadcrumbs`, `ClinicalIcon`, `responsive.css`) paired with a `60px` tablet icon rail, `< 1280px` `ClinicalDrawer` session sheet, compact single-row chrome, top-summary re-consent focus, and responsive `PortalPeekSheet` orientation satisfy every tablet, phone, and `1024x416` short-landscape ergonomics and WCAG 2.2 AA requirement with minimal regression surface. This decision supersedes the static `position="bottom"` `PortalPeekSheet` wiring in ADR-2657.

## 5. Consequences & Trade-offs

- **Positive Impact:** Tablet participants gain a `60px` icon rail and `>= 640px` content workspace; mobile participants see first task/section cards well above the bottom tab bar (`< 591px` at `320x640`) with zero Unicode emojis; `1024x416` short-landscape users keep `.modal-footer` actions pinned on screen and read amended consent redlines before entering credentials; and `#portal-peek-drawer` adapts cleanly between mobile bottom sheets and `size="md"` right slide-overs.
- **Negative Impact / Technical Debt:** `usePortalViewport.js` coordinates responsive drawer orientation in JavaScript alongside CSS media queries (`767px`, `1023px`, `1279px`), which must remain synchronized with `packages/ui/tokens.css` breakpoints.
- **Mitigation Strategy:** Unit suites in `packages/ui` and `apps/subject-portal` plus Playwright E2E checks across `320x640`, `375x812`, `390x844`, `768x1024`, `1024x768`, `1024x416`, `1280x720`, and `1440x900` enforce header height, main column width, modal footer pinning, initial re-consent focus, peek drawer classes, and zero Axe violations.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/subject-portal`, `docs/adr/`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm --filter ui build`, `pnpm --filter ui test`, `pnpm --filter ui lint`, `pnpm --filter ui typecheck`, `pnpm --filter subject-portal test`, `pnpm --filter subject-portal lint`, `pnpm --filter subject-portal test:e2e`, `pnpm --filter web typecheck`, `pnpm --filter web lint`, and `cadence check` quality gates.
