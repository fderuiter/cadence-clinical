# ADR-059: Expose Authenticated SDTM/ADaM Dataset-JSON Export Endpoints

* **Status:** Accepted
* **Date:** 2026-08-08
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
The platform requires a validated, authenticated mechanism within the execution-service to query clinical source rows and export them as conformant CDISC Dataset-JSON 1.0.0 format documents for biostatistical analysis. This covers SDTM domains (DM, AE, VS, LB, MH), ADaM datasets (ADSL, ADAE, ADVS), and full study-wide multi-dataset bundles.

This decision implements requirements under Trace-8.

## 2. Decision Drivers & Constraints
* **GxP Compliance & 21 CFR Part 11 Traceability:** Every export execution must participate in existing audit context conventions, and every export transaction must be logged.
* **CDISC Dataset-JSON 1.0.0 Validation:** Schema, keys, and relational/demographic cross-dataset consistency must be validated before returning any payload.
* **Security boundaries:** GatewayAuthMiddleware must protect all export endpoints against unauthenticated access.

## 3. Options Considered
### Option 1: On-the-fly serialization without database logging
* **Overview:** Build endpoints that transform and serialize clinical data dynamically on demand but do not persist any history of the export event.
* **Pros:** Simplest implementation, no database table required.
* **Cons:** Violates GxP trace constraints. If data is exported, the clinical trial audit log must record who performed the export and whether it succeeded or failed.

### Option 2: Transactional database logging via BiostatExport (Selected)
* **Overview:** Build a dedicated `BiostatExport` database model extending `AuditedModel` to log all exports, with extraction helper pipelines bridging the gap between raw data models, pure derivations, and the strict Dataset-JSON validator.
* **Pros:** Ensures full compliance, secure authenticated endpoints, robust error logging, and schema conformance validations.
* **Cons:** Additional table definition and database migration.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Choosing Option 2 guarantees that every biostatistical export execution participates in the database's GxP and audit trail conventions. It allows the system to log the success or failure status and specific error messages of validations.

## 5. Consequences & Trade-offs
* **Positive Impact:** Robust clinical export capabilities validated by strict Pydantic structures. Fully traceable exports logged transactionally.
* **Negative Impact / Technical Debt:** Requires maintenance of the `BiostatExport` table.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/execution/`
* **Verification Plan:** Verified locally and in CI via standard integration tests in `tests/test_biostat_exports.py` covering SDTM, ADaM, bundle exports, and validation failure handling.
