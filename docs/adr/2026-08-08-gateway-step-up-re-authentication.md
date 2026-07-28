# ADR-062: Gateway Step-Up Re-Authentication and Signature Token Issuance

* **Status:** Accepted
* **Date:** 2026-08-08
* **Authors:** @jules
* **Deciders:** @fderuiter, @jules

---

## 1. Context & Problem Statement
Pursuant to FDA 21 CFR Part 11 and EU Annex 11, executing critical clinical mutations requires explicit, double-keying re-authentication immediately before signature application.
Downstream microservices require a high-assurance, short-lived, verifiable signature token instead of direct handling of user credentials/passwords.

This decision implements requirements under Trace-8.

## 2. Decision Drivers & Constraints
* **Part 11 GxP Compliance:** Absolute assurance of active signer re-authentication via credentials.
* **Symmetric Cryptography:** Leverage `GATEWAY_SECRET` with HS256 to sign and verify signature tokens across the gateway and microservices.
* **Single-Use Replay Prevention:** Prevent reuse of signature tokens.
* **Action & Identity Binding:** Strictly bind the token to the current user and the targeted REST mutation path.

## 3. Options Considered
### Option 1: JWT Signature Token with unique `jti` and `batch_id`
* **Overview:** Issue an HS256 JWT `sig_token` containing `sub`, `username`, `action`, `roles`, `iat`, `exp` (60s), a unique UUID `jti`, and an optional `batch_id`. Maintain in-memory or distributed replay caches to verify `jti` single-use tracking at the gateway and downstream levels.
* **Pros:**
  * ✅ Full compliance with 21 CFR Part 11.
  * ✅ Robust single-use replay prevention.
  * ✅ Zero credential leak to downstream services.
* **Cons:**
  * ❌ In-memory caches must be kept clean (automatically pruned on expiry).

## 4. Decision Outcome
* **Chosen Option:** Option 1
* **Justification:** Guarantees GxP-compliant double-keying re-authentication, absolute action/identity binding, single-use security, and zero password propagation downstream.

## 5. Consequences & Trade-offs
* **Positive Impact:**
  * Strict single-use tracking using the unique `jti` claim.
  * Extensible design supporting multi-form approvals using optional `batch_id` mapping.
* **Negative Impact:**
  * None.

## 6. Implementation & Verification
* **Files Modified:**
  * `apps/gateway/main.py`
  * `packages/security/middleware.py`
  * `tests/test_gateway.py`
* **Verification:**
  * Executed `uv run pytest tests/test_gateway.py` and `tests/test_security_middleware.py`.
