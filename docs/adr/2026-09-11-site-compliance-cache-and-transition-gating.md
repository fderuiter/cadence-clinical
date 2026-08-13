# ADR-2162: Site Compliance Cache and Transition Gating

- **Status:** Accepted
- **Date:** 2026-09-11
- **Authors:** @jules
- **Deciders:** @jules

---

## 1. Context & Problem Statement

To coordinate and track site operational milestones safely, the CTMS and execution services require a mechanism to determine whether a site is compliant based on the status of required documents approved in the eTMF. Direct dynamic query of the eTMF status can be expensive and prone to transient failures. Therefore, we require a cached, event-driven site compliance cache and transition gating mechanism.

This implements the requirements under PRD-CTMS-001.

## 2. Decision Drivers & Constraints

- **Driver 1:** Need to ensure site milestone transitions are gated on eTMF compliance.
- **Driver 2:** Performance efficiency by caching compliance status instead of querying the eTMF dynamically on every check.
- **Driver 3:** Compliance with 21 CFR Part 11 auditing on status changes.

## 3. Options Considered

### Option 1: Dynamic Querying of eTMF

- **Overview:** Query the eTMF database directly on every transition check.
- **Pros:**
  - ✅ Real-time data consistency.
- **Cons:**
  - ❌ Heavy database load and increased latency during milestone transitions.

### Option 2: Event-Driven Site Compliance Cache (Selected)

- **Overview:** Cache the compliance status of sites and update the cache based on eTMF document approval events.
- **Pros:**
  - ✅ Fast retrieval times.
  - ✅ Reduced system coupling and direct isolation of execution microservice database operations.
- **Cons:**
  - ❌ Requires a mechanism to keep the cache in sync.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Choosing Option 2 balances performance and system isolation while satisfying the operational site tracking and compliance gating constraints outlined in PRD-CTMS-001.

## 5. Consequences & Trade-offs

- **Positive Impact:** Retrieval of compliance status is instant and milestone gating has low latency.
- **Negative Impact / Technical Debt:** Eventual consistency window for the cache sync must be kept small.
- **Mitigation Strategy:** Automated compliance triggers keep the status up to date on any document approval event.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/execution/`, `apps/web/`
- **Verification Plan:** Verified through unit and integration testing inside `apps/execution/tests/test_execution_compliance.py`.
