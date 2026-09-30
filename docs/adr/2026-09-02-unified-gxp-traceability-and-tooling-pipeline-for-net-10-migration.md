# ADR-2429: Unified GxP Traceability and Tooling Pipeline for .NET 10 Migration

Also known as: ADR-2235 (former number)

- **Status:** Accepted
- **Date:** 2026-09-02
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Cadence Clinical Research Software is executing a clean-break architectural transition to **.NET 10 (C# 14 / ASP.NET Core 10)** per [ADR-2233](2026-09-02-migration-to-dotnet-10-architecture-and-standards.md) and [ADR-2428](2026-09-02-core-service-architecture-cqrs-transactional-outbox-and-gxp-interceptor-standard.md). As documented in `docs/MIGRATION_WORKFLOW.md`, microservices are systematically transcribed using a 6-step SOP from Python 3.14+ to .NET 10.

However, a comprehensive infrastructure and workflow audit identified critical gaps across developer and CI pipelines:
1. **GxP Compliance & RTM Generation Gap:** `scripts/generate_rtm.py` and `scripts/sync_gxp.py` only scan `.py` test files with pytest, ignoring `.cs` test files (`[GxPRequirement("...")]`, `/// @req:...`) and failing to ingest xUnit/TRX test execution results. As tests transition to .NET, the Requirements Traceability Matrix (`docs/SDLC/Requirements_Traceability_Matrix.md`) drops requirement coverage.
2. **Git Hook Desynchronization:** `.githooks/pre-commit` and `pre-commit.ps1` enforce `dotnet format` and ADR validation, but `git config core.hooksPath` is not set by default. Meanwhile, the project's `.pre-commit-config.yaml` (installed via `pnpm setup:dev` / `make setup`) exclusively checks Python and frontend assets, allowing unformatted C# code to bypass local commit gates.
3. **Developer CLI Packaging & Command Routing:** `packages/cli/src/Program.cs` lacks execution implementations for `test`, `gxp sync`, `doctor`, and `check`. Furthermore, `Cadence.Cli` is not registered in a `.config/dotnet-tools.json` local manifest, causing documented `dotnet cadence` commands to fail upon execution.
4. **CI/CD Runtime Mismatch:** `.github/workflows/ci.yml` sets up Python 3.12, which triggers an immediate fatal failure in `scripts/runtime_guard.py` (which requires Python 3.14+) when running `scripts/sync_gxp.py`.
5. **Docker Compose Environment:** `docker/Dockerfile.dotnet` exists, but `docker/docker-compose.yml` lacks a corresponding `dotnet` profile service.

---

## 2. Decision Drivers & Constraints

* **21 CFR Part 11 Regulatory Continuity (PRD-SYS-053):** Requirements Traceability Matrix (RTM) generation must reflect both legacy Python tests and new .NET 10 xUnit tests simultaneously throughout the phased migration without coverage drops.
* **Deterministic Developer Experience:** A single command invocation (`dotnet cadence` / `pnpm sync-gxp`) must work identically on Windows and POSIX developer environments.
* **Zero Commit Drift:** C# code style (`.editorconfig`), Roslyn analyzer rules, and ADR validation must be enforced on every commit.
* **CI/CD Hermeticity:** GitHub Actions CI workflows must execute against compliant Python 3.14+ and .NET 10 runtimes without environment failures.

---

## 3. Options Considered

### Option 1: Unified Dual-Stack Pipeline & Local Pre-Commit Integration (Selected)
* Extend `scripts/generate_rtm.py` and `scripts/sync_gxp.py` to scan `.cs` files for `[GxPRequirement("...")]` and `/// @req:...` docstrings, merging xUnit test results (`.trx` / JUnit XML) with pytest `report.xml`.
* Integrate `dotnet format Cadence.sln --verify-no-changes` directly into `.pre-commit-config.yaml` alongside existing Python linters.
* Establish a `.config/dotnet-tools.json` local tool manifest and complete the command dispatcher in `Cadence.Cli`.
* Update `.github/workflows/ci.yml` to Python 3.14+ and expand PR triggers.
* Add the `dotnet` service profile in `docker/docker-compose.yml`.
- ***Pros:***
  - ✅ Addresses requirements
- ***Cons:***
  - ❌ Trade-offs accepted


### Option 2: Split Python and .NET Pipelines (Rejected)
* Build completely isolated CLI and RTM reporting tools in .NET (`dotnet cadence gxp sync`) that generate separate documentation files from the Python pipeline.
* *Trade-off:* Fractures SDLC audit documentation into dual disparate reports, violating 21 CFR Part 11 unified validation ledger standards.

---
- ***Pros:***
  - ✅ Addresses requirements
- ***Cons:***
  - ❌ Trade-offs accepted


## 4. Decision Outcome

**Chosen Option:** **Option 1 (Unified Dual-Stack Pipeline & Local Pre-Commit Integration)**.

### Concrete Implementation Blueprint

1. **Dual-Stack GxP Traceability (`scripts/generate_rtm.py` & `scripts/sync_gxp.py`):**
   - Update `scan_tests()` in `generate_rtm.py` to traverse `.cs` files across `tests/`, `apps/`, and `packages/`, extracting requirement tags from `[GxPRequirement("...")]` attributes and `/// @req:...` XML doc comments.
   - Update `parse_test_results()` to parse `.trx` (Visual Studio / xUnit TRX format) as well as JUnit XML test outputs.
   - Update `step_run_tests()` in `sync_gxp.py` to execute `dotnet test Cadence.sln --logger "trx;LogFileName=test_results.trx"` in addition to `pytest`.

2. **Pre-Commit Hook Consolidation (`.pre-commit-config.yaml`):**
   - Add a local pre-commit hook executing `dotnet format Cadence.sln --verify-no-changes`.
   - Add ADR and architecture sentinels so that any `git commit` automatically validates C# style and architectural rules.

3. **Developer CLI (`Cadence.Cli` & `.config/dotnet-tools.json`):**
   - Implement `Cadence.Cli` handlers for `check` (running ArchUnitNET tests and dotnet format), `doctor` (verifying .NET 10 SDK, Postgres, Neo4j, Python runtime), `gxp sync` (delegating to `sync_gxp.py`), and `test` (running xUnit suites).
   - Add local tool manifest `.config/dotnet-tools.json`.

4. **CI/CD Workflow Alignment (`.github/workflows/ci.yml`):**
   - Update Python setup step: `python-version: "3.14"`.
   - Add `dotnet test` with TRX logger and diff verification against updated GxP docs.
   - Expand PR triggers to all branches targeting `main`, `master`, and `dotnet-migration`.

5. **Docker Compose Dev Profile (`docker/docker-compose.yml`):**
   - Define a `dotnet-dev` service targeting `docker/Dockerfile.dotnet` under `profiles: ["dotnet"]` to support containerized hot-reloading development.

---

## 5. Consequences & Trade-offs

* **Positive Impact:**
  - Complete, unbroken regulatory traceability across the entire migration lifespan.
  - Local developers receive immediate feedback on C# formatting and ADR integrity prior to committing.
  - Unified commands (`dotnet cadence` / `pnpm sync-gxp`) maintain identical behavior across human and AI agents.
* **Negative Impact & Liabilities:**
  - Requires maintaining regex/parsing logic for both Python docstrings and C# attributes in `generate_rtm.py`.
* **Mitigation:**
  - The C# parser uses clean, deterministic regular expressions matching the standardized `[GxPRequirement("...")]` and `/// @req:...` syntax enforced by Roslyn Analyzers and pre-commit checks.

---

## 6. Implementation & Verification

* Target files:
  - `scripts/generate_rtm.py`, `scripts/sync_gxp.py`
  - `.pre-commit-config.yaml`
  - `packages/cli/src/Program.cs`, `packages/cli/src/Cadence.Cli.csproj`, `.config/dotnet-tools.json`
  - `.github/workflows/ci.yml`
  - `docker/docker-compose.yml`
* Verification:
  - Run `python scripts/validate_adrs.py` to ensure ADR index integrity.
  - Verify `generate_rtm.py` scans `tests/Cadence.UnitTests/SecurityAndDomainTests.cs` and maps `PRD-SYS-053`.
