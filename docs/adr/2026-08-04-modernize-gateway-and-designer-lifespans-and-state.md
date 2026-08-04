# ADR-256: Modernize Gateway and Designer Lifespans, Isolate State in app.state, and Handle Test Flags

* **Status:** Accepted
* **Date:** 2026-08-04
* **Authors:** @google-labs-jules[bot]
* **Deciders:** @fderuiter
* **Requirement Trace:** PRD-SYS-001, Trace-17

---

## 1. Context & Problem Statement
The API Gateway and Designer services previously relied on deprecated `@app.on_event("startup")` and `@app.on_event("shutdown")` event hooks in FastAPI. These legacy event handlers do not guarantee correct cleanup order, which can cause unclosed connection warnings or resource leaks during service termination. Furthermore, managing critical resources (like HTTP clients and JWKS caches) within global namespaces made test isolation difficult, leading to state bleeding between test runs and potential outbound network initializations during automated tests.

## 2. Decision Drivers & Constraints
* **Driver 1:** Reliability & Stability - Avoid resource leaks and guarantee clean teardown order for external database/network clients.
* **Driver 2:** Test Isolation - Eliminate mutable global state and ensure test cases do not impact each other.
* **Driver 3:** Security & Compliance - Strictly block all outbound network traffic during test execution unless explicitly mocked.

## 3. Options Considered
### Option 1: Retain `@app.on_event` and use global references
* **Overview:** Keep legacy event handlers and manage connections globally.
* **Pros:**
  * ✅ Requires no architectural modification to existing test suites or import statements.
* **Cons:**
  * ❌ Susceptible to connection leaks on shutdown.
  * ❌ Bleeds state across test executions.

### Option 2: Modern async `lifespan` context manager and local `app.state` [Selected]
* **Overview:** Migrate both API Gateway and Designer to FastAPI's modern lifespan pattern, storing connections in local application state (`app.state`) and using a backward-compatible descriptor proxy for module level lookups.
* **Pros:**
  * ✅ Guaranteed cleanup of HTTP clients and Neo4j drivers on shutdown.
  * ✅ Excellent test isolation by storing state on the FastAPI `app` instance.
  * ✅ Backwards compatibility with existing tests and modules via the `GatewayModule` proxy descriptor.
* **Cons:**
  * ❌ Requires slightly more complex initialization and proxy setups.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 solves the connection leak issues and enforces robust test isolation without breaking existing third-party or legacy tests that import global attributes directly.

## 5. Consequences & Trade-offs
* **Positive Impact:** Safer deployment rollouts, fully isolated automated tests, and strict fail-closed network safety checks during tests.
* **Negative Impact / Technical Debt:** Added complexity in main.py via proxy descriptors (`GatewayModule`).
* **Mitigation Strategy:** High test coverage validating both initialization paths, cleanup execution, and correct descriptor resolution.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/gateway/`, `apps/designer/`, `tests/`
* **Verification Plan:** Verified via `pytest tests/test_gateway.py` validating correct startup, shutdown, and mock assertions.
