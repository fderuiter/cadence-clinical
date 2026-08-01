# ADR-189: SDV and TSDV Data Model Foundations

* **Status:** Accepted
* **Date:** 2026-08-01
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To support Risk-Based Quality Management (RBQM) and streamline clinical trial operations, the Cadence Clinical Platform must implement Targeted Source Data Verification (TSDV) and Source Data Verification (SDV) capabilities. As clinical data volumes grow exponentially, checking 100% of data point entries is often inefficient and does not necessarily improve trial quality. Under ICH GCP E6 (R2) guidelines and FDA regulations (PRD-QRY-005, PRD-QRY-006, PRD-QRY-007), clinical platforms should support a targeted, risk-based approach to source data validation.

This requires a robust, flexible, and GxP-compliant database schema and application layer that can:
- Model TSDV study-specific sampling rules, exclusion criteria, random seed values, and sampling models.
- Support dynamic, level-agnostic SDV sign-offs (field, page, visit levels) with complete tracking of verification actions and drop states.
- Maintain a strict audit trail of who performed the verification, when, and the GxP reasoning for any dropped or rescinded verification statuses.

## 2. Decision Drivers & Constraints

* **GxP & CDISC Compliance (PRD-QRY-005):** Standardize audit ledger records for SDV sign-offs. We must track `created_at`, `created_by`, `reason_for_change`, and `version_index` on all configurations and verification records.
* **Sampling Flexibility (PRD-QRY-006):** Support multiple sampling models (SUBJECT_BASED, FIELD_BASED, COMBINED) and specific domain overrides (e.g., 100% SDV on safety endpoints, zero SDV on demographics).
* **High Performance & Multi-Tenant Isolation (PRD-QRY-007):** Ensure that TSDV evaluations and sign-offs execute efficiently without causing lock contention or schema migrations on core clinical observation data.
* **Traceability & Tamper Resistance:** Prevent orphaned records and preserve historical validation states.

## 3. Options Considered

1. **Option A (Selected):** Centralize TSDV configuration and level-agnostic SDV audit records in relational tables (`tsdv_configs` and `sdv_sign_offs`) in the core Postgres schema, with field-level boolean flags and metadata inline on `clinical_observations` for rapid querying.
2. **Option B:** Fully rely on an external audit service or separate microservice, importing and verifying data objects over HTTP/REST on-the-fly.

## 4. Decision Outcome

**Chosen option: Option A** because it natively couples GxP compliance with transactional database performance inside `apps/execution/`. By representing `TSDVConfig` as a versioned, audited model, study designers can change sampling rules over time under rigorous change control. By utilizing `SDVSignOff` with a generic `scope` (FIELD, PAGE, VISIT), we support multi-level verification workflows with minimal schema complexity and clean foreign-key validation constraints. Inline verification fields on `clinical_observations` ensure high-speed reads during form presentation.

## 5. Consequences & Trade-offs

* **Positive:**
  * Clean, centralized database representation of TSDV parameters.
  * Level-agnostic, auditable structure (`SDVSignOff`) allows standardizing verification signatures across different clinical contexts.
  * Fully compliant with FDA 21 CFR Part 11 signature/audit requirements.
  * Highly performant; avoids complex multi-table joins or remote API lookups during observation queries.
* **Negative:**
  * Requires careful synchronization when field-level flags are updated alongside page- or visit-level sign-off events.
  * Adding boolean columns on large tables (`clinical_observations`) increases database size, though indexing mitigates search performance issues.

## 6. Implementation & Verification

* **Target files/packages modified:**
  * `apps/execution/database/models.py`: Added `SDVSignOff`, `TSDVConfig` classes and corresponding `clinical_observations` columns (`is_sdv_verified`, `sdv_verified_by`, `sdv_verified_at`, `page_id`).
  * `apps/execution/routers/sdv.py`: Implemented API endpoints for configuring TSDV, evaluating study sampling requirements, and signing off or dropping verifications.
* **Verification tests added under `tests/`:**
  * `tests/test_sdv.py`: Verifies TSDV config CRUD, subject/field sampling evaluations, bulk sign-off APIs, and drop verification audit logging.
