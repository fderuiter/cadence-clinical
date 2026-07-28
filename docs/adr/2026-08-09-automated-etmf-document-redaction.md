# ADR-065: Automated eTMF Document Redaction

* **Status:** Accepted
* **Date:** 2026-08-09
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
To meet strict regulatory compliance standards (such as FDA 21 CFR Part 11 and GDPR/HIPAA), clinical documents stored in the electronic Investigator Site File (eISF) and electronic Trial Master File (eTMF) containing unblinded patient data or personally identifiable information (PII) must be de-identified before external distribution or inspection. Manually redacting these documents is error-prone, labor-intensive, and difficult to audit. We need an automated, auditable, and secure eTMF redaction pipeline that detects and masks PII/PHI categories while producing verifiable cryptographic manifests.

This decision implements requirements under Trace-5.

## 2. Decision Drivers & Constraints
* **GxP & 21 CFR Part 11 Compliance:** Every redaction operation must be fully auditable, require reason justifications, and preserve unredacted originals for authorized personnel.
* **Privacy:** The redacted output and any returned metadata or logs must never contain raw matched identifiers.
* **Role-Based Access Control (RBAC):** Inspectors and auditors are restricted to read-only access and must never see raw unredacted data if a redacted successor exists, nor perform redactions.

## 3. Options Considered
### Option 1: Client-Side Redaction Only
* **Overview:** Rely on the frontend or external clients to redact text and send the redacted text directly to the eTMF backend.
* **Pros:**
  * ✅ Offloads compute overhead from the backend.
* **Cons:**
  * ❌ No central verification or standard automated detection rules.
  * ❌ Susceptible to data leakage if client rules differ.

### Option 2: Server-Side Automated Redaction Endpoint (Selected)
* **Overview:** Implement a dedicated, authenticated `/auto-redact` endpoint under the eTMF API that loads the requested source version, executes the shared `DeidDetector` package, applies configured transforms, persists the redacted successor as a new version, and logs a tamper-evident signed manifest.
* **Pros:**
  * ✅ Guarantees consistent compliance logic across all clients.
  * ✅ Symmetrically signs the manifest, verifying integrity.
  * ✅ Supports easy policy changes (HIPAA vs. EU_CTR).
* **Cons:**
  * ❌ Higher server-side CPU utilization for pattern matching on large documents.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Implementing automated server-side redaction ensures central control over clinical PII/PHI policies. It enforces robust security boundaries, preventing silent data leakage while maintaining full 21 CFR Part 11 GxP compliance through non-sensitive database-level REDACT audit events.

## 5. Consequences & Trade-offs
* **Positive Impact:** Automated detection dramatically reduces human error and ensures proper separation of role privileges (Auditors/Inspectors see redacted content; Sponsors/CRAs see unredacted).
* **Negative Impact / Technical Debt:** Added CPU overhead during pattern scanning on large text fields.
* **Mitigation Strategy:** Bounded text limits and highly optimized regex patterns in the `DeidDetector` package.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/etmf`
* **Verification Plan:** Verified via unit and integration test suite in `tests/test_etmf_redaction.py`.
