# ADR-2664: Enforce Default GxP Audits, Step-Up Electronic Signatures, and Attribution Sentinels Across Standalone Services

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001, PRD-SYS-003, PRD-SUB-001, PRD-SUB-005, PRD-SUB-006

---

## 1. Context & Problem Statement

Thirteen standalone services called `CadenceDatabaseExtensions.AddCadencePostgresDbContext` without attaching `GxPAuditInterceptor`, leaving per-aggregate SHA-256 `AuditLogEntry` hash chains empty and dropping `[NotMapped]` domain events queued on `StatefulAggregateRoot.OutboxMessages` unless a service manually wired an interceptor and mapped `transactional_outbox`. At the same time, 16 regulated sign-off and unblinding/withdrawal routes across `ctms`, `imaging`, `payments`, `interop`, and `execution` lacked `RequireElectronicSignature` / `PostgresSigTokenUseStore` verification, and `AmbientAuditContextAccessor` in `apps/execution` defaulted missing `X-Change-Reason` headers to `"system_operation"` rather than rejecting unattributed mutations before handler execution.

## 2. Decision Drivers & Constraints

- **Default Tamper-Evident Audit Chains & Outbox Atomicity (ADR-2446, PRD-SYS-003):** Every standalone `DbContext` mapping `AuditLogEntry` must automatically write SHA-256 Merkle-chained `AuditLogEntry` blocks, flush `IStatefulAggregate.OutboxMessages` to `TransactionalOutboxMessage` in the same `SaveChangesAsync` transaction, and enforce PostgreSQL `KA001` insert-only triggers (`BEFORE UPDATE OR DELETE` and `BEFORE TRUNCATE`) without referencing `Cadence.Kernel.Persistence` (`K-XS-2`).
- **Single-Use Step-Up Electronic Signatures Across Instances and Restarts (ADR-2515, PRD-SUB-005, PRD-SUB-006):** Every regulated startup greenlight certification, monitoring sign-off, visit letter compilation, DoA sign-off/export, imaging chair adjudication, payable/remittance sign-off, eSource reconciliation decision, CGT bedside/destruction verification, IP kit batch reconciliation, device quarantine release, and participant `UNBLINDED` / `WITHDRAWN` state transition must verify and durably consume `X-Sig-Token` via `PostgresSigTokenUseStore<TDbContext>`, while requiring `>= 50`-character clinical justifications on `UNBLINDED` and `WITHDRAWN` transitions.
- **Fail-Closed Human Attribution & Scoped Service Accounts (PRD-SYS-001):** Every mutating `Execution` route must decode RFC 8187 `X-Change-Reason` headers via `ChangeReasonHeader` and reject missing or `< 10`-character reasons with HTTP `400` before handler execution, while non-HTTP workers, MassTransit consumers, and startup seeders supply explicit `svc:` service accounts and named reasons via `IAuditActorScope`.

## 3. Options Considered

### Option 1: Per-Host Opt-In Interceptor Registration and Inline Signature/Header Checks

- **Overview:** Require each standalone `Program.cs` and endpoint handler to manually register `GxPAuditInterceptor`, `TransactionalOutboxMessageConfiguration`, `X-Sig-Token` checks, and `X-Change-Reason` parsing.
- **Pros:**
  - Smaller touch surface in shared `packages/database` and `packages/security` helpers.
- **Cons:**
  - Newly added services or endpoints silently omit audit chaining, outbox flushing, durable replay prevention, or change-reason validation unless caught manually in code review.

### Option 2: Default Compliance Wiring in `Cadence.Database` / `Cadence.Security` Backed by Forward Migrations and CI Sentinels (Selected)

- **Overview:** Attach `GxPAuditInterceptor` and register scoped `IAuditActorScope` by default in `AddCadencePostgresDbContext` and `AddCadenceInMemoryTenancy`; map `TransactionalOutboxMessageConfiguration` across all 15 standalone `DbContext`s via forward migrations; add `RequireElectronicSignatureWhen` in `ElectronicSignatureEndpointFilter` and wire `AddCadenceDurableSigTokenStore<TDbContext>()` (`ConsumedSignatureTokenConfiguration`) across `ctms`, `imaging`, `payments`, and `interop` alongside `econsent`, `safety`, `execution`, `knowledge`, and `tickets`; enforce `RequireChangeReasonHeader()` across all 33 mutating `Execution` routes and remove `"system_operation"` / `"system"` fallbacks; update `ParticipantStateChange.vue` for 50-character justification and `SignatureCaptureModal` step-up re-authentication on `UNBLINDED` and `WITHDRAWN`; and lock every invariant with `AuditAttributionSentinelTests`, `SigTokenUseStoreWiringTests`, `RouteAuthorizationTests`, and `ServiceSchemaPostgresTests`.
- **Pros:**
  - Makes audit chaining, outbox persistence, PostgreSQL `KA001` immutability, durable single-use signature verification, and 10/50-character RFC 8187 change reasons automatic and fail-closed.
  - Preserves `K-XS-2` package boundaries and applied EF Core migration history while keeping `SigTokenUseStoreStartupCheck` fail-closed outside `Development`.
- **Cons:**
  - Background consumers, hosted workers, and non-HTTP test fixtures mutating audited entities must explicitly enter an `IAuditActorScope` (`svc:*`).

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Centralizing default `GxPAuditInterceptor` wiring, `IAuditActorScope` attribution, conditional/unconditional `ElectronicSignatureEndpointFilter` route gating, `PostgresSigTokenUseStore` replay prevention, and `ChangeReasonHeaderFilter` validation in shared libraries and CI sentinels eliminates compliance gaps structurally across all 15 standalone services.

## 5. Consequences & Trade-offs

- **Positive Impact:** All 15 standalone `DbContext` compositions write verifiable SHA-256 `AuditLogEntry` chains and flush `StatefulAggregateRoot.OutboxMessages` atomically; PostgreSQL rejects `UPDATE`, `DELETE`, and `TRUNCATE` on `AuditLogEntries` / `audit_log_entries` and module audit tables with `KA001`; all signed routes reject missing or replayed `X-Sig-Token` headers with HTTP `401 REAUTHENTICATION_REQUIRED`; and mutating `Execution` routes reject missing or `< 10`-character `X-Change-Reason` headers with HTTP `400` with zero `"system_operation"` fallbacks.
- **Negative Impact / Technical Debt:** Non-HTTP background mutations that omit `IAuditActorScope` fail closed with `InvalidOperationException` / `AuditChangeReasonUnresolvedException`.
- **Mitigation Strategy:** `AuditAttributionSentinelTests`, `SigTokenUseStoreWiringTests`, `RouteAuthorizationTests`, and `ServiceSchemaPostgresTests` verify interceptor wiring, durable signature stores, endpoint metadata, attribution rules, and PostgreSQL `KA001` triggers on every CI run.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/database`, `packages/security`, all 15 standalone `DbContext` services (`ctms`, `econsent`, `eisf`, `etmf`, `execution`, `fileshare`, `imaging`, `interop`, `knowledge`, `notifications`, `org`, `payments`, `quality`, `safety`, `tickets`), `apps/web`, `docs/schema/`, `docs/openapi/`, `docs/SDLC/`, and `tests/`.
- **Verification Plan:** Verified via `Cadence.ArchitectureTests` (`AuditAttributionSentinelTests`), `Cadence.UnitTests` (`RouteAuthorizationTests`, `SigTokenUseStoreWiringTests`), `Cadence.IntegrationTests` (`ServiceSchemaPostgresTests` against PostgreSQL 18), `pnpm --filter web test`, and `pnpm cadence check --base main`.
