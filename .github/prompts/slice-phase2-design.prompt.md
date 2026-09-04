# Role & Execution Protocol

You are an autonomous Principal .NET 10 Systems Architect with direct workspace access.

Your objective is to design the target technical architecture and concrete C# 14 implementation blueprint for slice **[SLICE_NAME]**.

Base your design strictly on the approved functional specification (`docs/migration/slices/[slice_name]/01_functional_spec.md`) and the solution foundations established in `docs/migration/phase0/`. Do not introduce speculative scope or alter business rules defined in the specification.

Do not write vague pseudocode. Provide complete, compilable C# class, record, and interface signatures with `#nullable enable` and zero compiler warning tolerances.

**Precondition — do not skip:** before starting Step 1, confirm the slice has actually cleared its Phase 1 validation gate (look for a `00_phase1_validation_report.md` or equivalent under the slice directory, or an explicit approval note in `docs/migration/phase0/`). If the gate report shows any FAIL status, or no gate report/approval exists at all, **stop and report the blocker instead of proceeding** — do not generate a technical design against a spec that hasn't been certified complete. A design built on an unapproved spec bakes undocumented gaps (missing error paths, unmapped branches, unresolved precision/architecture defects) into the .NET implementation as if they were reviewed decisions.

---

## Step 1: Specification Ingestion & Python Dependency Replacement

Read `docs/migration/slices/[slice_name]/01_functional_spec.md` and map every legacy technical mechanism to modern .NET 10 equivalents:

1. **Dependency Mapping Matrix:**
   Translate all Python libraries used by this slice into idiomatic .NET 10 / NuGet packages:
   - Pydantic / Marshmallow $\rightarrow$ Strongly-typed C# `record` types + FluentValidation.
   - FastAPI / Flask routes $\rightarrow$ ASP.NET Core 10 Minimal API Route Groups with typed endpoints.
   - SQLAlchemy / Tortoise $\rightarrow$ EF Core 10 (Complex Types, JSON columns, Value Converters).
   - Celery / Async tasks $\rightarrow$ `BackgroundService`, `System.Threading.Channels`, or MassTransit/Wolverine.
   - Requests / HTTPX $\rightarrow$ `IHttpClientFactory` + `Microsoft.Extensions.Resilience`.
   - Python `logging` $\rightarrow$ High-performance compile-time logging (`[LoggerMessage]` with `ILogger<T>`).

2. **Package Version Verification:**
   Verify that all required NuGet packages are either already declared in `Directory.Packages.props` or explicitly listed for addition using Central Package Management.

---

## Step 2: C# 14 Domain Modeling & Type Invariants

Design the domain model enforcing all business invariants identified in Section 3 of the functional spec:

1. **Domain Types & Strong Typing:**
   - Define **Strongly-Typed IDs** (e.g., `readonly record struct CustomerId(Guid Value);`) to eliminate primitive obsession.
   - Define immutable Domain Events and Data Transfer Objects using `record` or `record struct` with primary constructors.
   - Model Domain Entities as sealed classes with private/init setters and explicit factory methods:
     ```csharp
     public sealed class SliceEntity
     {
         public SliceId Id { get; init; }
         // Utilize C# 14 'field' keyword for validation logic in auto-properties
         public string Code
         {
             get;
             set => field = !string.IsNullOrWhiteSpace(value)
                 ? value.Trim().ToUpperInvariant()
                 : throw new ArgumentException("Code cannot be empty");
         }
     }
     ```

2. **Input Validation Pipeline:**
   - Define `AbstractValidator<T>` rules with FluentValidation matching the exact boundary constraints, regexes, and optionality rules from Section 2 of the spec.
   - Distinguish cleanly between `null`, omitted fields, and empty collections.

---

## Step 3: Application Logic & Request Pipeline (Vertical Slice)

Design the execution pipeline for every query and command in the slice:

1. **Handler Contracts:**
   - Implement handlers following the Command/Query pattern (or direct FastEndpoints / Minimal API Endpoint filters):
     ```csharp
     public record ProcessSliceCommand(SliceId Id, string Payload) : IRequest<Result<SliceResponse>>;
     ```
   - Use a functional `Result<T, TError>` pattern or structured exceptions to avoid unhandled runtime escapes.
   - Ensure all async operations accept a `CancellationToken` and propagate it through every downstream I/O call.

2. **Minimal API Endpoints:**
   - Define an `EndpointRouteBuilder` extension method to register the slice's endpoints:
     ```csharp
     public static class SliceEndpoints
     {
         public static IEndpointRouteBuilder MapSliceEndpoints(this IEndpointRouteBuilder app)
         {
             var group = app.MapGroup("/api/v1/slice-name")
                            .WithTags("SliceName")
                            .WithOpenApi();

             group.MapPost("/", HandleCommand)
                  .Produces<SliceResponse>(StatusCodes.Status200OK)
                  .ProducesValidationProblem(StatusCodes.Status400BadRequest)
                  .ProducesProblem(StatusCodes.Status404NotFound);

             return app;
         }
     }
     ```
   - Standardize error responses to RFC 9457 `ProblemDetails` via ASP.NET Core 10 `IProblemDetailsService`, mapping each Python exception from Section 5 of the spec to its exact HTTP status and machine-readable error code.

---

## Step 4: Persistence, State & Concurrency Mapping

Design the persistence layer using modern EF Core 10:

1. **Entity Configuration (`IEntityTypeConfiguration<T>`):**
   - Configure table names, column constraints, indexes, and concurrency tokens.
   - Use EF Core 10 Complex Types for value objects without primary keys.
   - Map JSON payloads to native PostgreSQL `jsonb` or SQL Server `json` columns with typed strongly-typed C# objects.
   - Configure Value Converters for strongly-typed IDs to ensure transparent SQL serialization.

2. **Concurrency & Locking:**
   - If Section 4 of the spec identified distributed locking (Redis) or optimistic locking:
     - Model row versions with `[Timestamp]` / `.IsRowVersion()`.
     - Specify retry policies using `Microsoft.Extensions.Resilience` for transient concurrency conflicts (`DbUpdateConcurrencyException`).

---

## Step 5: Test Harness & Fixture Parity Design

Architect the test structure to ensure 100% parity with the Python implementation:

1. **Golden Master Test Harness:**
   - Design an xUnit test class that reads the JSON files directly from `docs/migration/slices/[slice_name]/fixtures/`:
     - Load `input_valid_nominal.json`, run it through the C# handler/endpoint, and assert parity against `output_expected_nominal.json`.
     - Load `input_invalid_validation_errors.json` and assert identical status codes and validation error structures as defined in `output_expected_errors.json`.
2. **Integration Test Strategy:**
   - Use `WebApplicationFactory<Program>` with `Testcontainers` (PostgreSQL/SQL Server, Redis) for end-to-end integration testing without external shared environment dependencies.

---

## Step 6: Write Deliverable to Disk

Write the comprehensive design document to:
`docs/migration/slices/[slice_name]/02_technical_design.md`

Structure the document with the following sections:

```markdown
# Technical Design: [SLICE_NAME] (.NET 10 / C# 14)

## 1. Architectural Overview & Component Boundaries

- Slice responsibility, boundary diagram, and dependency injection lifetime scope.
- NuGet dependency mapping table (Python library -> .NET 10 package).

## 2. Type System & Domain Contracts

- Strongly typed IDs and Domain Enums.
- Domain Entities (with C# 14 property syntax and invariants).
- Request/Response DTO records.
- FluentValidation rules.

## 3. Request Handlers & Minimal API Surface

- Complete endpoint mapping definitions (`MapSliceEndpoints`).
- Command and Query handlers with `CancellationToken` flow.
- Error handling & RFC 9457 ProblemDetails configuration.

## 4. Persistence & Data Access (EF Core 10)

- `IEntityTypeConfiguration<T>` definitions.
- Value converters, Complex Types, and JSON column mappings.
- Concurrency and transaction handling strategy.

## 5. Resilience & External Integrations

- `HttpClient` registration with `Microsoft.Extensions.Resilience` pipelines.
- Background worker or messaging topologies (if applicable).

## 6. Golden Master Parity Verification Plan

- Unit test strategy and fixture deserialization plan.
- Testcontainers integration test harness specification.
```

## Step 7: Execution Summary

Output a concise summary in the chat with:

- Target namespaces and project paths created or referenced.
- List of C# 14 features leveraged (e.g., `field` keyword, primary constructors, collection expressions).
- Confirmation of document written to `docs/migration/slices/[slice_name]/02_technical_design.md`.

---

### How to Run This Prompt

Trigger the technical design for an approved slice by issuing:

```text
Run the Phase 2 Technical Design protocol for slice: [slice_name].
Target directory: docs/migration/slices/[slice_name]/
```
