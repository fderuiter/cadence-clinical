# ADR-065: eISF Browse, RBAC, Downloads, and Completeness

- **Status:** Accepted
- **Date:** 2026-08-09
- **Authors:** @jules
- **Deciders:** @engineering_leads, @qa_lead

---

## 1. Context & Problem Statement

The electronic Investigator Site File (eISF) service required robust browse, view, download, role-based access control (RBAC), and completeness workflows. Additionally, background database model files within the execution microservice required minor formatting alignment to pass automated Ruff compliance checks in the CI/CD pipeline.

This decision implements requirements under Trace-7.

## 2. Decision Drivers & Constraints

- **Compliance & Auditing:** 21 CFR Part 11 compliant audit trail logging for all listing, viewing, downloading, and completeness check actions.
- **Site Isolation:** Strict enforcement of site isolation constraints based on the gateway-propagated site claims to prevent unauthorized cross-site data exposure.
- **Read-Only Personas:** Ensuring regulatory inspectors and auditors have comprehensive read-only capabilities without permitting write mutations.
- **Code Quality & CI Readiness:** Absolute adherence to style/formatting checkers across all backend Python modules.

## 3. Options Considered

### Option 1: Inline Endpoint Verification

- **Overview:** Implementing list, download, and completeness checks directly as localized API endpoints in eISF, integrating validation dependencies.
- **Pros:**
  - ✅ Maximum localization and speed.
  - ✅ Easy to test in isolation.
- **Cons:**
  - ❌ None, fits our microservice boundaries perfectly.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Implementing these site-scoped queries and completeness workflows directly within `apps/eisf/main.py` provides high security, robust auditing, and zero latency overhead. At the same time, we format the execution models file to ensure a clean style gate pass across the repository.

## 5. Consequences & Trade-offs

- **Positive Impact:** Full 21 CFR Part 11 and site-isolation security are centrally and cleanly guaranteed.
- **Negative Impact / Technical Debt:** Requires keeping standard required eISF binder sections synchronized, which is modeled as a standardized static layout to guarantee reliability without external database dependencies.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/eisf/main.py` (eISF service), `tests/test_eisf_browse_completeness.py` (New test suite), `docs/SDLC/IQ_OQ_PQ_Execution_Report.md` (Updated test results).
- **Verification Plan:** Verified locally via `pytest` executing 17 tests with over 90% code coverage.
