# ADR-2813: Standardize MassTransit EF Core Transactional Outbox Across Core Services

- **Status:** Accepted
- **Date:** 2026-10-07
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Across Cadence core clinical microservices (`Execution`, `Notifications`, `Etmf`), domain event communication required a standardized, reliable transactional messaging pattern. Without an outbox, database state mutations and asynchronous event dispatches could become decoupled during failures, causing lost messages or uncoordinated state transitions. Furthermore, event consumers needed idempotent deduplication ledgers (`InboxState`) to prevent duplicate event delivery processing (PRD-SYS-001).

## 2. Decision Drivers & Constraints

- **Driver 1:** Atomicity of local database state mutations and domain event publications within EF Core transactions.
- **Driver 2:** Exactly-once event consumer processing semantics via MassTransit `InboxState` deduplication ledgers (PRD-SYS-001).
- **Driver 3:** C# 14 / .NET 10 solution-wide compliance and GxP audit attribution traceability (`svc:` service account identities).

## 3. Options Considered

### Option 1: Standardized MassTransit EF Core Transactional Outbox Across Services (Selected)

- **Overview:** Configure `AddEntityFrameworkOutbox` with PostgreSQL in `NotificationsDbContext` and `EtmfDbContext` alongside existing `ExecutionDbContext`, declare shared domain event records in `packages/contracts/`, and register consumers with MassTransit inbox deduplication ledgers.
- **Pros:**
  - ✅ Guarantees atomic persistence of outbox messages within local database transactions.
  - ✅ Provides automatic consumer message deduplication via `InboxState`.
  - ✅ Satisfies regulatory compliance requirement PRD-SYS-001.
- **Cons:**
  - ❌ Adds MassTransit EF Core outbox schema tables (`InboxState`, `OutboxState`, `OutboxMessage`) to target service database schemas.

### Option 2: Direct Bus Publishing Without Persistence Outbox

- **Overview:** Publish events directly to the message transport in application services without local database outbox storage.
- **Pros:**
  - ✅ Requires zero extra database migration entities.
- **Cons:**
  - ❌ Risk of dual-write failures (database commits succeeded but transport publish failed).
  - ❌ Fails PRD-SYS-001 transactional outbox compliance guarantees.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Chosen because it guarantees transactional event dispatch and exactly-once processing across core clinical services, ensuring GxP compliance and database-messaging consistency under PRD-SYS-001.

## 5. Consequences & Trade-offs

- **Positive Impact:** End-to-end reliability for cross-service events (`ClinicalQueryRaised`, `EConsentCompleted`, `EtmfDocumentFilingRequested`) with automatic deduplication.
- **Negative Impact / Technical Debt:** Additional outbox migration tables in service DbContexts.
- **Mitigation Strategy:** Automated test harness coverage using `ITestHarness` in `tests/Cadence.UnitTests`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/notifications`, `apps/etmf`, `apps/execution`, `packages/contracts`.
- **Verification Plan:** Unit tests in `tests/Cadence.UnitTests` verifying consumer idempotency, event publishing, and quality sentinels via `pnpm cadence check`.
