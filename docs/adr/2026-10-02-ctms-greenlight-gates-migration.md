# ADR-2804: CTMS Site Greenlight Gates Persistence Migration

- **Status:** Accepted
- **Date:** 2026-10-02
- **Authors:** @google-labs-jules[bot]
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The CTMS service introduced `CtmsSiteGreenlightGate` domain entities for country-level multi-site greenlight activation matrix and batch certification. An EF Core relational migration is required to align `CtmsDbContext` with the domain model and prevent model snapshot drift.
@req:PRD-SSU-001

## 2. Decision Drivers & Constraints

- **Driver 1:** Enforce schema synchronization across CTMS relational persistence tables (`ctms_site_greenlight_gates`).
- **Driver 2:** Compliance with GxP audit trail requirements and EF Core model validation sentinel gates.

## 3. Options Considered

### Option 1: Automatic In-Memory Model Generation

- **Overview:** Allow EF Core to auto-create schema on startup without explicit migrations.
- **Pros:** No migration files generated.
- **Cons:** Violates GxP database migration determinism and fails sentinel model tests.

### Option 2: Explicit EF Core Migration (`GreenlightGates`) - Selected

- **Overview:** Generate explicit `20261002190931_GreenlightGates` EF Core migration for `CtmsDbContext`.
- **Pros:** Deterministic schema evolution, full audit trail traceability.
- **Cons:** Requires migration file tracking under `apps/ctms/src/Infrastructure/Persistence/Migrations/`.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Guarantees deterministic database schema migration and passes EF Core model snapshot sentinels.

## 5. Consequences & Trade-offs

- **Positive Impact:** Relational persistence for CTMS country greenlight gates.
- **Negative Impact / Technical Debt:** None.
- **Mitigation Strategy:** Verified via `MigrationUnitTests`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/ctms`
- **Verification Plan:** Verified via `dotnet test` and `gxp sync`.
