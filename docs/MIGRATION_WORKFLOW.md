# Cadence Clinical Platform: .NET 10 Migration Workflow & Playbook

**Target Architecture:** .NET 10 (C# 14 / ASP.NET Core 10 Minimal APIs)
**Governing ADRs:** [ADR-2233](adr/2026-09-02-migration-to-dotnet-10-architecture-and-standards.md), [ADR-2428](adr/2026-09-02-core-service-architecture-cqrs-transactional-outbox-and-gxp-interceptor-standard.md)
**Regulatory Standards:** 21 CFR Part 11, ICH GCP E6(R2)

---

## 1. Executive Roadmap & Phased Execution Sequence

The Cadence platform transitions from Python 3.14+ to .NET 10 via a **tracer-bullet migration strategy**. The upstream-to-downstream Digital Data Flow (DDF) backbone is migrated first to validate end-to-end clinical operations, followed by regulatory operations, and finalized with integration services.

```text
┌────────────────────────────────────────────────────────────────────────┐
│ Phase 1: Core DDF Tracer Bullet                                        │
│  - Cadence.Apps.Gateway     (Reverse proxy, HMAC validation, OIDC)     │
│  - Cadence.Apps.Execution   (EDC core: subjects, observations, eCRF)   │
│  - Cadence.Apps.Designer    (CDISC USDM metadata)                      │
└────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│ Phase 2: Clinical Operations & Regulatory Governance                   │
│  - Cadence.Apps.Ctms        (Milestones, site monitoring, greenlight)  │
│  - Cadence.Apps.Etmf / Eisf (Trial Master File, regulatory binders)    │
│  - Cadence.Apps.Quality     (CAPA, deviations, root-cause analysis)    │
│  - Cadence.Apps.Safety      (Adverse events, SUSAR, safety reporting)  │
└────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│ Phase 3: Interoperability & Specialized Services                       │
│  - Cadence.Apps.AiGateway   (Medical coding proxy, MedDRA / WHODrug)   │
│  - Cadence.Apps.Interop     (FHIR eSource, CDISC ODM connectors)      │
│  - Cadence.Apps.Notifications, Econsent, Payments, Tickets, Fileshare  │
└────────────────────────────────────────────────────────────────────────┘
```

---

## 2. The 6-Step Service Transcription SOP

For every microservice (`apps/<service>`), engineers and AI agents must follow this exact 6-step sequence to ensure architectural consistency and regulatory compliance:

```text
[Step 1: Domain] ──▶ [Step 2: Persistence] ──▶ [Step 3: Application]
        │                     │                       │
        ▼                     ▼                       ▼
  Entities & Invariants   EF Core & Interceptor   Services & Validation
                                                      │
[Step 6: GxP Sync] ◀── [Step 5: Testing] ◀── [Step 4: Endpoints]
        │                     │                       │
        ▼                     ▼                       ▼
   Regenerate RTM         xUnit & Attributes      Minimal APIs & Filters
```

---

### Step 1: Pure Domain Modeling (`Domain/`)

- Create aggregates, entities, and value objects inheriting from `AggregateRoot<TId>` or implementing `IGxPAuditable`.
- Pure C# 14 only — **zero** dependencies on EF Core, ASP.NET Core, or third-party web packages.
- Enforce domain invariants through private setters and factory methods.

```csharp
namespace Cadence.Apps.Execution.Domain;

using System;
using Cadence.Core.Domain;

public sealed class Subject : AggregateRoot<string>
{
    public string SiteId { get; private set; } = string.Empty;
    public string SubjectIdentifier { get; private set; } = string.Empty;
    public string Status { get; private set; } = "SCREENED";

    private Subject() : base() { }

    public static Result<Subject> Register(string siteId, string identifier, string createdBy, string reason)
    {
        if (string.IsNullOrWhiteSpace(siteId))
            return DomainError.Validation("ERR_SITE_REQUIRED", "Site ID is required.");

        var subject = new Subject
        {
            Id = Guid.NewGuid().ToString("D"),
            SiteId = siteId,
            SubjectIdentifier = identifier,
            CreatedBy = createdBy,
            ReasonForChange = reason,
            VersionIndex = 1
        };
        return subject;
    }
}
```

---

### Step 2: Persistence & GxP Interceptor (`Infrastructure/`)

- Configure Entity Framework Core 10 Fluent API (`IEntityTypeConfiguration<T>`).
- Register `GxPAuditInterceptor` in `DbContextOptions` to automatically compute SHA-256 Merkle hashes and update version indices.
- Apply `.AsNoTracking()` to all query-only operations.

```csharp
namespace Cadence.Apps.Execution.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Cadence.Apps.Execution.Domain;
using Cadence.Database;

public class ExecutionDbContext(DbContextOptions<ExecutionDbContext> options) : DbContext(options)
{
    public DbSet<Subject> Subjects => Set<Subject>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Subject>(builder =>
        {
            builder.ToTable("subjects");
            builder.HasKey(s => s.Id);
            builder.Property(s => s.SiteId).IsRequired().HasMaxLength(64);
            builder.Property(s => s.SubjectIdentifier).IsRequired().HasMaxLength(64);
            builder.Property(s => s.Status).IsRequired().HasMaxLength(32);
        });
    }
}
```

---

### Step 3: Application Services & Validation (`Application/`)

- Define typed service interfaces (e.g. `ISubjectApplicationService`) and implementations.
- Use primary constructors for explicit dependency injection (100% Native AOT ready).
- Return strongly-typed `Result<TValue>` monads for domain rule validation.

```csharp
namespace Cadence.Apps.Execution.Application;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Cadence.Apps.Execution.Domain;
using Cadence.Apps.Execution.Infrastructure;
using Cadence.Core.Domain;

public interface ISubjectApplicationService
{
    Task<Result<SubjectDto>> RegisterSubjectAsync(RegisterSubjectCommand command, CancellationToken ct);
}

public record RegisterSubjectCommand(string SiteId, string Identifier, string User, string Reason);
public record SubjectDto(string Id, string SiteId, string Identifier, string Status, long VersionIndex);
```

---

### Step 4: Minimal API Route Mapping & Security (`Endpoints/`)

- Define route groups using ASP.NET Core 10 Minimal APIs (`MapGroup("/api/v1/...")`).
- Apply `GatewayHmacValidationFilter` to verify internal HMAC signatures.
- Return strongly-typed `Results<Ok<T>, ProblemHttpResult>`.

```csharp
namespace Cadence.Apps.Execution.Endpoints;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Cadence.Apps.Execution.Application;

public static class SubjectEndpoints
{
    public static RouteGroupBuilder MapSubjectEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/subjects", async (RegisterSubjectCommand cmd, ISubjectApplicationService service, CancellationToken ct) =>
        {
            var result = await service.RegisterSubjectAsync(cmd, ct);
            return result.IsSuccess
                ? TypedResults.Ok(result.Value)
                : TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: result.Error!.Description);
        })
        .WithName("RegisterSubject")
        .WithSummary("Registers a new subject under the active clinical protocol.");

        return group;
    }
}
```

---

### Step 5: Unit & Integration Testing (`tests/`)

- Test application workflows using `xunit.v3`, `FluentAssertions`, and `InMemoryRepository<T>`.
- A test that verifies a requirement declares its ID on the test method, with `[GxPRequirement("PRD-...")]`
  or an `/// @req:` line; one form is enough. Tag only what the test's assertions prove, and follow the
  rules in [TESTING.md](TESTING.md).

```csharp
namespace Cadence.UnitTests;

using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Cadence.Testing;

public class SubjectRegistrationTests
{
    /// <summary>
    /// Verifies that subject registration initializes the GxP audit trail at version 1.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-001")] // audit fields; see docs/TESTING.md before choosing an ID
    public async Task RegisterSubject_WithValidData_IncrementsVersionIndex()
    {
        // Assertions ...
    }
}
```

---

### Step 6: Regulatory Traceability & Documentation Synchronization

Regulatory compliance under 21 CFR Part 11 requires that all architectural evolutions, domain models, and test assertions stay synchronized across documentation without drift:

#### 6.1: GxP Traceability Matrix Sync

After adding or modifying tests, parse test execution output and regenerate the Requirements Traceability Matrix:

```bash
python scripts/sync_gxp.py
```

Verify that `docs/SDLC/Requirements_Traceability_Matrix.md` updates cleanly with zero unmapped requirements.

#### 6.2: Legacy ADR Supersession & Index Updating

When transcribing a service or pattern that supersedes a prior architectural decision:

1. Locate the legacy ADR in `docs/adr/`.
2. Update the status header:
   ```markdown
   - **Status:** Superseded by [ADR-XXXX](path-to-new-adr.md)
   ```
3. Add a standard supersession notice callout at the top:
   ```markdown
   > [!NOTE]
   > **Superseded Notice (YYYY-MM-DD):** This record documented the legacy Python implementation. As of YYYY-MM-DD, it has been formally superseded for .NET 10 by [ADR-XXXX](path-to-new-adr.md). Refer to `docs/MIGRATION_WORKFLOW.md` for current implementation standards.
   ```
4. Annotate the listing in `docs/adr/index.md` with `*(Superseded by ADR-XXXX)*`.
5. Validate via `python scripts/validate_adrs.py` to confirm zero broken links or invalid tags.

#### 6.3: System Architecture & Domain Glossary Sync

1. **`ARCHITECTURE.md`**: Update the service architecture section to reflect .NET 10 Minimal APIs, active port, and EF Core persistence.
2. **`CONTEXT.md`**: Refine domain terms to reflect C# 14 / .NET 10 implementation patterns and primary constructor models.

---

## 3. The Decommissioning & Cleanup Protocol ("Building the New & Clearing Out the Old")

To ensure an orderly, clean-break transition without accumulating technical debt or orphaned Python files, every migrated microservice must follow this 4-step decommissioning sequence:

```text
[1. Behavioral Parity] ──▶ [2. Gateway Cutover] ──▶ [3. Python Purge] ──▶ [4. Workspace Unlink]
```

### 1. Behavioral Parity & Schema Verification

- Validate that the new .NET 10 Minimal API endpoint schemas (`/openapi/v1.json`) match or cleanly supersede the legacy Python FastAPI schemas.
- Verify that 100% of regulatory requirement tags (`@req:PRD-SYS-XXX`) tested in the Python test suite are covered in the new xUnit test suite with equal or greater test assertion coverage.

### 2. Gateway Route Cutover

- In `apps/gateway` (YARP reverse proxy), redirect the upstream routing destination for the service route prefix to the active .NET 10 microservice port:
  - Gateway: `8000`
  - Designer: `8001`
  - Execution: `8002`
  - eTMF: `8003`
  - Interop: `8004`
  - Quality: `8005`
  - Notifications: `8006`
  - CTMS: `8007`
  - Safety: `8008`
- Execute live end-to-end regression tests through the gateway to confirm zero regression.

### 3. Deprecated Python Source Purge

- Delete the legacy Python implementation files within the service directory:
  - Remove `apps/<service>/src/cadence/<service>/`
  - Remove `apps/<service>/tests/` (once ported to `tests/`)
  - Remove legacy Alembic migration files (`apps/<service>/alembic/`) superseded by EF Core migrations.

### 4. Inventory Update & Verification

- Remove the file's row from [PYTHON_REFERENCE_INVENTORY.md](PYTHON_REFERENCE_INVENTORY.md); when a package has no
  Python left, delete its `__init__.py` files and `pyproject.toml` and its README's reference-sources line.
- Execute `pnpm cadence check` and `pnpm cadence gxp sync` to confirm clean builds and 100% RTM compliance.

---

## 4. Developer & Agent Command Reference

| Task                      | Command                                                    | Description                                                    |
| :------------------------ | :--------------------------------------------------------- | :------------------------------------------------------------- |
| **Local Host Setup**      | `powershell .\scripts\setup_dotnet_env.ps1`                | Provisions SDK, global tools, HTTPS certs, and .env on Windows |
| **Linux/macOS Setup**     | `bash scripts/setup_dotnet_env.sh`                         | Provisions SDK, tools, and certs on POSIX systems              |
| **Build Solution**        | `dotnet build Cadence.sln`                                 | Compiles all apps, packages, and test projects                 |
| **Format Verification**   | `dotnet format Cadence.sln --verify-no-changes`            | Enforces `.editorconfig` rules (fails on deviations)           |
| **Auto-Format Code**      | `dotnet format Cadence.sln`                                | Automatically fixes whitespace and namespace formatting        |
| **Run Fast Unit Tests**   | `dotnet test Cadence.sln --filter "Category!=Integration"` | Runs in-memory unit tests                                      |
| **Run Integration Tests** | `dotnet test Cadence.sln --filter "Category=Integration"`  | Runs Testcontainers PostgreSQL tests                           |
| **ADR Validation**        | `python scripts/validate_adrs.py`                          | Validates ADR index formatting and requirement tags            |
| **GxP Compliance Sync**   | `python scripts/sync_gxp.py`                               | Parses tests, extracts `@req` tags, and updates SDLC docs      |
| **Docker Dev Stack**      | `docker compose -f docker/docker-compose.yml up -d`        | Starts unprofiled operational core stack (add `--profile storage` for MinIO/ClamAV) |
