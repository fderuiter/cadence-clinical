# Cadence.Testing

Shared test infrastructure for the .NET test projects.

## Project

- Project: `packages/testing/src/Cadence.Testing.csproj` (assembly `Cadence.Testing`).
- `Cadence.Testing`: `GxPRequirementAttribute` (requirement traceability on tests) and `InMemoryRepository<TEntity, TId>` for aggregates derived from `Cadence.Core.Domain.AggregateRoot<TId>`.
- `Cadence.Testing.Security`: `TestSecurityContext` (test gateway secret, `CreateHmacHeaders`, `CreatePrincipal`, and a `GatewaySigningHandler` that signs `HttpClient` requests like the gateway), `SignedClientExtensions`.
- `Cadence.Testing.Time`: `FakeClock` implementing the kernel `IClock`.
- `Cadence.Testing.GoldenMaster`: `GoldenMasterScenario`, `GoldenMasterFixtureLoader`, `GoldenMasterAssert`.
- There are no entity factories (such as `SubjectFactory`) in this package.

## Key dependencies

- Project references: `Cadence.Core.Domain`, `Cadence.Security`, `Cadence.Kernel.Abstractions`.
- Packages: `FluentAssertions`, `NSubstitute`, `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.EntityFrameworkCore`.

## Referenced by

`tests/Cadence.UnitTests.csproj`, `tests/Cadence.ArchitectureTests.csproj`, `tests/Cadence.E2ETests.csproj`.

## Tests

No tests target this package directly; it is exercised by every test project that references it.
