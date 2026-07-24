# ADR-048: SDV/TSDV Persistence Foundation and Migration Support

* **Status:** Accepted
* **Date:** 2026-07-28
* **Authors:** @jules
* **Deciders:** @fderuiter, @jules

---

## 1. Context & Problem Statement
The Cadence Clinical trial execution engine needs support for Source Data Verification (SDV) and Targeted SDV (TSDV) as required by clinical data monitoring and compliance standards. This requires establishing audited persistence models to track field-level verification state, aggregate (page/visit-level) sign-offs, and study-level targeted sampling configurations.

## 2. Decision Drivers & Constraints
* **Driver 1 (Compliance):** GxP and FDA 21 CFR Part 11 require full audit logs and version index tracking for all modifications to clinical state and clinical trials.
* **Driver 2 (Database Patterns):** Schema changes must utilize the existing asynchronous SQLAlchemy 2.0 pattern and participate in the automated database triggers/audit tracking.
* **Driver 3 (Schema Compatibility):** Existing observations and trials must remain unaffected by the schema upgrades. Defaults and nullability constraints must be carefully selected.

## 3. Options Considered
### Option 1: Inline Observation Verification State
Extend the existing `ClinicalObservation` with verification columns and implement independent `SDVSignOff` and `TSDVConfig` models.
- Pros:
  * ✅ Extremely clean division of responsibilities.
  * ✅ High-performance queries for checking if individual observation fields are verified.
  * ✅ Decoupled per-study TSDV sampling settings that do not overload the study metadata/designer tables.
- Cons:
  * ❌ Moderately increases the size of the observation table.

### Option 2: Purely Dynamic Key-Value Verification Store
Maintain a single generic key-value or EAV-style verification state table.
- Pros:
  * ✅ No schema additions on the primary observation table.
- Cons:
  * ❌ Hard to query efficiently.
  * ❌ Complex audit trails and version index tracking.

## 4. Decision Outcome
* **Chosen Option:** Option 1
* **Justification:** Option 1 is fully aligned with our database-first, strongly typed, performance-focused clinical execution engine. By having direct columns on `ClinicalObservation` and clean audited auxiliary tables, the monitoring UI and backend sampling logic can interact with the DB directly and securely.

## 5. Consequences & Trade-offs
* **Positive Impact:** Full audit coverage is achieved immediately. The DB triggers dynamically intercept all insert and update mutations on `SDVSignOff` and `TSDVConfig` to produce cryptographic audit trails.
* **Negative Impact / Technical Debt:** Requires running schema upgrades in pre-boot migrations to set up the new tables and observation columns.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/execution/database/`
* **Verification Plan:** Verify migration setup against a local in-memory SQLite database, asserting that triggers and table structures exist, and that they participate in shadow trigger-based audit logs.
