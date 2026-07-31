# ADR-119: Regulatory Inspection Portal & Auditor View Access Control Foundation

* **Status:** Accepted
* **Date:** 2026-08-28
* **Authors:** @jules
* **Deciders:** @lead-architect, @qa-validator

---

## 1. Context & Problem Statement
To satisfy GxP guidelines, FDA 21 CFR Part 11, and regulatory expectations, external auditors and regulatory inspectors must be granted read-only access to specific system records (such as eTMF binders, EDLs, and audit trails) while being strictly prohibited from modifying any system data or state (clinical subjects, visits, observations, redactions, status transitions, etc.).

Previously, services had inline string checks to identify if a user was an auditor or inspector. This created duplication and potential authorization bypass gaps. A unified, framework-consistent approach was needed to enforce read-only boundaries across all services.

This ADR specifically relates to requirements under **PRD-SYS-001**.

## 2. Decision Drivers & Constraints
* **Driver 1:** Enforcing strict read-only access boundaries for external auditors across all microservices (specifically `etmf` and `execution`).
* **Driver 2:** Eradicating duplicate inline role checks by centralizing dependency validation logic.
* **Driver 3:** Enforcing that only authorized auditor/inspector roles can retrieve the eTMF audit logs.
* **Constraint:** Must remain framework-consistent with the existing `GatewayAuthMiddleware` and dependency injection model.

## 3. Options Considered
### Option 1: Inline Role Checks in Route Bodies
* **Overview:** Check for the presence of "auditor", "inspector", or "regulatory_inspector" strings in `request.state.roles` within each route function.
* **Pros:**
  * ✅ Easy to implement ad-hoc.
* **Cons:**
  * ❌ Severe risk of authorization bypass due to missing or inconsistent checks across newly added endpoints.
  * ❌ Complex to maintain or update when new auditor personas/roles are introduced.

### Option 2: Reusable FastAPI Role-Check Dependencies [Selected]
* **Overview:** Define reusable dependency factories (`require_role`, `require_any_role`) and dedicated helpers (`verify_not_auditor`, `verify_is_auditor`, `is_auditor`) in the shared `packages/security` package, and inject them into FastAPI route signatures using `Depends`.
* **Pros:**
  * ✅ Promotes clean, declarative, and framework-consistent route design.
  * ✅ Single, authoritative definition of auditor personas and enforcement rules.
  * ✅ Easy to audit and verify that all mutating endpoints reject auditor personas.
* **Cons:**
  * ❌ Requires refactoring existing route parameters.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 guarantees that auditor identity modeling and read-only guardrails are enforced uniformly across all services. It eradicates inline string duplication and provides declarative validation consistent with the REST gateway.

## 5. Consequences & Trade-offs
* **Positive Impact:**
  * ✅ Absolute assurance that no auditor persona can trigger clinical mutations or unauthorized status updates.
  * ✅ Reliable and easy-to-read route signatures.
  * ✅ Standardized error handling (HTTP 403 Forbidden with descriptive details).
* **Negative Impact / Technical Debt:**
  * ❌ Slight increase in route parameter signatures due to dependency injections.

## 6. Implementation & Verification
* **Affected Repositories / Services:**
  * `packages/security/rbac.py` and `packages/security/__init__.py`
  * `apps/etmf/main.py`
  * `apps/execution/main.py`
  * `tests/test_rbac.py`
* **Verification Plan:**
  * Validate with automated tests inside `tests/test_rbac.py` checking each dependency and route restriction.
  * Ensure 100% test pass rate locally and in GitHub CI.
