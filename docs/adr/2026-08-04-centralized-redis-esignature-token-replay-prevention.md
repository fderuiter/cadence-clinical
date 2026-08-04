# ADR-257: Centralized Redis eSignature Token Replay Prevention

* **Status:** Accepted
* **Date:** 2026-08-04
* **Authors:** @google-labs-jules[bot]
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

In distributed multi-node production environments, verifying electronic signatures solely within local process memory introduced a potential security vulnerability. Signature tokens could be replayed across different server instances within their 60-second validity window. This violated strict 21 CFR Part 11 electronic signature compliance and GxP data integrity standards (specifically **Trace-17**).

We need to guarantee that once an electronic signature token (`X-Sig-Token`) is executed or verified, it is treated as spent and cannot be replayed on any server node within the distributed cluster. At the same time, we must maintain zero friction for local development and test suite runs by not forcing a mandatory local Redis dependency.

## 2. Decision Drivers & Constraints

* **GxP Compliance & 21 CFR Part 11 Traceability (Trace-17):** Absolute single-use enforcement of cryptographic signature tokens across multi-node deployments.
* **Minimal Latency Overhead:** Centralized check must execute extremely fast (sub-15ms) to avoid slowing down critical clinical workflows.
* **Developer Experience & Ease of Setup:** Safe fallback to a local in-memory store in development environments when Redis is not configured, requiring no extra setup.
* **Fault Tolerance & Graceful Degradation:** If Redis experiences temporary connection dropouts, the system should degrade gracefully without halting clinical operations.

## 3. Options Considered

### Option 1: Centralized Redis Caching with Lazy Initialization and Fail-Safe Fallbacks (Selected)
* **Overview:** Use Redis to store spent token states with a strict 60-second TTL. Connection is established lazily upon first use. Uses atomic `set(..., nx=True)` operations to prevent concurrent replays. Falls back gracefully to thread-safe in-memory cache if `REDIS_URL` is not configured or in case of transient Redis errors.
* **Pros:**
  * ✅ High-performance distributed single-use verification under 15ms.
  * ✅ Automatic self-cleanup of spent tokens using Redis TTL.
  * ✅ Zero developer friction: gracefully falls back to thread-safe in-memory dictionary.
  * ✅ Fault-tolerant: degrades gracefully during outages instead of blocking investigator actions.
* **Cons:**
  * ❌ Requires managing a Redis instance/cluster in production.

### Option 2: Database-Backed State Storage
* **Overview:** Store the token usage state in the relational PostgreSQL database table.
* **Pros:**
  * ✅ Relational database is already part of the deployment.
* **Cons:**
  * ❌ Substantially higher latency and database write amplification for short-lived, transient token states.
  * ❌ Requires manual cron job cleanup to prevent infinite table growth, introducing operational complexity.

## 4. Decision Outcome

* **Chosen Option:** Option 1
* **Justification:** Redis is the industry standard for fast, atomic caching and lock state management. Lazy initialization and in-memory failover provide the perfect compromise between strict production security compliance (Trace-17) and local development simplicity.

## 5. Consequences & Trade-offs

* **Positive Impact:** Secure, multi-node electronic signature verification that prevents replay attacks within the 60-second validity window.
* **Negative Impact / Technical Debt:** Adds Redis as a runtime requirement for production environments.
* **Mitigation Strategy:** Provide clear environment variable documentation and ensure the application starts up and runs correctly even if Redis is completely unavailable.

## 6. Implementation & Verification

* **Affected Repositories / Services:** `packages/security/`
* **Verification Plan:** Verified via `tests/test_sig_token_verifier.py`. Tests cover Redis-based token storage patterns, atomic lock-acquisition failures, and graceful fallback to the thread-safe local dictionary. All tests pass with 100% success.
