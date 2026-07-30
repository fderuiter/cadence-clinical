# ADR-111: Structured Expiration Metadata and Migration Runners for eTMF/eISF

* **Status:** Accepted
* **Date:** 2026-08-19
* **Authors:** @jules
* **Deciders:** @fderuiter, @gxp-lead
* **Requirement Reference:** PRD-SYS-001

---

## 1. Context & Problem Statement
Regulated clinical documentation (both in eTMF and eISF repositories) often has formal date validity requirements, such as Issue Date and Expiration Date. These need to be indexed, first-class, queryable attributes rather than nested, un-typed metadata blobs to allow robust warnings, reporting, and automated alerts.

Additionally, to ensure zero-downtime rolling deployments (GAMP 5, GxP 21 CFR Part 11 compliant), schema evolution must occur idempotently without relying solely on SQLite in-memory generation or raw `create_all`.

This decision implements requirements under PRD-SYS-001.

## 2. Decision Drivers & Constraints
* **Driver 1:** First-class queryable attributes for date warnings/reporting on documents.
* **Driver 2:** Zero-downtime database deployment with idempotent migrations.
* **Driver 3:** Enforcing role-based access gates (RBAC) so that only authorized personnel can set/modify expiration metadata.

## 3. Options Considered
### Option 1: Un-typed JSON metadata
* **Overview:** Store issue date, expiration date, and owner inside the existing `metadata_json` column.
* **Pros:**
  * ✅ Requires no schema migration.
* **Cons:**
  * ❌ Cannot be cleanly indexed or queried across databases.
  * ❌ Cannot enforce type safety and constraints at the database layer.

### Option 2: First-Class Indexed Columns & Migration Runners (Selected)
* **Overview:** Add indexed database columns and build idempotent pre-boot migration runners.
* **Pros:**
  * ✅ Enables efficient, database-level index query operations.
  * ✅ Strong typing (date) and structured fields.
  * ✅ GxP compliant, secure, role-gated, and fully auditable.
* **Cons:**
  * ❌ Requires a database schema migration.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Adding indexed fields directly to the schema ensures reliable search and performance for expiration reporting, while pre-boot migration runners guarantee safe rolling deployments on SQLite and PostgreSQL.

## 5. Consequences & Trade-offs
* **Positive Impact:** Strongly-typed metadata and robust GxP compliance.
* **Negative Impact / Technical Debt:** Database schema migration is required.
* **Mitigation Strategy:** Provide idempotent DDL migrations checking columns and table existence dynamically before executing ALTER statements.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/etmf`, `apps/eisf`
* **Verification Plan:** Verified via automated integration tests in `tests/test_etmf_eisf_expiration_metadata.py`.
