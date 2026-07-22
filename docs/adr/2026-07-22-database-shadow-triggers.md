# ADR 2026-07-22: Database Shadow Triggers

## Status
Accepted

## Context
Standard application-level logging is insufficient to meet strict regulatory non-repudiation and data integrity mandates. If an administrator modifies data directly in the database, application-layer audit trails are bypassed, violating Phase I-IV clinical trial requirements.

## Decision
We will utilize database-level shadow triggers (e.g., PL/pgSQL triggers) on all transactional tables to capture row-level `INSERT`, `UPDATE`, and `DELETE` events. These triggers will automatically write immutable records to the `audit_logs` table, ensuring all state mutations are recorded regardless of the client or interface used.

## Alternatives Considered
- Application-layer interceptors (e.g., ORM hooks): Rejected because they fail to capture direct database queries executed outside the application.
- Logical replication slots: More complex to manage and process synchronously without introducing significant architectural overhead.

## Trade-offs
- **Positive:** Guarantees 100% capture of all data changes; satisfies FDA 21 CFR Part 11 requirements for unalterable system logs.
- **Negative:** Minor performance penalty on high-frequency write operations due to trigger execution overhead.

## Traceability
| **Reference** | **Description** |
| :--- | :--- |
| `CAD-SDLC-SEC-005` (05_Security_Compliance_Audit_Spec.md) | Security, Compliance & Audit Trail Spec - Section 7.1 (Shadow Trigger Logic) |
