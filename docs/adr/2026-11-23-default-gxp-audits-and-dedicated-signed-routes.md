# ADR-2823: Default Standalone GxP Merkle Auditing, Durable Signature Token Stores, and Dedicated Signed Routes

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001, PRD-SYS-002, PRD-SUB-001

---

## 1. Context & Problem Statement

Standalone microservices registered through `AddCadencePostgresDbContext<TDbContext>` previously depended on each service host manually attaching `GxPAuditInterceptor`, binding non-HTTP service-account identities for background consumers and workers, and registering durable `ConsumedSignatureToken` replay stores whenever new 21 CFR Part 11 regulated routes were introduced. In addition, three clinical endpoints mixed routine unsigned operations with regulated signed actions on the same route (`PATCH /api/v1/execution/subjects/{subjectId}/state` for `UNBLINDED`, `POST /api/v1/ctms/letters/compile` with `finalize=true`, and `GET /api/v1/ctms/doa/sites/{site_id}/export-pdf`), preventing strict per-route `RequireElectronicSignature` enforcement and browser step-up re-authentication. Finally, Execution only attached `ChangeReasonHeaderFilter` to selected route groups while `AmbientAuditContextAccessor` silently fell back to `"system_operation"` when `X-Change-Reason` was absent.

## 2. Decision Drivers & Constraints

- **Fail-Closed 21 CFR Part 11 & ICH E6(R3) Audit Trails (`PRD-SYS-002`):** Every standalone PostgreSQL module schema (`ctms`, `econsent`, `eisf`, `etmf`, `execution`, `fileshare`, `imaging`, `interop`, `knowledge`, `notifications`, `org`, `payments`, `quality`, `safety`, `tickets`) must append Merkle-chained `AuditLogEntry` records and drain `IStatefulAggregate.OutboxMessages` into `TransactionalOutboxMessage` inside the same database transaction by default, without duplicating interceptors on hosts that wire custom actor callbacks or duplicating PostgreSQL `KA001` shadow triggers.
- **Attributable Non-HTTP Service Accounts:** Background MassTransit consumers, scheduled `BackgroundService` workers, and startup seeders must bind explicit `svc:*` service-account actors and non-synthetic reasons rather than relying on fallback strings.
- **Dedicated Single-Use Signed `POST` Routes (`PRD-SYS-001`, `PRD-SUB-001`):** Every regulated approval, certification, adjudication, disbursement, reconciliation, emergency unblinding, visit-letter finalization, and signed DoA PDF export action must sit on a dedicated `POST` route gated by `RequireElectronicSignature` backed by a durable PostgreSQL `ConsumedSignatureToken` replay store.
- **Universal Execution Change-Reason Enforcement:** All mutating Execution endpoints must enforce `ChangeReasonHeaderFilter` at the route-group level (with explicit `.AllowWithoutChangeReason()` opt-outs strictly for stateless validation, conversion, and dry-run helpers), and `AmbientAuditContextAccessor` must throw `AuditChangeReasonUnresolvedException` rather than returning `"system_operation"`.

## 3. Options Considered

### Option 1: Per-Service Manual Interceptor Wiring and Conditional In-Handler Signature Checks

- **Overview:** Keep `GxPAuditInterceptor` opt-in per `Program.cs` and check `X-Sig-Token` conditionally inside handlers when request flags such as `status == "UNBLINDED"` or `finalize == true` are present.
- **Pros:**
  - Avoids introducing new HTTP endpoints or OpenAPI client methods.
- **Cons:**
  - Leaves future standalone services vulnerable to silent audit-interceptor omission.
  - Hides regulated actions from declarative endpoint metadata (`ElectronicSignatureRequired`), breaking `ArchitectureDriftValidatorService` and route-level replay/action binding.

### Option 2: Default Interceptor & Outbox Draining in `AddCadencePostgresDbContext`, Durable Token Stores, Dedicated Signed `POST` Routes, and Route-Group Change-Reason Filters (Selected)

- **Overview:** Attach `GxPAuditInterceptor` by default in `AddCadencePostgresDbContext<TDbContext>` (when not already configured by the host callback) using `StandaloneAuditActorScope` for fail-closed HTTP and `svc:*` actor resolution; drain `IStatefulAggregate.OutboxMessages` across standalone contexts including `CtmsDbContext`; add `ConsumedSignatureToken` migrations and `AddCadenceDurableSigTokenStore<TDbContext>()` to `ctms`, `payments`, `imaging`, and `interop`; split emergency unblinding, letter finalization, and signed DoA PDF export onto dedicated signed `POST` endpoints; wire `SignatureCaptureModal.vue` across web clinical workflows; and enforce `ChangeReasonHeaderFilter` across all Execution route groups while removing `"system_operation"`.
- **Pros:**
  - Guarantees Merkle audit chaining and transactional outbox draining by default across all 15 standalone PostgreSQL schemas.
  - Makes every regulated action declaratively discoverable via `ElectronicSignatureRequired` metadata and protected against replay across restarts and replicas.
  - Eliminates synthetic `"system_operation"` audit reasons fail-closed.
- **Cons:**
  - Requires EF Core migrations across four services (`ctms`, `payments`, `imaging`, `interop`) and coordinated OpenAPI/TypeScript client updates for the three split signed `POST` routes.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Centralizing `GxPAuditInterceptor` registration in `AddCadencePostgresDbContext<TDbContext>`, backing all nine signature-bearing services with durable `ConsumedSignatureToken` stores, splitting mixed endpoints into dedicated signed `POST` routes, and enforcing route-group `ChangeReasonHeaderFilter` in Execution establishes defense-in-depth GxP auditability and 21 CFR Part 11 step-up signature verification across the platform.

## 5. Consequences & Trade-offs

- **Positive Impact:** Standalone services automatically emit Merkle-chained `AuditLogEntry` rows and drain domain outbox events without boilerplate; regulated actions across CTMS, Execution, Payments, Imaging, and Interop reject missing, mismatched, or replayed `X-Sig-Token` headers with HTTP `401 Unauthorized`; and web operators complete real `SignatureCaptureModal.vue` re-authentication flows.
- **Negative Impact / Technical Debt:** Callers attempting `PATCH /api/v1/execution/subjects/{subjectId}/state` with `UNBLINDED` or `POST /api/v1/ctms/letters/compile` with `finalize=true` now receive HTTP `400 Bad Request` directing them to the dedicated signed `POST` endpoints.
- **Mitigation Strategy:** Updated OpenAPI specs, generated TypeScript API clients, MSW demo handlers, and Vue clinical components (`ParticipantStateChange.vue`, `CtmsGreenlightSection.vue`, `SsuGreenlightConsole.vue`, `CtmsDelegationSection.vue`, `CtmsMonitoringSection.vue`) to invoke the dedicated signed `POST` routes.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `packages/database/src/CadenceDatabaseExtensions.cs`, `packages/database/src/StandaloneAuditActorScope.cs`
  - `apps/ctms`, `apps/execution`, `apps/payments`, `apps/imaging`, `apps/interop`, `apps/etmf`, `apps/fileshare`, `apps/knowledge`, `apps/notifications`, `apps/safety`, `apps/tickets`
  - `packages/ui/signing.js`, `apps/web/src/components/canvas/ParticipantStateChange.vue`, `apps/web/src/components/clinical/*`
  - `ARCHITECTURE.md`, `docs/SDLC/Signature_Token_Cryptographic_Contract.md`, `docs/openapi/*`, `docs/schema/*`
- **Verification Plan:**
  - `tests/Cadence.ArchitectureTests` verifies standalone `GxPAuditInterceptor` wiring, named `svc:*` service accounts, and absence of `"system_operation"` fallbacks.
  - `tests/Cadence.UnitTests/Security/SigTokenUseStoreWiringTests.cs` and `RegulatedRoutesAndExecutionChangeReasonTests.cs` verify durable `ISigTokenUseStore` wiring across all nine signature-bearing hosts, `ElectronicSignatureRequired` metadata and `401` enforcement across regulated routes, and Execution `ChangeReasonHeaderFilter` coverage.
  - `tests/Cadence.IntegrationTests/Database/StandaloneGxPAuditAndOutboxPostgresTests.cs` verifies real PostgreSQL Merkle `AuditLogEntry` persistence, single interceptor registration, and `TransactionalOutboxMessage` draining across standalone schemas.
  - `apps/web/tests/components/CtmsGreenlightAndDelegationSignatures.spec.ts` and `StudyCanvasRtsmWorkflows.spec.js` verify end-to-end `SignatureCaptureModal.vue` step-up token issuance and header propagation.
