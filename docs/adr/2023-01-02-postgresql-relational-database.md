# ADR 2023-01-02: PostgreSQL Relational Database for Execution

## Status
Accepted

## Context
For the downstream Electronic Data Capture (EDC) system (`apps/execution`), the platform needs a secure, highly reliable storage mechanism. This database will handle critical clinical records, including eCRF form evaluations, subject state machines, query management, and structured audit logs. Strict adherence to 21 CFR Part 11 mandates complete data immutability and robust transactional consistency.

## Decision
We decided to adopt PostgreSQL as the central relational database engine for the `apps/execution` component. All database operations will be orchestrated via Async SQLAlchemy and SQLModel.

## Alternatives Considered
- **NoSQL Databases (MongoDB/Cassandra):** Lacked the built-in, out-of-the-box rigid transactional guarantees (ACID) needed to safely enforce compliance-level audit trails and relational data constraints without significant application-level orchestration.
- **MySQL/MariaDB:** A viable option, but PostgreSQL offers superior native support for JSONB fields (useful for flexible forms) and advanced concurrency models that better align with our async architecture.

## Trade-offs
- **Positive:** Guarantees absolute data integrity and ACID compliance. Native support for JSONB allows flexible data capture without sacrificing relational safety. Excellent ecosystem for async Python tools (asyncpg).
- **Negative:** Schema migrations require careful planning and coordination to avoid downtime. Relational structures demand rigorous upfront modeling compared to schema-less alternatives.
