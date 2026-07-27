# ADR-065: Debounced Clinical Code Lookup UI Primitive

* **Status:** Accepted
* **Date:** 2026-08-10
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
To enforce data standards, such as NCI Thesaurus Controlled Terminology (CT), clinical metadata designers and investigators need rapid feedback when entering terminology concept codes in eCRF forms. However, making direct, synchronous network requests on every keystroke causes excessive request floods to terminology endpoints, degrades client and server performance, and disrupts standard synchronous validation workflows. We need a reusable, accessible UI primitive and debounce utility that delivers real-time validation feedback.

## 2. Decision Drivers & Constraints
* **Driver 1:** Support distinct loading, valid, invalid, and degraded states accessibly (using standard ARIA status regions).
* **Driver 2:** Prevent network floods during typing with a bounded debounce utility.
* **Driver 3:** Maintain loose decoupling from specific endpoints so the component is reusable in any application or environment.
* **Driver 4:** Ensure it works seamlessly alongside existing synchronous form validation loops.

## 3. Options Considered
### Option 1: Monolithic Inline Input Component
* **Overview:** Write a single, self-contained component bound directly to specific NCI EVS API lookup endpoints.
* **Pros:**
  * ✅ Quick to integrate in a single view.
* **Cons:**
  * ❌ Not reusable in other contexts.
  * ❌ Couples the UI rendering layer with specific API client libraries and credentials.

### Option 2: Pure-JS HTML Primitive and Decoupled Debounce Helper
* **Overview:** Implement `createClinicalLookupInput` as a pure JavaScript function that returns accessible HTML strings, coupled with a pure `debounce` utility. The application is responsible for wiring the debounced keyup/input handler to execute the API call and update the visual status.
* **Pros:**
  * ✅ Highly flexible and independent of specific endpoints or OIDC authentication details.
  * ✅ Accessible states are fully distinguishable and styled under unified CSS variables.
  * ✅ Supports clean, standard asynchronous integration without disrupting synchronous form validation.
* **Cons:**
  * ❌ Requires the host application to write the small glue code to wire the event listener and the API client.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 offers maximum reusability and architectural decoupling, complying with Cadence Clinical's technical standards. It empowers any view (eCRF, Global Library, MDR rules) to implement high-fidelity, debounced terminology lookups with visual status badges that reuse the existing UI theme and conventions.

## 5. Consequences & Trade-offs
* **Positive Impact:**
  * Consistent UI representation for lookup states across the workspace.
  * Screen-reader accessible feedback with `aria-live="polite"` and `role="status"`.
  * Drastic reduction in terminology service queries.
* **Negative Impact / Technical Debt:**
  * Developers must explicitly register a debounced event handler to trigger lookups on custom eCRFs.
* **Mitigation Strategy:**
  * Clear documentation, jsdocs, and comprehensive unit and verification suites are provided to demonstrate usage.

## 6. Implementation & Verification
* **Affected Repositories / Services:**
  * `packages/ui`: Modified `packages/ui/index.js` and `packages/ui/tests/index.test.js` to implement and test `createClinicalLookupInput` and `debounce`.
  * `apps/web`: Modified `apps/web/src/style.css` to add CSS rules for lookups.
* **Verification Plan:**
  * Unit tests in `packages/ui/tests/index.test.js` run via Vitest.
  * Playwright visual test script inside `verification/verify_lookup.py` captures rendered visual states against all four lookup states.
