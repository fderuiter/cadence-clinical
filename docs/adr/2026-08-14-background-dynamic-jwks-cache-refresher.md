# ADR-2174: Background Dynamic JWKS Cache Refresher

- **Status:** Accepted
- **Date:** 2026-08-14
- **Authors:** @fderuiter
- **Deciders:** @engineering-lead, @security-architect
- **Requirement Reference:** PRD-SYS-103

---

## 1. Context & Problem Statement

The Cadence Clinical platform's API Gateway handles cryptographic token verification using JSON Web Key Sets (JWKS) provided by Keycloak. Previously, the gateway relied on dynamic on-demand fetching of public keys. This strategy suffered from several architectural disadvantages:
1. **Cold Start Latency:** The very first request after boot encountered a significant response time penalty while the gateway synchronously fetched keys over the network.
2. **Availability Risks:** If the identity provider (Keycloak) was temporarily unreachable during an incoming token validation event, requests would immediately fail, compromising platform reliability.
3. **Redundant Network Calls:** Without a systematic background refresh cycle, cache expiration or lock contention during dynamic lookup under high concurrent traffic caused potential bottlenecks.

To address these concerns, we require a robust, resilient background process to continuously fetch, cache, and refresh Keycloak JWKS keys, keeping them warm in memory.

## 2. Decision Drivers & Constraints

- **Compliance (GxP / 21 CFR Part 11):** High-availability of security services is essential for system reliability and continuous audit logging.
- **Performance:** Minimizing request latency is paramount. Verification of standard JWTs must use pre-warmed, in-memory caches.
- **Resilience:** Network glitches or temporary Keycloak outages must be handled elegantly via retry mechanisms without dropping existing valid cached public keys.

## 3. Options Considered

### Option 1: On-demand Fetching with Lock Contention Handling

This option continues using lazy initialization, but wraps the network fetching step in an asynchronous double-checked lock.
- **Pros:**
  - ✅ Simple to implement.
  - ✅ Only fetches JWKS when a request actually arrives.
- **Cons:**
  - ❌ First request penalty still exists.
  - ❌ Prone to sudden request failures if the identity provider is offline when a new key ID is presented.

### Option 2: Dedicated Background Refresher Task with Exponential Backoff (Selected)

Deploy a persistent, long-running asyncio task (`_background_jwks_loop`) that initializes at application startup and runs for the lifespan of the gateway process.
- **Pros:**
  - ✅ **Zero Cold-Start Overhead:** Public keys are populated immediately after application bootstrap before handling user requests.
  - ✅ **Hourly Refresh:** Pre-emptively updates keys every 3600 seconds to support rotating signing keys.
  - ✅ **Exponential Backoff:** If a fetch fails (due to a transient network issue or identity provider downtime), the refresher retries with a sleep interval starting at 1.0s, doubling each time up to a maximum cap of 300.0s (5 minutes).
  - ✅ **Graceful Degradation:** During failed refreshes, the previous successfully-cached JWKS keys remain in memory so token validation can proceed unimpeded.
- **Cons:**
  - ❌ Consumes a persistent background execution slot in the event loop, though overhead is negligible.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Implementing a dedicated background asyncio task meets our dual requirements for sub-millisecond token verification latencies and robust system resilience. Keeping the keys warm in memory and wrapping retries in a capped exponential backoff ensures we handle transient cloud infrastructure glitches in an enterprise-grade manner.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Drastically reduced response times on cold starts.
  - Resilience against temporary identity provider unreachability.
- **Negative Impact / Technical Debt:**
  - Background loop introduces a long-running active task that must be gracefully shut down.
- **Mitigation Strategy:**
  - Registered shutdown hooks in FastAPI (`@app.on_event("shutdown")`) to cleanly cancel the background task and await its completion, avoiding unhandled task exceptions or resource leaks.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `apps/gateway/main.py`: Houses the long-running worker loop and startup/shutdown lifecycle hooks.
- **Verification Plan:**
  - Standard unit tests in `apps/gateway/tests/test_gateway.py` covering:
    - **Successful Refresh:** Verifying the initial pre-warming and standard 1-hour sleep cycle.
    - **Failure Retry & Backoff:** Simulating Keycloak failures to assert the doubling retry sequence (`1s`, `2s`, `4s`...).
    - **Backoff Cap:** Asserting that the retry delay never exceeds the maximum threshold of `300s`.
