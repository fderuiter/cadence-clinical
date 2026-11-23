---
name: implement
description: "Implement a feature, bug fix, or refactor with strict adherence to Cadence .NET 10 coding standards, GxP traceability, and automated quality gates. Use when implementing approved plans, specs, or tickets."
license: MIT
metadata:
  author: Cadence Team
  version: "3.0"
---

# Cadence Implementation Workflow (.NET 10 / C# 14)

Executes code implementation tasks adhering to .NET 10 Clean Architecture standards, 21 CFR Part 11 requirements, and automated quality gates.

## Antigravity Tool Execution Matrix

| Phase                      | Antigravity Tool             | Invocation Pattern                                                                                         | Objective                                                                              |
| :------------------------- | :--------------------------- | :--------------------------------------------------------------------------------------------------------- | :------------------------------------------------------------------------------------- |
| **Context Review**         | `find_by_name` / `view_file` | Read relevant spec, records, and tests via `AbsolutePath`                                                  | Map dependencies and domain invariants                                                 |
| **Surgical Editing**       | `replace_file_content`       | Target: C# file, single contiguous chunk                                                                   | Implement changes with strict nullable typing                                          |
| **File Creation**          | `write_to_file`              | Target: new module/test, with complete contents                                                            | Scaffold new records, services, or test suites                                         |
| **Format Check**           | `run_command`                | `CommandLine="dotnet format Cadence.sln --verify-no-changes"`, `WaitMsBeforeAsync=5000`                    | Verify .editorconfig styling                                                           |
| **Auto-Remediate**         | `run_command`                | `CommandLine="dotnet format Cadence.sln"`, `WaitMsBeforeAsync=5000`                                        | Auto-format whitespace & namespace order                                               |
| **Fast Tests**             | `run_command`                | `CommandLine="dotnet test --solution Cadence.sln --filter \"Category!=Integration\" --ignore-exit-code 8"` | Run fast unit tests                                                                    |
| **Architecture Sentinels** | `run_command`                | `CommandLine="dotnet test --project tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj"`     | Verify Clean Architecture layer purity                                                 |
| **Compliance Sync**        | `run_command`                | `CommandLine="pnpm cadence gxp sync"`                                                                      | Validate requirement tags and results (preview only; never commit `docs/SDLC` reports) |
| **Walkthrough Report**     | `write_to_file`              | Target: `<appDataDir>/brain/<conversation-id>/walkthrough.md`                                              | Emit completion artifact with file links                                               |

---

## 1. Pre-Implementation Review

- Consult [`docs/MIGRATION_WORKFLOW.md`](../../../../../docs/MIGRATION_WORKFLOW.md) and [`CONTEXT.md`](../../../../../CONTEXT.md).
- Target directories:
  - `apps/designer/`: Protocol design / CDISC USDM / Neo4j graph.
  - `apps/execution/`: EDC runtime / eCRF observations / PostgreSQL EF Core 10.
  - `apps/gateway/`: YARP reverse proxy, OIDC, and HMAC authentication.
  - `packages/contracts/`: Shared cross-service DTOs and Polly HTTP clients.
  - `packages/engine_core/`: Core domain aggregates, `IGxPAuditable`, and `Result<T>`.
  - `packages/testing/`: Centralized test fakes and fixtures.
  - `packages/security/`: HMAC token signing and verification.

---

## 2. Implementation Execution Standards

### C# 14 / .NET 10 Coding Rules

1. **File-Scoped Namespaces**: Always use `namespace Cadence.<Layer>;`.
2. **Using Directives Ordering**:
   - `System` and `System.*` (alphabetical)
   - Third-party packages (alphabetical)
   - First-party `Cadence.*` packages (alphabetical)
3. **Immutability & Nullability**:
   - `<Nullable>enable</Nullable>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` are active.
   - Use `record` types with primary constructors and `init` properties.
   - Return `Result<TValue, DomainError>` monads; avoid throwing exceptions for domain logic.
4. **Entity Framework Core 10**:
   - Apply `.AsNoTracking()` to all query-only read operations.
   - Attach `GxPAuditInterceptor` on mutating `DbContext` instances to compute SHA-256 Merkle block hashes.
5. **Mandatory 21 CFR Part 11 Traceability**:
   - Annotate all test methods with XML doc comments (`/// @req:PRD-SYS-XXX`) and `[GxPRequirement("PRD-SYS-xxx")]` attributes.

---

## 3. Mandatory Quality Gate Sequence

Before concluding any implementation turn, execute the quality gates:

```bash
# 1. Format code according to .editorconfig
dotnet format Cadence.sln

# 2. Build solution in Release configuration
dotnet build Cadence.sln --configuration Release

# 3. Verify architecture sentinels and unit tests
dotnet test --solution Cadence.sln

# 4. Validate GxP requirement tags and results (writes a preview; commit nothing under docs/SDLC)
pnpm cadence gxp sync
```

---

## 4. Produce Antigravity Walkthrough Artifact (MANDATORY TOOL: `write_to_file`)

Record changes in `<appDataDir>/brain/<conversation-id>/walkthrough.md` via `write_to_file`:

- Multi-component summary of files created/modified with clickable `[basename](file:///path/to/file)` links.
- Verification results table showing test outcomes and sentinel checks.
- Key GxP requirement IDs satisfied (`PRD-SYS-xxx`).
