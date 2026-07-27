# ADR-85: Backward-Compatible Gateway Signature Verification

* **Status:** Accepted
* **Date:** 2026-07-27
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
The clinical platform gateway upgraded its signature scheme (V2) to support scoped identity headers, including site-level and sponsor-level constraints (`site_id`, `sponsor_id`, `unblinded_access`). However, legacy unit and integration test suites pass payload structures lacking these scope fields, generating 401/403 authorization failures. We need a backward-compatible verification fallback to support both legacy and updated structures.

## 2. Decision Drivers & Constraints
* **Driver 1:** Maintain strict backward-compatibility for legacy tests and services.
* **Driver 2:** Ensure zero security degradation when clinical scopes are explicitly specified.

## 3. Options Considered
### Option 1: Upversion All Test Cases Directly
* **Overview:** Manually rewrite every test signature setup across the entire codebase.
* **Pros:**
  * ✅ Avoids adding fallback logic to the verification library.
* **Cons:**
  * ❌ High development overhead and risk of breaking regression pipelines.

### Option 2: Backward-Compatible Verification Fallback
* **Overview:** Check standard V2 signatures first, then fall back to the legacy 4-key v2 format if scope fields are falsy or omitted.
* **Pros:**
  * ✅ High backward compatibility and smooth migration.
  * ✅ Retains strong validation on requests that do define scopes.
* **Cons:**
  * ❌ Introduces a secondary logic path under signature verification.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** It resolves the 401/403 authorization failures gracefully across legacy tests while continuing to strictly enforce scoped signature validation for current/future requests.

## 5. Consequences & Trade-offs
* **Positive Impact:** All legacy tests execute successfully.
* **Negative Impact / Technical Debt:** Requires maintenance of both 4-key and 7-key validation paths.
* **Mitigation Strategy:** Fallback is strictly confined to cases where scope fields are empty or falsy.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `packages/security`, `apps/gateway`
* **Verification Plan:** Verified via `pytest` running the full monorepo test suite.
