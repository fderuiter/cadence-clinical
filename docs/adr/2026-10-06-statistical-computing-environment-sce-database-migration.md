# ADR-2809: Statistical Computing Environment (SCE) Database Migration

- **Status:** Accepted
- **Date:** 2026-10-06
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The Statistical Computing Environment (SCE) module in `apps/interop` introduces persistence schema support for statistical programs, database lock tokens, execution run records, and biostat export audit logs (PRD-SYS-001).

## 2. Decision Drivers & Constraints

- **Driver 1:** Support SCE execution run tracking and statistical package lockfile validation in the Interop service.
- **Driver 2:** Maintain EF Core migration history and schema consistency in compliance with GxP audit guidelines.

## 3. Options Considered

### Option 1: EF Core Migration for SCE Entities (Selected)

- **Overview:** Add an EF Core migration (`SceEntities`) to `apps/interop` mapping `StatisticalProgram`, `DatabaseLockAuthToken`, `ExecutionRunRecord`, and `BiostatExportAuditLog` entities to PostgreSQL tables.
- **Pros:**
  - ✅ Provides deterministic, version-controlled database schema migrations.
  - ✅ Integrates with GxP audit interceptors and PostgreSQL schema documentation tools.
- **Cons:**
  - ❌ Requires database migration deployment during service rollout.

### Option 2: Unmigrated Model Definitions

- **Overview:** Define EF Core entity mappings without generating corresponding EF Core migration files.
- **Pros:**
  - ✅ No migration files needed initially.
- **Cons:**
  - ❌ Fails migration unit tests and causes runtime database schema drift.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Adding the `SceEntities` EF Core migration in `apps/interop` ensures full database schema synchronization and passes all automated migration quality gates.

## 5. Consequences & Trade-offs

- **Positive Impact:** Accurate, versioned persistence schema for SCE entities in the Interop database.
- **Negative Impact / Technical Debt:** Requires running EF Core migrations on Interop DbContext upon deployment.
- **Mitigation Strategy:** Automated migration unit tests verify no pending model changes exist without migrations.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/interop`
- **Verification Plan:** Unit tests in `MigrationUnitTests.cs` and `pnpm cadence check` quality gates.
