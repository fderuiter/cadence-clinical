# Agent and Contributor Guidelines

These rules apply to every change in this repository, by humans and AI agents alike. `CLAUDE.md`
points here; there is one copy of this file.

Read first:

1. [Kernel specification v0.2](docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md): the normative model.
   Every rule tagged `K-…` is an invariant that becomes a test.
2. [ARCHITECTURE.md](ARCHITECTURE.md): target architecture and the running services today.
3. [CONTEXT.md](CONTEXT.md): domain vocabulary.
4. [CONTRIBUTING.md](CONTRIBUTING.md): workflow, local checks and pull requests.

---

## Product mission

Cadence is an **operating system for clinical research**: one kernel holding a single body of
clinical truth, six engines (Knowledge, Execution, Evidence, Trust, Quality, Intelligence), and
products such as EDC, CTMS, eTMF, RTSM, safety and standards conformance built as applications on
them. No product owns clinical truth, and no representation (SDTM, ADaM, FHIR, the protocol PDF) is
clinical truth (K-TRUTH-1).

## Kernel rules that shape every change

- **K-SCOPE-1:** a concept belongs in the kernel only if it explains the provenance, authority,
  meaning or lifecycle of clinical evidence across more than one application. Otherwise it belongs to
  an application.
- **Forbidden redefinitions (spec §2.3):** applications never declare their own `Study`, `Site`,
  `Participant`/`Subject`, `Observation`, `Organization`, `Person`, `Role`, `Delegation`, `Specimen`,
  `Estimand` or `*AuditLog` types, and never use `string` study/site/participant IDs. Reference kernel
  IDs (`StudyId`, `SiteId`, `ParticipantId`, … in `Cadence.Kernel.Abstractions`). Enforced by K-XS-1
  in `tests/Architecture`; existing violations sit in a baseline that may only shrink.
- **K-XS-2:** applications reference `Cadence.Kernel.Abstractions` only, never kernel persistence.
- **Time (K-TIME-3):** never call `DateTime.Now/UtcNow` or `DateTimeOffset.Now/UtcNow` (banned in
  [`BannedSymbols.txt`](BannedSymbols.txt)). Inject `Cadence.Kernel.Time.IClock`; use `FakeClock`
  from `Cadence.Testing.Time` in tests.
- **Hold structural changes that contradict the spec.** If a change needs the spec to change, raise it
  on the kernel epic (#4719) instead of working around it.

---

## Technical stack

| Concern | Technology |
| --- | --- |
| Language and runtime | C# 14 / .NET 10 (`net10.0`, SDK pinned in `global.json`) |
| Web APIs | ASP.NET Core 10 Minimal APIs, `TypedResults`, endpoint filters (`IEndpointFilter`) |
| Data modelling | `record` types, primary constructors, FluentValidation, `System.Text.Json` source generation |
| Persistence | EF Core 10 + Npgsql on PostgreSQL 16+, GxP audit `SaveChangesInterceptor` |
| Messaging | MassTransit with the EF Core transactional outbox (`apps/execution`) |
| HTTP clients | `IHttpClientFactory` typed clients with Polly resilience pipelines |
| Identity | Keycloak OIDC at the gateway; HMAC-signed caller identity to services (`packages/security`) |
| Style and analysis | `.editorconfig`, `dotnet format`, NetAnalyzers, SonarAnalyzer, BannedApiAnalyzers; warnings are errors |
| Dependencies | Central Package Management: `Directory.Packages.props`, `Directory.Build.props` |
| Tests | xUnit v3, FluentAssertions, ArchUnitNET, Testcontainers |
| Clinical standards | CDISC USDM v3/v4, ODM, CDASH, SDTM/ADaM (Dataset-JSON) |
| CLI | `packages/cli` (Spectre.Console), run with `pnpm cadence <command>` |
| Frontend | Vue 3 + Vite (`apps/web`, `apps/subject-portal`, `packages/ui`) |

Neo4j is retired (ADR-2245); do not add Neo4j code.

## Where code goes

| Code | Directory |
| --- | --- |
| Kernel (IDs, value objects, `IClock`, domain, persistence) | `packages/kernel/` |
| Kernel specification and migration inventories | `docs/kernel/` |
| Data capture / EDC | `apps/execution/` |
| Other product services (until dispositioned under #4787) | `apps/<service>/` |
| OIDC gateway | `apps/gateway/` |
| Shared domain utilities | `packages/engine_core/`, `packages/core-models/` |
| Cross-service contracts | `packages/contracts/` |
| Security, signing, gateway authentication | `packages/security/` |
| Database helpers, audit interceptor | `packages/database/` |
| Health checks and telemetry | `packages/telemetry/` |
| Test fakes and helpers | `packages/testing/` |
| Tests | `tests/` (`Cadence.UnitTests`, `Cadence.ArchitectureTests`, `Cadence.E2ETests`) |
| Developer CLI and wizards | `packages/cli/`, `scripts/` |
| Git hooks | `.githooks/` |
| Architecture decision records | `docs/adr/` |
| GxP/SDLC documents | `docs/SDLC/` (RTM and IQ/OQ/PQ report are generated; never edit them by hand) |

---

## Coding patterns

### 1. Namespaces and using directives

File-scoped namespaces only. Usings go outside the namespace, `System.*` first, then everything else
alphabetically, with a blank line between groups (`.editorconfig`: `dotnet_sort_system_directives_first`,
`dotnet_separate_import_directive_groups`). `dotnet format` enforces this.

```csharp
using System.Collections.Generic;
using System.Threading;

using Cadence.Apps.Execution.Application.Queries;
using Cadence.Apps.Execution.Endpoints.Filters;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Cadence.Apps.Execution.Endpoints;
```

### 2. EF Core queries and audit

- Read-only queries use `.AsNoTracking()`.
- Filter booleans with strongly typed predicates (`.Where(s => s.IsActive)`); EF Core translates them
  to `IS TRUE`.
- Every `DbContext` registration includes the GxP audit interceptor, which fills `CreatedAt`,
  `CreatedBy`, `ReasonForChange` and `VersionIndex` (21 CFR Part 11). Mutating entities implement
  `IGxPAuditable`.
- Register contexts with `AddCadencePostgresDbContext` (`packages/database`). It resolves
  `ConnectionStrings:cadence`, then `POSTGRES_CONNECTION_STRING`, and fails outside `Development`
  when neither is set.

### 3. Nullability and immutability

`<Nullable>enable</Nullable>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` apply
solution-wide. Use primary constructors and `init` accessors on DTO records. Do not suppress nullable
warnings (`#pragma warning disable CS8618`) without a documented GxP justification.

```csharp
public record CreateSubjectCommand(
    string SiteCode,
    string SubjectIdentifier,
    DateOnly DateOfBirth,
    string ReasonForChange);
```

### 4. Minimal API endpoints

No MVC controllers. Group routes with `MapGroup("/api/v1/…")`, return `TypedResults`, declare
`Produces`/`ProducesProblem`, and add endpoint filters for cross-cutting rules (for example
`ChangeReasonHeaderFilter` in `apps/execution/src/Endpoints/Filters`). See
`apps/execution/src/Endpoints/QueryEndpoints.cs` for a complete example.

### 5. Identity and authorization

- Every service calls `builder.Services.AddCadenceSecurity()` and `app.UseCadenceSecurity()`. Only
  health, status and development API-doc endpoints may be `AllowAnonymous()`.
- Take the caller's identity and roles from the verified claims (for example
  `apps/execution/src/Endpoints/CallerIdentity.cs`), never from request bodies or unsigned headers.
- Never add a default or fallback secret. `GATEWAY_SECRET` is required at startup (ADR-2256).

### 6. Requirement traceability in tests (21 CFR Part 11)

Tests that verify a regulatory or system requirement declare it twice: an `@req:` tag in the XML doc
comment and a `[GxPRequirement]` attribute (`Cadence.Testing`). `pnpm cadence gxp sync` turns these
into the Requirements Traceability Matrix.

```csharp
/// <summary>
/// Enrolling a subject writes an audit entry naming the authenticated user.
/// @req:PRD-SYS-053
/// </summary>
[Fact]
[Trait("Category", "GxP_OQ")]
[GxPRequirement("PRD-SYS-053")]
public async Task EnrollSubject_WritesAuditEntryWithCaller()
{
    // Arrange, Act, Assert with FluentAssertions
}
```

### 7. Test infrastructure (`packages/testing`)

Use the shared helpers instead of ad-hoc fakes:

- `Cadence.Testing.InMemoryRepository<T>` for application-service tests without a database.
- `Cadence.Testing.Security.TestSecurityContext` (`CreateHmacHeaders`, `CreatePrincipal`,
  `CreateSignedClient`, `UseTestGatewaySecret`) for authenticated endpoint tests.
- `Cadence.Testing.Time.FakeClock` for deterministic time.
- `Cadence.Testing.GoldenMaster` for golden-master comparisons against the Python reference behaviour.

PostgreSQL integration tests need `CADENCE_TEST_POSTGRES=<connection string>` or
`CADENCE_TEST_DOCKER=1`; without either they are skipped, never faked.

### 8. Service boundaries and contracts

- No project references to another service's persistence layer.
- Cross-service calls go through typed clients and DTOs in `packages/contracts`, registered with
  `IHttpClientFactory` and Polly, and carry HMAC signatures from `Cadence.Security`.
- In the target architecture most of these calls disappear: applications share kernel concepts
  in-process (ADR-2244). Do not build new cross-service synchronisation for kernel concepts.

### 9. Layer purity

Inside each project, `Domain/` has no dependency on `Infrastructure/`, `Endpoints/`,
`Microsoft.AspNetCore`, Npgsql or EF Core. `Application/` depends on `Domain/` and ports only.
`tests/Cadence.ArchitectureTests.csproj` enforces this and the kernel rules above.

### 10. Database migrations

- Add a migration: `pwsh scripts/manage_migrations.ps1 add <service> <MigrationName>` (or
  `scripts/manage_migrations.sh`). Migrations go to `Infrastructure/Persistence/Migrations/`.
- Generate an idempotent SQL script for review: `pwsh scripts/manage_migrations.ps1 script <service>`.
- Verify migrations against real PostgreSQL (see pattern 7).

### 11. Porting Python reference sources

Some packages still contain the Python implementation as a behavioural reference, deleted as each C#
port is verified (#4789). Follow [docs/MIGRATION_WORKFLOW.md](docs/MIGRATION_WORKFLOW.md). Equivalents:

| Python | .NET 10 |
| --- | --- |
| `pydantic.BaseModel` | `record` with primary constructor and `System.Text.Json` source generation |
| FastAPI `APIRouter` / `Depends` / middleware | `MapGroup` / DI / `IEndpointFilter` |
| SQLAlchemy / SQLModel session | EF Core `DbContext` with `IEntityTypeConfiguration<T>` |
| `.where(col.is_(True))` | `.Where(x => x.IsActive)` |
| `httpx.AsyncClient` | `IHttpClientFactory` typed client |
| `pytest` / `pytest-asyncio` | xUnit v3 `[Fact]`/`[Theory]` with async `Task` tests |
| `pytest-archon` | ArchUnitNET |
| Ruff | `dotnet format` + analyzers |

Never re-add Python code, Python tooling or compatibility shims.

---

## Architecture decision records

- New decision: `pnpm cadence scaffold adr "<title>"`, then add it to [docs/adr/index.md](docs/adr/index.md).
- ADR numbers are unique; `pnpm cadence check adrs` fails on duplicates, missing `# ADR-NNNN:` headings
  and broken index links.
- When a decision is superseded: set the old ADR's status to
  `Superseded by [ADR-XXXX](…)`, add a `> [!NOTE]` callout at the top, annotate its index entry with
  `*(Superseded by ADR-XXXX)*`, and update [ARCHITECTURE.md](ARCHITECTURE.md) and [CONTEXT.md](CONTEXT.md).

## Documentation is part of the change

Every pull request updates the documentation its change affects, in the same PR: the service or
package README, `ARCHITECTURE.md`, `CONTEXT.md`, `docs/ENVIRONMENT_VARIABLES.md` for new settings, an
ADR for a new decision, and the [documentation index](docs/DOCUMENTATION_INDEX.md) for new documents.
Regenerated GxP documents come from `pnpm cadence gxp sync`, never hand edits.

---

## Commands

The CLI is not installed as a `dotnet` tool. Run it as `pnpm cadence <command>` or
`dotnet run --project packages/cli/src/Cadence.Cli.csproj -- <command>`.

| Task | Command |
| --- | --- |
| Build | `dotnet build Cadence.sln` |
| Unit tests | `dotnet test tests/Cadence.UnitTests.csproj` |
| Unit tests without integration | `pnpm cadence test --fast` (filters `Category!=Integration`) |
| Integration tests only | `pnpm cadence test --integration` |
| Architecture tests | `dotnet test tests/Cadence.ArchitectureTests.csproj` |
| Formatting | `dotnet format Cadence.sln --verify-no-changes` |
| All quality gates (pre-commit) | `pnpm cadence check` |
| One gate | `pnpm cadence check <gate>` (for example `adrs`, `markdown`, `readmes`, `arch-drift`) |
| Auto-fix | `pnpm cadence fix` |
| Diagnostics | `pnpm cadence doctor --auto-fix` |
| Traceability matrix | `pnpm cadence gxp sync` (validate only: `pnpm cadence gxp validate`) |
| Onboarding wizard | `pnpm cadence wizard onboarding` or `make wizard` |
| Database reset / seed / status | `pnpm cadence db reset`, `db seed`, `db status` |
| Migrations | `pwsh scripts/manage_migrations.ps1 add <service> <name>` |

The generated reference for every command is [docs/cli.md](docs/cli.md).

## GxP compliance sync

`pnpm cadence gxp sync` runs the tests, reads the `@req:` tags and test results, regenerates
`docs/SDLC/Requirements_Traceability_Matrix.md` and `docs/SDLC/IQ_OQ_PQ_Execution_Report.md`, and
stages them. Run it whenever you add or change requirement-tagged tests.
