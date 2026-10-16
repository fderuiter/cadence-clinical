# .NET 10 Solution Scaffold Guide

**Project:** Cadence Clinical Research Software Platform
**Target Runtime:** .NET 10 (C# 14 / ASP.NET Core 10 Minimal APIs)
**Governing ADRs:** [ADR-2233](../adr/2026-09-02-migration-to-dotnet-10-architecture-and-standards.md), [ADR-2428](../adr/2026-09-02-core-service-architecture-cqrs-transactional-outbox-and-gxp-interceptor-standard.md)
**Script Location:** `scripts/scaffold_dotnet_solution.sh`

---

## 1. Overview

The Cadence Clinical platform provides an automated, idempotent scaffolding script that generates the complete .NET 10 solution topology across all 18 microservices and core shared packages, binding them into `Cadence.sln` under Central Package Management (CPM).

---

## 2. Prerequisites

1. **.NET 10 SDK:**
   Verify your active .NET SDK:
   ```bash
   dotnet --version
   ```
   Must be `.NET 10.0.100` or preview equivalent matching `global.json`.
2. **SDK HTTPS Development Certificate:**
   ```bash
   dotnet dev-certs https --trust
   ```
3. **CPM Configuration:**
   Ensure `Directory.Build.props` and `Directory.Packages.props` exist at the solution root.

---

## 3. Running the Scaffold

Execute the scaffold script directly from the repository root:

```bash
./scripts/scaffold_dotnet_solution.sh
```

### Automated Scaffold Steps

The script performs 6 deterministic verification steps:

1. **Toolchain Verification:** Confirms `dotnet` CLI is available and reports the active SDK version.
2. **CPM & Global Props Validation:** Ensures `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`, `<Nullable>enable</Nullable>`, and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
3. **Clean Architecture Layer Scaffolding:** Generates `Domain/`, `Application/`, `Infrastructure/`, and `Endpoints/` directories and the C# project file (`Cadence.Apps.<Service>.csproj`) for all 18 services:
   - `execution`, `designer`, `gateway`, `ctms`, `etmf`, `eisf`, `quality`, `safety`, `interop`, `notifications`, `econsent`, `org`, `payments`, `tickets`, `fileshare`, `imaging`, `knowledge`, `ai_gateway`.
4. **Solution Binding:** Binds all core packages (`Packages/`), microservices (`Apps/`), and test suites (`Tests/`) into `Cadence.sln`.
5. **Python `uv` Workspace Isolation:** Ensures that newly scaffolded pure .NET projects do not break `uv` workspace resolution in `pyproject.toml`.
6. **Solution Build Verification:** Executes `dotnet build Cadence.sln --no-incremental` to ensure zero compilation or Roslyn analyzer errors.

---

## 4. Scaffold Layout Structure

```text
Cadence.sln
├── Solution Items/
│   ├── .editorconfig
│   ├── Directory.Build.props
│   ├── Directory.Packages.props
│   └── global.json
├── Packages/
│   ├── Cadence.Core.Domain    (packages/engine_core/src/Cadence.Core.Domain.csproj)
│   ├── Cadence.Security       (packages/security/src/Cadence.Security.csproj)
│   ├── Cadence.Contracts      (packages/contracts/src/Cadence.Contracts.csproj)
│   ├── Cadence.Database       (packages/database/src/Cadence.Database.csproj)
│   ├── Cadence.Testing        (packages/testing/src/Cadence.Testing.csproj)
│   └── Cadence.Cli            (packages/cli/src/Cadence.Cli.csproj)
├── Apps/
│   ├── Cadence.Apps.Gateway   (apps/gateway/src/Cadence.Apps.Gateway.csproj)
│   ├── Cadence.Apps.Designer  (apps/designer/src/Cadence.Apps.Designer.csproj)
│   ├── Cadence.Apps.Execution (apps/execution/src/Cadence.Apps.Execution.csproj)
│   ├── Cadence.Apps.Ctms      (apps/ctms/src/Cadence.Apps.Ctms.csproj)
│   ├── Cadence.Apps.Etmf      (apps/etmf/src/Cadence.Apps.Etmf.csproj)
│   └── ... (remaining 13 microservices)
└── Tests/
    ├── Cadence.UnitTests          (tests/Cadence.UnitTests.csproj)
    └── Cadence.ArchitectureTests  (tests/Cadence.ArchitectureTests.csproj)
```

---

## 5. Next Steps: Service Transcription

Once a service is scaffolded, follow the **6-Step Service Transcription SOP** documented in [MIGRATION_WORKFLOW.md](../MIGRATION_WORKFLOW.md):

1. **Step 1: Domain:** Transcribe entities and aggregate roots into `Domain/`.
2. **Step 2: Persistence:** Configure EF Core Fluent API and GxP interceptor in `Infrastructure/`.
3. **Step 3: Application:** Implement typed application services and FluentValidation in `Application/`.
4. **Step 4: Endpoints:** Define Minimal API route groups and filters in `Endpoints/`.
5. **Step 5: Testing:** Author xUnit tests with `[GxPRequirement]` attributes in `tests/`.
6. **Step 6: GxP Sync:** Run `pnpm cadence gxp sync` to validate the requirement tags and results. It writes a preview
   only; never commit the RTM or IQ/OQ/PQ report on a feature branch (ADR-2532).
