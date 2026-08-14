# ADR-256: Centralized layout utilities and 48px touch targets

- **Status:** Accepted
- **Date:** 2026-08-04
- **Authors:** @jules
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Previously, hardcoded inline styles and component-level layout declarations overrode global responsive behaviors. On mobile viewports and screens narrower than 1024px, multi-column grids failed to collapse, and tightly packed button rows did not wrap. This caused horizontal layout overflows, clipped interfaces, and high rates of accidental misclicks on destructive elements (e.g., clicking "Delete" instead of reordering arrows).

To resolve these issues and align with modern accessibility guidelines, this ADR establishes:

1. Transitioning layout controls from inline styles to centralized, token-driven CSS utility classes.
2. Increasing the mobile touch-target threshold from 44px to 48px to eliminate destructive misclicks and satisfy **PRD-CRF-015** (WCAG 2.1 AA criteria).
3. Enforcing a consistent stacking breakpoint at `< 1024px` for multi-column structures.

## 2. Decision Drivers & Constraints

- **Driver 1:** Preservation of touch-target compliance (minimum height/width of 48px) on touch devices and mobile/tablet viewport simulations to satisfy WCAG 2.1 AA accessibility regulations (**PRD-CRF-015** / **Trace-31**).
- **Driver 2:** Avoidance of horizontal overflow and clipped user interfaces on viewports narrower than 1024px.
- **Driver 3:** Decentralized inline styling was hard to maintain and caused layout inconsistencies across clinical view templates.

## 3. Options Considered

### Option 1: Hardcoded Element-Level Styles

- **Overview:** Keep individual styles on elements using hardcoded pixel values and inline CSS declarations.
- **Pros:** Quick to implement at the component level.
- **Cons:** Disallows uniform token overrides, does not scale, and fails to handle responsive wrapping consistently.

### Option 2: Token-Based Centralized Responsive Utilities (Selected)

- **Overview:** Transition layout controls and touch target sizing to global token properties (`--touch-target-min: 48px`) in shared stylesheets (`packages/ui/responsive.css`, `packages/ui/tokens.css`). Define standardized responsive layout utilities (`.grid-2-responsive`, `.grid-layout-responsive`, and `.responsive-grid`) collapsing at a strict 1024px threshold.
- **Pros:** Highly consistent, reusable, and allows rapid system-wide modifications.
- **Cons:** Requires refactoring multiple clinical view files to eliminate inline layout styling.

## 4. Decision Outcome

Chosen option: **Option 2 (Token-Based Centralized Responsive Utilities)** because it centralizes layout definitions, eliminates visual clutter, prevents horizontal clipping under 1024px, and increases the touch target threshold to 48px to fully satisfy GxP accessibility compliance under **PRD-CRF-015**.

## 5. Consequences & Trade-offs

- **Positive Impact:** Perfect compliance with WCAG 2.1 AA 48px touch targets, cleaner component templates with token-driven responsive spacing, and fluid single-column fallback configurations under 1024px.
- **Negative Impact:** Existing test cases and snapshots might require updates to align with the 48px touch targets instead of the previous 44px standard.

## 6. Implementation & Verification

- **Affected Repositories / Services / Files:**
  - `packages/ui/tokens.css` & `packages/ui/responsive.css`
  - `apps/web/src/style.css` & `apps/subject-portal/src/style.css`
  - `ConsentAuthoringView.vue`, `RulesView.vue`, `MdrView.vue`, `AuditView.vue`, `CtmsView.vue`, `EcrfView.vue`
  - `SignatureCaptureModal.vue`, `ApprovalHandoffModal.vue`
- **Verification Plan:**
  - Execute frontend tests using `pnpm -r test` to ensure layout helper assertions continue to pass.
  - Run `uv run python scripts/validate_adrs.py` to confirm successful ADR indexing and formatting.
