# Knowledge service

Knowledge base scaffold. It holds the article, article version, category, contextual help mapping and protocol knowledge chunk model; no business endpoints are mapped yet.

**Status:** a standalone ASP.NET Core 10 scaffold service. Under [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) and the [clinical kernel specification](../../docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md) (§12 capability map, §15 roadmap) it becomes an application module over the clinical kernel. The disposition of the scaffold services is tracked in issue #4787.

## Project

- Project: `apps/knowledge/src/Cadence.Apps.Knowledge.csproj` (assembly `Cadence.Apps.Knowledge`).
- `Program.cs`: composition root and route mapping.
- `Domain/`: EF Core entity classes (knowledge categories, articles and versions, contextual help mappings, protocol knowledge chunks, audit log).
  Every timestamp is a `DateTimeOffset`; the ones a writer must supply are `required` with no default, and the project bans `DateTime.Now/UtcNow` (K-TIME-3, #4784), so time comes from `Cadence.Kernel.Time.IClock`.
- `Infrastructure/Persistence/`: `KnowledgeDbContext`, a design-time `KnowledgeDbContextFactory`, and `Configurations/` (Fluent API mappings).
- `Infrastructure/Persistence/Migrations/`: `20260924145203_InitialCreate`.

## HTTP surface

- `GET /healthz`, `GET /healthz/live`, `GET /healthz/ready` (anonymous; mapped by `MapCadenceHealthAndStatusEndpoints` and `UseCadenceWebDefaults` in [packages/telemetry](../../packages/telemetry/README.md)). `/healthz/ready` checks `KnowledgeDbContext`.
- `GET /api/v1/knowledge/status` (anonymous status payload).
- OpenAPI and the Scalar reference UI are mapped in the Development environment only.
- The gateway forwards `/api/v1/knowledge/**` to this service. Compose publishes host port 8015 (container port 8080); the gateway's default upstream is `http://localhost:8015` (`KNOWLEDGE_SERVICE_URL`).

## Security

`Program.cs` calls `AddCadenceSecurity()` and `UseCadenceSecurity()` from [packages/security](../../packages/security/README.md). Every route not marked anonymous requires the gateway-signed caller identity. `GATEWAY_SECRET` (at least 32 characters) is required at startup; see [ADR-2256](../../docs/adr/2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md).

Training reads need `training_log:read` and are narrowed to the caller's studies; rolling a curriculum out needs `training_curriculum:assign` (sysadmin, sponsor data management) and a study the caller is assigned to; the delegation log needs `delegation_log:read`, and its sign-off refuses records outside the signer's studies. The assigning actor is the signed caller (`PermissionMatrix.Knowledge.cs`) ([ADR-2516](../../docs/adr/2026-10-16-routes-are-denied-unless-they-declare-who-may-call-them.md)).

Training answers from its own records only (#5400). With no curriculum the list is empty, and a missing curriculum or
assignment, or one in another study, is a 404; no demo curriculum, investigator or site is invented. Batch assignment
draws its audience from the study's delegation log: the request names sites, roles or people (there is no default
audience), and each person is assigned once per site. A site the caller does not reach is refused (403). A site or
person not on the log, or a filter that matches nobody, is refused (422). An inactive curriculum is refused (409). A
refused batch writes no assignment and no audit row. The console offers only the sites and roles on the log and
pre-selects nothing.

## Persistence

`AddCadencePostgresDbContext<KnowledgeDbContext>()` from [packages/database](../../packages/database/README.md) connects to the shared `cadence` database with its search path set to the `knowledge` schema ([ADR-2447](../../docs/adr/2026-09-29-module-schemas-via-search-path-and-migration-deployment.md)). The connection string comes from `ConnectionStrings:cadence`, then `POSTGRES_CONNECTION_STRING`; only the Development environment falls back to `cadence` on `localhost:5432`, and other environments fail at startup. `docker/docker-compose.yml` points it at the compose PostgreSQL and sets `Database__MigrateOnStartup=true`, so the service applies its migrations when it starts.

## Key dependencies

- Project references: `Cadence.Core.Domain`, `Cadence.Kernel.Abstractions`, `Cadence.Security`, `Cadence.Telemetry`, `Cadence.Contracts`, `Cadence.Database`.
- Packages: `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore`, `FluentValidation` (versions in `Directory.Packages.props`).

## Tests

- No service-specific tests exist yet.
- Architecture rules (`tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`) scan this assembly: `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`.

## Run locally

```bash
export GATEWAY_SECRET="<at least 32 characters>"
export ConnectionStrings__cadence="Host=localhost;Port=5432;Database=cadence;Username=cadence;Password=<password>"
dotnet run --project apps/knowledge/src/Cadence.Apps.Knowledge.csproj
```

The Aspire AppHost ([apps/apphost](../apphost/README.md)) also starts this service on port 8015, as docker compose does, with a reference to its `cadence` PostgreSQL database; it applies its migrations on start.
