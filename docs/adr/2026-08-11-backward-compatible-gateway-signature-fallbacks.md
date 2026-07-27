# ADR-85: Backward-Compatible Gateway Signature Fallbacks

* **Status:** Accepted
* **Date:** 2026-08-11
* **Authors:** @jules
* **Deciders:** @engineering-lead, @security-architect

---

## 1. Context & Problem Statement
The API Gateway platform recently introduced strict V2 gateway signatures (7-key canonical payload signing) to support tracking and verifying more granular scope attributes (e.g., `site_id`, `sponsor_id`, `unblinded_access`). However, legacy integration tests and clients continue to submit authentication headers in the legacy 4-key JSON payload signature format (`change_reason`, `roles`, `timestamp`, `user_id`), leading to `401 Unauthorized` or `403 Forbidden` verification failures. A seamless fallback mechanism is required to maintain backward compatibility with older signature payloads during the platform upgrade transition.

## 2. Decision Drivers & Constraints
* **Driver 1:** Maintain continuous backward compatibility with existing services, pipelines, and test suites.
* **Driver 2:** Preserves robust cryptographic security using HMAC-SHA256 signatures with a shared secret.
* **Driver 3:** Minimize development friction and refactoring overhead for clients still using legacy 4-key signature payload structure.

## 3. Options Considered
### Option 1: Force immediate migration of all legacy test suites and clients to 7-key signatures
* **Overview:** Update every test helper and external client immediately to use the 7-key canonical payload.
* **Pros:**
  * ✅ Avoids maintaining dual signature verification logic.
* **Cons:**
  * ❌ High development overhead and high friction, breaking legacy compatibility during rolling platform upgrades.

### Option 2: Provide an unconditional fallback mechanism to 4-key legacy signatures on validation failure
* **Overview:** The middleware first attempts validation using the 7-key canonical format. If verification fails, it seamlessly falls back to verify the 4-key legacy format, ensuring all clients and older tests succeed.
* **Pros:**
  * ✅ Perfect backward compatibility with zero code changes required on legacy clients/test suites.
  * ✅ Zero security regression since 4-key signatures are also securely signed using HMAC-SHA256 with the shared gateway secret.
* **Cons:**
  * ❌ Small double-signature hashing performance overhead only on validation fallback cases.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 provides a zero-friction, robust, and highly secure compatibility layer. It resolves test and client failures out-of-the-box while allowing gradual client migration to the modern 7-key format over time.

## 5. Consequences & Trade-offs
* **Positive Impact:** All legacy API integration tests and older test suites pass cleanly without modifications.
* **Negative Impact / Technical Debt:** We carry the legacy verification fallback logic in `verify_gateway_signature` which can eventually be deprecated in a future major release.
* **Mitigation Strategy:** Document the fallback mechanism clearly and encourage new service configurations to target the 7-key canonical signature format.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `packages/security/signing.py`
* **Verification Plan:** Validated by executing the entire backend test suite (`uv run pytest`) and verifying zero signature authentication regressions on both old and new format tests.
