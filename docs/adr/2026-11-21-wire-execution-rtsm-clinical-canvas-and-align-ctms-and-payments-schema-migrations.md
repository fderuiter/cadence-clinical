# ADR-2821: Wire Execution RTSM Clinical Canvas and Align CTMS and Payments Schema Migrations

- **Status:** Accepted
- **Date:** 2026-11-21
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001, PRD-SYS-053

---

## 1. Context & Problem Statement

Two persistence and clinical execution gaps required alignment across the platform:

1. The Clinical Canvas Conduct workspace (`StudyCanvasView.vue`) needed direct wiring to the Execution service's randomization and trial supply management (RTSM) endpoints (`/api/v1/execution/studies/{studyId}/sites/{siteId}/inventory`, `/api/v1/execution/studies/{studyId}/rtsm/randomize`, `/api/v1/execution/rtsm/dispense`, `/api/v1/execution/rtsm/reconcile`, and `/api/v1/execution/rtsm/unblind`) with role-blinded treatment arm masking and mandatory audit change reasons (PRD-SYS-001).
2. Recent domain state machine changes in `Cadence.Apps.Ctms` (`TransactionalOutbox` and string enum conversions on CTMS status columns) and `Cadence.Apps.Payments` (database check constraints on payable, self-billing invoice, participant expense claim, and participant disbursement status columns) modified the EF Core models without accompanying Npgsql schema migrations, leaving `HasPendingModelChanges()` true under PRD-SYS-053 and [ADR-2447](2026-09-29-module-schemas-via-search-path-and-migration-deployment.md).

## 2. Decision Drivers & Constraints

- **Blinded Clinical Conduct & Audit Attribution (PRD-SYS-001):** Blinded site coordinators must see masked treatment arms during randomization and kit dispensation while unblinded pharmacists and Emergency Unblinding workflows receive audited reason-for-change tracking.
- **Zero Model-to-Migration Drift (PRD-SYS-053):** Every service `DbContext` (`CtmsDbContext`, `PaymentsDbContext`, and peer modules) must apply cleanly into its isolated PostgreSQL schema (`ctms`, `payments`) with zero pending EF Core model changes.

## 3. Options Considered

### Option 1: Typed OpenAPI Execution RTSM Client + Explicit Service Schema Migrations (Selected)

- **Overview:** Route Clinical Canvas RTSM operations through a typed `executionApi` client backed by `docs/openapi/execution_openapi.json`, enforce `X-Change-Reason` headers on mutating RTSM calls, and generate explicit EF Core migrations (`AddCtmsTransactionalOutboxAndEnumStatusConversions` in `apps/ctms` and `AddPaymentStatusCheckConstraints` in `apps/payments`) with synchronized schema documentation.
- **Pros:**
  - ✅ Satisfies PRD-SYS-001 and PRD-SYS-053 with end-to-end OpenAPI contract safety and schema-isolated PostgreSQL migrations.
  - ✅ Restores `MigrationUnitTests` and `ServiceSchemaPostgresTests` parity across all shared-database service schemas.
- **Cons:**
  - ❌ Adds two incremental migration artifacts that must be maintained alongside service model snapshots.

### Option 2: Suppress EF Core `PendingModelChangesWarning` or Keep Local Canvas State

- **Overview:** Suppress `RelationalEventId.PendingModelChangesWarning` in `DbContext` configuration and keep RTSM state mocked inside frontend stores.
- **Pros:**
  - ✅ Avoids generating new migration files.
- **Cons:**
  - ❌ Violates PRD-SYS-053 schema governance and leaves production PostgreSQL schemas missing the CTMS transactional outbox table and Payments status check constraints.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Explicit Npgsql migrations keep `CtmsDbContext` and `PaymentsDbContext` aligned with their PostgreSQL schemas under PRD-SYS-053, while typed OpenAPI RTSM wiring delivers audited, role-blinded supply and randomization workflows on the Clinical Canvas under PRD-SYS-001.

## 5. Consequences & Trade-offs

- **Positive Impact:** Clinical Canvas Conduct users can view site inventory, randomize subjects, dispense and reconcile kits, and execute emergency unblinding against live Execution endpoints, while CTMS and Payments migrations apply deterministically in PostgreSQL 18.
- **Negative Impact / Technical Debt:** Incremental schema migrations increase migration history length in `apps/ctms` and `apps/payments`.
- **Mitigation Strategy:** Automated enforcement via `MigrationUnitTests`, `ServiceSchemaPostgresTests`, `PaymentsStudySiteScopeTests`, and the `schema-doc` quality gate.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/execution`, `apps/web`, `apps/ctms`, `apps/payments`, `docs/schema`, `tests/Cadence.UnitTests`, `tests/Cadence.IntegrationTests`.
- **Verification Plan:** Verified via `Cadence.UnitTests`, `Cadence.IntegrationTests`, Vitest (`StudyCanvasView.spec.ts`, `RtsmPanel.spec.ts`), Playwright accessibility and browser-to-database checks, and `cadence check`.
