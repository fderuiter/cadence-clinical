# ADR-2172: Standardize Batch Signature Flow Asynchronous State with TanStack Query

- **Status:** Accepted
- **Date:** 2026-08-13
- **Authors:** @google-labs-jules[bot]
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

In the regulatory batch signature flow, asynchronous operations (such as password validation, cryptographic signing, certificate manifestation, and offline demo persistence) were previously intertwined with Pinia client state and local UI state. This created high complexity in state tracking, potential error-handling leaks, and difficulty in ensuring strict identity re-authentication.

To improve robustness, separation of concerns, and automatic caching/mutation state updates, we need to standardize the asynchronous state orchestration of the regulatory batch signature flow. This decision implements and supports requirements under **PRD-SYS-001** and **PRD-CRF-006** for 21 CFR Part 11 compliant electronic signatures.

## 2. Decision Drivers & Constraints

- **Driver 1:** Regulatory compliance with 21 CFR Part 11 and GxP standards regarding identity re-authentication, change justification, and auditable evidence.
- **Driver 2:** Separation of concerns: keep the Pinia store synchronous and lightweight, while offloading asynchronous network operations to a dedicated query/mutation engine.
- **Driver 3:** Robust error handling, password wiping, and user experience with immediate feedback (loading/pending states, error alerts).
- **Constraint:** Maintain strict compatibility with Vue 3 SFC structures and existing backend/demo-mode execution interfaces.

## 3. Options Considered

### Option 1: Monolithic Action-Based Store Actions in Pinia

- **Overview:** Perform asynchronous API dispatching inside Pinia action functions, setting `isSigning`, `signatureError`, and `lastSignatureResult` state variables inside the store.
- **Pros:**
  - ✅ Familiar pattern for legacy Vue architectures.
- **Cons:**
  - ❌ Bloats store state with transient asynchronous lifecycle states.
  - ❌ Increases potential for state leaks (e.g., stale errors or pending states persisting across modal openings).
  - ❌ More difficult to handle cleanup (like immediate password wiping upon error or completion).

### Option 2: Migrating Asynchronous State Orchestration to TanStack Query (`useMutation`) [Selected]

- **Overview:** Use `@tanstack/vue-query`'s `useMutation` hook directly inside the signature modal to manage the async lifecycle, keeping Pinia store (`stores/signatures.ts`) strictly synchronous for form selection.
- **Pros:**
  - ✅ Decoupled client state (selected IDs) from network transactional state (pending/resolved/rejected).
  - ✅ Automatic handling of loading indicators, error reporting, and completion events.
  - ✅ Enhanced security: password input and credentials are scoped locally within the modal closure and wiped instantly after executing the mutation.
  - ✅ Improved testability: allows configuring fresh isolated `QueryClient` instances to prevent cross-test leakage.
- **Cons:**
  - ❌ Introduces a dependency on `@tanstack/vue-query`.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Option 2 provides a clean separation of concerns, simplifies the Pinia store logic, improves visual state reactivity inside `BatchSignatureModal.vue`, and ensures strict compliance with security and re-authentication mandates.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Clean separation between synchronous user selections (Pinia) and asynchronous network processes (TanStack Query).
  - Automatic loading/error state tracking, eliminating boilerplate UI reactive variables.
  - Guaranteed security: credentials are never stored in global reactive state and are immediately wiped.
- **Negative Impact / Technical Debt:**
  - Adds `@tanstack/vue-query` as a core dependency in the Vue SPA.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/web/`
- **Verification Plan:**
  - Run the frontend unit/integration test suite (`pnpm --filter web test`) to ensure all `BatchSignatureModal.spec.ts` cases pass.
  - Execute `python3 scripts/validate_adrs.py` to confirm correct ADR naming, required sections, and PRD-SYS-001/PRD-CRF-006 requirement mapping.
