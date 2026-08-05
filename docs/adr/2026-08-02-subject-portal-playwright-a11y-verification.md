# ADR-180: Subject Portal Accessibility Auditing and Verification

- **Status:** Accepted
- **Date:** 2026-08-02
- **Authors:** @jules
- **Deciders:** @jules

---

## 1. Context & Problem Statement

To satisfy PRD-SYS-001, we want to ensure high-fidelity compliance and quality standards of the patient-facing subject portal. Specifically, we introduced standalone Playwright-based headless browser tests in `apps/subject-portal` to verify accessibility conformance with WCAG 2.1 AA standards across the entire portal workflow. We also resolved pre-existing Python/Ruff linting warnings on `main` to ensure that our continuous integration validation passes successfully.

## 2. Decision Drivers & Constraints

- **Strict WCAG AA Verification:** Ensure the patient-facing clinical questionnaire components are accessible.
- **Build Integrity:** Keep the repository build and style verification passing.
- **Traceability:** Trace directly to GxP systems verification under PRD-SYS-001.

## 3. Options Considered

### Option 1: Manual accessibility verification

- **Overview:** Check accessibility using manual browser extensions.
- **Pros:** Simple setup.
- **Cons:** Cannot be automated or verified as part of CI/CD pipelines.

### Option 2: Automated Playwright and axe-core pipeline (Selected)

- **Overview:** Implement automated headless Playwright tests driving axe-core audits.
- **Pros:**
  - ✅ Full automation on every PR.
  - ✅ High-fidelity offline and authenticated mock session support.
- **Cons:** Requires running browser instances in CI environments.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Choosing automated Playwright axe-core verification allows us to guarantee that zero accessibility regressions are introduced in the clinical subject portal, fulfilling compliance constraints under PRD-SYS-001.

## 5. Consequences & Trade-offs

- **Positive Impact:** Automatic gating of all UI modifications against WCAG AA requirements.
- **Negative Impact / Technical Debt:** Added test suite execution time in frontend pipelines.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/subject-portal`
- **Verification Plan:** Verified locally via `pnpm lint`, `pnpm check`, and `pnpm --filter subject-portal lint` to guarantee that all style, formatting, and design guidelines are fully met.
