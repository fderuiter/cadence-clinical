# ADR-256: Scope CSS hover to can hover and add touch tap fallbacks

* **Status:** Accepted
* **Date:** 2026-08-04
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Standard CSS `:hover` states trigger inconsistently on mobile/touch screens, often causing a "sticky hover" state where an element remains highlighted or visible until the user taps elsewhere. To improve mobile user experiences across our clinical and patient portals (eCOA, ePRO), we must scope hover-based visual feedback so that it only applies to pointer devices supporting hover capability, and implement explicit tap fallbacks for touch interactions.

## 2. Decision Drivers & Constraints

* **Usability & Accessibility:** Ensuring WCAG compliance and optimal touch targets for patients with physical or visual impairments under **PRD-CRF-015** / **Trace-31**.
* **Consistency:** Centralized, reusable helper across both the clinical `apps/web` app and patient-facing `apps/subject-portal` (**PRD-SYS-001**).
* **GxP Requirements:** Regulatory guidelines for electronic clinical outcome assessments require clear, deterministic layout behaviors across varying user hardware.

## 3. Options Considered

### Option 1: Native CSS `@media (hover: hover)` Media Queries
* **Overview:** Directly wrap all `:hover` rules inside a `@media (hover: hover)` media query.
* **Pros:**
  * ✅ Declarative and runs without JS.
* **Cons:**
  * ❌ Harder to dynamically override for testing or manual accessibility overrides.
  * ❌ No shared, programmatic way to query hover capability within Vue JS application states.

### Option 2: Centralized dynamic pointer detection via body class (Selected)
* **Overview:** Centralize a JS helper in the shared `packages/ui` package that queries `window.matchMedia("(hover: hover)")`, adds/removes `.can-hover` class on `document.body`, and monitors changes.
* **Pros:**
  * ✅ Simple CSS scoping (e.g. `.can-hover .btn:hover`).
  * ✅ Allows Vue components to check body classes or shared media query states.
  * ✅ Completely avoids sticky hovers on touch devices.
* **Cons:**
  * ❌ Requires a brief JS execution on initial script load.

## 4. Decision Outcome

Chosen Option 2. Centralizing dynamic hover pointer capability detection inside `packages/ui/index.js` allows us to easily scope CSS hover rules to `.can-hover` selectors while providing a clean foundation for touch tap fallback interactions across the system.

## 5. Consequences & Trade-offs

* **Positive Impact:** All mobile and tablet devices are freed from sticky hover artifacts. Touch tap interactions function with appropriate visual transitions.
* **Negative Impact / Technical Debt:** Requires developers to explicitly write `.can-hover` or similar scoped patterns for hover transitions.
* **Mitigation Strategy:** Automated linters and review checklists will ensure correct application of scoped styles.

## 6. Implementation & Verification

* **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`.
* **Verification Plan:** Verify ADR validation by running validation scripts and check for successful execution of frontend and backend tests.

