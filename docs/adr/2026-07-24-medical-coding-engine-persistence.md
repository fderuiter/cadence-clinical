# ADR-050: Medical Coding Engine Persistence Foundation

* **Status:** Accepted
* **Date:** 2026-07-24
* **Authors:** @jules
* **Deciders:** @fderuiter, @jules

---

## 1. Context & Problem Statement

The Cadence Clinical platform requires a standardized, validated, and high-performance mechanism to code clinical verbatim terms (such as adverse events, concomitant medications, medical history) against industry-standard medical dictionaries (MedDRA, WHODrug, etc.). We need to define the relational database models for these terminologies, manage dictionary import execution/status, track investigator coding decisions, and maintain complete 21 CFR Part 11 and GxP compliant auditing.

## 2. Decision Drivers & Constraints

* **Driver 1 (Performance):** Ingesting massive vocabularies/dictionaries (hundreds of thousands of rows for MedDRA/WHODrug) must be highly performant and not suffer from per-row trigger overhead.
* **Driver 2 (Compliance & Traceability):** Coding assignments and alterations are critical clinical data points requiring strict auditing, including who made the change, when, and the reason for change (as mandated by 21 CFR Part 11 / EU Annex 11).
* **Driver 3 (Schema Integrity):** Enforce strict database-level unique and check constraints on dictionary hierarchies and status transitions to prevent dirty data in the clinical record.

## 3. Options Considered

### Option 1: Store Terminology Directly in Core Tables

Store coding attributes directly in the observation/verbatim tables without distinct terminology entities.
* **Pros:**
  * ✅ Fewer relational tables.
* **Cons:**
  * ❌ Massive data redundancy and spelling inconsistency across observations.
  * ❌ No centralized dictionary/vocabulary management or version upgrades.

### Option 2: Separate Terminology Tables with Bulk Auditing (Selected)

Define dedicated normalized tables for MedDRA hierarchies and WHODrug ingredients/records, managed by `DictionaryImportJob`. Apply the standard `AuditedModel` triggers, but provide a context flag `cadence.app_writing` that can be set to `'true'` to temporarily bypass row-by-row trigger logs during massive dictionary loads.
* **Pros:**
  * ✅ Enforces normalized database schemas with proper index lookups.
  * ✅ Standardizes the model layout, audit logs, and status transitions.
  * ✅ Bypasses auditing performance bottlenecks during bulk vocabulary loading while retaining full audit coverage for regular investigator coding assignments.
* **Cons:**
  * ❌ Requires more database tables and schema management.

---

## 4. Decision Outcome

* **Chosen Option:** Option 2
* **Justification:** Implementing dedicated relational structures for medical dictionaries aligns with the CDISC standards and our GxP compliance guidelines. By introducing the `cadence.app_writing` context bypass, we solve the performance issues of loading extremely large dictionary datasets while maintaining bulletproof audit trails on the coding assignments themselves.

### Relational Schema Definition

The following entities are registered under `apps/execution/database/models.py`:
1. **MedDRATerm & MedDRAHierarchy:** Models LLT, PT, HLT, HLGT, and SOC levels and their parent hierarchies.
2. **WHODrugRecord, WHODrugIngredient, WHODrugATC, WHODrugDrugATC, WHODrugDrugIngredient:** Normalizes WHODrug codes, substances, ATC classifications, and association maps.
3. **DictionaryImportJob:** Tracks metadata-driven terminology dictionary imports.
4. **ClinicalCodingAssignment & ClinicalCodingLedger:** Models the user-facing coding workflow (verbatim terms coded to specific standard dictionary terms), including a check constraint ensuring that coded status requires valid dictionary codes/terms, and an audit ledger capturing historical change reasons.

## 5. Consequences & Trade-offs

* **Positive Impact:** Fast ingest speeds for dictionary imports, perfect schema normalization, and full audit logs for investigator actions.
* **Negative Impact / Technical Debt:** Requires external background workers to manage bulk uploads and coordinate database connection session settings (`SELECT set_config('cadence.app_writing', 'true', true);`).

## 6. Implementation & Verification

* **Affected Repositories / Services:** `apps/execution/`
* **Verification Plan:**
  * Unit tests added in `tests/test_medical_coding.py` covering model creation, dictionary import job status flows, verification of constraint triggers, and clinical coding assignments.
  * Comprehensive integration tests added in `tests/test_medical_coding_lifecycle.py` verifying automatic coding during observation capture, mid-confidence suggestion preservation, and manual coder actions (ACCEPT/OVERRIDE) with roles and Part 11 checks.
