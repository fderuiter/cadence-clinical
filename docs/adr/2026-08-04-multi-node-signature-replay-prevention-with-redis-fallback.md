# ADR-[NUMBER]: Multi-Node Signature Replay Prevention with Redis Fallback

- **Status:** Accepted
- **Date:** 2026-08-04
- **Authors:** @jules
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

In a distributed, multi-node deployment of Cadence Clinical, 21 CFR Part 11 and EU Annex 11 compliance standards dictate that short-lived electronic signature tokens (`X-Sig-Token`) must be strictly single-use to prevent token replay attacks across distinct physical or container instances.
Using a purely in-memory set to prevent replay of the unique token identifier (`jti`) is susceptible to race conditions and token reuse if different requests are routed to different nodes.

## 2. Decision Drivers & Constraints

- **Driver 1:** Compliance with 21 CFR Part 11 & Trace-17 (Strict single-use constraints on e-signature step-up tokens).
- **Driver 2:** Support for multi-node deployments where API requests might be distributed across any number of app servers.
- **Driver 3:** High availability and fail-safe robustness, allowing the system to gracefully fall back to a safe local in-memory verifier state if Redis is not configured or becomes temporarily unavailable.

## 3. Options Considered

### Option 1: Strictly In-Memory Token Tracking

Keep tracking consumed token `jti` in a thread-safe, local in-memory structure only.

- **Pros:**
  - ✅ Simplest design with no external dependencies.
- **Cons:**
  - ❌ Does not prevent replay attacks across multi-node deployments.

### Option 2: Centralized Redis consumption cache with in-memory fallback (Selected)

Introduce a global cache for token consumption and downstream replay prevention. Utilize Redis with atomic `set(..., nx=True)` locks when a `REDIS_URL` is set, and dynamically fall back to thread-safe local in-memory dictionaries if Redis is not configured or experiences an runtime error.

- **Pros:**
  - ✅ Fully prevents replay attacks across multiple physical nodes.
  - ✅ Highly resilient with fail-safe in-memory fallback keeping single-node systems fully operational during Redis outages.
  - ✅ Standardized verification routines via `TokenConsumptionCache` and `DownstreamReplayCache`.
- **Cons:**
  - ❌ Introduces a runtime operational dependency on Redis.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Fully meets compliance Trace-17 for multi-node deployments while preserving robust reliability on single-node or local environments through automatic, graceful fallback mechanism.

## 5. Consequences & Trade-offs

- **Positive Impact:** Secure, multi-node single-use validation of signature tokens is now active and compliant with regulatory electronic records requirements.
- **Negative Impact / Technical Debt:** Requires monitoring of Redis service connectivity and proper handling of transient network exceptions during lock acquisition.
- **Mitigation Strategy:** Log warning messages when Redis connection fails and immediately switch validation over to the thread-safe in-memory tracking fallback to avoid complete service interruption.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `packages/security/sig_token_verifier.py`
  - `packages/security/middleware.py`
- **Verification Plan:**
  - Validated via integration and unit tests in `tests/test_sig_token_verifier.py` covering token verification, mismatched users, fallback states on Redis connection errors, and single-use constraints.

Traced to **Trace-17** and **Trace-13**.
