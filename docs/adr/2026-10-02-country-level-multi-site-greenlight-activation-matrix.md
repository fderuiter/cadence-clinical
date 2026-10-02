# ADR-2805: Country-Level Multi-Site Greenlight Activation Matrix

- **Status:** Accepted
- **Date:** 2026-10-02
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Site Startup (SSU) operations in CTMS require country-level oversight to evaluate 6-facet greenlight readiness across trial sites and execute 21 CFR Part 11 compliant batch greenlight activations with cryptographic HMAC-SHA256 signatures.
@req:PRD-SSU-001
@req:PRD-SSU-002
@req:PRD-SSU-003

## 2. Decision Drivers & Constraints

- **21 CFR Part 11 Batch Certification:** Batch activations require individual HMAC-SHA256 audit tokens per site.
- **Relational Model & Auditability:** Greenlight readiness gates are persisted in PostgreSQL (`ctms_site_greenlight_gates`) with immutable audit trails.

## 3. Options Considered

### Option 1: Relational Persistence with Minimal API Endpoints (Selected)

- **Overview:** Persist site greenlight gates in `CtmsDbContext` and expose REST Minimal API endpoints in `StartupGreenlightEndpoints`.
- **Pros:**
  - ✅ Strongly-typed EF Core persistence with audit attribution.
  - ✅ High throughput batch greenlight certification.
- **Cons:**
  - ❌ Requires database migration for new gate fields.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Meets all GxP site startup requirements with full auditability and test coverage.

## 5. Consequences & Trade-offs

- **Positive Impact:** Enables country-level site startup tracking and batch certification.
- **Negative Impact / Technical Debt:** Additional database table and migration maintenance.
- **Mitigation Strategy:** Comprehensive unit and integration test coverage.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/ctms`
- **Verification Plan:** Verified via `dotnet test` and `pnpm cadence check`.
