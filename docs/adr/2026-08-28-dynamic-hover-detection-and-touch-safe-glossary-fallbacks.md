# ADR-2154: Dynamic Hover Pointer Capability Detection & Touch-Safe Glossary Fallbacks

- **Status:** Accepted
- **Date:** 2026-08-28
- **Authors:** @jules
- **Deciders:** @fderuiter
- **Requirement Reference:** PRD-CRF-015, Trace-33

---

## 1. Context & Problem Statement

In multi-device clinical environments, users access the electronic Informed Consent Form (eConsent) and Patient Portal on desktops, tablets, and smartphones.

Historically, interactive visual clues like hover states (`:hover`) were designed assuming a standard desktop/pointer environment. On touch screens (e.g., tablets and smartphones), hover effects cause "sticky" or frozen visual states because touch taps simulate a hover event that is never cleared until another element is tapped.

Furthermore, the eConsent Glossary component (`IcfSectionEditor.vue`) showed definitions of glossary terms on hover using a popover. On touch devices, this triggered stickiness, making it impossible to clear the active popover or causing a disrupted user experience for clinical participants.

## 2. Decision Drivers & Constraints

- **Driver 1 (User Experience & Accessibility):** Ensure touch device users do not experience sticky, un-dismissible popovers or stuck button hover states.
- **Driver 2 (Code Duplication):** Centralize hover pointer capability detection into a single reusable helper function in the shared `@cadence/ui` package rather than duplicating it across apps.
- **Driver 3 (Browser Compatibility):** Gracefully handle environments without robust `matchMedia` support by providing high-fidelity fallback behavior.

## 3. Options Considered

### Option 1: Standard Hover CSS Rules and Basic Alert Fallbacks

- **Overview:** Keep standard `:hover` rules globally and fallback to `window.alert` for all glossary clicks across all platforms.
- **Pros:**
  - Simple to implement.
- **Cons:**
  - ❌ Sticky states remain unresolved on mobile/touch interfaces.
  - ❌ `alert()` creates an intrusive blocking dialog on mobile devices.

### Option 2: Dynamic body class `.can-hover` and tap toggles with click-outside listeners

- **Overview:** Scope CSS hover styles under a `.can-hover` parent class. Dynamically append `.can-hover` to `document.body` by evaluating `window.matchMedia('(hover: hover)').matches`. Re-bind the glossary popover to support tap-to-toggle on mobile, and add a document-wide listener to dismiss active popovers when tapping outside.
- **Pros:**
  - ✅ Eliminates sticky hover states on mobile/touch devices entirely.
  - ✅ Keeps accessibility-oriented states (`:focus`, `:focus-visible`) intact on all environments.
  - ✅ Provides an intuitive tap-to-toggle and dismiss-outside flow for glossary definitions on mobile screens.
- **Cons:**
  - ❌ Requires a small initialization script execution upon loading pages.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Option 2 provides a comprehensive, modern solution to touch-screen stickiness without degrading desktop mouse-based experiences or compromising key accessibility expectations (such as focus indicators).

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Interactive elements behave exactly as expected across all form factors.
  - Clear code organization where hover capability is bootstrapped from the shared UI library.
- **Negative Impact / Technical Debt:**
  - Slight dependency on JavaScript execution for hover styling, which is mitigated by defaulting to adding the `.can-hover` class in non-browser or non-supported environment checks.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `packages/ui/index.js` (Centralized `initHoverDetection` helper)
  - `apps/web/src/main.js` (Boots helper)
  - `apps/subject-portal/src/index.js` (Boots helper)
  - `apps/web/src/components/econsent/IcfSectionEditor.vue` (Touch/mobile adaptivity)
- **Verification Plan:**
  - Verified via a comprehensive unit test suite inside `IcfSectionEditor.spec.js` asserting proper behavior on both desktop/pointer (with hover matches) and mobile/touch (without hover matches) simulated environments.
  - ADR validation successfully verified through local run of `scripts/validate_adrs.py`.
