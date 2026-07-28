# ADR-098: Document Redaction Architecture, Regulatory Data-Handling, and Compliance Profiles

* **Status:** Accepted
* **Date:** 2026-08-18
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
Clinical documents managed inside the electronic Trial Master File (eTMF) and electronic Investigator Site File (eISF) frequently contain sensitive unblinded clinical records, personally identifiable information (PII), or protected health information (PHI). To ensure GxP data integrity and satisfy clinical data privacy laws, these documents must be de-identified before being accessed by external regulatory inspectors or published to public databases.

We need to formalize a robust, secure, and auditable server-side document redaction architecture. This decision covers the de-identification engine placement, regex-based detection, cryptographic manifest signing, version preservation, compliance profile mapping, and defines what remains strictly out of scope.

This decision directly implements requirements under **PRD-TMF-005** and **Trace-12**.

## 2. Decision Drivers & Constraints
* **Regulatory Compliance**: Align with FDA 21 CFR Part 11, GAMP 5, GDPR (EU General Data Protection Regulation), HIPAA (US Health Insurance Portability and Accountability Act), and EU CTR (European Union Clinical Trials Regulation).
* **Auditing & Traceability**: Maintain a complete, unalterable GxP historical record of every redaction operation, operator, change justification, and the resulting cryptographic manifest signature.
* **Identity Blinding**: Strictly avoid leaking raw sensitive identifiers or matched clinical subject names in audit trails, metadata, logs, or cryptographic manifests.
* **Scope Isolation (Excluded Tooling)**:
  * **Visual PDF/Image Redaction**: Intentionally excluded from the platform scope. We target text-based and indexed clinical verbatims/structured narrative data rather than performing pixel-level or OCR processing.
  * **Machine Learning / Named Entity Recognition (NER)**: Intentionally excluded. To ensure absolute repeatability, auditable determinism, and high execution speed in a GxP validated state, we reject stochastic AI/NER models in favor of curated, compiled regexes and custom term lookup tables.

## 3. Options Considered
### Option 1: Client-Side Redaction & Metadata Stripping
* **Overview**: Perform regex scanning and word masking directly within client frontend applications (Vue 3 browser clients) and submit only the final sanitized text to the eTMF backend.
* **Pros**:
  * Offloads compute overhead from server-side databases.
* **Cons**:
  * Violates central validation principles; hard to enforce consistent de-identification rules.
  * High risk of sensitive data leakage if client applications fail or bypass validations.
  * Difficult to audit and sign the provenance of redacted derivatives.

### Option 2: Centralized, Server-Side Sanitization with Shared Package (`packages/deid`) (Selected)
* **Overview**: Place all core de-identification, scanning, and transform logic in a dedicated shared Python package (`packages/deid`). The eTMF service exposes authenticated `/auto-redact` and `/manual-redact` API routes that invoke this package, apply transforms, persist redacted derivatives as separate versioned documents, and generate symmetrically signed cryptographic manifests.
* **Pros**:
  * Guarantees identical compliance rules are applied across all services and environments.
  * Supports robust role-based access control (blocking read-only auditor/inspector personas from accessing original files once a redacted derivative exists).
  * Enables symmetric HMAC-SHA256 signature sealing of redacted provenance manifests.
  * Preserves unredacted originals for authorized research or sponsor personnel.
* **Cons**:
  * Slight server-side CPU overhead to execute compiled regex patterns over large text blocks.

## 4. Decision Outcome
* **Chosen Option**: Option 2 (Centralized Server-Side Sanitization with Shared Package).
* **Justification**: Enforcing de-identification at the API gateway/service layer provides a secure regulatory boundary, preventing accidental PII/PHI leakage into operational databases or logs while maintaining full 21 CFR Part 11 audit trails.

### Architectural Blueprint & Decisions

#### A. Shared Package Placement
De-identification logic is maintained in `packages/deid` to prevent code duplication between `apps/etmf`, `apps/eisf`, and CLI scanning hooks.

#### B. Regex-Based & Literal Terms Detection
The engine performs deterministic pattern detection using compiled, curated regular expressions and literal word matches with safe boundaries.
- Resolves overlapping character span offsets by ascending start offset and descending length, prioritizing longer/wider matches (e.g., matching a full URL rather than a sub-string).

#### C. Non-Destructive Version Preservation
Raw unredacted documents are never overridden or modified. Instead:
- Redactions write a new successor document record incrementing the `version_index`.
- The redacted derivative points to the raw source version using a `redaction_source_id` field.
- Regulatory inspectors/auditors are restricted to viewing only the redacted derivative. Raw original views are blocked with HTTP 403 Forbidden.

#### D. Signed Cryptographic Manifests
Every redaction generates a cryptographic `RedactionManifest` recording:
- Anonymized metadata (replacement values, character span offsets, and category counts).
- Operator identity, change reason justification, and source-to-target version indexing.
- A symmetric HMAC-SHA256 signature using the workspace `REDACTION_SIGNING_SECRET` (defaulting to `"internal-gateway-secret-12345"`).

#### E. Default Date-Shift Policy
- **The Default Date Shift is exactly 365 days (1 year)**.
- Shifting dates preserves longitudinal clinical intervals (such as dosing times, visit patterns, or adverse event spans) while completely destroying actual calendar values.
- The shift value remains fully configurable (`shift_days` parameter) to accommodate clinical protocol-specific date-shifting rules.

## 5. Consequences & Trade-offs
* **Positive Impact**: Fully automated, reliable de-identification mapped to specific regulatory frameworks. Zero risk of human error leakage on automated runs.
* **Negative Impact**: Incremental CPU overhead when running regex evaluations on extensive narratives.
* **Mitigation Strategy**: The `DeidDetector` utilizes highly optimized, pre-compiled regular expressions and resolves overlapping matches before applying slicing transformations to keep execution times under 100 milliseconds per document.

## 6. Implementation & Verification
* **Affected Components**:
  - `packages/deid`: Core regex engines, transform pipelines, and manifest schema models.
  - `apps/etmf`: Exposing `/auto-redact` and `/manual-redact` endpoints, writing audit events.
* **Verification**:
  - Verified via comprehensive test coverage in `tests/test_etmf_redaction.py` and `tests/test_deidentification.py`.
  - Manual and automated redaction API responses, manifest signatures, role gates, and 21 CFR Part 11 transition audits are validated on every continuous integration run.
