# ADR-058: Organization Directory Service and Persistence Models Scaffold

* **Status:** Accepted
* **Date:** 2026-08-07
* **Authors:** @jules
* **Deciders:** @fderuiter, @jules

---

## 1. Context & Problem Statement
To support clinical trial operations, the Cadence Clinical platform requires a centralized, secure, and GxP-compliant Organization Directory service. This service must manage organizations (sponsors, CROs, central laboratories, sites), clinical sites, personnel/staff mappings (linked with Keycloak user IDs), and delegation of authority (DOA) records detailing significant delegated trial duties.
Additionally, to satisfy FDA 21 CFR Part 11 requirements, all mutations must be fully auditable through a secure, append-only audit trail and chronological row-versioning fields.

## 2. Decision Drivers & Constraints
* **Compliance (FDA 21 CFR Part 11 / GxP):** Must implement non-nullable audit fields (`created_at`, `created_by`, `reason_for_change`, `version_index`) and an append-only audit log.
* **Security & Site Isolation:** Sites must be isolated. Site-scoped entities must include `site_id` to integrate seamlessly with existing `TrialLockManager` checks.
* **Modularity:** Establish the service under a dedicated directory structure (`apps/org/`) following established platform conventions (like eTMF/Interop/Quality).
* **Database & Driver Compatibility:** Use asynchronous SQLAlchemy 2.0 with SQLite (for local testing/lifespan initialization) and future-proofing for PostgreSQL.

## 3. Options Considered
### Option 1: Integrate with an Existing Service (e.g., CTMS or Execution)
* **Overview:** Place the organization directory and delegation of authority tables within the CTMS or Execution microservices.
* **Pros:**
  * ✅ Fewer microservices to deploy and maintain.
* **Cons:**
  * ❌ Violates clean service boundary and single-responsibility principles.
  * ❌ Increases coupling, making it harder to scale or adapt organization mappings independently.

### Option 2: Dedicated Organization Directory Microservice (Selected)
* **Overview:** Build a standalone `apps/org` microservice with its own relational database management, schema setup, lifespan, and FastAPI endpoints.
* **Pros:**
  * ✅ High modularity and clear responsibility boundaries.
  * ✅ Clean data models separating core operational directory registries from downstream capture databases.
  * ✅ Easily enforces GatewayAuthMiddleware identity propagation and site-scoping validation rules.
* **Cons:**
  * ❌ Additional microservice component to run in the ecosystem.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 provides a robust, decoupled, and highly modular foundation that scales independently, adheres to standard platform microservice layout, and encapsulates sensitive GxP delegation workflows securely.

## 5. Consequences & Trade-offs
* **Positive Impact:**
  * Well-defined domain models and a dedicated persistence layer in `apps/org/models.py`.
  * Complete separation from execution/clinical capture tables while being fully compatible with system-wide site isolation checks.
  * Robust unit testing covering all schemas, async relationships, and health checks.
* **Negative Impact / Technical Debt:**
  * Need to configure and run the `org` service as a separate service container in the production stack.

## 6. Implementation & Verification
* **Affected Repositories / Services:**
  * `apps/org/models.py` (Persistence models)
  * `apps/org/database.py` (Asynchronous relational DB manager)
  * `apps/org/main.py` (FastAPI app & health check)
  * `apps/execution/database/audit.py` (Execution audit hooks exclusion)
* **Verification Plan:**
  * Run the unit and integration tests using `uv run pytest tests/test_org_service.py` to verify schema generation, relationships, and health check.
