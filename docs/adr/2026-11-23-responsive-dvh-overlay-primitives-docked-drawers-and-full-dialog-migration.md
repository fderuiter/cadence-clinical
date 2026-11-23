# ADR-2651: Responsive dvh Overlay Primitives, UX-3 Docked Drawers, and Full Dialog Migration

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (21 CFR Part 11 step-up electronic signature reachability, single-use `X-Sig-Token`, and dismissal locks), PRD-CRF-015 (WCAG 2.2 Level AA focus containment, three-tier responsive viewport geometry, and `UX-3` non-modal desktop inspectors)

---

## 1. Context & Problem Statement

Following the initial migration of unguarded web overlays in [ADR-2821](2026-11-22-migrate-web-overlays-to-clinical-primitives-and-enforce-ast-guardrails.md), two systemic gaps remained across `packages/ui`, `apps/web`, and `apps/subject-portal`:

1. **Static `vh` Bounds & Missing Safe-Area Viewport Fit:** Neither `apps/web/index.html` nor `apps/subject-portal/index.html` declared `viewport-fit=cover`, while `ClinicalModal.vue`, `ClinicalDrawer.vue`, `AiSuggestionDrawer.vue`, `ContextualHelpDrawer.vue`, `apps/web/src/style.css`, and `apps/subject-portal/src/style.css` used static `100vh` / `90vh` dimensions without `dvh` units or `env(safe-area-inset-bottom)` clearance. In addition, `ClinicalDrawer.vue` hardcoded `aria-modal="true"` and trapped focus via `useFocusTrap` at every viewport width rather than supporting non-modal `1280px+` desktop inspector docking (`UX-3` in [CLINICAL_CANVAS.md](../ux/CLINICAL_CANVAS.md)).
2. **`useFocusTrap` AST Exemption & Clipped Part 11 Signing Actions:** `custom-vue/no-raw-overlays` in `eslint.config.mjs` exempted raw `<div class="modal-backdrop">` / `<div class="modal-overlay">` markup whenever `<script>` imported `useFocusTrap`. That loophole allowed 43 bespoke modal and drawer files across `apps/web` and `apps/subject-portal`—including `SignatureCaptureModal.vue`, `TicketSignModal.vue`, `ReAuthModal.vue`, `SignatureModal.vue`, and `ReconsentModal.vue`—to bypass shared scroll and safe-area geometry, clipping `#btn-confirm-sig` on `320x640` mobile screens and `1024x416` keyboard-open tablet viewports. Meanwhile, imperative DOM wiring in `apps/subject-portal/src/features/signature.js` required persistent DOM nodes when closed (`keepMounted`).

## 2. Decision Drivers & Constraints

- **Unclipped 21 CFR Part 11 Signing Controls (PRD-SYS-001):** Every step-up re-authentication and signature dialog must keep credentials, reason selectors, inline error alerts (`#sig-error-msg`), and confirmation actions (`#btn-confirm-sig`) vertically scrollable and reachable at `320x640`, `390x844`, `1024x416`, and `1440x900` while preserving single-use `X-Sig-Token` headers, `X-Change-Reason` headers, immediate credential clearing, and `:dismissible="!busy"` submission locks.
- **Three-Tier Responsive Geometry & `UX-3` Desktop Docking (PRD-CRF-015):** Overlays must adapt across `320px–480px` (`90dvh` bottom-anchored sheets with `vh` fallbacks and `env(safe-area-inset-bottom)` padding), `481px–1279px` (scrollable centered modals and `100dvh` slide-over drawers), and `1280px+` (`desktopMode="docked"` drawers that omit `aria-modal`, backdrop masks, `Tab` capture, and sibling `inert` / `aria-hidden` attributes).
- **Zero Raw Overlay Exemptions:** `eslint.config.mjs` and `packages/ui/tests/design_system_harmony.test.js` must reject all raw overlay containers and raw `aria-modal` elements outside `packages/ui/src/components/` across both frontend apps, even when `useFocusTrap` is imported.

## 3. Options Considered

### Option 1: Upgrade Shared `ClinicalModal` / `ClinicalDrawer` / `useFocusTrap` Primitives, Migrate All 43 Raw Dialogs, and Close the `useFocusTrap` ESLint Exemption (Selected)

- **Overview:** Add `viewport-fit=cover` to both HTML entry points; define `100dvh` / `90dvh` (with `100vh` / `90vh` fallbacks) and safe-area tokens in `packages/ui/tokens.css` and `packages/ui/responsive.css`; upgrade `ClinicalModal.vue` and `ClinicalDrawer.vue` with `90dvh` mobile bottom sheets, `keepMounted` persistent `v-show` rendering, and `desktopMode="docked"` (`1280px+`) support coordinated with `useFocusTrap.js`; migrate all 43 remaining raw dialog files across `apps/web` and `apps/subject-portal` (preserving non-modal anchored tour popovers in `OnboardingTour.vue` and replacing overlay Unicode emojis with `lucide-vue-next` SVGs); delete static `100vh` overrides; remove the `importsFocusTrap` exemption in `eslint.config.mjs`; and expand `design_system_harmony.test.js` across both apps.
- **Pros:**
  - ✅ Guarantees reachable Part 11 signing controls, safe-area clearance, and consistent three-tier responsive behavior from a single shared implementation in `packages/ui`.
  - ✅ Fulfills `UX-3` non-modal desktop docking for contextual help, SOP, and readability inspectors without regressing mobile/tablet modal containment.
  - ✅ Permanently closes the `useFocusTrap` raw-overlay loophole at both lint time and test time across `apps/web` and `apps/subject-portal`.
- **Cons:**
  - ❌ Requires `keepMounted` support in `ClinicalModal` and `ClinicalDrawer` so legacy imperative DOM selectors in `apps/subject-portal/src/features/signature.js` continue to bind cleanly when closed.

### Option 2: Patch Overflow CSS Individually on Bespoke Signing Modals and Retain `useFocusTrap` Exemptions

- **Overview:** Add `max-height: 90dvh; overflow-y: auto` directly to `SignatureCaptureModal.vue` and `TicketSignModal.vue` while leaving the remaining raw dialogs on custom `useFocusTrap` wrappers.
- **Pros:**
  - ✅ Smaller diff in legacy views.
- **Cons:**
  - ❌ Leaves 40+ bespoke dialogs vulnerable to static `vh` clipping, missing safe-area insets, inconsistent keyboard dismissal locks, and `UX-3` modal-inspector violations.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Centralizing `dvh` safe-area bottom sheets, `keepMounted` persistent rendering, and `UX-3` desktop docking inside `ClinicalModal`, `ClinicalDrawer`, and `useFocusTrap`—and migrating every remaining raw dialog so the `importsFocusTrap` lint exemption can be deleted—eliminates off-screen Part 11 signing controls and enforces one governed overlay standard across both apps.

## 5. Consequences & Trade-offs

- **Positive Impact:** Zero raw modal or drawer containers remain outside `packages/ui`; `#btn-confirm-sig` and `#sig-error-msg` remain reachable on compact and keyboard-open viewports; `1280px+` docked drawers keep main workspace siblings interactive; and overlay icons adhere to zero-emoji `lucide-vue-next` clinical iconography.
- **Negative Impact / Technical Debt:** Non-modal floating popovers (such as anchored tour steps in `OnboardingTour.vue`) continue to use `role="dialog"` without `aria-modal` or backdrop containers.
- **Mitigation Strategy:** `custom-vue/no-raw-overlays` and `design_system_harmony.test.js` prohibit raw overlay containers and raw `aria-modal` elements outside `packages/ui/src/components/`, while unit and Playwright Chromium suites verify scroll reachability, bottom-sheet anchoring, and docked non-modal inspector semantics.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`, `eslint.config.mjs`, `docs/adr/`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm -r lint`, `pnpm --filter ui build`, `pnpm --filter ui test`, `pnpm --filter web test`, `pnpm --filter subject-portal test`, `pnpm --filter web typecheck`, `pnpm build`, `pnpm build:demo`, headless Chromium viewport verification (`320x640`, `390x844`, `1024x416`, `1440x900`), `pnpm cadence check adrs`, `pnpm cadence check markdown`, and `pnpm cadence check deid`.
