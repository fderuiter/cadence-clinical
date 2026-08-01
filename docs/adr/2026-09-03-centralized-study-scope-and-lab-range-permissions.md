# ADR-145: Centralized Study Scope and Lab Range Permissions

* **Status:** Accepted
* **Date:** 2026-09-03
* **Authors:** @jules
* **Deciders:** @engineering-lead, @quality-officer
* **Requirements:** PRD-SYS-001

---

## 1. Context & Problem Statement
To enforce secure, multi-tenant trial security at the API Gateway and microservice boundary, the execution microservice needs a reusable study scope checking utility. Currently, `StudyScopeChecker` class and `require_study_scope` are duplicated or implemented locally. In addition, roles require proper permissions for lab ranges to ensure that data management and clinical reader roles can trigger alerts and perform read/write operations with precise access control.

## 2. Decision Drivers & Constraints
* **Driver 1:** 21 CFR Part 11 and GxP compliance (`PRD-SYS-001`).
* **Driver 2:** Code duplication prevention and DRY principles.
* **Driver 3:** Robust study-scope isolation for data entry, monitoring, and administrative roles.

## 3. Options Considered
### Option 1: Inline study-scoping in every route handler
* **Overview:** Check study-scoping explicitly inside every route handler function.
* **Pros:**
  * ✅ High visibility on an endpoint-by-endpoint basis.
* **Cons:**
  * ❌ Severe code duplication, prone to security gaps if developers omit scope checks on new endpoints.

### Option 2: Centralized StudyScopeChecker dependency and role permission adjustments
* **Overview:** Move `StudyScopeChecker` and `require_study_scope()` to the shared `packages/security/rbac.py` package, and add the `alert` action to existing `lab_range` resource permissions for targeted roles.
* **Pros:**
  * ✅ 100% DRY compliance, eliminating local duplication in routing layer.
  * ✅ Clean, declarative security boundary across all execution and monitoring routers.
* **Cons:**
  * ❌ Couples routing dependencies to packages/security, which is acceptable since it acts as our core RBAC engine.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Centralizing the study scope checking ensures that endpoints can enforce per-study access in a consistent, fail-safe manner, while keeping the repository DRY. Adding `"alert"` to `"lab_range"` additively preserves legacy permissions while cleanly enabling real-time alerting functionalities.

## 5. Consequences & Trade-offs
* **Positive Impact:** Standardized API security gating across all current and future endpoints.
* **Negative Impact / Technical Debt:** Requires downstream API routers to import the shared dependency rather than implementing localized checks.
* **Mitigation Strategy:** Assert coverage of the centralized check inside regression testing suites (`tests/test_rbac.py`).

## 6. Implementation & Verification
* **Affected Repositories / Services:** `packages/security/`, `apps/execution/`
* **Verification Plan:** Verified using `pytest tests/test_rbac.py tests/test_sdv.py` which passes all rbac and sdv validation checks.
