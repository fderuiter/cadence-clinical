# Cadence Clinical Platform: .NET 10 Migration Workflow & Playbook

**Target Architecture:** .NET 10 (C# 14 / ASP.NET Core 10 Minimal APIs)  
**Governing ADRs:** [ADR-2233](file:///d:/Users/Fred/Documents/GitHub/cadence-clinical/docs/adr/2026-09-02-migration-to-dotnet-10-architecture-and-standards.md), [ADR-2234](file:///d:/Users/Fred/Documents/GitHub/cadence-clinical/docs/adr/2026-09-02-core-service-architecture-cqrs-transactional-outbox-and-gxp-interceptor-standard.md)  
**Regulatory Standards:** 21 CFR Part 11, ICH GCP E6(R2), PRD-SYS-053  

---

## 1. Executive Roadmap & Phased Execution Sequence

The Cadence platform transitions from Python 3.14+ to .NET 10 via a **tracer-bullet migration strategy**. The upstream-to-downstream Digital Data Flow (DDF) backbone is migrated first to validate end-to-end clinical operations, followed by regulatory operations, and finalized with integration services.

```text
┌────────────────────────────────────────────────────────────────────────┐
│ Phase 1: Core DDF Tracer Bullet                                        │
│  - Cadence.Apps.Gateway     (Reverse proxy, HMAC validation, OIDC)     │
│  - Cadence.Apps.Execution   (EDC core: subjects, observations, eCRF)   │
│  - Cadence.Apps.Designer    (CDISC USDM metadata, Neo4j study graph)   │
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
* Create aggregates, entities, and value objects inheriting from `AggregateRoot<TId>` or implementing `IGxPAuditable`.
* Pure C# 14 only — **zero** dependencies on EF Core, ASP.NET Core, or third-party web packages.
* Enforce domain invariants through private setters and factory methods.

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
* Configure Entity Framework Core 10 Fluent API (`IEntityTypeConfiguration<T>`).
* Register `GxPAuditInterceptor` in `DbContextOptions` to automatically compute SHA-256 Merkle hashes and update version indices.
* Apply `.AsNoTracking()` to all query-only operations.

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
* Define typed service interfaces (e.g. `ISubjectApplicationService`) and implementations.
* Use primary constructors for explicit dependency injection (100% Native AOT ready).
* Return strongly-typed `Result<TValue>` monads for domain rule validation.

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
* Define route groups using ASP.NET Core 10 Minimal APIs (`MapGroup("/api/v1/...")`).
* Apply `GatewayHmacValidationFilter` to verify internal HMAC signatures.
* Return strongly-typed `Results<Ok<T>, ProblemHttpResult>`.

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
* Test application workflows using `xunit.v3`, `FluentAssertions`, and `InMemoryRepository<T>`.
* Every test validating a regulatory or functional requirement **must** declare:
  1. XML doc comment with `/// @req:PRD-SYS-xxx`
  2. `[GxPRequirement("PRD-SYS-xxx")]` custom attribute

```csharp
namespace Cadence.UnitTests;

using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Cadence.Testing;

public class SubjectRegistrationTests
{
    [Fact]
    [Trait("Category", "Unit")]
    [GxPRequirement("PRD-SYS-053")]
    public async Task RegisterSubject_WithValidData_IncrementsVersionIndex()
    {
        /// <summary>
        /// Verifies that subject registration initializes the GxP audit trail at version 1.
        /// @req:PRD-SYS-053
        /// </summary>
        
        // Assertions ...
    }
}
```

---

### Step 6: Regulatory Traceability Synchronization
After adding or modifying tests, regenerate the Requirements Traceability Matrix (RTM):

```bash
python scripts/sync_gxp.py
```

Verify that `docs/SDLC/Requirements_Traceability_Matrix.md` updates cleanly and commit all changes.

---

## 3. Developer & Agent Command Reference

| Task | Command | Description |
| :--- | :--- | :--- |
| **Local Host Setup** | `powershell .\scripts\setup_dotnet_env.ps1` | Provisions SDK, global tools, HTTPS certs, and .env on Windows |
| **Linux/macOS Setup** | `bash ./scripts/setup_dotnet_env.sh` | Provisions SDK, tools, and certs on POSIX systems |
| **Build Solution** | `dotnet build Cadence.sln` | Compiles all apps, packages, and test projects |
| **Format Verification** | `dotnet format Cadence.sln --verify-no-changes` | Enforces `.editorconfig` rules (fails on deviations) |
| **Auto-Format Code** | `dotnet format Cadence.sln` | Automatically fixes whitespace and namespace formatting |
| **Run Fast Unit Tests** | `dotnet test Cadence.sln --filter "Category!=Integration"` | Runs in-memory unit tests |
| **Run Integration Tests** | `dotnet test Cadence.sln --filter "Category=Integration"` | Runs Testcontainers PostgreSQL & Neo4j tests |
| **ADR Validation** | `python scripts/validate_adrs.py` | Validates ADR index formatting and requirement tags |
| **GxP Compliance Sync** | `python scripts/sync_gxp.py` | Parses tests, extracts `@req` tags, and updates SDLC docs |
| **Docker Dev Stack** | `docker compose --profile dotnet up -d` | Starts .NET container with hot reload and live datastores |
