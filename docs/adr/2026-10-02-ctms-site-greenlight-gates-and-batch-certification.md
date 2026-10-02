# ADR-2805: CTMS Site Greenlight Gates and Batch Certification

- **Status:** Accepted
- **Date:** 2026-10-02
- **Authors:** @google-labs-stitch[bot]
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

In decentralized clinical trial management (CTMS) and site study startup (SSU), site activation requires verifying regulatory, ethical, and operational prerequisite gates before granting site greenlight status for subject enrollment.

## 2. Decision Drivers & Constraints

- **Regulatory Compliance:** Enforce ICH GCP E6(R2) and 21 CFR Part 11 site readiness verification before subject screening/enrollment (PRD-SYS-004, PRD-SYS-053).
- **Batch Operations:** Support country-level multi-site activation matrix evaluations and batch certification.

## 3. Options Considered

### Option 1: Ad-hoc site activation flags without persistence schema

- **Overview:** Track site readiness via informal metadata fields.
- **Pros:** Minimal schema changes.
- **Cons:** ❌ Inflexible, non-auditable, prone to regulatory non-compliance.

### Option 2: Dedicated CTMS Site Greenlight Gates schema in PostgreSQL (Selected)

- **Overview:** Add relational `ctms_site_greenlight_gates` table in PostgreSQL CTMS schema.
- **Pros:**
  - ✅ Strongly typed, auditable site activation gate tracking.
  - ✅ Supports batch certification and country-level site activation matrices.
- **Cons:**
  - ❌ Requires database migration in CTMS service.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Guarantees regulatory compliance and auditable site greenlight gate management across multi-site trial workflows.

## 5. Consequences & Trade-offs

- **Positive Impact:** Standardized, auditable site greenlight gate tracking in CTMS.
- **Negative Impact:** Schema migration `20261002120000_AddCtmsSiteGreenlightGates.cs` added to CTMS service.
- **Mitigation Strategy:** Covered by unit and integration tests in CTMS test suites.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/ctms/`
- **Verification Plan:** Verified via EF Core migration checks and CTMS unit tests.
