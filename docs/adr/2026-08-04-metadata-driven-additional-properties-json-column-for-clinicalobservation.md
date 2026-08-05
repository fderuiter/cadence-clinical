# ADR-117: Metadata-driven Additional Properties JSON column for ClinicalObservation

* **Status:** Accepted
* **Date:** 2026-08-04
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To support evolving clinical observation specifications and to prevent relational schema bloating/rigidity, we need a flexible, metadata-driven approach to persist custom, study-specific, or module-specific attributes (such as laboratory reference range details).
The previous implementation maintained physical database columns on `ClinicalObservation` (such as `lab_source`, `lab_site_id`, `lab_indicator`, etc.).
We need to purge these physical database columns and replace them with a dynamic `additional_properties` JSON column, while preserving backward-compatible getter/setter properties and constructors to prevent breaking existing code and tests (PRD-LAB-001).

## 2. Decision Drivers & Constraints

* **Compliance:** Tracing to GxP requirement PRD-LAB-001.
* **Flexibility:** Enabling clinical studies to author custom observation properties without performing relational database schema migrations.
* **Backward Compatibility:** Existing tests and queries that reference physical column names must continue to function via getter/setter ORM properties.
* **Database Cleanliness:** Purging highly specialized columns from the primary relational tables.

## 3. Options Considered

### Option 1: Retain Physical Columns
* **Overview:** Maintain physical relational columns for every custom field or clinical metric.
* **Pros:**
  * ✅ Simplifies raw SQL querying.
* **Cons:**
  * ❌ Schema migration overhead is high.
  * ❌ Tables become sparse and bloated.

### Option 2: Metadata-driven JSON Column (ADR-117) - Selected
* **Overview:** Introduce an `additional_properties` JSON column to store dynamic fields, and proxy the attributes via properties.
* **Pros:**
  * ✅ Schema is flexible and extensible.
  * ✅ Complete backward compatibility through descriptor properties on the ORM class.
  * ✅ Simplifies database schema management and avoids costly migrations for custom fields.
* **Cons:**
  * ❌ Dynamic field updates require flagging changes in the SQLAlchemy unit of work.

## 4. Decision Outcome

* **Chosen Option:** Option 2
* **Justification:** Implementing a JSON column under the `additional_properties` attribute satisfies PRD-LAB-001 and provides architectural flexibility without the cost of frequent schema evolution. Backward compatibility is achieved through standard Python property proxies.

## 5. Consequences & Trade-offs

* **Positive Impact:** Relational database table is clean and resilient to changes.
* **Negative Impact / Technical Debt:** Requires using `flag_modified` in the setters to ensure SQLAlchemy detects nested mutation changes.
* **Mitigation Strategy:** Setter methods explicitly trigger `flag_modified` on the `additional_properties` column.

## 6. Implementation & Verification

* **Affected Repositories / Services:**
  * `apps/execution/database/models.py` (ClinicalObservation modifications)
  * `apps/execution/database/migrate.py` (Schema evolution definition)
* **Verification Plan:**
  * Verified via tests in `tests/test_lab_reference_range_persistence.py`.
