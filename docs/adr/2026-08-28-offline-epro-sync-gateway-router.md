# ADR-118: Gateway Explicit Router for eCOA & Offline Sync

* **Status:** Accepted
* **Date:** 2026-08-28
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
With the implementation of offline storage and sync engine for patient ePRO entries under GxP 21 CFR Part 11 requirements, there is a strong need to register explicit REST API routes under the Gateway middleware with granular permission checking and study-scope enforcement, rather than routing them through a generic wildcard proxy.

This decision focuses on:
1. Creating an explicit Gateway router `apps/gateway/routers/ecoa.py` to handle eCOA submit and sync requests.
2. Protecting these endpoints with `form:write` permission checks and robust `can_access_study` study-scope validation checks.
3. Successfully proxying the request to the corresponding microservices downstream (e.g. interop and execution).

This decision implements requirements under PRD-SYS-001.

## 2. Decision Drivers & Constraints
* **Driver 1:** Absolute GxP 21 CFR Part 11 and study scope security.
* **Driver 2:** Traceability and audit trail preservation on identity validation.

## 3. Options Considered
### Option 1: Generic Proxy Routing
* **Overview:** Rely strictly on the catch-all `proxy_requests` wildcard proxy.
* **Pros:**
  * ✅ Codebase simplicity.
* **Cons:**
  * ❌ Fails to enforce explicit gateway-level permissions or study scope constraints.

### Option 2: Explicit Gateway Router with Guards (Selected)
* **Overview:** Build a dedicated `ecoa` router under the gateway, intercepting target paths and executing validation.
* **Pros:**
  * ✅ High security and granular guard enforcement.
* **Cons:**
  * ❌ Slower setup.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Explains why Option 2 was chosen to fulfill absolute GxP compliance.

## 5. Consequences & Trade-offs
* **Positive Impact:** Stronger security bounds.
* **Negative Impact / Technical Debt:** Added module to the gateway.
* **Mitigation Strategy:** Automated test coverage verification.

## 6. Implementation & Verification
* **Affected Repositories / Services:** API Gateway (`apps/gateway/`).
* **Verification Plan:** Verified through pytest `tests/test_gateway.py`.
