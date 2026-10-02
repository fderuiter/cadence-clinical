# ADR-2807: ModalErrorAlert Component and useAsyncRetry Composable Standard

- **Status:** Accepted
- **Date:** 2026-10-02
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Modal overlays (such as signature capture, file uploads, re-authentication, and upversioning impact drawers) require standardized inline error messaging and async action retry behaviors (PRD-SYS-001). Previously, error banners and retry actions were implemented ad-hoc across modal overlays, leading to inconsistent user experience and missing ARIA accessibility attributes (`role="alert"`, `aria-live="assertive"`).

## 2. Decision Drivers & Constraints

- **Driver 1:** Consistent error handling and retry patterns across all modal overlays in packages/ui and apps/web.
- **Driver 2:** Accessible error notifications using standard ARIA live region attributes (`role="alert"`, `aria-live="assertive"`).
- **Driver 3:** Clean state management for async function executions with retry capabilities without persisting sensitive payload credentials (PRD-SYS-001).

## 3. Options Considered

### Option 1: Shared ModalErrorAlert Component and useAsyncRetry Composable (Selected)

- **Overview:** Export `ModalErrorAlert.vue` and `useAsyncRetry` from `@cadence/ui`. `ModalErrorAlert` renders an accessible alert box with optional title, message slot, retry, and dismiss buttons. `useAsyncRetry` manages loading state, error state, execution, retries, and error clearing.
- **Pros:**
  - ✅ Standardized error UI and accessibility attributes across modal overlays (PRD-SYS-001).
  - ✅ Centralized async state management and retry helper.
  - ✅ Reusable across all frontend applications and UI packages.
- **Cons:**
  - ❌ Requires modal components to integrate with `useAsyncRetry` or handle `retry`/`dismiss` events.

### Option 2: Ad-hoc Error Alerts in Each Modal

- **Overview:** Keep individual error message elements in each modal component.
- **Pros:**
  - ✅ No shared package exports needed.
- **Cons:**
  - ❌ Inconsistent UI, duplicated logic, missing accessibility attributes, and unstandardized retry behavior.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Option 1 provides a unified, accessible, and testable error handling standard for modal overlays across the Cadence platform.

## 5. Consequences & Trade-offs

- **Positive Impact:** Standardized error displays, proper screen-reader alerts, and streamlined async retry handling across modal components.
- **Negative Impact / Technical Debt:** Minimal UI package surface expansion.
- **Mitigation Strategy:** Automated test coverage and CI lint validation.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`
- **Verification Plan:** Unit tests in `packages/ui/tests/ModalErrorAlert.test.js` and `apps/web/tests/components/ModalErrorAlertRetry.spec.ts`.
