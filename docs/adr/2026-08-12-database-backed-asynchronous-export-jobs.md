# ADR-2171: Database-Backed Asynchronous Export Jobs

- **Status:** Accepted
- **Date:** 2026-08-12
- **Authors:** @google-labs-jules[bot]
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Exporting large clinical datasets (SDTM domains, ADaM datasets, and complete study-wide bundles) as CDISC-compliant Dataset-JSON format files is a computationally intensive and IO-heavy task. Running these large exports synchronously within primary FastAPI request-response threads risks blocking the main event loop, causing client request timeouts and system-wide performance degradation. Additionally, regulatory standards (such as 21 CFR Part 11 and Trace-8) require complete audit logs and status tracing of clinical data exports, including precise tracking of job statuses, progress percentages, and validation error details.

---

## 2. Decision Drivers & Constraints

* **Performance & Responsiveness:** Endpoints must return immediately (within 200 milliseconds) with an HTTP 202 status and a unique job tracking ID, delegating processing tasks to the background.
* **Auditability & Traceability (Trace-8):** Every export execution (success, failure, progress) must be transactionally logged with caller context, metadata, and state.
* **Validation Efficiency:** Parent linkage checks for supplemental records must execute efficiently (linear-time complexity $O(N + M)$ rather than quadratic-time $O(N \times M)$) to prevent long execution times.
* **Isolation & Thread Safety:** Background tasks must use isolated database sessions via the `DatabaseSessionManager` to prevent concurrency issues and session collisions.

---

## 3. Options Considered

### Option 1: Synchronous On-Demand Generation

* **Overview:** Build endpoints that transform, validate, and serialize clinical data synchronously on demand.
* **Pros:** Simpler implementation with no database storage or background job overhead.
* **Cons:** Blocks the FastAPI event loop, leading to system-wide latency. Under heavy load or large datasets, this approach inevitably causes gateway/client timeouts and violates GxP operational stability standards.

### Option 2: Database-Backed Asynchronous Background Jobs (Selected)

* **Overview:** Introduce a database-backed state model (`DatasetExportJob`) and delegate serialization and validation work to FastAPI `BackgroundTasks`. The client immediately receives a job ID and polls the state periodically until completion.
* **Pros:**
  - Non-blocking FastAPI request cycle (HTTP 202 Accepted returned in < 200ms).
  - Resilient, persistent tracking of export jobs in PostgreSQL.
  - Linear-time parent record linkage validation.
  - Isolated transactional context ensures background operations do not corrupt primary thread transactions.
* **Cons:**
  - Increases system complexity by requiring polling endpoints and database state management.

---

## 4. Decision Outcome

* **Chosen Option:** Option 2
* **Justification:** Choosing Option 2 guarantees that large clinical exports run asynchronously without freezing the primary API gateways. This choice allows the platform to safely adhere to strict regulatory compliance guidelines under Trace-8 by capturing full transactional trace histories in PostgreSQL, including success, failure, progress, and diagnostic error output.

---

## 5. Consequences & Trade-offs

* **Positive Impact:** Responsive clinical export services, fully traceable GxP compliance logs, robust error recovery, and highly performant $O(N + M)$ record mapping and validation.
* **Negative Impact / Technical Debt:** Requires periodic database maintenance to clean up ancient job rows and demands a state-polling architecture on clients.
* **Mitigation Strategy:** Implement clear database models inheriting from `AuditedModel` to retain UUID primary keys, audit states, and clean schema separations.

---

## 6. Implementation & Verification

* **Affected Repositories / Services:**
  - `apps/execution/database/models.py` - Created `DatasetExportJob` model.
  - `apps/execution/presentation/routers/exports.py` - Asynchronous background router implementation.
  - `apps/execution/biostat/validator.py` - Optimized parent record linkage lookup using pre-indexed dicts.
* **Verification Plan:**
  - Full suite of integration tests implemented in `apps/execution/tests/test_async_exports.py` verifying async trigger, status polling, downloading, authorization gates, and failure gracefully registering error logs.
