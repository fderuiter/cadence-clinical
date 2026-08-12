# ADR-2169: Pool Connection State Eviction and Checkout Resets

- **Status:** Accepted
- **Date:** 2026-08-11
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

In a multi-tenant clinical research platform, ensuring strong isolation of database connection states is critical for GxP-compliant user auditing and compliance traceability (PRD-SYS-001). Under standard connection pooling mechanisms (such as SQLAlchemy's QueuePool), physical database connections are recycled across concurrent user requests. If connection-level session contexts or setting variables (e.g., current user ID or change reason) are not properly cleared, modified, or evicted upon connection checkin or checkout, stale session parameters could carry over across requests, compromising the audit trail's integrity.

## 2. Decision Drivers & Constraints

- **Compliance (GxP / 21 CFR Part 11):** Absolutely reliable user-action tracking and session isolation.
- **Security & Isolation:** Preventing leakage of user IDs or session variables across recycled connections.
- **Performance:** Keeping cleanup operations at $O(1)$ complexity without degrading transaction throughput.
- **Reliability:** Standardizing hooks for both simulated/SQLite and PostgreSQL configurations.

## 3. Options Considered

### Option 1: Manual Context Cleardown in Application Routers

Application routes manually call reset statements before committing or closing a connection.

- **Pros:**
  - Simple to implement in isolated endpoints.
- **Cons:**
  - ❌ Highly error-prone; developers could easily forget to clear the context.
  - ❌ Increases boilerplate code across all microservices.

### Option 2: Connection Pool Event Hooks (Selected)

Leverage SQLAlchemy pool lifecycle events (`"checkout"`, `"checkin"`, `"close"`) to automatically reset connection state settings and evict tracked connection mappings dynamically.

- **Pros:**
  - ✅ Completely automated, centralized, and transparent to the application logic.
  - ✅ Guaranteed state cleanup upon checkout and checkin.
  - ✅ High-performance $O(1)$ dictionary lookup and eviction for tracking connection-level configs.
- **Cons:**
  - ❌ Requires careful handling of connection IDs to prevent weakref errors or memory leaks.

## 4. Decision Outcome

**Chosen Option:** Option 2 (Connection Pool Event Hooks) because it ensures perfect session isolation and GxP audit integrity (PRD-SYS-001) in a transparent, automated manner with minimal performance overhead.

## 5. Consequences & Trade-offs

- **Positive Impact:** Robust multi-tenant session context isolation, 100% GxP audit-trail reliability, and automated cleanups.
- **Negative Impact:** Slightly more complex connection management class in core database wrappers.
- **Mitigation Strategy:** Added comprehensive integration tests to assert proper eviction and isolation behaviors on concurrent threads and recycled connections.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/execution` (specifically `apps/execution/database/core.py` and `apps/execution/database/audit.py`).
- **Verification Plan:** Verified via an integration test suite under `apps/execution/tests/test_pool_state_eviction.py` checking state reset and eviction. Run `uv run pytest apps/execution/tests/test_pool_state_eviction.py` to validate.
