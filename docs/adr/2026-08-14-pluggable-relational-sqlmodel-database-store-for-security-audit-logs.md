# ADR-2173: Pluggable Relational SQLModel Database Store for Security Audit Logs

* **Status:** Accepted
* **Date:** 2026-08-14
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To comply with 21 CFR Part 11 and PRD-SYS-001, the system requires an immutable, tamper-evident central cryptographic audit ledger. Initially, the audit ledger used an ephemeral in-memory storage store which did not persist events across restarts or shutdowns, risking data loss in dynamic multi-service architectures. We need to introduce a persistent relational storage layer utilizing SQLModel that integrates cleanly with local databases, enforces trigger-level immutability rules, and detects active transaction commit boundaries without exposing sibling database packages.

## 2. Decision Drivers & Constraints

* **Compliance (PRD-SYS-001):** The audit ledger must be durable, secure, and permanently immutable.
* **Architecture:** Shared packages like `security` must be completely decoupled from concrete service database imports (e.g., CTMS, eISF, execution) to avoid circular imports and violating package boundary rules.
* **Integrations:** Smooth handling of both synchronous and asynchronous SQLAlchemy/SQLModel engines.
* **Immutability:** Absolute prevention of modification or deletion on the `security_audit_logs` database table.

## 3. Options Considered

* **Option A (SQLModelAuditStore Adapter with Dynamic Session Resolution):** Implement a modular `SQLModelAuditStore` adapter and a dynamic session resolver utilizing string-based dynamic imports to maintain perfect architectural boundaries.
* **Option B (Direct Sibling Imports):** Directly import context sessions from apps into the security package. This option was rejected as it directly violates cross-service and package import boundaries, leading to cyclic imports and build failures.

## 4. Decision Outcome

Chosen option: Option A. We implemented a pluggable `SQLModelAuditStore` adapter subclass of `AuditStoreAdapter` along with dynamic string-based lookup for active thread/async-local context sessions. This enables zero compile-time dependencies from `packages/security` on any individual `apps/*` folders while retaining rich runtime database session reuse.

## 5. Consequences & Trade-offs

* **Positive:** Ensures 100% GxP compliance with durable SQLModel persistence and database-level triggers to enforce absolute immutability.
* **Positive:** Clean architecture without any sibling database import violations.
* **Negative:** Requires slightly more robust session-handling logic to seamlessly adapt to both sync and async contexts.

## 6. Implementation & Verification

* **Target files modified:**
  - `packages/security/audit_logger.py`
  - `packages/security/__init__.py`
  - `apps/execution/database/audit.py`
* **Verification:**
  - Added full test suite verifying triggers and session boundaries under `packages/security/tests/test_sqlmodel_audit_store.py`.
