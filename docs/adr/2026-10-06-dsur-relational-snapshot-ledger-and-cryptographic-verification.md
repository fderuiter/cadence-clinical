# ADR-2810: DSUR Relational Snapshot Ledger and Cryptographic Verification

- **Status:** Accepted
- **Date:** 2026-10-06
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Periodic Development Safety Update Reports (DSURs) under ICH E2F and 21 CFR Part 11 require immutable point-in-time snapshots of clinical trial safety cases, reporting evaluations, and patient exposure metrics. When compile requests omit explicit safety case lists, the system must automatically aggregate cases from `SafetyDbContext` and persist snapshot records with cryptographic verification digests.

## 2. Decision Drivers & Constraints

- **Driver 1:** 21 CFR Part 11 compliance requiring immutable, point-in-time snapshots with SHA-256 payload digests and PKCS#7 signature manifests.
- **Driver 2:** Pharmacovigilance safety case aggregation requirements (PRD-PVG-004, PRD-PVG-008).

## 3. Options Considered

### Option 1: Relational Snapshot Ledger Table (`dsur_snapshots`) (Selected)

- **Overview:** Store immutable DSUR snapshot records in `SafetyDbContext` (`dsur_snapshots`) with JSONB payload, SHA-256 digest, and Part 11 signature manifests.
- **Pros:**
  - ✅ Satisfies requirements PRD-PVG-004 and PRD-PVG-008
  - ✅ Enforces snapshot immutability on approved reports via database and API guards (409 Conflict)
  - ✅ Provides endpoints for version querying and cryptographic verification
- **Cons:**
  - ❌ Requires database migration and schema documentation maintenance

### Option 2: In-Memory / Ephemeral Snapshot Compilation

- **Overview:** Generate snapshots dynamically without persisting versioned database ledger records.
- **Pros:**
  - ✅ No database migration needed
- **Cons:**
  - ❌ Fails 21 CFR Part 11 audit trail and immutability requirements

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Chosen because it provides a fully compliant 21 CFR Part 11 snapshot ledger with SHA-256 integrity verification and automated database aggregation for PRD-PVG-004 and PRD-PVG-008.

## 5. Consequences & Trade-offs

- **Positive Impact:** Cryptographically verifiably point-in-time DSUR snapshots and automated database aggregation.
- **Negative Impact / Technical Debt:** Database storage for JSONB snapshot payloads.
- **Mitigation Strategy:** Indexing on `(report_id, version_index)` and `created_at`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `Cadence.Apps.Safety`
- **Verification Plan:** Unit tests in `DsurCompilerEngineTests.cs` and platform quality gate checks.

