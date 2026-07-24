# ADR-047: Quality & CAPA Foundation and Domain Models

* **Status:** Accepted
* **Date:** 2026-07-27
* **Authors:** @jules
* **Deciders:** @fderuiter, @jules

---

## 1. Context & Problem Statement
The Cadence Clinical platform requires a robust, compliant, and isolated Quality & CAPA subsystem to handle protocol deviation management, Root Cause Analysis (RCA), and Corrective and Preventive Actions (CAPA) tracking. To support 21 CFR Part 11 auditing regulations and avoid database schema leakage across bounds, we need to create a dedicated FastAPI microservice package (`apps/quality/`) with its own isolated database persistence managers and domain schema definitions.

## 2. Decision Drivers & Constraints
* **Driver 1 (Compliance):** Strict FDA 21 CFR Part 11 compliance requires mandatory change justification reasons, explicit versioning, creation metadata, and an append-only audit trail logging mechanism.
* **Driver 2 (Database Isolation):** Bounded contexts must be strictly decoupled; hence, Quality schemas and operational connections must be separate from CTMS and trial execution databases.
* **Driver 3 (Traceability):** Relational integrity must connect Deviation, Root Cause Analysis, and CAPA records with explicit SQLite/PostgreSQL foreign key constraints.

## 3. Options Considered
### Option 1: Shared Database Schema with CTMS
Incorporate deviation and CAPA management directly into the CTMS database and microservice.
- Pros:
  * ✅ Less microservice configuration.
- Cons:
  * ❌ Increases coupling between operations (CTMS) and quality assurance workflows.
  * ❌ Harder to scale or deploy the Quality system independently.

### Option 2: Standalone Quality Service with Isolated Persistence (Selected)
Establish a separate FastAPI service under `apps/quality/` with its own `QualityDatabaseManager` and separate SQLAlchemy 2.0 tables for Deviation, RCA, CAPARecord, and QualityAuditLog.
- Pros:
  * ✅ Clean microservice boundaries.
  * ✅ Isolated database connections using environment variables (`QUALITY_DATABASE_URL`).
  * ✅ Enforced database-level relationships and foreign key referential integrity.
- Cons:
  * ❌ Minimal operational overhead of an additional microservice setup.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 delivers perfect boundary isolation, making it straightforward to audit the Deviation → RCA → CAPA lifecycle without side effects on CTMS or trial execution models. It is fully aligned with our architectural principles.

## 5. Consequences & Trade-offs
* **Positive Impact:** Decoupled, highly cohesive, easily maintainable code. Perfect compliance with regulatory needs for mutable records using traceability fields and a separate append-only QualityAuditLog ledger.
* **Negative Impact / Technical Debt:** Requires separate deployment configuration.
* **Mitigation Strategy:** Decouple standard API authentication and gateways, using consistent GatewayAuthMiddleware setup.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/quality/`
* **Verification Plan:** Unit and integration testing verifying the database initialization, table creation, referential integrity cascading, and `/health` status endpoint access.
