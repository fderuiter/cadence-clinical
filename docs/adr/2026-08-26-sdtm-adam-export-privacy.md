# ADR-108: SDTM/ADaM Export Privacy: Deterministic Pseudonymization and Date De-identification

- **Status:** Accepted
- **Date:** 2026-08-26
- **Authors:** @jules
- **Deciders:** @architect, @sponsor_dm, @statistician
- **Requirement References:** PRD-SYS-001, GxP 21 CFR Part 11 Regulated

---

## 1. Context & Problem Statement

Prior to transmitting datasets to sponsors, clinical statisticians, or external regulatory authorities (e.g. FDA, EMA), structured clinical data (CDISC SDTM and ADaM formats) must undergo a rigorous de-identification pass to preserve patient privacy in compliance with HIPAA Safe Harbor and GDPR Recital 26.

Historically, inconsistent de-identification conventions existed across documents:

- The Data Standards & Interoperability Blueprint (§ 4.6) described a `[-30, +30]` day random narrative delta.
- The document-redaction engine defaults to a flat `365-day` date shift.

Neither of these is appropriate for structured SDTM/ADaM exports:

- Random shifting breaks longitudinal referential consistency across separate domains, datasets, and subsequent export calls (e.g., matching a patient's AE start date with their exposure or baseline vitals).
- A flat 365-day shift does not mask true chronological patterns if multiple patients are shifted by the same static offset.

Furthermore, we must establish clear authorization boundaries, deterministic pseudonymization formats, age-generalization caps, and study-specific key ownership.

## 2. Decision Drivers & Constraints

- **Driver 1 (Referential Integrity):** Transformed exports must survive strict cross-domain validation checks (e.g., `validate_dataset_json`). Cross-domain `USUBJID` and `SITEID` joins and `SUPP--` parent linkage (RDOMAIN, IDVAR, IDVARVAL) must remain completely unbroken.
- **Driver 2 (Anonymization Strength):** Dates must be shifted sufficiently to destroy true calendar values while maintaining intervals between events.
- **Driver 3 (Deterministic Logic):** The same patient's records across different domains (e.g., DM, AE, VS, ADSL) must be shifted by the exact same stable, deterministic offset across separate export calls.
- **Driver 4 (Audience Access Gating):** Strict RBAC determines who can access de-identified exports vs. raw data, defining the boundary between internal clinical staff and external regulators.

## 3. Options Considered

### Option 1: Apply Flat 365-day Date Shift

- **Pros:** Simpler to implement.
- **Cons:** Insufficient anonymity because all subjects are shifted by the same static amount, making it vulnerable to pattern matching.

### Option 2: Apply Random Date Shift per Subject (Regenerated Each Call)

- **Pros:** High entropy.
- **Cons:** Breaks reproducibility and referential integrity across separate export calls or independent domain requests (e.g., if DM is exported now and AE is exported later, they would have mismatching shifted dates).

### Option 3: Deterministic Per-Subject Date Shifting & Pseudonymization (Selected)

- **Pros:**
  - ✅ **Deterministic Date Shifting:** We derive a stable per-subject offset from the record's original `USUBJID` via HMAC-SHA256 keyed by the export salt, mapped to a bounded range `[-365, +365]` days. This ensures that the same subject gets the exact same offset across all domains, datasets, and separate export calls.
  - ✅ **Stable Pseudonymization:** `USUBJID`, `SUBJID`, and `SITEID` are pseudonymized deterministically using the export salt via HMAC-SHA256, outputting a 64-character hex string.
  - ✅ **Age Capping:** Numeric `AGE` fields are capped at `89` (values > 89 set to 89).
  - ✅ **Referential Integrity:** Preserves the parent-lookup contracts and joins between datasets automatically.

## 4. Decision Outcome

- **Chosen Option:** Option 3
- **Authoritative Policy Rules for SDTM/ADaM Exports:**
  1. **Permitted Audiences:** Inside the execution workspace, access is gated to roles (`ROLE_CRA`, `ROLE_DATA_MANAGER`, and statisticians). Intended external-regulator boundaries are "de-identified only"—only de-identified and pseudonymized payloads are exposed at the export endpoints.
  2. **Pseudonym Format:** Deterministic HMAC-SHA256 hex string over raw values, keyed by the secure `BIOSTAT_EXPORT_SALT` runtime secret.
  3. **Date-Shift Derivation:** Computed as `(int(HMAC(original_usubjid, salt), 16) % 731) - 365` yielding a stable offset in the range `[-365, 365]` days.
  4. **Date Formats Shifted:**
     - SDTM string dates (e.g. `AESTDTC`, `RFSTDTC` etc.): Shifted using a precision-preserving algorithm. If a date is partial (e.g., `YYYY-MM` or `YYYY-MM-UN`), numeric components are shifted, leaving imprecise placeholder tokens untouched, keeping chronological ordering (`AEENDTC >= AESTDTC`) intact.
     - ADaM integer dates (e.g. `TRTSDT`, `ASTDT` etc.): Shifted by adding the numeric offset directly to the SAS day integer value.
  5. **Age Generalization:** Any `AGE` value > 89 is capped at 89.
  6. **Key Ownership:** The study sponsor holds exclusive ownership of the export salt. Salts must be managed securely via runtime environment configuration (e.g., `BIOSTAT_EXPORT_SALT`) and rotated periodically in accordance with standard operating procedures.

---

## 5. Consequences & Trade-offs

- **Reconciliation of Prior Conventions:** This ADR explicitly reconciles and supersedes prior inconsistent de-identification conventions. The Interoperability Blueprint §4.6 ±30-day random narrative delta is superseded for structured clinical exports by this deterministic, stable per-subject `[-365, 365]` days date shift. Additionally, the default flat 365-day shift used in document redaction does not apply to structured SDTM/ADaM exports, which must enforce this per-subject deterministic model.
- **Positive Impact:** Full CDISC Dataset-JSON schema and cross-domain referential consistency checks are preserved intact. No raw identifiers or raw dates are ever leaked.
- **Mitigation Strategy:** The export salt must never be logged, persisted, or exposed in error logs, and must be sourced solely from secure runtime environments.

## 6. Implementation & Verification

- **Implementation:** The transform is implemented under `apps/execution/biostat/deid.py` and is automatically called immediately before Dataset-JSON serialization at the convergence point in `apps/execution/main.py`.
- **Verification:** The de-identification pipeline and privacy guarantees are validated with a comprehensive test suite in `tests/test_biostat_deidentification.py` asserting pseudonymization determinism, unchanged source records, identical transform of the same identifier across domains, interval preservation, and correct role-based access restrictions.
