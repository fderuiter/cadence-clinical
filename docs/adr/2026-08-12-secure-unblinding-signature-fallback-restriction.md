# ADR-86: Secure Unblinding Signature Fallback Restriction & Scope Normalization

* **Status:** Accepted
* **Date:** 2026-08-12
* **Authors:** @fderuiter
* **Deciders:** @engineering-lead, @security-architect

---

## 1. Context & Problem Statement
With the implementation of the 21 CFR Part 11 compliant emergency clinical unblinding API and the introduction of signature fallback logic for legacy tests, a vulnerability was identified. Specifically, if legacy 4-field or empty-scope 7-field signature fallback checks are executed unconditionally, a request containing active scope claims (such as `site_id`, `sponsor_id`, or `unblinded_access`) could bypass scope-aware cryptographic validation. We need to enforce strict validation rules such that signature verification fallbacks are strictly rejected if any scope parameter is active in the request.

Furthermore, we need to unify the scope-header normalization logic between the API gateway and the security middleware so both derive identical values (using a shared helper) to prevent discrepancies in signing vs verification.

This decision implements requirements under Trace-2.

## 2. Decision Drivers & Constraints
* **Security & Compliance:** Ensure active scope boundaries cannot be bypassed using legacy signatures.
* **Consistency:** Ensure both the API Gateway and the security middleware parse and normalize scope headers using the exact same rules.
* **FDA 21 CFR Part 11 Compliance:** Complete audit trail and cryptographic signature verification for state-changing emergency unblinding.
* **Reliability:** Keep all legacy mock configurations working without breaking security controls.

## 3. Options Considered
### Option 1: Unconditional Legacy Fallback Verification
Allow fallback to legacy 4-field signatures regardless of whether scope parameters are present in the request headers.
* **Pros:** Simplest implementation.
* **Cons:** High security risk, allows clients to claim scopes without cryptographically signing them.

### Option 2: Strictly Scoped Fallback Restriction & Shared Normalization (Selected)
Restrict the legacy fallback logic to execute only when no active scope parameters are present. If scope parameters are active, the signature MUST be verified against the modern scope-aware payload (canonical 8-field with tenant or 7-field fallback preserving scopes).
In addition, introduce a shared `normalize_scope_values` helper co-located in `packages/security/signing.py` to normalize and coerce scope inputs on both sides of the trust boundary.
* **Pros:** Secure, prevents scope-spoofing, ensures gateway/middleware alignment, satisfies security middleware test constraints.
* **Cons:** None.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Restricting fallback logic ensures legacy requests without scopes continue to verify correctly while guaranteeing that any scope-bearing request is cryptographically bound to its claims. Centralizing normalization avoids inconsistencies.

### 4.1 Terminological Reconciliation (7-field vs 8-field)
* **7-field Serialization:** Refers to the scope-aware signature payload prior to the addition of `tenant_id` (comprising `user_id`, `roles`, `timestamp`, `change_reason`, `site_id`, `sponsor_id`, `unblinded_access`). In the current codebase, this is verified as a fallback when `tenant_id` is absent/None.
* **8-field Serialization:** Refers to the canonical signature payload containing all 8 fields (the 7 fields plus `tenant_id`). It is the primary verification path.
* **Legacy 4-field Payload:** Backward compatibility signature containing only the identity fields (`user_id`, `roles`, `timestamp`, `change_reason`), only reachable when no scopes are active in the request.

## 5. Consequences & Trade-offs
* **Positive Impact:** Completely secure unblinding endpoint, unified scope normalization, and secure scope verification logic, passing all quality gates.
* **Negative Impact / Technical Debt:** None.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `packages/security/signing.py`, `packages/security/middleware.py`, `apps/gateway/main.py`
* **Verification Plan:** Verify that `test_verify_gateway_signature_scope_fallback_restrictions`, `test_gateway_scope_extraction_and_verification_integrity`, and all unit & integration test suites pass with 100% compliance.
