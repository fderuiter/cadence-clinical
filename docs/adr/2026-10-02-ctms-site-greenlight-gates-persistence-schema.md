# ADR-2807: CTMS Site Greenlight Gates Persistence Schema

- **Status:** Accepted
- **Date:** 2026-10-02
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Site Startup (SSU) and Country-Level Multi-Site Greenlight Activation Matrix workflows in CTMS require persistent storage of site greenlight gates (`ctms_site_greenlight_gates`) to track readiness percentages, facet compliance states (regulatory IRB, CTA budget, eISF baseline, DOA log, staff training, IP release), certification metadata, and sponsor/medical overrides (PRD-SSU-001, PRD-SSU-002, PRD-SSU-003). An EF Core migration was needed to align `CtmsDbContext` with the domain model.

## 2. Decision Drivers & Constraints

- **Driver 1:** Provide persistent relational storage for site greenlight activation gates in CTMS PostgreSQL persistence layer (`apps/ctms/src/Infrastructure/Persistence`).
- **Driver 2:** Maintain full GxP auditability and requirement traceability (PRD-SSU-001) for greenlight certification and override justifications.
- **Driver 3:** Ensure zero pending model changes in `CtmsDbContext` during EF Core migration verification tests.

## 3. Options Considered

### Option 1: Dedicated EF Core Migration for `ctms_site_greenlight_gates` (Selected)

- **Overview:** Generate an EF Core migration (`AddCtmsSiteGreenlightGates`) creating the `ctms_site_greenlight_gates` table with indexed lookups on `StudyId`, `SiteId`, `CountryCode`, and composite `(StudyId, CountryCode)`.
- **Pros:**
  - ✅ Relational persistence for greenlight readiness and certification state (PRD-SSU-001).
  - ✅ Indexed fields for fast country matrix and site-level greenlight queries.
  - ✅ Satisfies `MigrationUnitTests` verification gate across all DbContexts.
- **Cons:**
  - ❌ Schema evolution requirement managed via standard EF Core migration pipeline.

### Option 2: Transient In-Memory State

- **Overview:** Store greenlight gate states in memory without database persistence.
- **Pros:**
  - ✅ No schema migration needed.
- **Cons:**
  - ❌ Fails GxP persistence, audit trail, and regulatory requirements (PRD-SSU-001).

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Option 1 ensures relational persistence and auditability for CTMS site greenlight activation gates while satisfying EF Core model verification gates.

## 5. Consequences & Trade-offs

- **Positive Impact:** Relational persistence, indexed queries, and full auditability for site greenlight gates in CTMS.
- **Negative Impact / Technical Debt:** Additional table and migration maintenance in CTMS persistence layer.
- **Mitigation Strategy:** Automated EF Core model change detection and integration tests in CI/CD pipeline.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/ctms`
- **Verification Plan:** `dotnet test tests/Cadence.UnitTests` and `pnpm cadence check`.
