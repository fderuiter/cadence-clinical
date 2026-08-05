# ADR-112: Site-Aware Synchronized Document Deduplication

- **Status:** Accepted
- **Date:** 2026-08-26
- **Authors:** @jules
- **Deciders:** @engineering_leads

---

## 1. Context & Problem Statement

In the hybrid clinical systems landscape, the electronic Trial Master File (eTMF) acts as the central regulatory repository, while the electronic Investigator Site File (eISF) serves local site operations. Bidirectional synchronization between these services ensures that documents compiled locally at clinical sites (e.g. CVs, Form 1572s, Delegation of Authority logs) are safely propagated to the eTMF, and vice versa.

Prior to this specification, the eTMF receiving contract lacked site-aware correlation and content-level verification capabilities, leading to potential duplicate versions of synchronized records and failure to preserve critical provenance / metadata. Additionally, there was a risk of re-introducing raw, unredacted content if a synchronized document was replayed after a redact-derivative had already been established to protect PII/PHI.

## 2. Decision Drivers & Constraints

- **Driver 1:** PRD-SYS-001 | GxP 21 CFR Part 11 Compliance
- **Driver 2:** Durable Duplicate Prevention
- **Driver 3:** Privacy Protection (PII/PHI sanitization and derivative protection)

## 3. Options Considered

### Option 1: Naive Version Bumping

- **Overview:** Ingest and increment version index on every synchronization request.
- **Pros:**
  - ✅ Simple to implement.
- **Cons:**
  - ❌ Bloats storage and version history with identical copies.

### Option 2: Stable Correlation Key and Content-level Verification

- **Overview:** Implement stable correlation identity and content checksum-based deduplication with redaction-derivative coordination.
- **Pros:**
  - ✅ Eliminates storage bloat from replayed synchronization events.
  - ✅ Ensures unredacted content cannot overwrite redacted derivatives.
- **Cons:**
  - ❌ Slightly increased lookup overhead.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Option 2 meets all security, compliance, and storage efficiency drivers.

## 5. Consequences & Trade-offs

- **Positive Impact:** Durable duplicate prevention, absolute data traceability, and privacy gating.
- **Negative Impact / Trade-offs:** Minor database lookup during ingestion.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/etmf`, `apps/eisf`
- **Verification Plan:** Verified via e2e boundary tests in `tests/test_etmf_sync_provenance.py`.
