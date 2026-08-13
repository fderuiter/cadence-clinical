# ADR-068: Decouple ClinicalQueryPanel from Pinia and Auth store

- **Status:** Accepted
- **Date:** 2026-08-13
- **Authors:** @jules
- **Deciders:** @jules

---

## 1. Context & Problem Statement

The `ClinicalQueryPanel.vue` component, situated in the shared UI primitives package (`packages/ui/`), was previously coupled directly to Pinia and the frontend application-level auth stores. This direct dependency violates clean architectural boundaries and hampers package portability and reusable rendering of shared components in isolated contexts.

To decouple these responsibilities and adhere to clean presentation patterns under `PRD-QRY-001`, we need to refactor `ClinicalQueryPanel` to consume capabilities like query management permissions and query labels via generic props rather than pulling directly from store instances.

## 2. Decision Drivers & Constraints

- **Driver 1:** Decouple package boundaries to allow `packages/ui` components to remain pure presentational items without dependency on global store engines (Pinia).
- **Driver 2:** Improve testability and component boundary validation in isolated test runners.
- **Driver 3:** Ensure strict compliance with query workflows outlined in `PRD-QRY-001`.

## 3. Options Considered

### Option 1: Keep direct Pinia imports and mock Pinia in all component tests

- **Overview:** Maintain the status quo where `ClinicalQueryPanel` resolves stores directly from the active Pinia instance.
- **Pros:**
  - ✅ No changes to other consuming views like `EcrfView.vue`.
- **Cons:**
  - ❌ Breaks package boundary clean separation.
  - ❌ Requires heavy mocking of auth and clinical stores in unit tests.

### Option 2: Decouple store dependency and pass down state as props (Selected)

- **Overview:** Refactor `ClinicalQueryPanel` to accept `canManageQueries` and `queryLabel` as props. Consuming components such as `ClinicalFormField` and `EcrfView` can then bind these properties from the store context.
- **Pros:**
  - ✅ Restores clean separation of concerns.
  - ✅ Simplifies unit testing by passing props directly.
- **Cons:**
  - ❌ Slightly increases prop-drilling depth through layouts.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Choosing Option 2 establishes clean presentational components inside `packages/ui/` and moves state coordination to the store/view layers of `apps/web/`. This is fully aligned with our architectural principles and directly supports query validation requirements under `PRD-QRY-001`.

## 5. Consequences & Trade-offs

- **Positive Impact:** Portability of `packages/ui` components is greatly improved.
- **Negative Impact / Technical Debt:** Requires explicit prop-drilling or slot passing across layout boundaries like `ClinicalFormField`.
- **Mitigation Strategy:** Propagate the generic props clearly using Vue's type checking.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/web`
- **Verification Plan:** Verified using unit tests in `clinical_components.test.js` and automated CI style/lint checking.
