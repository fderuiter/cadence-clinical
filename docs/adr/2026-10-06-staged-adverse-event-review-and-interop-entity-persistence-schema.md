# ADR-2809: Staged Adverse Event Review and Interop Entity Persistence Schema

- **Status:** Accepted
- **Date:** 2026-10-06
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

As part of the continuous integration of clinical trial execution and interoperability services, EF Core entity models for staged adverse event review governance in the execution module and single clinical entry (SCE) entities in the interop module require database persistence schemas. Systems require structured, GxP-traceable database schemas to persist staged adverse event review states and interop domain entities.

Reference Requirement: PRD-SYS-001

## 2. Decision Drivers & Constraints

- **Driver 1:** Enforce auditability and schema migration tracking for staged adverse event reviews in the execution domain.
- **Driver 2:** Maintain relational database snapshot parity across execution and interop DbContexts (PRD-SYS-001).

## 3. Options Considered

### Option 1: Entity Framework Core Migrations for Execution and Interop Domains (Selected)

- **Overview:** Add EF Core migration files (`20261006193019_AddStagedAdverseEventReview` and `20261006202243_AddSceEntities`) with corresponding snapshot updates in execution and interop services.
- **Pros:**
  - ✅ Satisfies requirement PRD-SYS-001
  - ✅ Maintains exact operational boundaries and EF Core schema versioning
- **Cons:**
  - ❌ Requires explicit ADR documentation for schema evolution

### Option 2: Dynamic Unstructured Entity Storage

- **Overview:** Store staged adverse event review data in unmanaged generic JSON payload fields.
- **Pros:**
  - ✅ Avoids schema migrations
- **Cons:**
  - ❌ Violates GxP schema traceability and strict relational constraints

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Option 1 provides formal schema safety, strongly typed database mappings, and strict compliance with PRD-SYS-001.

## 5. Consequences & Trade-offs

- **Positive Impact:** Strongly typed schema definitions and versioned migrations for execution and interop domains.
- **Negative Impact / Technical Debt:** Requires maintenance of EF Core migrations and snapshot baselines.
- **Mitigation Strategy:** Automated database schema validation and CLI quality gate checking.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/execution`, `apps/interop`
- **Verification Plan:** Verified via `dotnet run --project packages/cli/src/Cadence.Cli.csproj -- check schema` and unit/integration tests.

