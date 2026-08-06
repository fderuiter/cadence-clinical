# ADR-2159: PostgreSQL Migration for Regulatory Services

- **Status:** Accepted
- **Date:** 2026-08-05
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Regulatory services store clinical trial audit logs, investigator PII, and protocol deviations in unencrypted SQLite database files inside the running containers, exposing sensitive GxP data to unauthorized host access. SQLite also fails to support native database session variables for reliable, transaction-linked identity auditing. To secure clinical data and keep legally binding audit logs, we must deploy dedicated PostgreSQL container instances for the eTMF, CTMS, and Quality & CAPA services, bound to encrypted host folders, and propagate user context inside SQL write transactions.

This decision implements requirements under PRD-SYS-001.

## 2. Decision Drivers & Constraints

- Secure sensitive clinical, compliance, and PII data at rest using operating system-level encrypted disks/volumes.
- Prevent unauthorized host access to relational data inside running containers.
- Enforce native, transaction-linked identity auditing at the database level.
- Standardize on production-grade PostgreSQL with active connection pooling.

## 3. Options Considered

1. **Option A (Selected):** Provision three dedicated PostgreSQL container instances in Docker Compose for eTMF, CTMS, and Quality services, using asyncpg drivers, active connection pools, host-level encrypted directory bindings, and propagation of active user identity contexts into Postgres session variables.
2. **Option B:** Maintain local SQLite database files and use application-level encryption layers. (Rejected due to high application schema maintenance overhead, lack of native session variables, and vulnerability to key leakage).

## 4. Decision Outcome

Chosen option: Option A because it fully satisfies GxP and regulatory audit requirements (PRD-SYS-001) while leveraging standard system architecture and native PostgreSQL transaction logs.

## 5. Consequences & Trade-offs

- **Positive:**
  - 100% data isolation for eTMF, CTMS, and Quality databases.
  - Native, untamperable audit logs with database-session user contexts.
  - Active connection pooling (size 10, overflow 20) prevents concurrent transaction blockages.
- **Negative:**
  - Slightly higher container/infra overhead compared to SQLite files.

## 6. Implementation & Verification

- Docker Compose updated with dedicated PostgreSQL container definitions.
- `packages/database/__init__.py` updated to propagate user credentials directly to database session context on every transactional write route.
- `scripts/reset_db.py` updated to handle PostgreSQL resets and migrations concurrently.
- Verified using existing and updated test suites.
