# Cadence.Database

Shared EF Core 10 and PostgreSQL plumbing for the services: DbContext registration, connection-string resolution, readiness health checks, and the GxP audit interceptor.

## Project

- Project: `packages/database/src/Cadence.Database.csproj` (assembly `Cadence.Database`).
- `CadenceDatabaseExtensions`: `AddCadencePostgresDbContext<TDbContext>()` registers the context with Npgsql retry-on-failure and a `ready`-tagged health check (used by `/healthz/ready`). `ResolvePostgresConnectionString` reads `ConnectionStrings:cadence`, then `POSTGRES_CONNECTION_STRING`; only the Development environment falls back to a local docker-compose database, and any other environment throws at startup.
- `CadenceDbContextExtensions`: `AddCadenceDbContext<TContext>()` binds a pre-compiled EF model when one exists.
- `GxPAuditInterceptor`: `SaveChangesInterceptor` that stamps audit fields and appends hash-chained `AuditLogEntry` rows.
- `Configurations/AuditLogEntryConfiguration`: Fluent API mapping for `AuditLogEntry`.
- Python reference sources remain in this folder until each C# port is verified (issue #4789): `audit.py`, `graph.py`, `mock_graph.py`, `datetime_helpers.py`, `tests/`, `pyproject.toml`. They are not the implementation.

## Key dependencies

- Project reference: `Cadence.Core.Domain`; framework reference `Microsoft.AspNetCore.App`.
- Packages: `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Design`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore`.

## Referenced by

Every .NET service except `designer` and `gateway`, plus `packages/cli` and all three test projects.

## Tests

```bash
dotnet test tests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Packages.DatabaseUnitTests|FullyQualifiedName~Cadence.UnitTests.Packages.CadenceDbContextExtensionsTests"
```

`tests/Audit/GxPAuditTrailGenerationTests.cs` exercises the interceptor through four service contexts (`--filter "FullyQualifiedName~Cadence.UnitTests.Audit"`).
