# ADR-117: CDASH-to-SDTM Domain Mapping & Mapped Record Database Persistence

* **Status:** Accepted
* **Date:** 2026-08-27
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
In metadata-driven eClinical platforms, electronic Case Report Form (eCRF) data collected according to Clinical Data Acquisition Standards Harmonization (CDASH) standards must be transformed into standard Study Data Tabulation Model (SDTM) domains (such as Demographics (DM), Adverse Events (AE), Vital Signs (VS), Laboratory Findings (LB), and Subject Visits (SV)) with derived variables (such as AESTDTC, AEENDTC, AESEQ, study days AEDY, VSDY, etc.).
We need to design a clean, automated, GxP-compliant CDASH-to-SDTM domain mapping transformation engine and persist the transformed SDTM records in the execution database to satisfy the PRD-SYS-001 requirements.

## 2. Decision Drivers & Constraints
* **Compliance:** 21 CFR Part 11 and GxP standards require all clinical modifications and transformed records to be fully auditable and version-controlled.
* **Traceability:** Robust traceability mapping from CDASH to SDTM variables with sequence generation and study day calculations.
* **Performance:** Pure stateless calculations decoupled from DB transactions where possible, converging on an atomic database write pipeline.

## 3. Options Considered
### Option 1: In-Memory Only Transformations
Perform CDASH-to-SDTM transformation on-the-fly and return them purely in-memory.
* **Pros:**
  * ✅ No database schema modifications needed.
* **Cons:**
  * ❌ No historical lineage or audit trail of the transformed SDTM records.
  * ❌ Fails GxP requirement of database-level audit ledger.

### Option 2: Database-native Mapped SDTM Domain Record Table
Establish a central `sdtm_domain_records` table mapping study-id, domain, and unique subject identifiers to the fully-serialized strongly-typed SDTMRecord Pydantic models.
* **Pros:**
  * ✅ Full auditable lineage per clinical data transaction.
  * ✅ Strong schema validation with Pydantic v2.
  * ✅ Decentralized JSON payload allows custom extensible schemas per SDTM domain without massive column expansion.
* **Cons:**
  * ❌ Minimal SQL query filtering on nested JSON keys (mitigated by indexing parent columns like study_id, domain, usubjid).

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Persisting mapped records via a dedicated `sdtm_domain_records` table with AuditedModel traits ensures GxP audit-logging, optimistic concurrency versioning, and secure clinical provenance.

## 5. Consequences & Trade-offs
* **Positive Impact:** Transformed SDTM records are persisted, versioned, and auditable, aligning perfectly with GxP rules.
* **Negative Impact / Technical Debt:** Requires a database migration step and table creation.
* **Mitigation Strategy:** Automated migrations run pre-boot during lifecycle starting, ensuring zero-downtime deployment.

## 6. Implementation & Verification
* **Affected Repositories / Services:**
  * `packages/core-models/sdtm/sdtm_models.py` (Pydantic models)
  * `apps/execution/database/models.py` (SDTMDomainRecord ORM model)
  * `apps/execution/database/migrate.py` (Trigger/Migration deployment)
  * `apps/execution/services/sdtm_mapper.py` (CDASHToSDTMMapper transformation service)
* **Verification Plan:**
  * Run automated unit and integration tests under `tests/test_sdtm_mapper.py`.
  * Ensure test coverage is validated locally and in CI/CD.
