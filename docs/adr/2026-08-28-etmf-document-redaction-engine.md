# ADR-119: eTMF Document Redaction Engine, PII/PHI Detection, and Boundary Design

* **Status:** Accepted
* **Date:** 2026-08-28
* **Authors:** @jules
* **Deciders:** @fderuiter, @qa-validator

---

## 1. Context & Problem Statement
Clinical documents managed within the electronic Trial Master File (eTMF) frequently contain sensitive unblinded clinical records, personally identifiable information (PII), or protected health information (PHI). To ensure GxP data integrity and satisfy clinical data privacy laws, we need a robust, automated, and secure server-side document redaction engine. This ADR documents the architectural boundaries, design choices, and compliance integrations for the redaction engine.

This decision directly implements requirements under **PRD-TMF-005** and **Trace-12**.

## 2. Decision Drivers & Constraints
* **GxP & 21 CFR Part 11 Compliance:** All redaction actions must be fully auditable, require change justification reasons, and preserve the unredacted original files for authorized personnel.
* **Privacy Controls:** Raw matched PII/PHI values must never leak into any audit trails, log files, or manifest files.
* **Separation of Roles:** Inspectors and external auditors must be locked into redacted successor views, with raw original views blocked (returning HTTP 403 Forbidden).
* **Scope Exclusion:** Avoid complex, stochastic, or heavy dependencies (like spaCy, Presidio, or visual PDF redaction canvasses) to ensure deterministic repeatability in a GxP validated state.

## 3. Options Considered
### Option 1: Heavy Machine Learning / Named Entity Recognition (NER) Stack
* **Overview:** Integrate a third-party NER stack (e.g., Presidio/spaCy) for free-text entity detection.
* **Pros:**
  * ✅ High accuracy on diverse unstructured texts.
* **Cons:**
  * ❌ Introduces heavy, non-deterministic dependencies.
  * ❌ Hard to validate under strict GxP computer system validation guidelines.

### Option 2: Pure-Python Regex-Based Detection & Shared Package [Selected]
* **Overview:** Perform deterministic pattern detection using a curated, pre-compiled regex library and literal terms lookup. Keep this engine within a reusable shared package (`packages/deid`) to prevent duplication.
* **Pros:**
  * ✅ Highly performant, predictable, and fully deterministic.
  * ✅ Easy to validate and verify via standard unit tests.
  * ✅ Shared package supports reuse across other pipelines (e.g., SDTM exports).
* **Cons:**
  * ❌ Requires manual maintenance of patterns for new identifier classes.

### Option 3: In-Place Destructive Overwrites
* **Overview:** Overwrite PII/PHI within original document records directly in the eTMF database.
* **Pros:**
  * ✅ Reduces storage overhead.
* **Cons:**
  * ❌ Violates 21 CFR Part 11 and GxP immutable record history requirements.
  * ❌ Disallows authorized sponsor users or clinical investigators from reviewing unredacted source logs.

### Option 4: Non-Destructive Version Preservation [Selected]
* **Overview:** Persist the redacted successor as a new document version (incrementing the `version_index`) while preserving the original version in a locked state, linking them via a `redaction_source_id` pointer.
* **Pros:**
  * ✅ Retains complete data provenance and compliance tracking.
  * ✅ Safely permits role-based view restrictions.
* **Cons:**
  * ❌ Slight incremental storage overhead.

## 4. Decision Outcome
* **Chosen Option:** Option 2 (Regex-Based & Shared Package) combined with Option 4 (Non-Destructive Version-Preserving Redaction).
* **Justification:** Option 2 satisfies both the regulatory requirements of 21 CFR Part 11 and the operational efficiency needs of PIs by allowing atomic, secure batch signatures with complete cryptographic validation and re-authentication. Option 4 satisfies non-destructive versioning requirements, ensuring the raw original remains available to authorized auditors while restricting standard inspectors.

### Architectural Key Points
1. **Curated Regex & Overlap Resolution:** The shared `packages/deid` uses compiled regular expressions for the 18 HIPAA Safe Harbor identifiers (plus custom terms), resolving overlapping matched spans by prioritizing wider ranges.
2. **Cryptographic Manifest Sealing:** Every redaction produces a Pydantic-based `RedactionManifest` signed symmetrically with HMAC-SHA256 using the gateway secret `REDACTION_SIGNING_SECRET`, saved directly in the redacted row's metadata.
3. **Audit Trail Sealing:** Logs a non-sensitive `REDACT` event in `TMFAuditLog` with no raw PII values present.

## 5. Consequences & Trade-offs
* **Positive Impact:**
  * ✅ Complete compliance with Part 11 auditing and data retention.
  * ✅ High usability with automated and manual redactions.
  * ✅ Prevents leak of unredacted information to unauthorized reviewers.
* **Negative Impact / Technical Debt:**
  * ❌ Requires maintaining patterns and rules over time.
  * ❌ Deliberate exclusion of visual canvasses and PDF rendering within this boundary (kept strictly out of scope of this headless backend microservice architecture).

## 6. Implementation & Verification
* **Affected Repositories / Services:**
  * `packages/deid`
  * `apps/etmf`
* **Verification Plan:**
  * Verified end-to-end via comprehensive unit tests in `tests/test_deidentification.py` and API route integration tests in `tests/test_etmf_redaction.py`.
