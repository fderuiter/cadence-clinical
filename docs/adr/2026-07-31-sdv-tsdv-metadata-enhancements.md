# ADR-049: SDV & TSDV Metadata and Database Migrations

- **Status:** Accepted
- **Date:** 2026-07-31
- **Authors:** @google-labs-jules
- **Deciders:** @google-labs-jules

---

## 1. Context & Problem Statement

To finalize Phase 11 SDV & TSDV data model foundations, we need to ensure that database migrations, audit trail captures, and API-layer schemas are fully aligned. This includes adding metadata columns (`is_sdv_verified`, `sdv_verified_by`, `sdv_verified_at`, `page_id`) and introducing the `SDVSignOff` and `TSDVConfig` models to support targeted verification rules.

This decision supports compliance tracing under Trace-1 and PRD-QRY-005.

## 2. Decision Drivers & Constraints

- **Driver 1:** 21 CFR Part 11 electronic records auditing consistency.
- **Driver 2:** Automated relational triggers must execute on SQLite and PostgreSQL.
- **Driver 3:** Backward compatibility for existing observation records.

## 3. Options Considered

### Option 1: Incremental Pre-Boot Migrations with Trigger Auto-Registration

Ensure that any new audited models inheriting from `AuditedModel` (such as `SDVSignOff` and `TSDVConfig`) are automatically registered inside the metadata loop, with schema-level migrations executed before service boot.

- Pros:
  - ✅ Avoids manual trigger creation code for new tables.
  - ✅ Pre-boot execution prevents table contention on deployment.
- Cons:
  - ❌ Requires precise schema inspection when backfilling columns.

### Option 2: Runtime Dynamic Schema Upgrades

Perform dynamic database upgrades on first query execution.

- Cons:
  - ❌ Highly risky for concurrent cluster environments.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Option 1 is fully aligned with our database-first, strongly typed, performance-focused clinical execution engine. By having direct columns on `ClinicalObservation` and clean audited auxiliary tables, the monitoring UI and backend sampling logic can interact with the DB directly and securely.

## 5. Consequences & Trade-offs

- **Positive Impact:** Database triggers deploy atomically alongside new models, capturing version history inside `audit_logs` without performance degradation.
- **Negative Impact / Technical Debt:** Requires keeping `migrate.py` in-sync with column additions on `ClinicalObservation`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/execution/database/migrate.py`, `apps/execution/database/models.py`, `apps/execution/main.py`.
- **Verification Plan:**
  - Verified using local sqlite memory test suite under `tests/test_sdv_tsdv_persistence.py`.
