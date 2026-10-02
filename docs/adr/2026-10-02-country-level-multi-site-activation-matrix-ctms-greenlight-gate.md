# ADR-2805: Country-Level Multi-Site Activation Matrix & CTMS Greenlight Gate

- **Status:** Accepted
- **Date:** 2026-10-02
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** @req:PRD-SSU-001, @req:PRD-SSU-002, @req:PRD-SSU-003

---

## 1. Context & Problem Statement

CTMS study startup requires country-level multi-site activation evaluation and 21 CFR Part 11 batch certification across study sites (@req:PRD-SSU-001). Persistent site greenlight gate state must be tracked in EF Core.

## 2. Decision Drivers & Constraints

- **Driver 1:** Country-level activation matrix evaluation across 6 operational readiness facets (@req:PRD-SSU-001).
- **Driver 2:** Persistent EF Core storage of greenlight gate records (`CtmsSiteGreenlightGate`) in CTMS PostgreSQL database (@req:PRD-SSU-002, @req:PRD-SSU-003).

## 3. Options Considered

### Option 1: CtmsSiteGreenlightGate EF Core Persistence & Batch Certification (Selected)

- **Overview:** Add `CtmsSiteGreenlightGate` domain model, EF Core mapping, and database migration in `apps/ctms`.
- **Pros:**
  - ✅ Persistent 6-facet site readiness and batch certification tokens
  - ✅ Full audit trail and GxP compliance
- **Cons:**
  - ❌ Requires database migration in `apps/ctms`

### Option 2: Transient In-Memory Evaluation

- **Overview:** Compute matrix state on demand without database persistence.
- **Pros:** No database schema changes needed
- **Cons:** Fails to track historical certification audit trails

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Persistent database storage in CTMS guarantees 21 CFR Part 11 compliance and auditability.

## 5. Consequences & Trade-offs

- **Positive Impact:** Full auditability and 21 CFR Part 11 compliance for site greenlight activation.
- **Negative Impact / Technical Debt:** Additional database table `ctms_site_greenlight_gates`.
- **Mitigation Strategy:** EF Core migrations and unit tests in `StartupGreenlightEndpointsTests.cs`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/ctms`, `apps/web`
- **Verification Plan:** `StartupGreenlightEndpointsTests.cs` and `MigrationUnitTests.cs`.
