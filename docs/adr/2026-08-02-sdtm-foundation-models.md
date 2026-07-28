# ADR-053: Shared SDTM Foundation Models & Terminology

* **Status:** Accepted
* **Date:** 2026-08-02
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
Historically, clinical data captured during clinical trials has been manually mapped to CDISC SDTM (Study Data Tabulation Model) formats near database lock. This introduces significant delays and human error. To automate this pipeline, the Cadence Clinical platform implements an automated EDC-to-SDTM mapper and structural validator. These components require a shared, strongly-typed foundation defining CDISC SDTM domains, variables, controlled terminology, and GxP compliant audit metadata.

This decision implements requirements under PRD-MDR-001.

## 2. Decision Drivers & Constraints
* **Driver 1:** Enforce type safety, structure, and validation rules for core SDTM domains (DM, AE, VS, LB, CM) and supplemental qualifiers (SUPPQUAL) using Pydantic v2.
* **Driver 2:** Strict compliance with GxP and FDA 21 CFR Part 11 auditing requirements. All data mutations must carry explicit audit fields: `created_at`, `created_by`, `reason_for_change`, and `version_index`.
* **Driver 3:** Enforce standard CDISC controlled terminology (SEX, RACE, AE severity/seriousness/relationship/outcome, and HL7 Null-Flavor codes) at the validation layer.

## 3. Options Considered
### Option 1: Inline Database Models
* **Overview:** Build SDTM validation directly within SQLAlchemy database or serialization schemas of the clinical execution engine.
* **Pros:**
  * ✅ Less architectural separation; simpler initially.
* **Cons:**
  * ❌ Violates separation of concerns.
  * ❌ Core models cannot be shared cleanly with design or integration microservices.

### Option 2: Shared `sdtm` Core Model Package (Selected)
* **Overview:** Create a modular, self-contained `sdtm` package within `packages/core-models/` that utilizes Pydantic v2 for data structures, enums, normalization, and auditing constraints.
* **Pros:**
  * ✅ Highly reusable across multiple microservices (Execution, Gateway, Interop, and Designer).
  * ✅ Purely Python-driven and database-agnostic.
  * ✅ Easily testable and validated prior to database persistence or serialization.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Implementing a modular `sdtm` package in `packages/core-models/` maintains clean system boundaries and enables the EDC-to-SDTM mapper, validator, and analytics tools to share a single source of truth for SDTM variables and CDISC controlled terminology.

## 5. Consequences & Trade-offs
* **Positive Impact:** Strongly typed SDTM datasets with automated validation errors on missing Required variables. High-confidence normalization of verbatim/raw values.
* **Negative Impact / Technical Debt:** Duplicate normalization logic can emerge if other packages implement bespoke normalizations.
* **Mitigation Strategy:** Decouple terminology functions and make them standard, exposing them directly from the `sdtm` package.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `packages/core-models/sdtm/`
* **Verification Plan:** Verify compile and schema correctness with unit tests covering required/optional fields, SUPPQUAL, and terminology normalizations. Run `pytest` to verify automated checks pass.
