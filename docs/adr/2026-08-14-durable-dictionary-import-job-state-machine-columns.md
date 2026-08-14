# ADR-2174: Durable Dictionary Import Job State Machine Columns

- **Status:** Accepted
- **Date:** 2026-08-14
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To support a resilient, startup-bootstrapped background loop for importing large medical coding dictionary files (e.g. MedDRA and WHODrug), the platform requires a durable task-tracking mechanism. The existing model utilized ephemeral in-memory queues that could be easily orphaned during server scaling, scaling down, restarts, or unexpected crash events.

To implement a cooperative background polling loop with distributed row locking and exponential backoff, the `DictionaryImportJob` database schema must be extended. This decision addresses the persistence of state-machine columns to track retry counters, file uniqueness, and GxP compliant initiating user contexts (PRD-SYS-001).

## 2. Decision Drivers & Constraints

- **Restart-Safety & Durability:** The system must durably store the location of partial artifacts and progress to resume jobs across server lifecycles.
- **Cryptographic Deduplication:** Preventing redundant uploads of identical dictionary zip files to protect database and disk storage.
- **GxP 21 CFR Part 11 Auditability (PRD-SYS-001):** Background processing steps must fully associate back to the initiating Data Manager's context (ID and reason for change) even when run inside worker threads.

## 3. Options Considered

### Option 1: Direct Table Schema Columns (Selected)

Add dedicated columns to the `DictionaryImportJob` relational database model:

- `file_hash`: Cryptographic SHA-256 hash of the uploaded archive.
- `retry_count`: Track retry attempts for backoff logic.
- `next_attempt_at`: Timestamp indicating when the job is next eligible to run.
- `user_id` and `change_reason`: Initiating user context.
- `temp_zip_path`: The path to the stored ZIP artifact.

- **Pros:**
  - ✅ Clean, strongly-typed relational query capability.
  - ✅ Integrates directly with standard database triggers and indexes.
  - ✅ Allows clean row-level locking via standard SQL features.
- **Cons:**
  - ❌ Requires schema migration for the new attributes.

### Option 2: Generic JSON payload column

Store execution state and retry parameters inside a generic unstructured JSON text column.

- **Pros:**
  - ✅ High flexibility for future parameters without modifying schemas.
- **Cons:**
  - ❌ Loss of direct strongly-typed queries and SQL indexing on state-machine properties.
  - ❌ Complex schema queries required for locking and filtering jobs.

## 4. Decision Outcome

**Chosen Option:** Option 1 (Direct Table Schema Columns) because it provides first-class relational integrity, strongly-typed schema attributes, and clean compatibility with our global database auditing trigger rules. This satisfies PRD-SYS-001 compliance standards.

## 5. Consequences & Trade-offs

- **Positive Impact:** Strongly-typed state-machine transitions can be easily monitored and queried. Complete visibility into current jobs, retry queues, and duplicated file hashes.
- **Negative Impact:** Modifying existing PostgreSQL / SQLite ORM database schemas requires model up-versioning.
- **Mitigation Strategy:** Automated startup migrations handle the schema updates safely across all execution environments.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `apps/execution/database/models/coding.py`: Extended `DictionaryImportJob` with the new columns.
  - `apps/execution/presentation/routers/dictionaries.py` and `apps/execution/workers/dictionary_worker.py`: Implemented logic.
- **Verification Plan:**
  - Verified using advanced dictionary import unit tests `test_dictionary_import_advanced_features` in `apps/execution/tests/test_medical_coding.py`.
