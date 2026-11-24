# Tickets service

Support ticket service. It serves authenticated support ticket creation (`POST /api/v1/tickets`), study/site-scoped listing (`GET /api/v1/tickets`), ticket detail lookup (`GET /api/v1/tickets/{ticket_id}`), visibility-scoped discussion comments (`GET /api/v1/tickets/{ticket_id}/comments`, `POST /api/v1/tickets/{ticket_id}/comments`), workflow status transitions (`POST /api/v1/tickets/{ticket_id}/transitions`), 21 CFR Part 11 closure e-signatures (`POST /api/v1/tickets/{ticket_id}/sign`), and Merkle-chained audit trail queries (`GET /api/v1/tickets/{ticket_id}/audit-trail`) with deterministic 6-queue routing, Dual-Target SLA timestamps from `IClock`, participant PII rejection, mandatory `X-Change-Reason` headers, and shared `audit_log_entries` Merkle-chain auditing ([ADR-2641](../../docs/adr/2026-11-20-contextual-sop-resolution-and-audited-ticket-escalation.md)).

**Status:** a standalone ASP.NET Core 10 scaffold service. Under [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) and the [clinical kernel specification](../../docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md) (§12 capability map, §15 roadmap) it becomes an application module over the clinical kernel. The disposition of the scaffold services is tracked in issue #4787.

## Project

- Project: `apps/tickets/src/Cadence.Apps.Tickets.csproj` (assembly `Cadence.Apps.Tickets`).
- `Program.cs`: composition root and route mapping.
- `Domain/`: EF Core entity classes (tickets, comments, attachments, audit log).
  Every timestamp is a `DateTimeOffset`; the ones a writer must supply are `required` with no default, and the project bans `DateTime.Now/UtcNow` (K-TIME-3, #4784), so time comes from `Cadence.Kernel.Time.IClock`.
- `Infrastructure/Persistence/`: `TicketsDbContext`, a design-time `TicketsDbContextFactory`, and `Configurations/` (Fluent API mappings, including `ConsumedSignatureTokenConfiguration`).
- `Infrastructure/Persistence/Migrations/`: `20260924145604_InitialCreate`, `20261120192739_EnforceTicketStatusCheckConstraint`, `20261120210113_EnforceTenantRowLevelSecurity`, `20261122164714_AddConsumedSignatureTokens`, `20261122203820_EnforceStandaloneConcurrencyAndTemporalRevisions`.

## HTTP surface

- `GET /healthz`, `GET /healthz/live`, `GET /healthz/ready` (anonymous; mapped by `MapCadenceHealthAndStatusEndpoints` and `UseCadenceWebDefaults` in [packages/telemetry](../../packages/telemetry/README.md)). `/healthz/ready` checks `TicketsDbContext`.
- `GET /api/v1/tickets/status` (anonymous status payload).
- `POST /api/v1/tickets` (`tickets:create`, requires `X-Change-Reason`, rejects direct participant PII, derives reporter/creator strictly from verified gateway claims, enforces study/site `CallerScope`, and computes Dual-Target SLA deadlines via `IClock`).
- `GET /api/v1/tickets` and `GET /api/v1/tickets/{ticket_id}` (`tickets:read`, filtered to the caller's authorized study and site scope; supports UUID or human-readable `TKT-YYYY-NNNN` reference).
- `GET /api/v1/tickets/{ticket_id}/comments` (`tickets:read`, fails closed to `PUBLIC` notes unless the caller holds a role in `SponsorCommentCapableRoles`) and `POST /api/v1/tickets/{ticket_id}/comments` (`tickets:comment`, requires `X-Change-Reason`, scans for participant PII, and rejects `INTERNAL_SPONSOR` comments with HTTP 403 unless the caller holds `SponsorCommentCapableRoles`).
- `POST /api/v1/tickets/{ticket_id}/transitions` (`tickets:transition`, requires `X-Change-Reason`, enforces `TicketTransitionGraph`, pauses/resumes SLA targets via `IClock`, requires root-cause summary + resolution code on `RESOLVED`, and scans for participant PII).
- `POST /api/v1/tickets/{ticket_id}/sign` (`tickets:sign`, requires `X-Change-Reason` and a single-use step-up `X-Sig-Token` with semantic action `tickets.ticket.sign` backed by `tickets.consumed_signature_tokens`; locks the ticket against subsequent comments or transitions).
- `GET /api/v1/tickets/{ticket_id}/audit-trail` (`tickets:read`, returns the Merkle-chained `audit_log_entries` history for the ticket and its caller-visible comments, failing closed on `INTERNAL_SPONSOR` comment audit entries unless the caller holds `SponsorCommentCapableRoles`).
- OpenAPI and the Scalar reference UI are mapped in the Development environment only.
- The gateway forwards `/api/v1/tickets/**` to this service. Compose publishes host port 8009 (container port 8080).

## Security

`Program.cs` calls `AddCadenceSecurity()` and `UseCadenceSecurity()` from [packages/security](../../packages/security/README.md). Every route not marked anonymous requires the gateway-signed caller identity. `GATEWAY_SECRET` (at least 32 characters) is required at startup; see [ADR-2256](../../docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md).

## Persistence

`AddCadencePostgresDbContext<TicketsDbContext>()` from [packages/database](../../packages/database/README.md) connects to the shared `cadence` database with its search path set to the `tickets` schema ([ADR-2447](../../docs/adr/2026-09-29-module-schemas-via-search-path-and-migration-deployment.md)). The connection string comes from `ConnectionStrings:cadence`, then `POSTGRES_CONNECTION_STRING`; only the Development environment falls back to `cadence` on `localhost:5432`, and other environments fail at startup. `docker/docker-compose.yml` points it at the compose PostgreSQL and sets `Database__MigrateOnStartup=true`, so the service applies its migrations when it starts.

## Key dependencies

- Project references: `Cadence.Core.Domain`, `Cadence.Kernel.Abstractions`, `Cadence.Security`, `Cadence.Telemetry`, `Cadence.Database`, `Cadence.Deid`.
- Packages: `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore`, `FluentValidation` (versions in `Directory.Packages.props`).

## Tests

- Unit and endpoint tests: `tests/Cadence.UnitTests/Tickets/TicketEndpointsTests.cs` and `tests/Cadence.UnitTests/Tickets/TicketLifecycleTests.cs`.
- PostgreSQL integration tests: `tests/Cadence.IntegrationTests/Tickets/TicketPersistenceAndAuditPostgresTests.cs`.
- Architecture rules (`tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`) scan this assembly: `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`.

## Run locally

```bash
export GATEWAY_SECRET="<at least 32 characters>"
export ConnectionStrings__cadence="Host=localhost;Port=5432;Database=cadence;Username=cadence;Password=<password>"
dotnet run --project apps/tickets/src/Cadence.Apps.Tickets.csproj
```

The Aspire AppHost ([apps/apphost](../apphost/README.md)) also starts this service on port 8009, as docker compose does, with a reference to its `cadence` PostgreSQL database; it applies its migrations on start.

## Optimistic Concurrency and Immutable Revisions

- `PATCH /api/v1/tickets/{ticket_id}` updates ticket fields or status transitions under `VersionIndex` / `If-Match` optimistic concurrency (returning HTTP `409` on stale writes) and appends a numbered immutable snapshot to `ticket_revisions` (`KA001`).
- `GET /api/v1/tickets/{ticket_id}` accepts optional `revision` and `as_of` query parameters to reconstruct prior committed revisions.
