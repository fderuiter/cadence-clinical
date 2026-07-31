# ADR-117: Offline Data Ingestion & Sync Engine

* **Status:** Accepted
* **Date:** 2026-08-27
* **Authors:** @jules
* **Deciders:** @fpderutier
* **Requirement Reference:** PRD-SYS-001

---

## 1. Context & Problem Statement
In clinical trials, site coordinators and subject participants often record ePRO and eCRF data on mobile or web PWA devices in areas with limited or no internet connectivity. These offline entries are queued in client IndexedDB storage. Upon reconnection, they must be synchronized reliably with the server without data loss, out-of-order mutations, or GxP audit trail corruption (PRD-SYS-001).

## 2. Decision Drivers & Constraints
* **GxP 21 CFR Part 11 Compliance (PRD-SYS-001):** All database transactions must preserve their full historical lineage. Duplicate transactions must be discarded idempotently without corrupting version indexes.
* **Timestamp-Vector Conflict Resolution:** If a record was updated on the server after the offline data was captured, the conflict must be flagged for manual review rather than overwritten with stale offline values.
* **Ordering Guarantee:** Offline deltas must be processed in the strict sequential order of their client timestamps (`client_timestamp_utc`).

## 3. Options Considered
### Option 1: Client-overwrite-all (Last-Write-Wins)
* **Overview:** The incoming offline delta always overwrites any existing database records.
* **Pros:** Simpler to implement on the server side.
* **Cons:** Overwrites concurrent server-side modifications without flagging them, violating clinical data integrity.

### Option 2: Database-Backed Idempotent Sync with Vector Comparison (Selected)
* **Overview:** Introduce a tracking table (`processed_offline_batches`) to record synchronized batch IDs. Retrieve the latest `AuditLog` timestamp for any existing clinical record to detect if there has been a newer concurrent server-side modification, and if so, preserve both versions and flag the status as `NEEDS_REVIEW`.
* **Pros:** Fully compliant with GxP and 21 CFR Part 11, guarantees strict idempotency, detects and handles conflicts gracefully (PRD-SYS-001).
* **Cons:** Slightly more database interactions per batch.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Meets all clinical data integrity and regulatory compliance requirements. It ensures safe conflict resolution, prevents duplicate database insertions, and processes deltas in chronological order.

## 5. Consequences & Trade-offs
* **Positive Impact:** Guarantees absolute network level idempotency, preserves complete database history, and exposes unresolved conflicts to data managers for review.
* **Negative Impact / Technical Debt:** Requires keeping a persistent registry of successfully processed offline batch IDs.
* **Mitigation Strategy:** Pruning historical processed batch records older than a retention window can be performed periodically if required.

## 6. Implementation & Verification
* **Affected Repositories / Services:**
  - `apps/execution/database/models.py` (added `ProcessedOfflineBatch`)
  - `apps/execution/database/migrate.py` (registered class in pre-boot migration scope)
  - `apps/execution/services/offline_sync.py` (implemented `OfflineSyncEngine`)
  - `apps/execution/routers/offline.py` (added sync API endpoint)
* **Verification Plan:**
  - Validated via `tests/test_offline_sync.py` verifying standard delta ingestion, duplicate retry idempotency, sequential chronological ordering, cryptographic payload validation, and server-side conflict resolution flagging.
