# ADR-058: ePRO Sync Durable Reconciliation, Defeated Record Retention, and Structural Queries

* **Status:** Accepted
* **Date:** 2026-08-07
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
The ePRO (electronic Patient-Reported Outcome) synchronization engine reconciles offline participant diaries with server-side clinical observations. In multi-device, offline-first scenarios, reconciliation can lead to conflict states resolved via Client-Wins, Server-Wins, or Merge strategies. Historically, defeated or partially overridden values were silently discarded, which violates strict GxP (Good Clinical Practice) data integrity and FDA 21 CFR Part 11 requirements. Additionally, edits submitted against missing or deleted target records (e.g. non-existent instruments or assignments) were not formally flagged, resulting in hidden synchronization discrepancies.

This decision addresses the need to:
1. Durably persist all defeated sync inputs (shadow records) to ensure complete data traceability.
2. Formally detect edits targeting missing/deleted target records as structural synchronization conflicts, reject direct updates to the primary tables, and turn them into auditable clinical queries.
3. Keep the Interop microservice self-contained and auditable without side effects on general execution GxP audit handlers.

## 2. Decision Drivers & Constraints
* **Driver 1:** FDA 21 CFR Part 11 and GxP compliance regarding immutable data logs and absolute prevention of silent data loss.
* **Driver 2:** Traceability of all synchronization conflicts, allowing investigators and clinical monitors to review discarded or merged values.
* **Driver 3:** Robust validation boundaries preventing erroneous mobile app writes from corrupting clinical datasets.

## 3. Options Considered
### Option 1: In-line primary table versioning
* **Overview:** Save every version of submissions directly in the `epro_submissions` table with custom revision indexing and status fields.
* **Pros:**
  * ✅ Single database table simplifies basic queries.
* **Cons:**
  * ❌ Drastically increases table volume and query complexity when fetching only the active/winning answers.
  * ❌ Mixing active data and historically rejected payloads degrades system performance and reporting correctness.

### Option 2: Dedicated Shadow/Defeated Model and Local Clinical Query Schema (Selected)
* **Overview:** Introduce a dedicated `epro_defeated_submissions` table/model to persist defeated/overwritten inputs and a local `clinical_queries` table/model to record structural exceptions.
* **Pros:**
  * ✅ High separation of concerns: Active/winning data is isolated from historical defeated payloads.
  * ✅ Absolutely no overwritten or defeated sync payload is silently discarded.
  * ✅ Clear structured clinical query generation when synchronization attempts refer to non-existent targets.
  * ✅ Self-contained within the Interop gateway service boundary.
* **Cons:**
  * ❌ Introduces two new database tables.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 provides complete regulatory auditability for offline sync conflicts without bloating primary transaction tables or causing side effects in the clinical execution system.

## 5. Consequences & Trade-offs
* **Positive Impact:** Full 21 CFR Part 11 traceability. Defeated inputs are easily reviewable by clinical staff. Structural errors (ghost submissions) instantly raise highly visible clinical queries for correction.
* **Negative Impact / Technical Debt:** Marginal increase in relational database schema footprint.
* **Mitigation Strategy:** Automated unit and integration tests run on every build to guarantee database migrations and constraints remain correct.

## 6. Implementation & Verification
* **Affected Repositories / Services:** Interop microservice (`apps/interop/`).
* **Verification Plan:** Verified via `tests/test_interop_defeated.py` validating correct persistence of defeated inputs on conflict scenarios, and robust handling of structural sync conflicts (rejection, query opening, exception logging).
