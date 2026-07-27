# ADR-067: Standalone In-Application Ticketing Service and Platform Integration

* **Status:** Accepted
* **Date:** 2026-08-13
* **Authors:** Jules
* **Deciders:** Jules

---

## 1. Context & Problem Statement
The Cadence Clinical platform requires a standalone, robust in-application ticketing service responsible for managing issue tracking, helpdesk requests, and user support operations.
To align with FDA 21 CFR Part 11 and full GxP auditing standards, all ticket records and mutations must be version-tracked, and an immutable, append-only audit trail ledger must capture every write operation securely.

## 2. Decision Drivers & Constraints
* **Driver 1:** Decoupled architectural boundaries to isolate the tickets service and storage from core EDC execution systems.
* **Driver 2:** Strict compliance with 21 CFR Part 11 regarding electronic records and signatures, necessitating mutable-record auditing, non-empty change reasons, version tracking, and immutable audit logging.
* **Driver 3:** API gateway routing capability and collision-safe OpenAPI schema documentation aggregation under a dedicated prefix namespace (`Tickets_`).

## 3. Options Considered
### Option 1: Store tickets within the execution core database
* **Overview:** Add ticketing tables directly inside the core clinical database of `apps/execution`.
* **Pros:**
  * ✅ Simplifies setup by avoiding a new service and database.
* **Cons:**
  * ❌ Violates system service-isolation guidelines.
  * ❌ Couples support workflows to transactional EDC clinical trials data models.

### Option 2: Build a standalone Tickets service
* **Overview:** Scaffold an independent FastAPI service utilizing `RelationalDatabaseManager` connected to its own isolated database configuration.
* **Pros:**
  * ✅ High encapsulation, independent scalability, and absolute separation of concerns.
  * ✅ Focused GxP auditing with a dedicated `TicketAuditLog` model and immutable ledger constraints.
* **Cons:**
  * ❌ Additional service configuration in Docker Compose.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Scaffolding a standalone ticketing service conforms to the platform's microservices patterns and safeguards the integrity of GxP EDC data by keeping helpdesk operations physically and logically distinct.

## 5. Consequences & Trade-offs
* **Positive Impact:** Clear service boundary, independent schema migrations, and targeted audit tracing.
* **Negative Impact / Technical Debt:** Managing a new environment configuration key `TICKETS_DATABASE_URL` and service orchestration mapping.
* **Mitigation Strategy:** Configure automatic SQLite fallbacks for test execution and pre-register standard proxy-routing rules via the API Gateway.

## 6. Implementation & Verification
* **Affected Services / Files:** `apps/tickets/`, `apps/gateway/main.py`, `apps/execution/database/audit.py`, `docker/docker-compose.yml`.
* **Verification Plan:** Verified via python backend testing under `tests/test_tickets_service.py` to assert correct health checking, direct access restriction, schema creation, CRUD behaviors, and immutable audit trail protections.
