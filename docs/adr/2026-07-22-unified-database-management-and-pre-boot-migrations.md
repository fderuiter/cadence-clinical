# ADR 2026-07-22: Unified Database Management and Pre-Boot Migrations

## Status
Accepted

## Context
Clinical microservices lacked a standardized approach to database initializations, connection pooling, and schema migration routines. This caused schema drift, transient query errors during rolling updates, and potential data leaks due to shared session state across concurrent coroutines. To achieve 100% data reliability, zero-downtime platform upgrades, and compliance-level auditing, we need to standardize database operations.

## Decision
We have decoupled schema migrations into an isolated pre-boot process and consolidated database operations into a unified, shared database helper module (`DatabaseSessionManager`).

1. **Isolated Pre-Boot Schema Updates:** Schema migration and table creation routines have been moved outside the runtime web application process. This isolated execution into a pre-boot phase ensures the web application is only run once the database schema is fully prepared, avoiding table locks and startup latency.
2. **Coroutine-Safe Session Management:** Introduced `ContextResetMiddleware` utilizing Python `contextvars` to isolate session tokens. By explicitly binding sessions to request lifecycles and safely resetting context tokens during teardown, we prevent transaction-level data pollution across concurrent asynchronous tasks.
3. **Atomic Compliance Auditing:** Integrated event listeners to automatically inject audit logs within the same database transaction as the original mutation.

## Alternatives Considered
- **Runtime Migrations on Startup:** Previously we considered running migrations as part of the application startup. This was rejected because it causes startup latency, table locks, and issues during rolling updates where multiple instances might attempt concurrent migrations.
- **Manual Session Passing:** Passing session objects explicitly through every function call instead of using `contextvars`. Rejected because of massive refactoring requirements and the potential to introduce human errors.

## Trade-offs
- **Positive:** Zero-downtime platform upgrades, strict compliance-level auditing without orphaned audit records, and coroutine-safe operations.
- **Negative:** Adds complexity to the deployment process as a pre-boot migration job must run before the application can start.
