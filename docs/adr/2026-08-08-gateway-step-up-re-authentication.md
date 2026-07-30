# ADR-063: Gateway Step-Up Re-Authentication and Signature Token Issuance

* **Status:** Accepted
* **Date:** 2026-08-08 (Updated: 2026-08-25)
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
Pursuant to FDA 21 CFR Part 11 and EU Annex 11, executing critical clinical mutations requires explicit, double-keying re-authentication immediately before signature application.
Downstream microservices require a high-assurance, short-lived, verifiable signature token instead of direct handling of user credentials/passwords.

This decision implements requirements under **Trace-15**, **Trace-13**, and **PRD-SYS-001**. (Note: Trace-8 is strictly reserved for eCOA Subject Identity & Gateway Boundary).

---

## 2. Decision Drivers & Constraints
* **Part 11 GxP Compliance:** Absolute assurance of active signer re-authentication via credentials.
* **Symmetric Cryptography:** Leverage `GATEWAY_SECRET` with HS256 to sign and verify signature tokens across the gateway and microservices.
* **Single-Use Replay Prevention:** Prevent reuse of signature tokens.
* **Action & Identity Binding:** Strictly bind the token to the current user and the targeted REST mutation path.

---

## 3. Options Considered
### Option 1: JWT Signature Token with unique `jti` and `batch_id`
* **Overview:** Issue an HS256 JWT `sig_token` containing `sub`, `username`, `action`, `roles`, `iat`, `exp` (60s), a unique UUID `jti`, and an optional `batch_id`. Maintain in-memory or distributed replay caches to verify `jti` single-use tracking at the gateway and downstream levels.
* **Pros:**
  * ✅ Full compliance with 21 CFR Part 11.
  * ✅ Robust single-use replay prevention.
  * ✅ Zero credential leak to downstream services.
* **Cons:**
  * ❌ In-memory caches must be kept clean (automatically pruned on expiry).

---

## 4. Decision Outcome
* **Chosen Option:** Option 1
* **Justification:** Guarantees GxP-compliant double-keying re-authentication, absolute action/identity binding, single-use security, and zero password propagation downstream.

---

## 5. Consequences & Trade-offs
* **Positive Impact:**
  * Strict single-use tracking using the unique `jti` claim.
  * Extensible design supporting multi-form approvals using optional `batch_id` mapping.
  * Decoupled backend and frontend: Reusable frontend components like `apps/web/src/components/SignatureCaptureModal.vue` consume this contract seamlessly by retrieving the token via the API Gateway and forwarding it to downstream mutation paths (e.g. `/api/v1/etmf/documents/{document_id}/sign-off`).
* **Negative Impact:**
  * Replay tracking requires cache storage (retained strictly until token expiration, ensuring highly efficient lookup and memory safety).

---

## 6. Implementation & Verification
* **Files Modified:**
  * `apps/gateway/main.py`
  * `packages/security/middleware.py`
  * `apps/etmf/main.py`
  * `apps/designer/main.py`
  * `apps/web/src/components/SignatureCaptureModal.vue`
* **Verification & Automated Testing Coverage:**
  * Hardened automated test cases verified under **#319** (Dated: 2026-08-25):
    - `tests/test_gateway.py` (Validates JWT payload claims, HS256 signatures, temporal expiration, and replay protection under `test_signature_verification_success`, `test_signature_gated_mutation_enforcement`, `test_signature_token_altered_signature_rejected`, `test_signature_gated_mutation_expired_token`, `test_signature_gated_mutation_mismatched_action`).
    - `tests/test_security_middleware.py` (Validates `verify_sig_token` logic, downstream signature-gated endpoint verification, expiration checks, and replay blockage under `test_downstream_signature_gated_endpoint_requires_sig_token`, `test_downstream_signature_gated_endpoint_valid_sig_token`, `test_downstream_signature_gated_endpoint_expired_token`, `test_downstream_signature_gated_endpoint_mismatched_action`, `test_downstream_signature_gated_endpoint_replay_blocked`, `test_verify_sig_token_helper_scenarios`).
    - `tests/test_etmf_signing_lifecycle.py` (Integration of `X-Sig-Token` step-up verification, post-signature locking, `IMMUTABILITY_VIOLATION` response, and `TMFAuditLog` ledger integration).
    - `tests/test_signature_manifestation.py` (Validation of cryptographic self-signed X.509 certificate generation, asymmetric private-key signing, and non-repudiation manifestation models).
