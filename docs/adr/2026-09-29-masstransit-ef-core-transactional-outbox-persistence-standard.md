# ADR-2248: MassTransit EF Core Transactional Outbox Persistence Standard

- **Status:** Accepted
- **Date:** 2026-09-29
- **Authors:** @fderuiter
- **Deciders:** @fderuiter


---

## 1. Context & Problem Statement

Platform microservices (such as eConsent and Payments) persist state changes to PostgreSQL without transactional outbox protection, risking event loss or dual-write inconsistencies during transport failures. Standardizing MassTransit EF Core transactional outbox persistence across all event-producing microservices guarantees reliable domain event delivery for clinical workflows.

## 2. Decision Drivers & Constraints

- **Driver 1:** Centralize MassTransit transactional outbox extension methods (`AddCadenceTransactionalOutbox` and `AddCadenceTransactionalOutboxEntities`) inside the shared `Cadence.Database` package.
- **Driver 2:** Guarantee zero message loss and atomic PostgreSQL commits during simulated transport outages across microservices (PRD-SYS-001, PRD-SYS-053).
- **Driver 3:** Enforce consistent outbox entity mappings (`InboxState`, `OutboxMessage`, `OutboxState`) across all microservice DbContext configurations.

## 3. Options Considered

### Option 1: Centralized MassTransit Outbox Extensions in Cadence.Database (Selected)

- **Overview:** Define `AddCadenceTransactionalOutbox` and `AddCadenceTransactionalOutboxEntities` extension methods in `Cadence.Database` and configure them across microservice DbContext models and Program.cs composition roots.
- **Pros:**
  - ✅ Satisfies requirement PRD-SYS-001
  - ✅ Standardizes PostgreSQL outbox polling and retry parameters across all microservices
  - ✅ Ensures atomic state mutations and domain event staging in single database transactions
- **Cons:**
  - ❌ Requires EF Core schema migrations for outbox tables across microservice persistence layers

### Option 2: Ad-Hoc Service-Level Outbox Configurations

- **Overview:** Duplicate MassTransit outbox registration and entity mapping code inside each microservice.
- **Pros:**
  - ✅ No shared package extensions
- **Cons:**
  - ❌ Fragmented configuration, risk of inconsistent retry and polling policies across microservices

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Centralizing MassTransit outbox registration in `Cadence.Database` enforces platform-wide durability standards, reduces boilerplate, and guarantees atomic transactional outbox behavior.

## 5. Consequences & Trade-offs

- **Positive Impact:** Guarantees atomic domain event delivery without dual-write drift across microservices during bus transport outages.
- **Negative Impact / Technical Debt:** Microservice PostgreSQL schemas include MassTransit outbox tables (`inbox_state`, `outbox_message`, `outbox_state`).
- **Mitigation Strategy:** Automated integration tests and EF Core schema migrations verified in CI pipelines.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `Cadence.Database`, `apps/econsent`, `apps/payments`, `apps/execution`, `apps/safety`, `apps/org`.
- **Verification Plan:** Unit and integration tests in `tests/Packages/DatabaseTransactionalOutboxTests.cs` and sentinel quality gate verification via `dotnet cadence check`.
