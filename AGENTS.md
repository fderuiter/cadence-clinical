# Agent Guidelines: Cadence Clinical Research Software Platform

## Product Mission

Cadence Clinical Research Software is a unified, standalone eClinical platform synthesising
upstream Clinical Metadata Management (MDR) with downstream Electronic Data
Capture (EDC) into an automated Digital Data Flow (DDF) platform.

---

## Technical Stack & Standards (.NET 10 Target Architecture)

The Cadence platform has executed a clean-break architectural migration to **.NET 10 (C# 14 / ASP.NET Core 10)**. Existing Python implementations serve as the authoritative behavioral reference and anti-corruption baseline to be systematically transcribed and phased out.

| Concern            | Technology                                                                                                    |
| ------------------ | ------------------------------------------------------------------------------------------------------------- |
| Language & Runtime | **C# 14 / .NET 10** (`net10.0`, Native AOT ready)                                                             |
| Web API Framework  | **ASP.NET Core 10 Minimal APIs** with Endpoint Filters (`IEndpointFilter`)                                    |
| Data Modeling      | C# 14 `record` types, primary constructors, FluentValidation, `System.Text.Json` source generation            |
| Async HTTP         | `IHttpClientFactory` with typed HTTP clients and Polly resilience pipelines                                   |
| Code Style & Lints | **`.editorconfig` + `dotnet format` + Roslyn Analyzers** (`Microsoft.CodeAnalysis.NetAnalyzers`, `SonarAnalyzer`) |
| Designer DB        | Async **Neo4j.Driver** for .NET 10 (`apps/designer/`)                                                         |
| Execution DB       | **Entity Framework Core 10 + Npgsql** for PostgreSQL (`apps/execution/`) with `SaveChangesInterceptor`       |
| Dependency Mgmt    | **Central Package Management (CPM)** via `Directory.Packages.props` and `Directory.Build.props`               |
| Clinical Standards | CDISC USDM v3.0/v4.0, CDISC ODM XML/JSON                                                                      |
| GxP Audit Fields   | `CreatedAt`, `CreatedBy`, `ReasonForChange`, `VersionIndex` (21 CFR Part 11)                                  |
| CLI & DX Tooling   | Cadence CLI (`dotnet cadence`) in `packages/cli/` built with `Spectre.Console`                                 |

---

## Directory Target Rules for Generated Code

| Code type                      | Target directory                                                                           |
| ------------------------------ | ------------------------------------------------------------------------------------------ |
| Solution Root                  | `Cadence.sln`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`       |
| Study authoring / MDR logic    | `apps/designer/`                                                                           |
| Data capture / eCRF logic      | `apps/execution/`                                                                          |
| OIDC Auth & Gateway routers    | `apps/gateway/`                                                                            |
| Shared Domain & Core Ports     | `packages/engine_core/`, `packages/core-models/`                                            |
| Cross-Service API Contracts    | `packages/contracts/`                                                                      |
| Security & Gateway HMAC Tokens | `packages/security/`                                                                       |
| Centralized Test Fakes         | `packages/testing/`                                                                        |
| Unit & Architecture Tests      | `tests/` (`Cadence.UnitTests`, `Cadence.ArchitectureTests`)                                |
| Developer CLI & Interactive Wizards | `packages/cli/`, `scripts/`                                                           |
| Git Pre-Commit Quality Gates   | `.githooks/`                                                                               |
| Architecture Decision Records  | `docs/adr/`                                                                                |
| Migration Playbook             | `docs/MIGRATION_WORKFLOW.md`                                                               |
| GxP compliance docs            | `docs/SDLC/` (never edit manually — always via `scripts/sync_gxp.py`)                      |

---

## Python Reference & Transcription Matrix

> **Authoritative Playbook:** For the end-to-end 6-step service transcription recipe, project templates, and migration sequence, refer to `docs/MIGRATION_WORKFLOW.md`.

When transcribing existing Python modules into .NET 10 C# implementations, agents must strictly follow these direct paradigm equivalents:

| Python 3.14+ (Reference Pattern) | .NET 10 / C# 14 (Target Standard) | Migration Rule |
| :--- | :--- | :--- |
| `pydantic.BaseModel` | `public record Model(...)` with init properties | Use immutable records with primary constructors and `System.Text.Json` source generator attributes. |
| `FastAPI` APIRouter | `app.MapGroup("/api/v1/...")` | Use ASP.NET Core 10 Minimal APIs with `TypedResults` (`Ok<T>`, `NotFound`, `BadRequest<ProblemDetails>`). |
| `FastAPI Depends(...)` | ASP.NET Core Native DI (`[FromServices]`, constructor injection) | Inject scoped services/ports via endpoint parameters or primary constructors. |
| `FastAPI Middleware` | Endpoint Filters (`IEndpointFilter`) | Apply cross-cutting GxP concerns (HMAC auth, trace context) via filter chains. |
| `SQLAlchemy / SQLModel` | `Microsoft.EntityFrameworkCore` (`DbContext`) | Define domain configurations with Fluent API (`IEntityTypeConfiguration<T>`). |
| `.where(col.is_(True))` (E712) | `.Where(x => x.IsActive)` | EF Core evaluates strongly-typed boolean expressions to `IS TRUE` SQL on PostgreSQL. |
| `SQLAlchemy Session` | `IUnitOfWork` / `DbContext` | Encapsulate mutations in Unit of Work; apply `.AsNoTracking()` to all query-only read operations. |
| `HTTPX AsyncClient` | `IHttpClientFactory` + `HttpClient` | Use typed HTTP clients configured with transient fault retry policies. |
| `pytest` + `pytest-asyncio` | `xunit.v3` (`[Fact]`, `[Theory]`) | Use async `Task` test signatures with `FluentAssertions` assertions. |
| `pytest-archon` | `ArchUnitNET` / Roslyn Architecture Analyzers | Enforce hexagonal architecture and zero-cross-service database imports at build time. |
| `Ruff` (lint + format) | `dotnet format` + `.editorconfig` + Roslyn Analyzers | Code style and warnings are checked at build time; warnings treated as errors. |

---

## Critical Coding Patterns (C# 14 / .NET 10)

### 1. File-Scoped Namespaces & Using Directives Ordering

Every C# file must use modern **file-scoped namespaces**. Using directives must be placed outside the namespace and sorted strictly:
1. `System` and `System.*` namespaces — alphabetical
2. Third-party library namespaces — alphabetical
3. Solution first-party namespaces (`Cadence.*`) — alphabetical

```csharp
// 1. System usings
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// 2. Third-party usings
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

// 3. First-party Cadence usings
using Cadence.Core.Domain;
using Cadence.Execution.Domain.Models;
using Cadence.Security.Signing;

namespace Cadence.Execution.Endpoints;

public static class SubjectEndpoints
{
    // ...
}
```

---

### 2. Entity Framework Core 10 Query Rules & GxP Shadow Interceptor

> **GxP-critical.** Under 21 CFR Part 11, every entity state mutation must capture immutable audit fields. Read queries must never cause connection leaks or unindexed client-side evaluation.

1. **Read Queries Must Use `.AsNoTracking()`:**
   ```csharp
   // ✔ CORRECT — Zero tracking overhead for read operations
   var subjects = await dbContext.Subjects
       .AsNoTracking()
       .Where(s => s.SiteId == siteId && s.IsActive)
       .ToListAsync(cancellationToken);
   ```

2. **GxP Audit Interceptor (`ISaveChangesInterceptor`):**
   All `DbContext` registrations must include the Cadence GxP Audit Interceptor to automatically populate `CreatedAt`, `CreatedBy`, `ReasonForChange`, and `VersionIndex` before saving changes.

---

### 3. Nullable Reference Types & Immutability

The solution enforces `<Nullable>enable</Nullable>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.

- Use primary constructors for domain records and service classes.
- Use `init` property accessors on data transfer records.
- Never suppress nullable warnings with `#pragma warning disable CS8618` without documented GxP justification.

```csharp
// ✔ CORRECT — Strongly typed, immutable DTO with primary constructor
public record CreateSubjectCommand(
    string SiteId,
    string SubjectIdentifier,
    DateTime DateOfBirth,
    string ReasonForChange
);
```

---

### 4. Minimal API Route Definition & Typed Results

Avoid classic MVC controllers. Standardize exclusively on ASP.NET Core 10 Minimal APIs with strongly-typed endpoint results and endpoint filters:

```csharp
public static class ClinicalObservationEndpoints
{
    public static RouteGroupBuilder MapClinicalObservationEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/", CreateObservation)
            .WithName("CreateClinicalObservation")
            .WithSummary("Registers an eCRF clinical observation.")
            .AddEndpointFilter<GatewayHmacValidationFilter>()
            .Produces<ObservationDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return group;
    }

    public static async Task<Results<Created<ObservationDto>, BadRequest<ProblemDetails>>> CreateObservation(
        CreateObservationRequest request,
        [FromServices] IObservationApplicationService service,
        CancellationToken ct)
    {
        var result = await service.RecordObservationAsync(request, ct);
        return result.IsSuccess 
            ? TypedResults.Created($"/api/v1/observations/{result.Value.Id}", result.Value)
            : TypedResults.BadRequest(result.ToProblemDetails());
    }
}
```

---

### 5. Mandatory 21 CFR Part 11 Traceability in Tests

Every test validating a regulatory or system requirement must declare requirement traceability using **both** XML documentation comments and strongly-typed `[GxPRequirement]` attributes:

```csharp
[Fact]
[Trait("Category", "GxP_OQ")]
[GxPRequirement("PRD-SYS-053")]
public async Task RegisterSubject_WithValidPayload_PersistsAuditTrailAndAdvancesState()
{
    /// <summary>
    /// Validates that subject registration generates an immutable audit record with user identity.
    /// @req:PRD-SYS-053
    /// </summary>
    
    // Arrange
    var repository = new InMemorySubjectRepository();
    var subject = SubjectFactory.Create(siteId: "SITE-101", status: "ENROLLED");
    
    // Act
    await repository.SaveAsync(subject);
    
    // Assert
    var persisted = await repository.GetByIdAsync(subject.Id);
    persisted.Should().NotBeNull();
    persisted!.VersionIndex.Should().Be(1);
}
```

---

### 6. Centralized Test Infrastructure (`Cadence.Testing`)

When writing tests across microservices or libraries, agents must use `Cadence.Testing` rather than defining ad-hoc mock payloads:
1. **Domain Entity Factories:** Use `SubjectFactory`, `ObservationFactory`, `ProtocolDefinitionFactory` from `Cadence.Testing.Factories`.
2. **In-Memory Repository Fakes:** Use `InMemoryRepository<T>` from `Cadence.Testing.Fakes` to test application use cases without touching live databases.
3. **Security Test Context:** Use `TestSecurityContext.CreateHmacHeaders(...)` from `Cadence.Testing.Security`.

---

### 7. REST API-First Architecture & Microservice Decoupling

To ensure proper GxP boundaries across microservices:
1. **No Sibling Database References:** Cross-boundary project references to sibling service persistence layers are strictly prohibited.
2. **Cross-Service Communication via REST:** All cross-service operations must be executed via typed HTTP clients calling well-defined REST endpoints.
3. **HMAC Gateway Token Authentication:** Every cross-service HTTP request must include internal gateway signatures generated via `Cadence.Security.Signing`.

---

### 8. Architecture Unit Tests (`ArchUnitNET`) & Boundary Purity

Clean Architecture layers must maintain strict directional dependency flow:
- `Domain/` (and `Cadence.Core.Domain`) must have **zero** dependencies on `Infrastructure/`, `Endpoints/`, `Microsoft.AspNetCore`, or database drivers (`Npgsql`, `EntityFrameworkCore`).
- `Application/` depends only on `Domain/` and core ports; it must never reference `Endpoints/` or UI presentation logic.
- Every mutating entity must implement `IGxPAuditable`.
- Layer purity is enforced at build/test time via `tests/Cadence.ArchitectureTests.csproj`.

---

### 9. Cross-Service Communication via Typed Contracts (`Cadence.Contracts`)

Cross-service HTTP requests must never use raw, unvalidated string URLs:
- All service-to-service DTOs and client interfaces (e.g. `IExecutionApiClient`) must reside in `packages/contracts/`.
- Typed clients must be registered in DI using `IHttpClientFactory` and configured with `Polly` exponential backoff and transient circuit breakers.
- Every cross-service request must carry HMAC signatures generated via `Cadence.Security.Signing.HmacSigner`.

---

### 10. Database Schema Migrations & Tooling (`scripts/manage_migrations.ps1`)

Entity Framework Core migrations must follow a disciplined, audited change management protocol:
- Use `powershell .\scripts\manage_migrations.ps1 add <service> <MigrationName>` to generate migrations under `Infrastructure/Persistence/Migrations/`.
- Always generate idempotent SQL scripts (`scripts/manage_migrations.ps1 script <service>`) for DBA inspection and GxP qualification.
- Migrations must be verified in test suites using Testcontainers PostgreSQL instances.

---

### 11. Interactive Developer Wizards (`dotnet cadence wizard`)

Developers and agents should leverage the interactive wizard suite for operational tasks:
- `dotnet cadence wizard onboarding`: Guided tour for SDK, HTTPS dev certs, datastores, and `.env`.
- `dotnet cadence wizard migrate <service>`: Scaffolds Single-Assembly Clean Architecture layout and project boilerplate.
- `dotnet cadence wizard decommission <service>`: Guided 4-step sequence to retire and purge legacy Python code.

---

### 12. ADR Supersession & Living Documentation Lifecycle

Whenever a .NET 10 implementation supersedes a legacy Python architectural pattern:
1. Update the legacy ADR status in `docs/adr/` to `* **Status:** Superseded by [ADR-XXXX](path-to-new-adr.md)`.
2. Add the standard `> [!NOTE]` supersession callout at the top.
3. Annotate the entry in `docs/adr/index.md` with `*(Superseded by ADR-XXXX)*`.
4. Validate with `python scripts/validate_adrs.py`.
5. Update `ARCHITECTURE.md` service status and `CONTEXT.md` domain concepts.

---

### 13. Pure .NET Projects & uv Workspace Boundaries

When scaffolding a new .NET project under `packages/<name>` or `apps/<name>` that does not contain a `pyproject.toml`, agents must immediately add the directory path to `[tool.uv.workspace] exclude` in `pyproject.toml`. Without this exclusion, `uv`'s wildcard matching (`packages/*`, `apps/*`) fails during workspace dependency resolution.

---

### 14. Pre-Commit Quality Gate Multi-Runtime Invariant

The repository's `.pre-commit-config.yaml` runs both C# and Python quality gates locally before any commit is created:
1. `dotnet format Cadence.sln --verify-no-changes` verifies C# style, namespace ordering, and `.editorconfig` rules.
2. `python scripts/validate_adrs.py` verifies ADR status and index integrity.
3. `python scripts/validate_architecture_drift.py` ensures service topology parity.
Agents must ensure all code changes satisfy `dotnet format` and ADR validation prior to proposing commits.

---

## Primary Developer & Agent CLI Commands (`dotnet cadence`)

Agents should use the native Cadence CLI tool for development, diagnostics, and testing:

| Task                 | Command                                  | Description                                                        |
| :------------------- | :--------------------------------------- | :----------------------------------------------------------------- |
| Fast Unit Tests      | `dotnet cadence test --fast`             | Runs unit tests across all projects bypassing heavy containers     |
| Architecture Sentinels | `dotnet test tests/Cadence.ArchitectureTests.csproj` | Verifies Clean Architecture layer boundaries via ArchUnitNET |
| Integration Tests    | `dotnet cadence test --integration`      | Runs integration test suites with Testcontainers (PG & Neo4j)      |
| Code Formatting      | `dotnet format`                          | Verifies and auto-formats C# code against `.editorconfig` rules    |
| Developer Onboarding | `dotnet cadence wizard onboarding`       | Interactive .NET 10 setup, certs, datastores, and environment check |
| Service Migration    | `dotnet cadence wizard migrate <name>`   | Interactive scaffolding for Clean Architecture microservices       |
| Service Decommission | `dotnet cadence wizard decommission <name>`| Interactive retirement and safe purge of legacy Python code      |
| Manage Migrations    | `powershell .\scripts\manage_migrations.ps1 add <svc> <name>` | Scaffolds EF Core PostgreSQL migrations |
| Multi-Engine Seeding | `dotnet cadence db seed --tier full`     | Seeds multi-engine clinical test scenarios across Neo4j and PG     |
| GxP Sync             | `dotnet cadence gxp sync`                | Executes tests, parses TRX/JUnit XML, regenerates RTM docs        |

---

## GxP Compliance Sync Protocol

The CI `compliance` gate regenerates the Requirements Traceability Matrix (RTM) and diffs it against checked-in files.

### Running GxP Sync

```bash
python scripts/sync_gxp.py
```

This runs test execution, parses `@req:PRD-SYS-xxx` tags and `.trx` / JUnit results, regenerates `docs/SDLC/Requirements_Traceability_Matrix.md` and `docs/SDLC/IQ_OQ_PQ_Execution_Report.md`, and stages them in git.
