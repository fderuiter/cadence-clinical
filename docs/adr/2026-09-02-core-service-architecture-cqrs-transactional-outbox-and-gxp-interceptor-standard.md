# ADR-2428: Core Service Architecture CQRS Transactional Outbox and GxP Interceptor Standard

Also known as: ADR-2234 (former number)

- **Status:** Accepted
- **Date:** 2026-09-02
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

As Cadence Clinical transitions to .NET 10 (C# 14 / ASP.NET Core 10) under ADR-2233, each microservice requires a deterministic, standardized internal design pattern. Clinical research operations involve complex regulatory validations, transactional integrity requirements (21 CFR Part 11 / ICH GCP E6), and asynchronous event distribution without dual-write data loss.

This decision record establishes the mandatory internal architecture, request dispatching, event outbox, error-handling, and persistence-level audit standards across all Cadence backend services under requirement `PRD-SYS-053`.

---

## 2. Decision Drivers & Constraints

- **21 CFR Part 11 Compliance (PRD-SYS-053):** Regulatory guarantees against data tampering, non-repudiation, and transactional outbox reliability.
- **Native AOT Compilation & Execution Performance:** Sub-millisecond response times, zero reflection bottlenecks, and minimal binary footprint.
- **Developer Ergonomics & Transparent Stack Traces:** F12 / Go-to-definition navigation, high cohesion within services, and clear OpenTelemetry distributed spans.
- **Solution Maintainability:** Fast build times across 20+ microservices without excessive assembly sprawl.

---

## 3. Options Considered & Settled Architecture

### Option 1: Service Internal Layering: Single-Assembly Clean Architecture

- **Decision:** Each microservice resides in a single assembly (`Cadence.Apps.<Service>.csproj`) strictly organized by namespaces:
  - `Cadence.Apps.<Service>.Domain`: Aggregates, entities, value objects, domain invariants, repository ports.
  - `Cadence.Apps.<Service>.Application`: Use case services, commands, queries, DTOs, FluentValidation rules.
  - `Cadence.Apps.<Service>.Infrastructure`: EF Core `DbContext`, repository adapters, external HTTP clients.
  - `Cadence.Apps.<Service>.Endpoints`: ASP.NET Core 10 Minimal API route definitions and filters.
- **Pros:**
  - ✅ Avoids 60+ assembly solution bloat, drastically reduces compilation times, simplifies Native AOT trimming.
  - ✅ Maintains clean dependency boundaries enforced by static architecture sentinels.
- **Cons:**
  - ❌ Requires strict namespace discipline within a single assembly.

### Option 2: Request Dispatch: Direct Typed Application Services

- **Decision:** Endpoints directly inject typed domain application services (e.g. `ISubjectApplicationService`) via primary constructors, rather than utilizing in-process reflection-based mediators (MediatR). Cross-cutting concerns (authentication, audit tracing) are applied via ASP.NET Core `IEndpointFilter`s.
- **Pros:**
  - ✅ Ensures 100% Native AOT compatibility, zero reflection overhead, direct IDE navigation, and transparent call stacks.
- **Cons:**
  - ❌ Replaces magic mediator dispatch with explicit endpoint-to-service dependencies.

### Option 3: Cross-Service Event Spine: MassTransit + PostgreSQL Transactional Outbox

- **Decision:** All cross-service domain events are committed to PostgreSQL using MassTransit's EF Core Transactional Outbox pattern within the same database transaction that persists entity mutations.
- **Pros:**
  - ✅ Eliminates dual-write vulnerability; mathematically impossible for clinical data to persist without scheduled event delivery.
- **Cons:**
  - ❌ Requires outbox polling and event relay worker infrastructure.

### Option 4: Error Handling: Strongly-Typed `Result<T, DomainError>` Monad

- **Decision:** Expected domain validation failures return `Result<TValue, DomainError>` records. Minimal API endpoints pattern-match on results to return `TypedResults.Ok(value)` or `TypedResults.Problem(error.ToProblemDetails())`.
- **Pros:**
  - ✅ Zero exception-throwing allocation overhead on high-frequency clinical validations.
- **Cons:**
  - ❌ Requires explicit result matching in endpoint handlers.

### Option 5: GxP Audit Trail: EF Core `SaveChangesInterceptor` with SHA-256 Merkle Chaining

- **Decision:** Implement `GxPAuditInterceptor` (`ISaveChangesInterceptor`) in EF Core. Before committing, the interceptor automatically populates `CreatedAt`, `CreatedBy`, `ReasonForChange`, `VersionIndex`, computes a SHA-256 hash digest chained to the previous audit entry, and writes to an append-only `AuditLogEntry` table.
- **Pros:**
  - ✅ Guarantees tamper-evident audit records at the persistence boundary across all mutating operations without relying on fragile database triggers.
- **Cons:**
  - ❌ Interceptor computation adds minimal latency during `SaveChanges` calls.

---

## 4. Decision Outcome

**Chosen Option:** The unified "Golden Stack" combining Single-Assembly Clean Architecture, Direct Typed Application Services, MassTransit Transactional Outbox, `Result<T>` Monad with RFC 7807 `ProblemDetails`, and `GxPAuditInterceptor`.

---

## 5. Consequences & Trade-offs

- **Positive:**
  - Fast, modular builds across all services.
  - Provable 21 CFR Part 11 transactional integrity and Merkle-chained audit logs.
  - Zero reflection overhead for maximum Native AOT throughput.
  - Deterministic RFC 7807 error responses across all APIs.
- **Negative:**
  - Developers and AI agents must adhere strictly to namespace partitioning rather than relying on project boundaries.
- **Mitigation:**
  - Automated Roslyn and ArchUnitNET sentinels enforce namespace boundary rules during build and CI verification.

---

## 6. Implementation & Verification

- Target files: Core primitives implemented in `packages/engine_core/src/` (`Result.cs`, `DomainError.cs`, `AuditLogEntry.cs`, `GxPAuditInterceptor.cs`).
- Verification: Verified via unit test suite in `tests/Cadence.UnitTests/Cadence.UnitTests.csproj` with traceability tags (`PRD-SYS-053`).
