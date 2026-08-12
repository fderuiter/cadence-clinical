# ADR-[NUMBER]: NestJS API Gateway Promise Coalescing and Eager Key Prefetching

- **Status:** Accepted
- **Date:** 2026-08-12
- **Authors:** @jules
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The upcoming NestJS API Gateway rewrite requires high-throughput token verification. Under heavy concurrent authentication loads (e.g., system-wide restarts, login spikes, or identity provider key rotations), the gateway faces a high risk of "cache stampede" (also known as dog-piling). Multiple concurrent requests for uncached Key IDs could flood the Identity Provider (IDP) with redundant HTTP fetches, leading to rate limiting, increased latency, or downtime.

We need a resilient, low-latency, and robust mechanism that merges overlapping remote JWKS fetches, prefetches keys eagerly on startup without blocking gateway boot if the IDP is offline, and handles dynamic fetch errors gracefully without breaking active user sessions.

## 2. Decision Drivers & Constraints

- **Driver 1 (Performance & Latency):** Instantly retrieve cached keys (under 1ms overhead) on the hot-path.
- **Driver 2 (Resilience):** Do not block gateway startup if the IDP is offline or unreachable during eager prefetching.
- **Driver 3 (Cache Stampede Prevention):** Merge multiple concurrent, identical remote JWKS HTTP fetches into exactly one network call.
- **Driver 4 (Session Continuity):** Maintain previous cached keys on dynamic fetch failures so existing sessions are not terminated.

## 3. Options Considered

### Option 1: Native Node.js Promise Coalescing & In-Memory Cache

Implement a custom `JwksCoalescerService` using native `Map` collections:

- `keyCache` (or `publicKeyCache`) to hold parsed public keys.
- `inFlightFetches` to track active HTTP promises for each Key ID.
  Use native `Promise` chaining to multiplex in-flight fetches, and `AbortController` to enforce timeouts.

- **Pros:**
  - ✅ High performance: zero external cache/network overhead for local hits.
  - ✅ Complete multiplexing: identical Key ID requests resolve with the same network request promise.
  - ✅ Full control over timeouts, startup resilience, and dynamic error handling.
- **Cons:**
  - ❌ Requires custom in-memory implementation in TypeScript/Node.js.

### Option 2: Relying on Standard/Third-party JWKS Client Libraries

Use standard libraries such as `jwks-rsa`.

- **Pros:**
  - ✅ Quick integration.
- **Cons:**
  - ❌ Limited control over startup non-blocking behaviors (startup resilience).
  - ❌ Harder to tune timeout settings dynamically or handle specific cache retention policies.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Implementing a native, tailored promise coalescing module gives us absolute control over the synchronization primitives (merging promises using in-flight map), timeout thresholds, startup prefetch error swallowing, and dynamic key preservation during failure.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Fast-path verification time under 0.2ms.
  - Exactly one HTTP request is made for concurrent uncached keys.
  - Gateway boots successfully even if the IDP is unreachable.
- **Negative Impact / Technical Debt:**
  - Custom cache and promise-coalescing management code to maintain.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `packages/gateway-rewrite`
- **Verification Plan:**
  - Comprehensive unit testing suite (`jwks-coalescer.test.ts`) covering all requirements: eager prefetching, startup resilience, fast-path local bypass, cache stampede prevention, dynamic error key retention, and timeouts.
  - This architecture aligns with and supports Trace-17 (Gateway Step-Up Re-Authentication and Signature Token Issuance).
