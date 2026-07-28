# ADR-094: Biostatistical Export Pipeline Interoperability and Architecture

* **Status:** Accepted
* **Date:** 2026-08-14
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To support clinical trial submissions to regulatory bodies (such as the FDA or EMA), clinical data captured in downstream EDC transactions must be transformed, validated, and serialized into CDISC-compliant standards. Specifically, the system must export **SDTM** (Study Data Tabulation Model) domains and derive **ADaM** (Analysis Data Model) datasets in the standardized **CDISC Dataset-JSON 1.0.0** schema.

This process requires a robust, performant transformation architecture that implements the rigorous mapping guidelines and derivation rules defined in **§2.2 (SDTM Domain Extraction)** and **§2.3 (ADaM Dataset Metadata Alignment)** of `docs/SDLC/04_Data_Standards_Interoperability_Blueprint.md`.

---

This decision implements requirements under Trace-8.

## 2. Decision Drivers & Constraints

* **Regulatory Submission Readiness:** High-fidelity compliance with CDISC SDTM v2.0, ADaM v1.3, and CDASH v2.1 specifications.
* **Deterministic Computations:** No side-effects, zero unhandled division-by-zero errors, and no coercion of missing clinical values (e.g. converting a missing numeric laboratory value to `0.0` or a missing age to `-1`).
* **Auditability & Traceability (21 CFR Part 11):** Every export execution (success or failure) must be transactionally logged with caller context, metadata, and status.
* **Zero Heavy Scientific Dependencies:** The pipeline must avoid heavy, non-portable scientific-computing runtimes (such as NumPy, SciPy, or Pandas) in downstream microservices, maintaining a lightweight pure-Python execution boundary.
* **Real-time Validation:** Automated schema conformance checking before any JSON payload is serialized and returned to the caller.

---

## 3. Options Considered

### Option 1: Client-Side Mapping & Conversion
* **Overview:** Rely on external clinical analysis software or client-side JavaScript to perform the data extract, mapping, and conversion.
* **Pros:** Offloads the computational overhead of formatting to client nodes.
* **Cons:** Hard to enforce strict schema validation, lacks GxP traceability at the server/database level, and raises substantial security and data-leakage concerns.

### Option 2: Heavyweight Data Science Pipeline (Pandas/NumPy)
* **Overview:** Integrate Pandas and NumPy on the backend to manipulate dataframes and export JSON.
* **Pros:** Familiar interface for biostatisticians.
* **Cons:** High container overhead, slow startup, and potential non-determinism/type coercion when handling SQL nulls (e.g. Pandas converting integer columns with missing values to float).

### Option 3: Lightweight, Pure-Python Declarative Mapping Table & Pipeline (Selected)
* **Overview:** Implement direct, pure-Python extractors and mappers backed by a declarative table (`SDTM_MAPPINGS` in `apps/execution/biostat/mappings.py`), utilizing Pydantic v2 domain models for CDISC Dataset-JSON 1.0.0 serialization and schema enforcement.
* **Pros:** Highly performant, completely avoids Pandas/NumPy, guarantees type safety, prevents accidental numeric/missing-value coercion, and seamlessly logs each export to the transactional `BiostatExport` database.
* **Cons:** Requires explicit Python-level derivation logic for complex ADaM algorithms (e.g. `TRTEMFL`, `CHG`, `PCHG`).

---

## 4. Decision Outcome

* **Chosen Option:** Option 3 (Lightweight, Pure-Python Declarative Pipeline).
* **Justification:** Choosing Option 3 allows the platform to achieve deterministic compliance, absolute type safety, and real-time schema validation at a fraction of the computational footprint. It guarantees full alignment with SDLC Interoperability Blueprint §2.2 and §2.3.

---

## 5. Consequences & Trade-offs

* **Positive Impact:**
  * Zero-dependency build footprint.
  * Robust, strict schema validation.
  * Accurate historical audit logging of statistical exports for FDA inspections.
* **Negative Impact / Technical Debt:**
  * Complex statistical procedures must be explicitly programmed in Python rather than written in SQL or loaded through Pandas dataframes.
* **Mitigation Strategy:**
  * Maintain comprehensive regression test coverage (as built in `tests/test_biostat_export.py`) to verify complex derivations and edge-cases deterministically.

---

## 6. Implementation & Verification

* **Affected Repositories / Services:**
  * `apps/execution/`
* **Verification Plan:**
  * Automated regression tests in `tests/test_biostat_export.py` covering SDTM AGE computation, sequence assignment, supplemental qualifiers (`SUPP--` structures), partial-date imputation, ADAE TRTEMFL logic, and ADVS CHG/PCHG derivations.
  * Integration tests with SQLite/PostgreSQL in-memory verification, checking Dataset-JSON schema outputs, audit trails, and unauthorized requests.
