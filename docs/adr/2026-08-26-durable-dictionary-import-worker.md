# ADR-115: Durable Dictionary-Import Worker and Recovery Contract

- **Status:** Proposed
- **Date:** 2026-08-26
- **Authors:** @jules
- **Deciders:** @architect, @sponsor_dm
- **Requirement References:** PRD-SYS-001

---

## 1. Context & Problem Statement

The Medical Coding Engine inside the clinical execution service (`apps/execution`) is responsible for importing large dictionary terminologies (e.g., MedDRA, WHODrug) from uploaded ZIP archives, parsing their constituent files, and persisting hundreds of thousands of records into relational database tables.

Currently, the terminology import process is initiated via the `POST /api/v1/dictionaries/import` endpoint. The controller:

1. Validates the archive layout synchronously.
2. Creates a `DictionaryImportJob` row in the database in `PENDING` state.
3. Spawns an asynchronous in-process task via FastAPI's `BackgroundTasks` to invoke `process_dictionary_import`.

While this in-process model is simple, it suffers from a critical robustness and reliability gap. If the application server restarts, crashes, or scales down while a dictionary import is `PENDING` or `PROCESSING`:

- The in-memory `BackgroundTasks` queue is wiped out.
- The database `DictionaryImportJob` records are left permanently orphaned in `PENDING` or `PROCESSING` states with no active execution thread.
- The temporary archive file may be leaked on disk, or deleted, making recovery impossible without re-uploading the entire package.

Citing **ADR-050** (which highlighted technical debt and the eventual need for durable external/background task handling) and **ADR-065** (establishing query-subsystem and transaction idempotency precedents), we require a durable, crash-resilient execution and recovery contract for medical dictionary imports that ensures zero orphaned tasks, automatic retry-on-failure, concurrency safety, and GxP-compliant recovery audit trails.

## 2. Decision Drivers & Constraints

- **Restart-Safety & Crash-Resilience:** Application process restarts or crashes must never orphan accepted import jobs. Accepted jobs must automatically recover, resume, or retry upon system reboot.
- **No New Infrastructure (Constraint):** The system must not introduce external queue brokers, distributed engines, or third-party background task systems (such as Celery, RabbitMQ, or Redis) due to platform deployment simplicity constraints and the rejection of Redis-based caching patterns in previous architectural decisions (e.g., ADR-056, USDM cache ADR).
- **Async SQLAlchemy & Audit-Model Compatibility:** The execution model must be fully compatible with asynchronous SQLAlchemy 2.0, standard connection pooling, and the global shadow trigger-based database audit model (`AuditLog` and `AuditedModel`).
- **GxP 21 CFR Part 11 Auditability:** Every execution step, status transition, worker claim, and retry attempt must be fully audited. The original requesting user's identity and change reason must be propagated into the background context.
- **Preservation of Existing API & Job Contracts:** The existing synchronous pre-flight validations, the immediate `202 Accepted` response mapping, and the `GET /api/v1/dictionaries/jobs/{job_id}` polling contract must be preserved to prevent breaking existing tests and client integration layers.

## 3. Options Considered

### Option 1: External Distributed Broker/Queue (e.g., Celery + RabbitMQ/Postgres)

- **Overview:** Introduce a dedicated task queuing framework like Celery using RabbitMQ or PostgreSQL as the backend broker.
- **Pros:**
  - ✅ Industry-standard approach for heavy asynchronous background workloads.
  - ✅ Out-of-the-box support for heartbeats, retries, and task state tracking.
- **Cons:**
  - ❌ Violates the constraint of avoiding new infrastructure or heavy messaging stacks.
  - ❌ Celery does not integrate cleanly with FastAPI's asynchronous dependency injection and async SQLAlchemy sessions without significant boilerplate.
  - ❌ Difficult to propagate dynamic gateway-signed GxP audit contexts (`user_id`, `change_reason`) natively across distributed workers without custom serialization layers.

### Option 2: Postgres-Native Persisted-Dispatch Polling Worker (Selected)

- **Overview:** Build a database-backed, cooperative polling worker running as an in-process `asyncio.Task` loop within the FastAPI lifespan. The worker coordinates tasks using relational database-native locking mechanics (`SELECT ... FOR UPDATE SKIP LOCKED`).
- **Pros:**
  - ✅ Zero new infrastructure: relies strictly on the existing PostgreSQL/SQLite relational database.
  - ✅ Proven pattern: mirrors the repository's existing Standalone Notifications Service foundation dispatch loop (`poll_and_dispatch` in `apps/notifications/main.py`) and the background sealer.
  - ✅ Full async compatibility: integrates seamlessly with async SQLAlchemy 2.0 and FastAPI lifespan startup/shutdown hooks.
  - ✅ Perfect GxP audit propagation: because jobs are stored in relational tables, the requesting user's ID, change reason, and step-up signature metadata are preserved in the job payload and can be re-bound dynamically using `audit_context` inside the background task thread.
- **Cons:**
  - ❌ Relies on polling, which introduces a minor database read query overhead (mitigated by bounded exponential polling sleep intervals).

### Option 3: BackgroundTasks + Database Recovery Sweep

- **Overview:** Continue relying on FastAPI's in-process `BackgroundTasks` for standard execution, but add a database-driven startup recovery script that sweeps the `DictionaryImportJob` table upon server boot, identifies stuck `PENDING` or `PROCESSING` jobs, and re-enqueues them into `BackgroundTasks`.
- **Pros:**
  - ✅ Minimal changes to the existing controller path.
- **Cons:**
  - ❌ Lacks concurrency safety: if multiple application instances scale up concurrently, they may run concurrent recovery sweeps and duplicate the in-memory execution of the same job.
  - ❌ No runtime heartbeat or lease management: if a worker thread dies silently at runtime without a server crash (e.g., OOM on a single thread), the job remains stuck in `PROCESSING` forever until the next global boot.
  - ❌ No built-in retry/backoff or worker-level claiming.

## 4. Decision Outcome

- **Chosen Option:** Option 2: Postgres-Native Persisted-Dispatch Polling Worker
- **Justification:** Reusing the proven, database-backed polling worker pattern guarantees complete crash-resilience and GxP compliance with zero external dependencies. By leveraging `SELECT ... FOR UPDATE SKIP LOCKED`, we ensure high-concurrency safety across horizontally scaled container instances without any risk of duplicate processing.

---

### Core Execution & Recovery Contract

#### 1. Atomic Job Claiming & Cooperative Leases

To prevent multi-worker race conditions, the background worker will query the `dictionary_import_jobs` table using a database-native pessimistic locking mechanism. A job is defined as "claimable" if its status is `PENDING`, or if it is `PROCESSING` but its cooperative lease has expired.

```sql
SELECT * FROM dictionary_import_jobs
WHERE status = 'PENDING'
   OR (status = 'PROCESSING' AND lease_expires_at < :now)
ORDER BY started_at ASC
FOR UPDATE SKIP LOCKED
LIMIT 1;
```

When a worker claims a job:

- It sets the status to `PROCESSING`.
- It assigns its worker/node identifier to `locked_by` (e.g., a host name or UUID).
- It sets `lease_expires_at` to `:now + lease_duration` (e.g., 5 minutes in the future).
- It commits this atomic state change, releasing the row-level lock.
- At runtime, the active worker thread runs a periodic background heartbeat task that extends `lease_expires_at` periodically while the parser is still successfully processing ZIP members.
- If the worker crashes or the node is terminated, the heartbeat stops, the lease expires, and another healthy worker instance safely reclaims the job during the next poll.

#### 2. Automatic Retry & Bounded Exponential Backoff

If a dictionary import job fails due to database locks, transient disk I/O, or connection drops, the worker will automatically schedule a retry:

- It increments the `attempts` counter.
- If `attempts < max_attempts` (defaulting to 3), the status is set back to `PENDING`, and `next_retry_at` is set to `:now + backoff_delay`.
- The backoff delay is calculated using a bounded exponential backoff formula:
  $$\text{delay} = \min(\text{max\_delay}, \text{base\_delay} \times 2^{\text{attempts} - 1})$$
  _(e.g., base delay = 10s, max delay = 300s, matching the notification dispatcher retry contract)._
- If `attempts >= max_attempts`, the job status is set permanently to `FAILED` with terminal error details.

#### 3. Idempotency & Zero-Partial Commits

To maintain strict GxP data integrity:

- All terminology inserts from a single dictionary import attempt must be executed within a single SQL transaction. Any failure must trigger an atomic rollback, ensuring **zero partial records** or orphaned tables are committed to the clinical database.
- To prevent duplicate processing of identical files, the system will compute and persist a SHA-256 hash of the uploaded ZIP file (`sha256_hash`).
- Before starting a job, the claiming worker checks if a successful job with the same `sha256_hash` already exists in `COMPLETED` state. If so, it instantly marks the new job as `COMPLETED` (idempotent short-circuit), avoiding redundant processing.

#### 4. Artifact Durability & Conditional Cleanup

Under the current in-process model, the uploaded ZIP file is stored in a temporary directory and unconditionally deleted in a `finally` block. Under the durable model:

- Uploaded files must be moved to a durable storage directory (configured via `STORAGE_DIR` or persistent volumes) and registered via `durable_artifact_path` on the job record.
- The physical file is strictly preserved across retry attempts.
- Cleanup is executed **only** when the job transitions to a terminal state (`COMPLETED` or exhausted `FAILED`).

#### 5. `run_impact_analysis` Lifecycle Chaining

Post-import impact analysis (which checks the biostatistical and clinical query impact of up-versioning a dictionary) remains a chained post-COMMIT step:

- Once the dictionary terms are committed successfully, the job status transitions to `COMPLETED`.
- Immediately after, the worker invokes `run_impact_analysis` with `actor="system"`.
- If impact analysis fails, the dictionary itself remains fully committed, but the job status is marked as `FAILED` (or a dedicated status if preferred), ensuring that the terminology is available but administrators are alerted to the analytical failure.

#### 6. GxP Audit Log & Context Re-Binding

Because context variables (like `current_user_id` and `current_change_reason`) do not cross process or network boundaries, the background worker must re-bind these variables before executing the import:

- The initiating user's ID (`user_id`) and change justification (`change_reason`) are captured and persisted as core metadata columns on the `DictionaryImportJob` row during endpoint submission.
- Upon claiming a job, the background thread initializes the `audit_context(job.user_id, job.change_reason)` context manager.
- Within the active database session, the worker sets the database configuration `cadence.app_writing = 'true'` to bypass massive per-row auditing logs, adhering to **ADR-050**'s performance contract.

#### 7. Error Redaction & Security Guidelines

To maintain data integrity and security within the GxP audit trail, any error details captured during failed import attempts must adhere to strict redaction and security guidelines:

- **1,000-Character Constraint:** In accordance with database schema constraints, the `error_details` field must be strictly capped at a maximum of 1,000 characters. Any error message or traceback exceeding this limit must be truncated cleanly (e.g., using `details[:1000]`).
- **Information Leak Prevention:** The background worker must actively redact sensitive data from error tracebacks before writing them to the database. Explicitly, it must prevent leakage of:
  - Local file system paths (e.g., container directory structures, absolute uploaded path references, temporary directory prefixes).
  - Direct database payloads, SQL connection parameters, internal network hostnames, or environmental secrets.
  - Personally Identifiable Information (PII) or Protected Health Information (PHI) accidentally extracted from uploaded dictionary files.

---

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - **Bulletproof Reliability:** Process restarts, application upgrades, or node failures can never orphan dictionary import jobs.
  - **Concurrency Safety:** `SKIP LOCKED` guarantees that scaled application nodes coordinate work safely without double-claiming.
  - **Full GxP Lineage:** Propagates user context into background processes, preserving complete Part 11 auditing metadata.
  - **Operational Integrity:** Rolling back failed database transactions prevents "half-imported" terminology states.
- **Negative Impact / Technical Debt:**
  - **Poller Query Overhead:** Periodic database queries to poll for pending jobs (can be minimized by matching poll intervals to active job states).
  - **Storage Management:** Requires managing a durable disk directory for uploaded ZIPs until completion.
- **Mitigation Strategy:** Set the cooperative polling sleep interval to 5 seconds when idle, and run the polling worker within the same OS process to completely eliminate external networking overhead.

---

## 6. Implementation & Verification

### Persistence Schema Contracts

To support this durable execution model, the `DictionaryImportJob` table in `apps/execution/database/models.py` must be expanded with the following fields:

| Field Name                                                                                                                                                                                                                                                                                                                                                                                                | Type          | Nullable | Description                                                       |
| :-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | :------------ | :------- | :---------------------------------------------------------------- |
| `locked_by`                                                                                                                                                                                                                                                                                                                                                                                               | `String(255)` | Yes      | Identifier of the worker node currently holding the active lease. |
| `lease_expires_at`                                                                                                                                                                                                                                                                                                                                                                                        | `DateTime`    | Yes      | Expiration timestamp for the active cooperative lease.            |
| `attempts`                                                                                                                                                                                                                                                                                                                                                                                                | `Integer`     | No       | Cumulative number of import execution attempts (default: 0).      |
| `max_attempts`                                                                                                                                                                                                                                                                                                                                                                                            | `Integer`     | No       | Maximum permitted retries before terminal failure (default: 3).   |
| `next_retry_at`                                                                                                                                                                                                                                                                                                                                                                                           | `DateTime`    | Yes      | Scheduled timestamp for the next retry attempt.                   |
| `durable_artifact_path`                                                                                                                                                                                                                                                                                                                                                                                   | `String`      | Yes      | Disk path to the persisted ZIP file.                              |
| `sha256_hash`                                                                                                                                                                                                                                                                                                                                                                                             | `String(64)`  | Yes      | SHA-256 cryptographic hash of the uploaded archive.               |
| `original_filename`                                                                                                                                                                                                                                                                                                                                                                                       | `String(255)` | Yes      | Original name of the uploaded dictionary package.                 |
| `file_size`                                                                                                                                                                                                                                                                                                                                                                                               | `Integer`     | Yes      | Size of the uploaded file in bytes.                               |
| `user_id`                                                                                                                                                                                                                                                                                                                                                                                                 | `String(255)` | Yes      | Propagated audit context: original initiating user identifier.    |
| These additions are completely additive and preserve the existing model columns: `dictionary_type`, `dictionary_version`, `status` (reusing `ImportState`), `started_at`, `completed_at`, `progress_percentage`, `records_imported`, `errors_encountered`, and `error_details`. This preserves backward compatibility and ensures standard shadow trigger-based database audits are tracked successfully. |

#### Schema Migration, Backfill, and Rollback Strategy

1. **Migration & Backfill Defaults**:
   - For existing rows, non-nullable columns `attempts` and `max_attempts` backfill with default values `0` and `3` respectively (`DEFAULT 0` and `DEFAULT 3`).
   - All other added columns (`locked_by`, `lease_expires_at`, `next_retry_at`, `durable_artifact_path`, `sha256_hash`, `original_filename`, `file_size`, `user_id`, `change_reason`) are nullable (`NULL`), allowing seamless schema application without downtime or table locks on existing records.
2. **Polling & Lease Performance Indexes**:
   - `idx_dict_import_poll`: Composite index on `(status, next_retry_at)` to optimize `SKIP LOCKED` queries scanning for claimable (`PENDING` or `FAILED` retryable) jobs.
   - `idx_dict_import_lease`: Composite index on `(locked_by, lease_expires_at)` to optimize background heartbeat renewals and dead-node orphan recovery sweeps.
3. **Rollback Plan**:
   - Downward migrations safely drop indexes `idx_dict_import_poll` and `idx_dict_import_lease` before executing `ALTER TABLE dictionary_import_jobs DROP COLUMN ...` for the 11 added fields, restoring the schema to its pre-ADR-115 state without affecting existing core import metadata.

### API & Validation Contracts

- **Polling GET Contract Compatibility:** The existing polling route `GET /api/v1/dictionaries/jobs/{job_id}` must return the exact Pydantic schema `JobStatusResponse` without modifying any existing field names or types, ensuring that existing black-box tests continue passing.
- **ProblemDetails Shape Compliance:** Any new validation or runtime errors on job routes must return the RFC 7807 compliant `ProblemDetails` schema with a `400` status code for validation failures as specified in **ADR-086**.
- **Controller Responsiveness Contract:** The controller `POST /api/v1/dictionaries/import` must immediately return a `202 Accepted` status along with the generated `job_id` and a status of `PENDING`. Synchronous pre-flight validations (such as `validate_archive_layout`, unsupported dictionary types, and RBAC terminal role validations) must execute on the controller thread _before_ persisting the initial job record to guarantee instant feedback for invalid uploads.

---

### Reviewable File-Level Change Map

The downstream implementation must apply changes to the following modules:

#### 1. `apps/execution/main.py`

- **Upload Controller:**
  - Read the uploaded archive on-stream, compute its `sha256_hash`, and write it to the configured durable storage path (`STORAGE_DIR`).
  - Capture `user_id` from `get_principal` and `change_reason` from headers.
  - Create the initial `DictionaryImportJob` row, populating `durable_artifact_path`, `sha256_hash`, `user_id`, and `change_reason`.
  - Replace the existing `background_tasks.add_task(...)` invocation with a simple database commit. The background polling worker will automatically detect and claim the pending job.
- **FastAPI Lifespan Hooks:**
  - Update the lifespan context manager to initialize the background polling worker.
  - Spawn the worker task on startup using `asyncio.create_task(run_dictionary_import_worker(...))`.
  - Cancel and gracefully await the task completion on shutdown to prevent aborted transactions.

#### 2. `apps/execution/coding/importer.py`

- Adapt `process_dictionary_import` to accept the database job record, execute under the established lease, and perform automatic heartbeats.
- Wrap the entire extraction loop in a transaction block. If an exception occurs, trigger `session.rollback()` to guarantee zero partial records.
- Maintain progress reporting, capped at 90% during processing and 100% upon successful `COMPLETED` transition.
- Delete the physical archive file in the durable storage directory _only_ on successful terminal completion or when maximum retries are exhausted.

#### 3. `apps/execution/coding/{worker}.py` (New Module)

- Implement the background loop:
  - Periodic loop running every 5 seconds (or using chunked sleeps for responsive shutdowns).
  - Executes the pessimistic lock query `SELECT ... FOR UPDATE SKIP LOCKED` inside a separate session.
  - Spawns `process_dictionary_import` in a managed worker task.
  - Binds `audit_context(job.user_id, job.change_reason)` and sets the session configuration `cadence.app_writing = 'true'` inside the worker's transaction thread.

---

### Downstream Qualification Plan

To guarantee system stability, the downstream implementation must satisfy the following qualification criteria:

#### Existing Tests (Must Remain Operational & Green)

The following test suites must pass without modifications to their functional checks:

- `tests/test_medical_coding.py` (verifying happy-path MedDRA/WHODrug imports, parser behavior, and record insertions).
- `tests/test_medical_coding_lifecycle.py` (verifying auto-coding during observations, suggestion generation, and manual coder overrides).
- Note: Since existing tests rely on FastAPI's `BackgroundTasks` executing within the endpoint lifecycle, the test suite harness must be updated to either:
  1. Start the lifespan polling worker during test environment setup.
  2. Or, trigger a manual polling sweep synchronously within the test execution thread.

#### New Validation Tests (To Be Added)

The implementation must introduce dedicated unit and integration tests covering:

1. **Pessimistic Concurrency (`SKIP LOCKED`):** Mock multiple concurrent worker instances and verify that they never double-claim or execute the same pending job simultaneously.
2. **Crash & Recovery Reclaim:** Insert a job in `PROCESSING` state with an expired lease. Verify that a healthy worker automatically reclaims the job and schedules a retry.
3. **Exponential Backoff Exhaustion:** Simulate transient processing failures and assert that the job increments its `attempts` counter, schedules the next retry with correct exponential delays, and ultimately transitions to `FAILED` upon exhaustion.
4. **Artifact Durability & Rollback:** Simulate a parsing crash midway through a large MedDRA file. Verify that:
   - The database transaction is fully rolled back with **zero** partial terminology records committed.
   - The uploaded ZIP file is **not** deleted from disk, remaining fully available for the next scheduled retry.
