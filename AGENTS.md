# Agent and Contributor Guidelines

These rules apply to every change in this repository, by humans and AI agents alike. `CLAUDE.md`
points here; there is one copy of this file.

Read first:

1. [Kernel specification v0.2](docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md): the normative model.
   Every rule tagged `K-…` is an invariant that becomes a test.
2. [ARCHITECTURE.md](ARCHITECTURE.md): target architecture and the running services today.
3. [CONTEXT.md](CONTEXT.md): domain vocabulary.
4. [CONTRIBUTING.md](CONTRIBUTING.md): workflow, local checks and pull requests.
5. [Project plan](docs/PROJECT_PLAN.md) and [goals and priorities](docs/GOALS_AND_PRIORITIES.md): active delivery scope, module ownership, and verification outcomes.

---

## Issue workflow

Follow [docs/ISSUE_MANAGEMENT.md](docs/ISSUE_MANAGEMENT.md) when filing or working issues.
Check current code, recent PRs, and the project plan before proposing work or closure.
Use its canonical taxonomy and horizon caps; Now work must meet its Definition of Ready.
Prefer an existing owner, native sub-issues, and native dependencies over duplicate tickets.
Preserve original context and salvage unique source and comment evidence before consolidation.
Keep raw ideas in Discussions and preserve unmet dependency gates until replacements are verified.

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
  in `tests/Cadence.ArchitectureTests/Architecture`; existing violations sit in a baseline that may only shrink.
- **K-XS-2:** applications reference `Cadence.Kernel.Abstractions` and the module SDK `Cadence.Kernel.Modules` only,
  never kernel Domain, Application, Persistence or Api (ADR-2600).
- **Time (K-TIME-3):** never call `DateTime.Now/UtcNow` or `DateTimeOffset.Now/UtcNow` (banned in
  [`BannedSymbols.txt`](BannedSymbols.txt), enforced solution-wide across all C# projects in root `Directory.Build.props`). Inject
  `Cadence.Kernel.Time.IClock`; in tests use `FakeClock` from `Cadence.Testing.Time`, or `SystemClock.Instance`
  where the code under test checks the real clock ([docs/TESTING.md](docs/TESTING.md#time-in-tests)).
- **Every K-rule has a test that names it:** the test method that proves a rule carries `[KernelRule("K-…")]`
  (`Cadence.Testing`). `KernelRuleCoverageTests` fails on a rule with no such test outside its shrink-only baseline
  ([docs/TESTING.md](docs/TESTING.md#linking-a-test-to-a-kernel-rule)).
- **Hold structural changes that contradict the spec.** If a change needs the spec to change, raise it
  on the kernel epic (#4719) instead of working around it.

---

## Technical stack

| Concern              | Technology                                                                                                           |
| -------------------- | -------------------------------------------------------------------------------------------------------------------- |
| Language and runtime | C# 14 / .NET 10 (`net10.0`, SDK pinned in `global.json`)                                                             |
| Web APIs             | ASP.NET Core 10 Minimal APIs, `TypedResults`, endpoint filters (`IEndpointFilter`)                                   |
| Data modelling       | `record` types, primary constructors, FluentValidation, `System.Text.Json` source generation                         |
| Persistence          | EF Core 10 + Npgsql on PostgreSQL 18 (ADR-2533), GxP audit `SaveChangesInterceptor`                                  |
| Messaging            | MassTransit 8 (Apache 2.0, ADR-2524) with the EF Core transactional outbox (`apps/execution`)                        |
| HTTP clients         | `IHttpClientFactory` typed and named clients (outbound integrations; DB retries use EF Core strategies per ADR-2523) |
| Identity             | Keycloak OIDC at the gateway; HMAC-signed caller identity to services (`packages/security`)                          |
| Style and analysis   | `.editorconfig`, `dotnet format`, NetAnalyzers, SonarAnalyzer, BannedApiAnalyzers; warnings are errors               |
| Dependencies         | Central Package Management: `Directory.Packages.props`, `Directory.Build.props`                                      |
| Tests                | xUnit v3, FluentAssertions, ArchUnitNET, Testcontainers                                                              |
| Clinical standards   | CDISC USDM v3/v4, ODM, CDASH, SDTM/ADaM (Dataset-JSON)                                                               |
| CLI                  | `packages/cli` (System.CommandLine + Spectre.Console, Native AOT), run with `pnpm cadence <command>`                 |
| Frontend             | Vue 3 + Vite (`apps/web`, `apps/subject-portal`, `packages/ui`)                                                      |

Neo4j is retired (ADR-2245); do not add Neo4j code.

## Where code goes

| Code                                                       | Directory                                                                                                                                                                                          |
| ---------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Kernel (IDs, value objects, `IClock`, domain, persistence) | `packages/kernel/`                                                                                                                                                                                 |
| Kernel specification and migration inventories             | `docs/kernel/`                                                                                                                                                                                     |
| Data capture / EDC                                         | `apps/execution/`                                                                                                                                                                                  |
| Other product services (until dispositioned under #4787)   | `apps/<service>/`                                                                                                                                                                                  |
| OIDC gateway                                               | `apps/gateway/`                                                                                                                                                                                    |
| Shared domain utilities                                    | `packages/engine_core/`                                                                                                                                                                            |
| Cross-service contracts                                    | `packages/contracts/`                                                                                                                                                                              |
| Security, signing, gateway authentication                  | `packages/security/`                                                                                                                                                                               |
| Database helpers, audit interceptor                        | `packages/database/`                                                                                                                                                                               |
| Health checks and telemetry                                | `packages/telemetry/`                                                                                                                                                                              |
| Test fakes and helpers                                     | `packages/testing/`                                                                                                                                                                                |
| Tests                                                      | `tests/<Project>/` (`Cadence.UnitTests`, `Cadence.IntegrationTests`, `Cadence.ArchitectureTests`, `Cadence.E2ETests`; shared fakes in `Cadence.TestSupport`) and `packages/kernel/consumer-tests/` |
| Developer CLI and wizards                                  | `packages/cli/`, `scripts/`                                                                                                                                                                        |
| Git hooks                                                  | `.githooks/`                                                                                                                                                                                       |
| Architecture decision records                              | `docs/adr/`                                                                                                                                                                                        |
| GxP/SDLC documents                                         | `docs/SDLC/` (RTM and IQ/OQ/PQ report are generated; never edit them by hand)                                                                                                                      |

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
- Explicit transactions go through `ExecuteInTransactionAsync` (`packages/database`), which runs the unit of work inside
  the retrying execution strategy; `BeginTransaction` is a banned API (ADR-2523). One `SaveChangesAsync` is already
  atomic and needs no transaction.
- Register contexts with `AddCadencePostgresDbContext` (`packages/database`). It resolves
  `ConnectionStrings:cadence`, then `POSTGRES_CONNECTION_STRING`, and fails outside `Development`
  when neither is set.

### 3. Nullability and immutability

`<Nullable>enable</Nullable>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` apply
solution-wide. Use primary constructors and `init` accessors on DTO records. Do not suppress nullable
warnings (`#pragma warning disable CS8618`) without a documented GxP justification. Every other
suppression also carries its reason (`Justification = "..."` or a comment on the line), and projects
that set `<CadenceRequireXmlDocs>true</CadenceRequireXmlDocs>` need an XML comment on every public
member ([CONTRIBUTING.md §3](CONTRIBUTING.md#3-local-checks)).

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
`apps/execution/module/Endpoints/FormQueryEndpoints.cs` for a complete example.

### 5. Identity and authorization

- Every service calls `builder.Services.AddCadenceSecurity()` and `app.UseCadenceSecurity()`. Only
  health, status and development API-doc endpoints may be `AllowAnonymous()`.
- Take the caller's identity and roles from the verified claims (for example
  `apps/execution/src/Endpoints/CallerIdentity.cs`), never from request bodies or unsigned headers.
- Never add a default or fallback secret. `GATEWAY_SECRET` is required at startup (ADR-2256).

### 6. Requirement traceability in tests (21 CFR Part 11)

A test that verifies a regulatory or system requirement declares the requirement ID on the test method,
with a `[GxPRequirement]` attribute (`Cadence.Testing`) or an `@req:` line in its XML doc comment. One form
is enough. These tags feed the Requirements Traceability Matrix, and
[docs/TESTING.md](docs/TESTING.md) has the full rules. The ones agents most often get wrong:

- Tag only the requirement the test's assertions prove. Most tests need no tag.
- Look the ID up in `docs/SDLC/01_Product_Requirements_Document_PRD.md` before using it. PRD-SYS-001 is
  audit fields and audit entries; PRD-SYS-053 is the agent lifecycle hooks and never belongs on a product test.
- `pnpm cadence check rtm` fails on an unknown ID, a tag on a class, a `[Trait("Requirement", …)]` (not a
  supported form), and PRD-SYS-053 outside `tests/**/Packages/AgentHook*.cs`.
- After adding, removing or moving a tag, run `pnpm cadence gxp sync` to validate the tags and results. It
  writes a preview to `TestResults/gxp/preview/` and nothing under `docs/SDLC`; never commit the RTM or the
  IQ/OQ/PQ report on a feature branch (ADR-2532).

```csharp
/// <summary>
/// Enrolling a subject writes an audit entry naming the authenticated user.
/// </summary>
[Fact]
[GxPRequirement("PRD-SYS-001")]
public async Task EnrollSubject_WritesAuditEntryWithCaller()
{
    // Arrange, Act, Assert with FluentAssertions
}
```

### 7. Test infrastructure (`packages/testing`)

Use the shared helpers instead of ad-hoc fakes ([docs/TESTING.md](docs/TESTING.md) describes each one):

- `Cadence.Testing.InMemoryRepository<T>` for application-service tests without a database.
- `Cadence.Testing.Security.TestSecurityContext` (`CreateHmacHeaders`, `CreatePrincipal`,
  `CreateSignedClient`, `UseTestGatewaySecret`) for authenticated endpoint tests.
- `Cadence.Testing.Time.FakeClock` for deterministic time.
- `Cadence.Testing.GoldenMaster` for golden-master comparisons against the Python reference behaviour.

PostgreSQL integration tests need `CADENCE_TEST_POSTGRES=<connection string>` or
`CADENCE_TEST_DOCKER=1`; without either they are skipped, never faked.

### 8. Service boundaries and contracts

- No project references to another service's persistence layer.
- Cross-module clinical mutations go through the in-process kernel command path (`Cadence.Kernel.Abstractions`
  and `Cadence.Kernel.Modules`, ADR-2244, ADR-2600); asynchronous cross-service events use MassTransit
  records in `packages/contracts` via the EF Core transactional outbox (ADR-2524).
- Do not build new cross-service HTTP synchronisation for kernel concepts.

### 9. Layer purity

Inside each project, `Domain/` has no dependency on `Infrastructure/`, `Endpoints/`,
`Microsoft.AspNetCore`, Npgsql or EF Core. `Application/` depends on `Domain/` and ports only.
`tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj` enforces this and the kernel rules above.

### 10. Database migrations

- Add a migration across any of the 18 persistence modules: `pnpm cadence migrations add <module> <MigrationName>` (or `scripts/manage_migrations.sh` / `pwsh scripts/manage_migrations.ps1`).
- Generate an idempotent SQL script for review (written to `TestResults/migrations/<module>.sql`): `pnpm cadence migrations script <module|all>`.
- Verify snapshots, `.Designer.cs` companions, attributes, and DDL uniqueness offline: `pnpm cadence migrations verify` — and verify migrations against real PostgreSQL (see pattern 7).
- Regenerate the schema reference after a model change: `pnpm cadence generate-schema-doc` rewrites
  [docs/schema](docs/schema/README.md), and the `schema-doc` gate fails while it is stale or any module has pending model changes.

### 11. Porting Python reference sources

Some packages still contain Python files as a behavioural reference for C# ports that are missing or
untested; [docs/PYTHON_REFERENCE_INVENTORY.md](docs/PYTHON_REFERENCE_INVENTORY.md) lists each one and what it
needs before it can be deleted (#4789). Follow [docs/MIGRATION_WORKFLOW.md](docs/MIGRATION_WORKFLOW.md). Equivalents:

| Python                                       | .NET 10                                                                    |
| -------------------------------------------- | -------------------------------------------------------------------------- |
| `pydantic.BaseModel`                         | `record` with primary constructor and `System.Text.Json` source generation |
| FastAPI `APIRouter` / `Depends` / middleware | `MapGroup` / DI / `IEndpointFilter`                                        |
| SQLAlchemy / SQLModel session                | EF Core `DbContext` with `IEntityTypeConfiguration<T>`                     |
| `.where(col.is_(True))`                      | `.Where(x => x.IsActive)`                                                  |
| `httpx.AsyncClient`                          | `IHttpClientFactory` typed client                                          |
| `pytest` / `pytest-asyncio`                  | xUnit v3 `[Fact]`/`[Theory]` with async `Task` tests                       |
| `pytest-archon`                              | ArchUnitNET                                                                |
| Ruff                                         | `dotnet format` + analyzers                                                |

Never re-add Python code, Python tooling or compatibility shims.

---

## Architecture decision records

- New decision: `pnpm cadence scaffold adr "<title>"`, then add it to [docs/adr/index.md](docs/adr/index.md).
- ADR numbers are unique; `pnpm cadence check adrs` fails on duplicates, missing `# ADR-NNNN:` headings
  and broken index links.
- When a decision is superseded: set the old ADR's status to
  `Superseded by ADR-XXXX` (linking the new ADR), add a `> [!NOTE]` callout at the top, annotate its index entry with
  `*(Superseded by ADR-XXXX)*`, and update [ARCHITECTURE.md](ARCHITECTURE.md) and [CONTEXT.md](CONTEXT.md).

## Documentation is part of the change

Every pull request updates the documentation its change affects, in the same PR: the service or
package README, `ARCHITECTURE.md`, `CONTEXT.md`, `docs/ENVIRONMENT_VARIABLES.md` for new settings, an
ADR for a new decision, and the [documentation index](docs/DOCUMENTATION_INDEX.md) for new documents.
The generated RTM and IQ/OQ/PQ report come only from the integration owner's `pnpm cadence gxp snapshot`,
never from hand edits or a feature branch.

---

## Commands

The CLI is not installed as a `dotnet` tool. Run it as `pnpm cadence <command>` or
`dotnet run --project packages/cli/src/Cadence.Cli.csproj -- <command>`.

| Task                                                                          | Command                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| ----------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Build                                                                         | `dotnet build Cadence.sln`                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| Frontend production builds                                                    | `pnpm build` (builds `packages/ui`, `apps/web`, and `apps/subject-portal`; `pnpm build:demo` for demo bundles)                                                                                                                                                                                                                                                                                                                                                                                            |
| Unit tests                                                                    | `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj`                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| Kernel SDK consumer contract tests                                            | `dotnet test packages/kernel/consumer-tests/Cadence.Kernel.ConsumerTests/Cadence.Kernel.ConsumerTests.csproj` (also included in `dotnet test Cadence.sln`)                                                                                                                                                                                                                                                                                                                                                |
| Frontend unit, VitePress portal, and Playwright E2E tests                     | `pnpm test` (`pnpm -r test` + `pnpm exec vitest run --config vitest.config.mjs`), `pnpm --filter subject-portal test:e2e`, `pnpm --filter web test:e2e`, `pnpm --filter web test:e2e:workflows`                                                                                                                                                                                                                                                                                                           |
| Unit tests without integration                                                | `pnpm cadence test --fast` (filters `Category!=Integration`)                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| Integration tests only                                                        | `pnpm cadence test --integration` (`tests/Cadence.IntegrationTests`, needs PostgreSQL; add `CADENCE_TEST_BROWSER=1` for browser correction test)                                                                                                                                                                                                                                                                                                                                                          |
| Architecture tests                                                            | `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`                                                                                                                                                                                                                                                                                                                                                                                                                            |
| Formatting                                                                    | `dotnet format Cadence.sln --verify-no-changes`                                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| Dependency alignment (MSBuild, `pnpm` catalog, CLI tools & Docker pins)       | `dotnet restore Cadence.sln && dotnet build Cadence.sln && pnpm install --frozen-lockfile && pnpm cadence check dependencies && dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter FullyQualifiedName~ContainerImagePinTests`                                                                                                                                                                                                                                                          |
| Workflow security & actionlint                                                | `pnpm cadence check workflow-security && pnpm cadence check actionlint` (enforces 40-hex SHAs, `sha256:` digests, `persist-credentials: false`, least-privilege permissions, ref-guarded typed `workflow_dispatch` inputs passed via `env:`, and `workflow_call`-only deploy/release workflows)                                                                                                                                                                                                           |
| Installer checksum tests                                                      | `bash tests/test_installer_checksums.sh`                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| Dockerfile lint (Hadolint)                                                    | `pnpm cadence check hadolint` (or `find docker -type f -name "Dockerfile*" -exec hadolint --failure-threshold error {} +`)                                                                                                                                                                                                                                                                                                                                                                                |
| Container builds (.NET & SCE)                                                 | `docker build -t cadence-gateway:latest -f docker/Dockerfile.dotnet --build-arg APP_NAME=gateway .` (also `APP_NAME=kernel-api`, `docker/sce/python`, `docker/sce/r`)                                                                                                                                                                                                                                                                                                                                     |
| Container vulnerability scan (Trivy)                                          | `trivy image --exit-code 1 --ignore-unfixed --pkg-types os,library --severity CRITICAL,HIGH cadence-gateway:latest`                                                                                                                                                                                                                                                                                                                                                                                       |
| Provision local guardrails (`.cadence/venv` & `.cadence/bin`)                 | `./scripts/setup_wizard.sh --minimal` (provisions `detect-secrets==1.5.0` and `pip-audit==2.10.1` in `.cadence/venv` plus SHA-256-verified `actionlint` v1.7.12, `hadolint` v2.15.1, and `shellcheck` v0.11.0 in `.cadence/bin`)                                                                                                                                                                                                                                                                          |
| Provision local `pip-audit` standalone (`.venv` or `PATH`)                    | `python3 -m venv .venv && .venv/bin/python -m pip install --upgrade pip pip-audit==2.10.1`                                                                                                                                                                                                                                                                                                                                                                                                                |
| Pre-commit guardrails (staged index blobs; `.githooks/pre-commit`)            | `pnpm cadence check --pre-commit` (skips `public-bundle`, `branch-freshness`, `architecture-tests`, and `unit-tests`; scopes `demo-manifest` to staged seed/fixture paths)                                                                                                                                                                                                                                                                                                                                |
| All quality gates (full repo or scoped diff; requires restored `Cadence.sln`) | `dotnet restore Cadence.sln && pnpm cadence check` (runs all 42 gates, including `actionlint`, `hadolint`, `public-bundle`, `branch-freshness`, `architecture-tests`, `unit-tests`, and `demo-manifest`; scope branch or push diffs with `--base <ref>` or `--push-before <sha>`; exclude specific gates with `--exclude <gate>`)                                                                                                                                                                         |
| Whole CI workflow locally (before merging; Actions is off until 2027)         | `CADENCE_TEST_POSTGRES=<connection string> pnpm cadence ci` (about 20 minutes, [docs/CI.md](docs/CI.md); forwards `--base <ref>` to CLI checks, runs `actionlint`, `native-aot-build`, and every PR job, requires `actionlint`, `hadolint`, `shellcheck`, `bash`, `pip-audit` and `detect-secrets-hook` in `.cadence/bin`, `.cadence/venv`, `.venv` or on `PATH`, plus restored `Cadence.sln`; `--allow-missing-tools` skips missing tools, `--allow-db-skip` skips DB) <!-- pragma: allowlist secret --> |
| One gate                                                                      | `pnpm cadence check <gate> [--base <ref> \| --push-before <sha>]` (for example `actionlint`, `hadolint`, `adrs`, `markdown`, `readmes`, `workflow-security`, `rulesets`, `arch-drift`, `vulnerabilities`, `public-bundle`, `demo-manifest`, `branch-freshness`, `architecture-tests`, `unit-tests`)                                                                                                                                                                                                       |
| Reconcile GitHub rulesets (while Actions runners stay off until 2027)         | `pnpm cadence check rulesets`, `pnpm cadence rulesets --dry-run` (preview), and `pnpm cadence rulesets` (sync `.github/rulesets/{main,release_branches,version_tags}.json` via `gh api`; requires repository admin credentials)                                                                                                                                                                                                                                                                           |
| PHI/PII scan                                                                  | `pnpm cadence check deid`; mark a line that is not PHI with a `deid-ignore` comment, and drop stale `.deid-baseline.txt` entries with `CADENCE_UPDATE_BASELINE=1 pnpm cadence check deid` ([CONTRIBUTING.md §6](CONTRIBUTING.md#6-repository-hygiene))                                                                                                                                                                                                                                                    |
| Auto-fix                                                                      | `pnpm cadence fix [--full] [--openapi]` (`dotnet format`, Prettier, baseline/ADR/CLI/doc/demo sync, and diff-based schema/OpenAPI/client sync)                                                                                                                                                                                                                                                                                                                                                            |
| Diagnostics                                                                   | `pnpm cadence doctor --auto-fix`                                                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| Traceability validation (branch)                                              | `pnpm cadence gxp sync` (from existing TRX + `*.database.txt`: `pnpm cadence gxp sync --results-dir <dir>`; no tests: `pnpm cadence gxp validate`)                                                                                                                                                                                                                                                                                                                                                        |
| GxP snapshot (integration owner / release pipeline)                           | `CADENCE_GXP_SNAPSHOT=1 pnpm cadence gxp snapshot` (from verified `.NET` + browser CI evidence: `CADENCE_GXP_SNAPSHOT=1 pnpm cadence gxp snapshot --results-dir <dir>`)                                                                                                                                                                                                                                                                                                                                   |
| Onboarding wizard                                                             | `pnpm cadence wizard onboarding` or `make wizard`                                                                                                                                                                                                                                                                                                                                                                                                                                                         |
| Database reset / migrate                                                      | `pnpm cadence db reset`, `db migrate`                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| Migrations (all 18 persistence modules)                                       | `pnpm cadence migrations add <module> <name>`, `migrations script [module\|all]`, `migrations list [module\|all]`, `migrations verify [module\|all]`                                                                                                                                                                                                                                                                                                                                                      |

The generated reference for every command is [docs/cli.md](docs/cli.md).

Lockstep dependency cohorts are governed by shared MSBuild properties in `Directory.Build.props` (`DotNetServicingVersion`, `AspireHostingVersion`, `IdentityModelVersion`, `DotNetAiVersion`, `OpenTelemetryVersion`, `OpenTelemetryInstrumentationVersion`, `MassTransitVersion`, `FluentValidationVersion`, consumed by `Directory.Packages.props`, `apps/apphost/src/Cadence.AppHost.csproj`, and `ValidateDependencyAlignment` in `Directory.Build.targets`) and by `catalog:` in `pnpm-workspace.yaml` (consumed as `"catalog:"` across workspace `package.json` manifests; ADR-2527). Where MSBuild property indirection or `pnpm` catalogs limit automated Dependabot edits:

- **NuGet / `.NET` cohort updates:** Bump the shared property in `Directory.Build.props` (and align `dotnet-ef` in `.config/dotnet-tools.json` and `docker/Dockerfile.dotnet` whenever `DotNetServicingVersion` changes), keep `<PackageVersion>` entries referencing `$(PropertyName)` in `Directory.Packages.props`, and run `dotnet restore Cadence.sln && dotnet build Cadence.sln` (`ValidateDependencyAlignment` fails with `CADDEP001`–`CADDEP003` on `.NET 11+` or split `IdentityModel`/`Aspire` resolutions).
- **Frontend `pnpm` catalog updates:** Bump the version once under `catalog:` in `pnpm-workspace.yaml`, keep `"catalog:"` specifiers in workspace `package.json` files, run `pnpm install` to refresh `pnpm-lock.yaml`, and verify with `pnpm install --frozen-lockfile && pnpm cadence check dependencies` plus `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter FullyQualifiedName~ContainerImagePinTests`.

Active CI workflow jobs in `.github/workflows/ci.yml` (`actionlint`, `lint-and-static`, `dotnet-quality`, `native-aot-build`, `docs-build`, `security`, `container-security`, `unit-tests`, `dotnet-tests`, `accessibility-e2e`, `compliance`, `ci-gate`, `classify`, `release-and-archive`, `deploy-docs`) and `.github/workflows/conflict-check.yml` (`conflict-check`) back two required GitHub Actions (`integration_id: 15368`) branch ruleset checks on `main` and `release/*` (`strict_required_status_checks_policy: true`): `CI Gate` and `Check for Merge Conflicts`, while `.github/rulesets/version_tags.json` enforces `creation`, `required_signatures`, `deletion`, and `non_fast_forward` on `refs/tags/v*` with `bypass_actors` pinned to release admins (`RepositoryRole` `5`, `bypass_mode: "always"`), and `.github/workflows/production-pipeline.yml` and `.github/workflows/deploy-docs.yml` accept `on: workflow_call` only from `ci.yml` ([docs/CI.md](docs/CI.md)). Verify parity with `pnpm cadence check rulesets`, preview updates with `pnpm cadence rulesets --dry-run`, and apply them with `pnpm cadence rulesets`.

## GxP compliance sync

`pnpm cadence gxp sync` validates a branch: it runs the tests (or reuses verified TRX reports and `*.database.txt`
evidence under `--results-dir <dir>`, as CI's `compliance` job does), reads the `@req:` tags and test results, writes
a preview of the RTM and IQ/OQ/PQ report to `TestResults/gxp/preview/` (gitignored), and fails on unknown
requirement IDs, failing tagged tests and skipped database tests. It writes and stages nothing under
`docs/SDLC`. Run it whenever you add or change requirement-tagged tests (`--dry-run` checks the tags and
integrity stamps without running tests).

`docs/SDLC/Requirements_Traceability_Matrix.md`, `docs/SDLC/IQ_OQ_PQ_Execution_Report.md` and
`docs/SDLC/runs/` are a snapshot, not feature-branch merge currency (ADR-2532):

- Never commit them on a feature branch and never resolve a conflict in them by hand. The `gxp-snapshot`
  gate (pre-commit, `pnpm cadence check`, CI) refuses such changes.
- On a conflict in them, or if your branch already changed them, take `main`'s copy and do not regenerate on the
  branch: `git checkout origin/main -- docs/SDLC/Requirements_Traceability_Matrix.md docs/SDLC/IQ_OQ_PQ_Execution_Report.md docs/SDLC/runs`.
- Only the integration owner refreshes them, on the integrated candidate against PostgreSQL:
  `CADENCE_GXP_SNAPSHOT=1 pnpm cadence gxp snapshot` (or `--results-dir <dir>` from verified CI PostgreSQL evidence in
  the gated release workflow) writes and stages them, and `--commit` also commits.
