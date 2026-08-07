# ADR-2162: Skip to content link and strict layout validation

- **Status:** Accepted
- **Date:** 2026-08-07
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Keyboard-only and screen-reader users currently experience high interaction friction on every page transition. Without a bypass mechanism, users must tab through multiple repetitive global navigation elements—including compliance badges, status cards, and seven sidebar navigation buttons—before reaching the actual page content. This violates WCAG 2.4.1 (Bypass Blocks).

Furthermore, this gap went unnoticed because our automated accessibility testing suite defaults to a relaxed validation mode. This suppresses landmark checks (`landmark-one-main`) to support isolated component fragment testing, masking structural accessibility regressions in CI/CD.

We need a way to support bypass blocks via a skip-to-content link, global visually hidden styling, and a customizable strict mode toggle for accessibility testing (PRD-CRF-015).

## 2. Decision Drivers & Constraints

- Ensure WCAG 2.4.1 (Bypass Blocks) compliance in AppShell layouts.
- Support isolated frontend component tests without enforcing layout landmark checks globally.
- Minimize inline CSS duplications for visually-hidden focusable skip-links.
- Support strict structural GxP/WCAG verification (PRD-CRF-015).

## 3. Options Considered

1. **Option A (Selected): First-Focusable Skip Link & Strict Validation Option Toggle**
   - Place the "Skip to main content" link as the absolute first focusable element in the DOM inside `AppShell.vue`.
   - Visual concealment via global stylesheet (`style.css`) using `.skip-link` utility.
   - Target the main content container with `id="main-content"` and add `tabindex="-1"`.
   - Update the custom `accessibility-matcher.js` to allow tests to deep-merge custom options, enabling individual tests (like `AppShell.spec.js`) to toggle strict landmark checks.

2. **Option B: Strict Global Landmark Checks**
   - Enforce landmark rules (`landmark-one-main`) globally for all component tests.
   - Negative: Breaks hundreds of isolated unit/fragment component tests.

## 4. Decision Outcome

Chosen option: Option A because it allows us to enforce strict layout accessibility validation specifically on shell/layout components while maintaining compatibility with simple fragment/component tests, and satisfying PRD-CRF-015.

## 5. Consequences & Trade-offs

- Positive: Full WCAG 2.4.1 compliance on the primary layout.
- Positive: Granular control over accessibility rule auditing via deep-merging options inside `toBeAccessible` matcher.
- Negative: Must manually identify and wrap page content with `<main id="main-content" tabindex="-1">`.

## 6. Implementation & Verification

- **Target files/packages modified:**
  - `apps/web/src/components/AppShell.vue`
  - `apps/web/src/style.css`
  - `packages/ui/accessibility-matcher.js`
  - `apps/web/tests/components/AppShell.spec.js`
- **Verification tests added:**
  - Strict accessibility and skip-to-content structure tests are in `apps/web/tests/components/AppShell.spec.js`.
