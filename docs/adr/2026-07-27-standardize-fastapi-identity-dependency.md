# ADR-[NUMBER]: Standardize FastAPI Identity Dependency and Site Access Migration

* **Status:** Accepted
* **Date:** 2026-07-27
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
Inconsistent user identity extraction and role synonym normalization across the clinical microservices introduced security verification gaps and bypassed auditing compliance (21 CFR Part 11). For site-scoped clinical users, there was also a risk of cross-site access attempts violating tenant-isolation policies.

This decision implements requirements under Trace-8.

## 2. Decision Drivers & Constraints
* **Driver 1:** 21 CFR Part 11 auditing compliance requires a traceable audit log for write operations.
* **Driver 2:** Strict site-isolation tenancy model.
* **Driver 3:** Support both synchronous and asynchronous endpoints/test suites.

## 3. Options Considered
### Option 1: Synchronous/Manual identity extraction across services
* **Overview:** Rely on manual headers or state inspection across individual controllers.
* **Pros:**
  * ✅ High isolation per route.
* **Cons:**
  * ❌ Repetitive, error-prone, and misses role normalization.

### Option 2: Centralized FastAPI async dependency cascade with synchronous fallback
* **Overview:** Implement `get_principal` as an asynchronous FastAPI dependency with normalized roles, and check justification across a multi-tiered fallback cascade (Request State -> Query Parameters -> Request Body -> Custom HTTP Headers). Provide a `get_principal_sync` helper for synchronous consumers/test suites.
* **Pros:**
  * ✅ Eliminates header-parsing workarounds.
  * ✅ Strict, automated 21 CFR Part 11 audit compliance and site isolation.
  * ✅ Backwards compatible with legacy/sync code via synchronous fallback.
* **Cons:**
  * ❌ Adds complexity to security middleware and context layers.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 unifies role-normalization, site isolation, and change justifications at the route boundary without requiring breaking changes in legacy synchronous consumers.

## 5. Consequences & Trade-offs
* **Positive Impact:** Standardized, central security and compliance validations.
* **Negative Impact / Technical Debt:** Added async/sync boundary tracking for the principal object.
* **Mitigation Strategy:** Solid coverage of both async and sync test suites.

## 6. Implementation & Verification
* **Affected Repositories / Services:** packages/security/rbac.py, packages/security/signing.py, packages/security/middleware.py
* **Verification Plan:** Verified via backend pytest suite (938 passed cases) and ADR validation script.
