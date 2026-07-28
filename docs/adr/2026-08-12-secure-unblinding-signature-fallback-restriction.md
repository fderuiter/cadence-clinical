# ADR-86: Secure Unblinding Signature Fallback Restriction

* **Status:** Accepted
* **Date:** 2026-08-12
* **Authors:** @jules
* **Deciders:** @engineering-lead, @security-architect

---

## 1. Context & Problem Statement
With the implementation of the 21 CFR Part 11 compliant emergency clinical unblinding API and the introduction of signature fallback logic for legacy tests, a vulnerability was identified. Specifically, if legacy 4-field signature fallback checks are executed unconditionally, a request containing active scope claims (such as `site_id`, `sponsor_id`, or `unblinded_access`) could bypass scope-aware cryptographic validation. We need to enforce strict validation rules such that signature verification fallbacks are strictly rejected if any scope parameter is active in the request.

This decision implements requirements under Trace-2.

## 2. Decision Drivers & Constraints
* **Security & Compliance:** Ensure active scope boundaries cannot be bypassed using legacy signatures.
* **FDA 21 CFR Part 11 Compliance:** Complete audit trail and cryptographic signature verification for state-changing emergency unblinding.
* **Reliability:** Keep all legacy mock configurations working without breaking security controls.

## 3. Options Considered
### Option 1: Unconditional Legacy Fallback Verification
Allow fallback to legacy 4-field signatures regardless of whether scope parameters are present in the request headers.
* **Pros:** Simplest implementation.
* **Cons:** High security risk, allows clients to claim scopes without cryptographically signing them.

### Option 2: Strictly Scoped Fallback Restriction (Selected)
Restrict the legacy fallback logic to execute only when no active scope parameters are present. If scope parameters are active, the signature MUST be verified against the modern 7-field scope-aware payload.
* **Pros:** Secure, prevents scope-spoofing, satisfies security middleware test constraints.
* **Cons:** None.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Restricting fallback logic ensures legacy requests without scopes continue to verify correctly while guaranteeing that any scope-bearing request is cryptographically bound to its claims.

## 5. Consequences & Trade-offs
* **Positive Impact:** Completely secure unblinding endpoint and scope verification logic, passing all quality gates.
* **Negative Impact / Technical Debt:** None.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `packages/security/signing.py`, `packages/ui/tests/signing.test.js`
* **Verification Plan:** Verify that `test_verify_gateway_signature_scope_fallback_restrictions` and all 945 pytest suites pass with 100% compliance.
