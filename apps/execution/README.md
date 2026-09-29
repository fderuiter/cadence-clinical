# Execution Microservice

## Overview

Clinical application microservice for `execution` within the Cadence Clinical platform.

---

## Architectural Boundaries

Implements clinical domain services and application workflows within `apps/execution/`.
- **GxP Data Isolation:** Operates within isolated domain boundaries without direct cross-service database access.
- **Gateway Interception:** All client traffic is routed via the API Gateway for authentication, rate-limiting, and audit logging.

---

## Public APIs / Exports

Service routes and contract interfaces:
- `HTTP` endpoints under `/api/v1/execution`

---

## Errors and Concurrency

- Domain errors come back as RFC 7807 ProblemDetails with a `code` extension. `DomainErrorHttpMapping` sets the status.
- Writes use optimistic concurrency: each aggregate row carries a `_version` token. When another writer changed or
  deleted the row after it was read, the save is rejected rather than retried over their change.
  `ConcurrencyConflictExceptionHandler` returns **409 Conflict** with `code: CONCURRENCY_CONFLICT`, and nothing
  from the rejected save is written or audited. Clients re-read and re-apply (ADR-2448).
- Only transient database failures (serialization failures, deadlocks, dropped connections) are retried, by the
  Polly pipeline in `SaveChangesResiliencePipelineFactory`, and never inside an explicit transaction.
- Every change is audited by `GxPAuditInterceptor` in the same save (ADR-2446), attributed to the caller or a
  named `svc:` service account (ADR-2257).

---

## Dependencies

Interacts with core platform capabilities and shared libraries in `packages/`.

---

## Local Development & Testing

Run the execution unit tests (set `CADENCE_TEST_POSTGRES` to a PostgreSQL connection string to include the
PostgreSQL integration tests):

```bash
dotnet test tests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Execution"
```
