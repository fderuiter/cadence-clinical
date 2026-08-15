# ADR-2167: Unified Ports and Adapters Architecture, Transaction Propagation, and Gateway Connection Pooling

- **Status:** Accepted
- **Date:** 2026-08-11
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To maintain strict GxP and microservice isolation boundaries across the Cadence Clinical Research Software Platform, we must prevent cross-contamination and illegal sibling database imports/helpers (e.g., in-memory sibling module injection, `sys.modules` manipulation) between services such as `eTMF` and `apps/org` or `apps/execution`.
Furthermore, relational database transactional operations must support robust, nested context execution without leaking connections or corrupting active sessions.
Finally, high-performance, low-latency inter-service REST operations require standardizing connection pooling across the platform.

This decision directly aligns with standard system requirements under **PRD-SYS-001** and microservice SLA parameters.

## 2. Decision Drivers & Constraints

- **Strict Microservice Boundary Decoupling:** Prohibit sibling database or model imports across microservices.
- **100ms SLA Enforcement:** High-performance REST clients must maintain optimized asynchronous connection pooling to meet our low-latency constraints.
- **Transaction Integrity:** Provide robust propagation mechanisms to reuse active database sessions safely in nested wrappers.
- **Elimination of Hacking Patterns:** Remove in-memory dynamic module injection (`sys.modules` probing) in test suites or production profiles in favor of standard Port-and-Adapter register patterns.

## 3. Options Considered

1. **Option A (Selected) - Ports and Adapters + Connection Pooling + Session Propagation:**
   - Introduce registry/adapter hooks for critical cross-boundary calls (e.g. `verify_trial_lock_status`, `resolve_personnel_assignments`, `is_sponsor_known_to_org_directory`).
   - Share a single `httpx.AsyncClient` instance with explicit limits (100 max connections, 20 keepalives) at the `GatewayBaseClient` class level.
   - Reuse existing context sessions in the transactional decorator by inspecting and yielding the thread/async-local context.
2. **Option B (Alternative) - Sibling Imports with Lax Controls:**
   - Allow lazy imports of sibling modules or rely on runtime system probing hacks. This degrades maintainability, introduces dependency cycles, and risks regression in isolated execution modes.

## 4. Decision Outcome

Chosen option: **Option A** because it enforces architectural cleanliness, guarantees 100% adherence to microservice separation guidelines, boosts REST performance to satisfy the 100ms SLA, and provides flawless transaction propagation in relational engines.

### Implementation Details:

- **Ports and Adapters:** Introduced explicit adapter registration endpoints (`register_trial_lock_status_resolver`, `register_personnel_assignments_resolver`, `register_sponsor_known_resolver`) to hook authoritative handlers cleanly in execution contexts and unit/integration tests (using a specialized autouse pytest fixture).
- **Connection Pooling:** Built a classmethod `get_shared_client()` returning a cached, high-capacity asynchronous client inside `GatewayBaseClient`.
- **Database propagation:** Intercepted active transactional decorators to check if an existing session is bound to the current context var before spawning a new one.

## 5. Consequences & Trade-offs

- **Positive:**
  - No sibling database or model imports across microservices.
  - Fast, pooled, and SLA-compliant HTTP client execution.
  - Modular and clean tests via pytest fixtures standardizing adapter behavior.
- **Negative:**
  - Slight increase in setup code to register adapters/resolvers in tests.

## 6. Implementation & Verification

- **Files Modified:**
  - `apps/etmf/adapters/lock_client.py`
  - `apps/etmf/tests/conftest.py`
  - `packages/database/__init__.py`
  - `packages/security/gateway_client.py`
  - `packages/security/org_client.py`
- **Verification:** Run `uv run pytest` and verify full test coverage of adapter callbacks and transactional decorators.
