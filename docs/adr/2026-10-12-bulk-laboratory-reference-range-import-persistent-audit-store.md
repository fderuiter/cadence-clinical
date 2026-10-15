# ADR-2815: Bulk Laboratory Reference Range Import & Persistent Audit Store

- **Status:** Accepted
- **Date:** 2026-10-12
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

In clinical trial execution (`apps/execution`), laboratory reference ranges define normal limits, critical bounds, and demographics for safety evaluation and CTCAE toxicity grading. Previously, reference ranges had to be manually entered item-by-item, which was error-prone and inefficient for studies with hundreds of laboratory analytes. An automated, GxP-compliant bulk reference range import workflow was needed to support CSV and XLSX file formats with pre-flight dry-run validation, automatic LOINC catalog mapping, UCUM SI unit conversion, and persistent 21 CFR Part 11 audit job logging. Reference requirements PRD-SYS-121 and SRS-EDC-001.

## 2. Decision Drivers & Constraints

- **21 CFR Part 11 Regulatory Auditability:** Mandatory `X-Change-Reason` header validation and `IGxPAuditable` version tracking on bulk import operations (PRD-SYS-121).
- **Pre-Flight Validation:** Asynchronous or synchronous dry-run preflight preview to detect formatting, range boundary, or unit errors before database mutation (SRS-EDC-001).
- **UCUM SI Normalization & LOINC Mapping:** Support automatic unit conversion (e.g. `mg/dL` to `umol/L`) and LOINC code lookup.
- **Relational Integrity & Multi-Tenancy:** Schema isolation under PostgreSQL `bulk_lab_range_import_jobs` and `lab_reference_ranges` in `ExecutionDbContext`.

## 3. Options Considered

### Option 1: Native Execution Microservice Bulk Import Engine with Persistent Import Job Ledger (Selected)

- **Overview:** Implement native C# `BulkLabRangeFileParser`, pre-flight `DryRunAsync`, and transactional `CommitImportAsync` backed by `bulk_lab_range_import_jobs` and `lab_reference_ranges` PostgreSQL tables in EF Core.
- **Pros:**
  - ✅ Satisfies requirements PRD-SYS-121 and SRS-EDC-001.
  - ✅ Atomic transactional EF Core commits with Part 11 change justification logging.
  - ✅ Dry-run preview returning structured row previews and validation errors before database mutation.
  - ✅ In-memory active range cache invalidation and pre-computation.
- **Cons:**
  - ❌ Requires database migration for import job tracking.

### Option 2: Client-Side Only Parsing & Direct Single Entity Posts

- **Overview:** Parse CSV/XLSX in browser memory and issue individual REST requests for each row.
- **Pros:**
  - ✅ No backend job persistence needed.
- **Cons:**
  - ❌ Lacks Part 11 transactional batch audit records.
  - ❌ Fails to fully address requirements PRD-SYS-121 or long-term maintainability.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Chosen because it satisfies PRD-SYS-121 and SRS-EDC-001 while guaranteeing 21 CFR Part 11 audit compliance, performance, and transactional safety for bulk clinical reference range operations.

## 5. Consequences & Trade-offs

- **Positive Impact:** Rapid onboarding of central/local lab reference ranges with automatic UCUM unit normalization and LOINC mappings (PRD-SYS-121).
- **Negative Impact / Technical Debt:** Maintenance overhead of additional structured abstractions and database schema tables (`bulk_lab_range_import_jobs`).
- **Mitigation Strategy:** Automated test coverage and CI lint validation; EF Core PostgreSQL migrations managed via `scripts/manage_migrations.sh`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/execution`, `apps/web`
- **Verification Plan:** Verified via `dotnet test tests/Cadence.UnitTests` (`LabRangeImportServiceTests`), `Cadence.ArchitectureTests`, and `pnpm --filter web exec vitest run` (`LabGovernanceConsole.spec.ts`).
