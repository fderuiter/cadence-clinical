# ADR-119: Extend Centralized Permission Matrix for Clinical Execution

* **Status:** Accepted
* **Date:** 2026-08-28
* **Authors:** @jules
* **Deciders:** @lead-architect, @qa-validator

---

## 1. Context & Problem Statement
The clinical execution module requires clear, deterministic role-based boundaries on sensitive transactional resources (e.g., TSDV, Form Submission, PI Sign-off, Medical Coding, Trial Lock, and Unmasked Exports) to ensure compliance with FDA 21 CFR Part 11, EU Annex 11, and the trial blinding plan. In order to enforce these boundaries cleanly without splitting logic across downstreams, we need to extend the centralized declarative permission matrix in `packages/security/rbac.py` with these resources and align the system specifications and automated tests 1:1.

This ADR specifically relates to requirements under **PRD-SYS-001** and **Trace-14**.

## 2. Decision Drivers & Constraints
* **Driver 1:** Regulatory compliance (FDA 21 CFR Part 11 re-authentication and blinding plan integrity).
* **Driver 2:** Maintenance of a single source of truth for all role permissions.
* **Driver 3:** Keeping system specifications in sync with executable codebase definitions.
* **Constraint:** No performance regressions or dependencies on external authentication services.

## 3. Options Considered
### Option 1: Ad-hoc Role Checking in Endpoint Routers
* **Overview:** Enforce checks manually in routers or service layers using helper lists of authorized roles for each specific execution endpoint.
* **Pros:**
  * ✅ Quick to implement for individual endpoints.
* **Cons:**
  * ❌ Severe risk of role drift and security gaps across endpoints.
  * ❌ Violates the centralized RBAC design pattern.
  * ❌ Documentation cannot easily be validated against code programmatically.

### Option 2: Centralized Declared Resource Keys in RBAC Permission Matrix [Selected]
* **Overview:** Add centralized resource keys (`tsdv_config`, `form_submission`, `pi_signoff`, `medical_coding`, `trial_lock`, `export_unmasked`) directly to the centralized `ROLE_PERMISSIONS` dictionary in the shared `packages/security/rbac.py` package.
* **Pros:**
  * ✅ Single source of truth for all resource authorization queries across all microservices.
  * ✅ Simplifies automated integration testing of permissions.
  * ✅ Allows 1:1 trace documentation mapping.
* **Cons:**
  * ❌ Requires editing the core security packages and updating the human-readable matrix documentation.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 provides absolute structural integrity and prevents authorization drift, satisfying the regulatory trace audit guidelines.

## 5. Consequences & Trade-offs
* **Positive Impact:**
  * ✅ Unified permission checks with simple `has_permission(principal, "resource:action")` statements.
  * ✅ Robust unit test assertions and coverage.
  * ✅ Human-readable specs align completely with Python definitions.
* **Negative Impact / Technical Debt:**
  * ❌ Sponsoring developers must register any new resource keys in both `rbac.py` and the compliance specifications.

## 6. Implementation & Verification
* **Affected Repositories / Services:**
  * `packages/security/rbac.py`
  * `docs/SDLC/05_Security_Compliance_Audit_Spec.md`
* **Verification Plan:**
  * Added programmatic tests in `tests/test_rbac.py` executing `has_permission` assertions across all canonical roles.
  * Verified utilizing `uv run pytest tests/test_rbac.py --no-cov`.
