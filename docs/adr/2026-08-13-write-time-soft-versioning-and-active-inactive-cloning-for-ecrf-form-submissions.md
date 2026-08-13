# ADR-2172: Write-Time Soft-Versioning and Active Inactive Cloning for eCRF Form Submissions

- **Status:** Accepted
- **Date:** 2026-08-13
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

In clinical trials governed by GxP and 21 CFR Part 11, eCRF form submission histories must be preserved immutably. When a form submission is modified during subject migration or clinical data updates, we must not overwrite existing rows. Instead, we require a write-time soft-versioning and active/inactive cloning scheme that preserves older submissions as read-only and inactive while maintaining the latest active representation. This addresses requirement PRD-EDC-005 (Real-Time Row Ingestion and Index Tracking).

## 2. Decision Drivers & Constraints

- **GxP and Part 11 Compliance:** Complete audit trail of all form submission modifications.
- **Immutability:** Past versioned rows must be protected against modifications or deletes at the database session level.
- **Performance:** Fast querying for the active version of submissions while maintaining the full history.
- **Cryptographic Integrity:** All submission records (active and inactive) must be integrated into the platform's cryptographic ledger sealing.

## 3. Options Considered

### Option 1: Hard Deletes with Event Auditing

- **Overview:** Overwrite rows in place and write audit events to a sidecar log.
- **Pros:** Simpler database schema, smaller table size.
- **Cons:** Violates strict GxP immutability for primary data tables, hard to recover old states from logs.

### Option 2: Write-Time Soft-Versioning with Inactive Cloning (Selected)

- **Overview:** Implement versioning columns (`protocol_version`, `is_active`, `is_readonly`, `cloned_from_id`) directly on `FormSubmission`. Protect inactive/read-only records via SQLAlchemy flush event listeners and automatically filter active rows in endpoints.
- **Pros:** Fully compliant, highly audit-ready, integrated into the cryptographic ledger sealer, and simple to query.
- **Cons:** Table size grows over time due to historical records, requiring proper indexing.

## 4. Decision Outcome

**Chosen Option:** Option 2. We implemented write-time soft-versioning, active/inactive cloning, read-only guards, default active query filtering, and cryptographic ledger sealing for eCRF form submissions.

### Justification

This approach meets all regulatory constraints by securing past record states via database session listeners (raising `PermissionError` on mutation of read-only/inactive rows), while keeping queries clean with a default active filter.

## 5. Consequences & Trade-offs

- **Positive Impact:** Full audit visibility, immutable past records, and seamless integration with the cryptographic ledger.
- **Negative Impact / Technical Debt:** Database size increases linearly with the number of edits; mitigated by database connection pooling and proper index optimization on `is_active`.
- **Mitigation Strategy:** Added indexes on `is_active` and `protocol_version` to keep queries highly performant.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/execution/` database models and routes.
- **Verification Plan:** Verified via `test_study_versions.py` ensuring read-only guards reject deletes and updates, default filtering works, and cloning creates the proper lineages.
