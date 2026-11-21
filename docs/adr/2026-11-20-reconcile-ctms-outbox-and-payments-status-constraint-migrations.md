# ADR-2642: Reconcile CTMS Transactional Outbox and Payments Status Check Constraint Migrations

- **Status:** Accepted
- **Date:** 2026-11-20
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001, PRD-SYS-053

---

## 1. Context & Problem Statement

Domain state-machine hardening in `apps/ctms` (monitoring visit lifecycle state machine plus MassTransit EF Core transactional outbox entities) and `apps/payments` (closed status check constraints across invoices, expense claims, and site payables, plus `APPROVED -> RECONCILED` invoice workflow enforcement) updated `CtmsDbContext` and `PaymentsDbContext` without corresponding EF Core schema migrations or model snapshot updates:

1. `CtmsDbContext` added `InboxState`, `OutboxMessage`, and `OutboxState` transactional outbox tables (`inbox_state`, `outbox_message`, `outbox_state`), changed `monitoring_visits.visit_status` default from `'Planned'` to `'Scheduled'`, and registered `ck_monitoring_visits_visit_status`.
2. `PaymentsDbContext` registered `ck_self_billing_invoices_status`, `ck_patient_expense_claims_status`, and `ck_site_payable_items_status` check constraints against its domain status value sets.
3. Because EF Core 10 enforces `PendingModelChangesWarning` as an error at migration time, both `MigrationUnitTests` and `ServiceSchemaPostgresTests` failed on `CtmsDbContext` and `PaymentsDbContext`.

## 2. Decision Drivers & Constraints

- **Deterministic Schema Provisioning (`PRD-SYS-053`, `ADR-2447`, `ADR-2533`):** Every `DbContext` model modification must ship with an explicit EF Core migration and synchronized `*ModelSnapshot.cs` so `Database.MigrateAsync()` succeeds cleanly against PostgreSQL 18 without runtime schema drift or `PendingModelChangesWarning` exceptions.
- **Audit & Transactional Outbox Integrity (`PRD-SYS-001`, `ADR-2814`):** CTMS monitoring visit lifecycle transitions and outbox message persistence must commit atomically within the `ctms` schema using MassTransit's standard `inbox_state`, `outbox_message`, and `outbox_state` tables.
- **Database-Enforced Domain Invariants (`PRD-SYS-001`):** Relational check constraints in `ctms` and `payments` must mirror the C# aggregate status state machines so invalid status strings cannot bypass application validation.

## 3. Options Considered

### Option 1: Suppress `PendingModelChangesWarning` in `CtmsDbContext` and `PaymentsDbContext`

- **Overview:** Configure `ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))` so existing migrations run without failing on unrecorded model deltas.
- **Pros:**
  - ✅ Avoids adding new migration files.
- **Cons:**
  - ❌ Leaves PostgreSQL deployments missing `ctms` outbox tables (`inbox_state`, `outbox_message`, `outbox_state`) and `ctms`/`payments` status `CHECK` constraints, breaking runtime persistence and violating `PRD-SYS-053`.

### Option 2: Add Explicit EF Core Migrations and Synchronized Snapshots for `CtmsDbContext` and `PaymentsDbContext` (Selected)

- **Overview:** Generate `20261121013134_AddMonitoringVisitStateMachineAndOutbox` in `apps/ctms` (backfilling legacy `'Planned'`/`'Completed'` `visit_status` rows before applying `ck_monitoring_visits_visit_status` and creating the MassTransit outbox tables/indexes) and `20261121013140_AddPaymentDomainStatusConstraints` in `apps/payments` (adding `ck_patient_expense_claims_status`, `ck_self_billing_invoices_status`, and `ck_site_payable_items_status`), update both `ModelSnapshot` classes, and align `PaymentsStudySiteScopeTests` with the `APPROVED` pre-reconciliation ERP export transition.
- **Pros:**
  - ✅ Brings compiled EF Core models, migration histories, snapshots, and PostgreSQL 18 schemas into 1-to-1 parity.
  - ✅ Preserves `PendingModelChangesWarning` fail-closed drift detection across all services.
- **Cons:**
  - ❌ Adds two incremental schema migration files to maintain in service migration histories.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Explicit migrations keep PostgreSQL schemas, transactional outbox tables, and relational status check constraints synchronized with the domain models while preserving EF Core's fail-closed `PendingModelChangesWarning` guardrail.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - `CtmsDbContext` and `PaymentsDbContext` migrate cleanly from empty and existing databases in `MigrationUnitTests` and `ServiceSchemaPostgresTests`.
  - Database-level `CHECK` constraints enforce valid lifecycle statuses for monitoring visits, self-billing invoices, patient expense claims, and site payable items.
- **Negative Impact / Technical Debt:**
  - Existing CTMS databases with historical `'Planned'` or `'Completed'` monitoring visit rows are normalized to `'Scheduled'` and `'Closed'` during migration upgrade before adding `ck_monitoring_visits_visit_status`.
- **Mitigation Strategy:**
  - Explicit `UPDATE` statements execute inside `20261121013134_AddMonitoringVisitStateMachineAndOutbox.Up` prior to `AddCheckConstraint` so upgrades on populated schemas remain idempotent and constraint-safe.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `apps/ctms` (`Infrastructure/Persistence/Migrations/20261121013134_AddMonitoringVisitStateMachineAndOutbox.cs`, `CtmsDbContextModelSnapshot.cs`)
  - `apps/payments` (`Infrastructure/Persistence/Migrations/20261121013140_AddPaymentDomainStatusConstraints.cs`, `PaymentsDbContextModelSnapshot.cs`)
  - `tests/Cadence.IntegrationTests` (`Payments/PaymentsStudySiteScopeTests.cs`)
- **Verification Plan:**
  - `MigrationUnitTests` (`tests/Cadence.UnitTests`) verifies zero pending model changes across all EF Core `DbContext` types.
  - `ServiceSchemaPostgresTests` and `PaymentsStudySiteScopeTests` (`tests/Cadence.IntegrationTests`) verify live PostgreSQL 18 migration execution, check constraint creation, and study/site-scoped invoice ERP export before reconciliation.
