# ADR-066: Signature Verification Fallback and Rolling Gateway Upgrade Support

* **Status:** Accepted
* **Date:** 2026-08-11
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
During a rolling upgrade of the clinical platform, gateway components might construct signature payloads using newer algorithms or additional metadata fields (such as `site_id` or updated header prefixes), while older running components in the cluster or offline clients are still validating signatures against legacy configurations. If signature validation strictly rejects mismatches without backward compatibility, it will cause transaction failures and service downtime during deployments. We need a robust signature verification scheme in `packages/security/signing.py` that supports rolling gateway upgrades with transparent fallback mechanisms.

## 2. Decision Drivers & Constraints
* **Driver 1:** 100% backward compatibility for existing / legacy signature payloads.
* **Driver 2:** Zero-downtime rolling upgrades of gateways and backend microservices.
* **Driver 3:** Enforce strict validation of newer signatures while gracefully accepting old, verified signatures if valid.
* **Driver 4:** Clear separation of concerns within the security context and middleware.

## 3. Options Considered
### Option 1: Monolithic Signature Version Lock
* **Overview:** Force all nodes and components to upgrade synchronously and reject any legacy payload format instantly.
* **Pros:**
  * ✅ Simplifies validation code.
* **Cons:**
  * ❌ Requires platform downtime.
  * ❌ Breaks existing offline client synchronization.

### Option 2: Backward-Compatible Signature Verification Fallback (Selected)
* **Overview:** Validate signatures using the modern structure, but fallback to verifying against the legacy format if validation fails on the first pass.
* **Pros:**
  * ✅ Supports zero-downtime rolling upgrades perfectly.
  * ✅ Accepts older legacy formats and offline sync payloads transparently.
  * ✅ Highly secure and robust.
* **Cons:**
  * ❌ Slightly increases code complexity in the signature package.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Choosing a backward-compatible verification fallback ensures that newer gateways can start propagating signatures with updated payloads or additional metadata (e.g. including `site_id`) without breaking running backend pods or offline clinical sync logs that are still using legacy signature rules.

## 5. Consequences & Trade-offs
* **Positive Impact:** Allows frictionless zero-downtime rolling upgrades. Ensures eISF/eConsent/CTMS services remain fully operational during standard platform releases.
* **Negative Impact / Technical Debt:** We carry legacy validation paths that must be deprecated and removed in a future major cleanup release.
* **Mitigation Strategy:** Log deprecation warnings when legacy fallback verification is triggered to track active legacy clients.

## 6. Implementation & Verification
* **Affected Repositories / Services:**
  * `packages/security/signing.py`
  * `packages/security/middleware.py`
  * `apps/gateway/main.py`
* **Verification Plan:** Validated via `pytest` run across all `tests/test_eisf_api.py` and security verification tests.
