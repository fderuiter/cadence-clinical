# ADR 2026-09-25: Decommission Python Scripts to Native .NET 10 Tooling Services and Cadence.Cli Commands

- **Status:** Accepted
- **Date:** 2026-09-25
- **Authors:** @cadence-team
- **Requirements:** PRD-SYS-053, PRD-SYS-001

---

## Status

Accepted

## Context

The Cadence Clinical platform contained 55 operational Python scripts in `scripts/` that performed tasks ranging from code formatting, secret detection, and OpenAPI drift verification to GxP compliance synchronization and requirements traceability matrix (RTM) generation.

Executing these Python scripts introduced friction in pre-commit hooks and CI pipelines due to Python interpreter resolution, virtual environment activation overhead, and subprocess launching in C# orchestrator services.

## Decision

1. **Port Operational Tooling to Native .NET 10 C# Services:**
   All 55 Python operational scripts were refactored and ported to C# static services in `Cadence.ToolingCore` (`packages/tooling_core/src/Services/`) and exposed via Spectre.Console CLI commands in `Cadence.Cli` (`packages/cli/src/Commands/ToolCommands.cs`).

2. **In-Process Orchestrator Execution:**
   - `GxPComplianceService` invokes `SyncGxPService.SyncCompliance()` directly without spawning subprocesses (`python3 scripts/sync_gxp.py`).
   - `QualityGateOrchestrator` invokes `OpenApiDriftValidatorService.ValidateDrift()` directly without spawning `uv` / `python` processes.
   - `FixCommand` invokes `SelfHealService.ExecuteSelfHealing()` directly.

3. **Update Pre-Commit Hooks & Repository Configurations:**
   - `.pre-commit-config.yaml` hook entries were updated to invoke `dotnet run --project packages/cli/src/Cadence.Cli.csproj --no-build -- <command>`.
   - `package.json`, `Makefile`, and `.github/workflows/ci.yml` were updated to call native `Cadence.Cli` commands.

4. **Decommission Python Scripts:**
   All 55 Python scripts under `scripts/` were permanently removed from the repository.

## Alternatives Considered

- **Option 1: Retain Python Scripts with Wrapper Shell Scripts**
  - Cons: Maintains Python execution friction, virtual environment dependencies, and slow execution times across Windows/Linux developer environments.
- **Option 2: Port to Native .NET 10 C# Tooling Services (Selected)**
  - Pros: Unifies operational tooling under the .NET 10 solution, eliminates subprocess overhead, and provides strongly typed fail-fast validation in `Cadence.Cli`.

## Trade-offs

- **Pros:**
  - Standardized on .NET 10 across the whole operational and verification stack.
  - Eliminated Python execution friction and subprocess overhead in quality gates and compliance sync.
  - Fail-fast C# compilation guarantees strongly typed verification tooling.
- **Cons:**
  - Developers and CI runners require .NET 10 SDK installed to execute quality gates.
