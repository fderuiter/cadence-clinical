# ADR-252: Implement slot-driven dynamic accessibility context in Vue 3 SPA components

- **Status:** Accepted
- **Date:** 2026-08-11
- **Authors:** @jules
- **Deciders:** @jules

---

## 1. Context & Problem Statement

Our shared clinical UI components packages inside `packages/ui` must comply with strict accessibility guidelines (such as WCAG 2.1) to ensure safe and inclusive use by screen-reader users, as specified under **PRD-CRF-015** and **Trace-33**. Prior implementations lacked a cohesive slot-driven architecture to communicate dynamic accessibility attributes like field labels, input relationships, validation error status, and input descriptions directly from the layout wrapper to nested form elements (e.g., input controls, lookup selectors, or radio groups).

This decision establishes a slot-driven context system for shared Vue 3 SPA components, passing accessibility states (labels, help text, error boundaries, unique IDs, and descriptions) dynamically to nested components.

Requirements Traceability:

- **PRD-CRF-015**: In-Memory Accessibility Auditing
- **Trace-33**: In-Memory Accessibility Auditing

## 2. Decision Drivers & Constraints

- Maintain full WCAG 2.1 compliance and screen-reader accessibility for dynamic form fields.
- Avoid introducing heavyweight state-management libraries or external design system bindings to keep `packages/ui` portable and dependency-light.
- Keep components intuitive, modular, and easy for clinical developers to use.

## 3. Options Considered

### Option 1: Explicit Prop Drilling

- **Overview:** Explicitly pass properties (like error states, descriptions, and IDs) from parent layout components down to child inputs.
- **Pros:**
  - ✅ Simple to trace in code.
- **Cons:**
  - ❌ High boilerplate and brittle integration across deeply-nested custom slots.

### Option 2: Slot-Driven Accessibility Context (Selected)

- **Overview:** Leverage Vue 3 slot scopes to dynamically expose accessibility context (attributes like `id`, `aria-describedby`, `aria-invalid`, and labeled IDs) from the outer wrapper directly to standard inputs and custom inputs inside slots.
- **Pros:**
  - ✅ Decoupled parenting with high extensibility.
  - ✅ Automatic mapping of validation alerts and error tags.
  - ✅ Elegant and robust screen-reader announcement.
- **Cons:**
  - ❌ Requires slightly more advanced slot-usage understanding from clinical developers.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Option 2 solves the boilerplate issues of prop drilling while guaranteeing that nested inputs always inherit the correct `aria-describedby` and validation states in accordance with **PRD-CRF-015**.

## 5. Consequences & Trade-offs

- **Positive Impact:** Screen readers can now dynamically announce validation statuses and associated labels cleanly.
- **Negative Impact / Technical Debt:** Slot-usage becomes the standard, requiring developers to follow slot-scope API patterns.
- **Mitigation Strategy:** Provide consistent templates and comprehensive regression testing.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`
- **Verification Plan:** Validated via Vitest testing in `apps/web/tests/clinical_components.test.js` and local ADR quality gate verification runs.
