# ADR-2243: Topological Tier-Based .NET 10 Migration and Legacy Python Decommissioning

- **Status:** Accepted
- **Date:** 2026-09-28
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Prior to this migration, fourteen clinical operations, regulatory governance, and interoperability microservices existed alongside or exclusively as legacy Python FastAPI applications without unified .NET 10 C# project definitions. This created dual-runtime technical debt, prevented unified type safety across clinical workflows, and required maintaining dual build tooling. This ADR documents the complete transcription of all 14 targeted microservices to .NET 10 (C# 14 / ASP.NET Core 10 Minimal APIs) and the decommissioning of legacy Python modules, satisfying 21 CFR Part 11 audit requirements under PRD-SYS-053.

## 2. Decision Drivers & Constraints

- **Driver 1:** Eliminate dual-runtime (Python/C#) operational overhead across clinical operations.
- **Driver 2:** Ensure 100% GxP audit trail integrity with EF Core SHA-256 Merkle chain interceptors (PRD-SYS-053).
- **Driver 3:** Enforce strict Clean Architecture boundaries and Central Package Management under `Cadence.sln`.
- **Constraint:** Preserve existing database schemas and domain models without data loss or breaking changes.

## 3. Options Considered

### Option 1: Incremental Service Proxying (Dual-Runtime Maintenance)

- **Overview:** Keep Python microservices running in production while proxying requests through ASP.NET Core gateway adapters.
- **Pros:**
  - ✅ Minimal initial code transcription effort.
- **Cons:**
  - ❌ Retains Python runtime dependencies, build fragmentation, and memory overhead.

### Option 2: Topological Tier-Based Migration and Legacy Python Decommissioning (Selected)

- **Overview:** Transcribe microservices in topological order: Tier 3 clinical/regulatory core services (`ctms`, `etmf`, `eisf`, `quality`, `safety`, `econsent`, `payments`, `tickets`), followed by Tier 4 edge/integration services (`notifications`, `interop`, `ai_gateway`, `fileshare`, `imaging`, `knowledge`), then decommission legacy Python modules.
- **Pros:**
  - ✅ Single high-performance .NET 10 runtime across all 18 microservices.
  - ✅ Unified C# 14 type safety, Minimal APIs, and EF Core 10 Merkle audit interceptor integration.
- **Cons:**
  - ❌ Requires thorough endpoint parity verification and test coverage across all microservices.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Transitioning all microservices sequentially to .NET 10 achieves complete runtime unification, sub-millisecond response times, and automated 21 CFR Part 11 audit hash verification via `GxPAuditInterceptor` (PRD-SYS-053).

## 5. Consequences & Trade-offs

- **Positive Impact:** All 18 microservices are now pure .NET 10 Web SDK projects compiling deterministically under `Cadence.sln`. Legacy Python FastAPI adapters and `uvicorn` dependencies are decommissioned from deployment pipelines.
- **Negative Impact / Technical Debt:** Requires ongoing maintenance of C# xUnit test suites and GxP traceability matrices.
- **Mitigation Strategy:** Automated CI sentinels (`dotnet cadence check`, `sync_gxp.py`) validate build integrity, architectural boundaries, and RTM compliance on every PR.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/ctms`, `apps/etmf`, `apps/eisf`, `apps/quality`, `apps/safety`, `apps/econsent`, `apps/payments`, `apps/tickets`, `apps/notifications`, `apps/interop`, `apps/ai_gateway`, `apps/fileshare`, `apps/imaging`, `apps/knowledge`, `apps/org`, `apps/execution`, `apps/designer`, `apps/gateway`.
- **Verification Plan:**
  - `dotnet build Cadence.sln` compiles with zero warnings and zero errors.
  - `dotnet test Cadence.sln` passes unit, architecture, and E2E test suites.
  - `dotnet run --project packages/cli/src/Cadence.Cli.csproj -- check` passes all quality sentinels.
