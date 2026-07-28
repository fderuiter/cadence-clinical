# ADR-050: Lab Reference Range Management

* **Status:** Accepted
* **Date:** 2026-07-30
* **Authors:** @jules
* **Deciders:** @fderuiter, @jules

---

## 1. Context & Problem Statement

Clinical trials require rigorous tracking and verification of laboratory test results against predefined reference ranges. To support this requirement, the Cadence Clinical platform needs a durable and audited data persistence model for lab reference ranges, alongside safe and deployable schema evolution for existing clinical databases.

This decision implements requirements under Trace-1.

## 2. Decision Drivers & Constraints

* **Driver 1 (Compliance & Traceability):** GxP standards and FDA 21 CFR Part 11 require full audit logs and version index tracking for all mutations on clinical trial parameter structures.
* **Driver 2 (Database Patterns):** All data model additions must leverage the asynchronous SQLAlchemy 2.0 pattern and participate in shadow trigger-based database mutations.
* **Driver 3 (Schema Compatibility):** Schema changes must be applied safely to existing deployments. Standard SQLAlchemy `create_all` does not add columns to pre-existing tables, necessitating a explicit schema upgrade/evolution mechanism.

## 3. Options Considered

### Option 1: Schema Evolution via Async SQL-inspection & ALTER TABLE (Selected)
Extend `ClinicalObservation` with new snapshot fields and define `LabReferenceRange` inheriting from `AuditedModel`. Introduce a helper function `upgrade_existing_tables` within the migration script (`migrate.py`) to inspect the database schema at pre-boot and safely run dialec-agnostic `ALTER TABLE` operations.
* **Pros:**
  * ✅ Clean, lightweight, and does not require complex third-party migration frameworks.
  * ✅ High compatibility across SQLite and PostgreSQL databases.
  * ✅ Ensures that triggers are deployed on the freshly added columns seamlessly.
* **Cons:**
  * ❌ Manual handling of column detection and sql query generation.

### Option 2: Relying on full-schema recreates or separate tables
Instead of altering `clinical_observations`, store evaluation results in a separate one-to-one or one-to-many table, or recreate the entire table.
* **Pros:**
  * ✅ Avoids altering existing tables.
* **Cons:**
  * ❌ Severe performance overhead during query/evaluation lookups.
  * ❌ Full table recreates are highly dangerous for live clinical production data.

## 4. Decision Outcome

* **Chosen Option:** Option 1
* **Justification:** Option 1 guarantees a robust, deployable schema upgrade path that is entirely safe for existing databases. By dynamically verifying existing columns through SQLAlchemy's `inspect` API, we run `ALTER TABLE` commands only when columns are missing, ensuring high reliability.

The `LabReferenceRange` model stores:
* `study_id`, `test_code`, `test_name` (identifiers)
* `source` ("CENTRAL" or "LOCAL")
* `site_id` (optional local site applicator)
* `unit`, `normalized_unit` (original and normalized measurement units)
* `sex_applicability` (e.g. 'M', 'F', 'ALL')
* `age_low`, `age_high` (nullable bounds)
* `low_bound`, `high_bound` (normal limits)
* `critical_low`, `critical_high` (critical alarm limits)

The `ClinicalObservation` is extended with:
* `lab_source`, `lab_site_id` (source and site references)
* `lab_indicator`, `lab_out_of_range`, `matched_normal_bounds` (persisted snapshot evaluation details)

## 5. Consequences & Trade-offs

* **Positive Impact:** Full audit trail coverage for reference ranges and snapshot evaluations. Upgrades are pre-boot automatic, zero-downtime ready, and schema-compliant.
* **Negative Impact / Technical Debt:** Requires adding specific column-types to python models and manually writing migration alter-queries, though this remains extremely maintainable.

## 6. Implementation & Verification

* **Affected Repositories / Services:** `apps/execution/database/models.py`, `apps/execution/database/migrate.py`
* **Verification Plan:** Verified using a focused persistence unit test verifying that tables are correctly created, schema changes are successfully applied, precision is retained, and GxP write protection/trigger-based audit logs are perfectly captured.
