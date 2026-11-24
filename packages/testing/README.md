# Cadence.Testing

Shared test infrastructure for the .NET test projects.

## Project

- Project: `packages/testing/src/Cadence.Testing.csproj` (assembly `Cadence.Testing`).
- `Cadence.Testing`: `GxPRequirementAttribute` (requirement traceability on tests), `KernelRuleAttribute` (the kernel rule a test method proves, checked by `KernelRuleCoverageTests`; see [docs/TESTING.md](../../docs/TESTING.md#linking-a-test-to-a-kernel-rule)) and `InMemoryRepository<TEntity, TId>` for aggregates derived from `Cadence.Core.Domain.AggregateRoot<TId>`.
- `Cadence.Testing.Security`: `TestSecurityContext` (test gateway secret, `CreateHmacHeaders`, `CreatePrincipal`, and a `GatewaySigningHandler` that signs `HttpClient` requests like the gateway; test identities carry the tenant code `TenantCode`, and a test of a caller without a tenant uses `identity with { TenantId = null }`), `SignedClientExtensions`. `TestFieldProtection`: test-only `FIELD_ENCRYPTION_KEY` / `FIELD_LOOKUP_HASH_KEY` values, a second encryption key for wrong-key tests, ready-made `IFieldEncryptor` / `ILookupHasher` instances, and `UseTestFieldProtectionKeys()` for `WebApplicationFactory` hosts.
- `Cadence.Testing.Persistence`: `StoredRowReader` reads table rows as stored (bypassing EF Core value converters), to assert that encrypted or hashed columns never hold plaintext. `PostgresTestImage` names the one PostgreSQL major and digest-pinned image every database fixture, CI and docker compose run, and refuses a server of another major ([ADR-2533](../../docs/adr/2026-10-19-tests-ci-and-compose-run-one-pinned-postgresql-major.md)).
- `Cadence.Testing.Time`: `FakeClock` implementing the kernel `IClock`.
- `Cadence.Testing.Hosting`: `TestHostSettings` (`UseTestPostgresConnectionString`, `UseTestStorage`, and `DisableBackgroundHostedServices` / `RemoveBackgroundHostedServices`, which strip background `IHostedService` pollers and startup migrators from unit `WebApplicationFactory` hosts while keeping `SigTokenUseStoreStartupCheck` and any caller-requested in-memory workers active; [ADR-2833](../../docs/adr/2026-11-23-adopt-microsoft-testing-platform-and-isolate-unit-hosts.md)) and `StartupValidationFactory<TEntryPoint>`, which builds a service's real host without starting it and runs its `ValidateOnStart` checks on the test thread, so refused configuration is asserted without the `WebApplicationFactory` startup race (see [docs/TESTING.md](../../docs/TESTING.md)).
- `Cadence.Testing.GoldenMaster`: `GoldenMasterScenario`, `GoldenMasterFixtureLoader`, `GoldenMasterAssert`.
- There are no entity factories (such as `SubjectFactory`) in this package.
- Time (K-TIME-3): the project bans `DateTime.Now/UtcNow`. `TestSecurityContext` signs at `SystemClock.Instance.UtcNow` unless the test passes a time.

## Key dependencies

- Project references: `Cadence.Core.Domain`, `Cadence.Security`, `Cadence.Kernel.Abstractions`.
- Packages: `FluentAssertions`, `NSubstitute`, `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.EntityFrameworkCore`.

## Referenced by

`tests/Cadence.UnitTests/Cadence.UnitTests.csproj`, `tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`, `tests/Cadence.E2ETests/Cadence.E2ETests.csproj`.

## Tests

No tests target this package directly; it is exercised by every test project that references it.
