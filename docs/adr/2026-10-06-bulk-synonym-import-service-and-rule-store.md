# ADR-2812: Bulk Synonym Import Service & Hierarchy Rule Store

- **Status:** Accepted
- **Date:** 2026-10-06
- **Authors:** @cadence-platform
- **Deciders:** @data-management-lead, @architecture-board

---

## 1. Context & Problem Statement

In clinical trial medical coding (MedDRA and WHODrug), sponsors and data managers frequently maintain pre-approved verbatim synonym dictionaries across studies and enterprise programs. Manual entry of verbatim synonym mappings is error-prone, inefficient, and lacks automated pre-flight validation or 21 CFR Part 11 compliant audit logging. An integrated Bulk Synonym Import Service is required to ingest dictionary files (CSV/XLSX), provide pre-flight error summary reports, enforce Part 11 audit lineage, and propagate active synonym rules to medical coding queues with study-level precedence over enterprise-level rules.
@req:PRD-SYS-001

## 2. Decision Drivers & Constraints

- **Part 11 Compliance:** Require electronic signature reason for change and immutable dual-attribution audit lineage for all rule creations and deactivations.
- **Pre-flight Quality Gates:** Fail-fast validation reporting invalid CSV rows, line numbers, error descriptions, and duplicate counts prior to rule ingestion.
- **Hierarchical Scope Precedence:** Study-specific active synonym rules must override enterprise/cross-study synonym rules when auto-coding queue items.
- **Role-Based Access Control:** Ingestion and deactivation routes must be restricted to `DATA_MANAGER` and `SPONSOR_DESIGNER` roles.

## 3. Options Considered

### Option 1: Client-side Parsing & Unverified Direct Storage Ingestion

- **Overview:** Parse CSV files entirely in the web browser and post bulk JSON payload directly into database tables.
- **Pros:**
  - Fast frontend response without multipart form endpoints.
- **Cons:**
  - Bypasses server-side schema verification and streaming pre-flight checks.
  - Lacks strict 21 CFR Part 11 server-side audit logging and SHA-256 file hashing.

### Option 2: Backend Ingestion Service with EF Core Migration & Hierarchical Rule Store (Selected)

- **Overview:** Build `SynonymImportService` and `SynonymFileParser` in `Cadence.Apps.Execution` with EF Core persistence for `synonym_rules` and `bulk_synonym_import_jobs`, exposing multipart REST endpoints and frontend integration.
- **Pros:**
  - ✅ Enforces server-side pre-flight CSV parsing with 1-based line number error reporting.
  - ✅ Records immutable import job ledger with file SHA-256 hash and 21 CFR Part 11 reason for change.
  - ✅ Supports study-level vs enterprise-level scoping with study-level priority precedence.
  - ✅ Role-gated via ASP.NET Core authorization policies (`DATA_MANAGER` and `SPONSOR_DESIGNER`).
- **Cons:**
  - Requires EF Core database migration and schema documentation updates.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Provides complete GxP regulatory compliance, robust server-side validation, and deterministic auto-coding rule propagation precedence.

## 5. Consequences & Trade-offs

- **Positive Impact:** Enables Data Managers and Sponsor Designers to rapidly ingest bulk synonym dictionaries while maintaining complete audit traceability and zero invalid rule leakage.
- **Negative Impact / Technical Debt:** Requires periodic maintenance of database index performance on `(verbatim_text, dictionary_type, scope, status)`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `Cadence.Apps.Execution`, `apps/web`.
- **Verification Plan:** Verified with backend unit tests (`SynonymImportServiceTests`, `SynonymEndpointsTests`) and frontend Vitest suites (`coding_synonyms.spec.js`, `SynonymImportDrawer.spec.js`).
