# ADR-2233: Migration to .NET 10 Architecture and Standards

- **Status:** Accepted
- **Date:** 2026-09-02
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Cadence Clinical Research Software is an enterprise eClinical platform unifying Clinical Metadata Repository (MDR) capabilities with Electronic Data Capture (EDC) in an automated Digital Data Flow (DDF) pipeline. Historically, the backend services have run on Python 3.14+ (FastAPI, SQLAlchemy/SQLModel, Pydantic v2).

While Python provided rapid domain prototyping, clinical trial data processing at scale—including high-frequency eCRF transactional mutations, real-time SDTM transformations, Merkle-root cryptographic sealing, and concurrent multi-tenant data locks—demands:

1. **Ultra-Low Latency & High Throughput:** Native multi-threaded execution without GIL bottlenecks, achieving sub-millisecond p99 latency.
2. **Strict Compile-Time Safety & Native AOT Readiness:** Elimination of runtime attribute/type errors through strong compile-time typing, trimming, and ahead-of-time compilation.
3. **Supply Chain & Memory Determinism:** Single-binary deployment profiles, minimal runtime footprint, and centralized dependency governance under Central Package Management (CPM).
4. **Regulatory Audit Traceability (21 CFR Part 11 / GxP):** Deterministic compile-time requirements tracking (`PRD-SYS-053`) and persistence-level audit interception.

To address these requirements, Cadence is executing a clean-break architectural transition to **.NET 10 (C# 14 / ASP.NET Core 10)**. Existing Python implementations serve as the authoritative behavioral reference and anti-corruption baseline to be systematically transcribed and phased out.

---

## 2. Decision Drivers & Constraints

- **Performance & Concurrency:** Microservice operations must satisfy strict internal SLAs (<100ms cross-service, <10ms database round-trips) under concurrent EDC site submissions.
- **GxP Compliance (PRD-SYS-053):** 21 CFR Part 11 and ICH GCP E6(R2) compliance requires non-repudiation, cryptographic block sealing, and deterministic requirement traceability (`@req:PRD-SYS-053`).
- **Developer & Agent Ergonomics:** AI coding agents and human engineers require unambiguous coding standards, strong compiler diagnostics, and standardized project layout.
- **CDISC Standards Fidelity:** High-throughput processing and serialization of CDISC USDM v3/v4 protocol models and ODM XML/JSON datasets.

---

## 3. Options Considered

### Option 1: Clean-Break Architecture to .NET 10 (Selected)

- **Overview:** Establish .NET 10 (C# 14 / ASP.NET Core 10) as the sole target architecture across all development documentation, `AGENTS.md`, and agent tooling immediately. Retain existing domain directory boundaries (`apps/<service>` and `packages/<library>`) with C# project files bound via a root `Cadence.sln`. Treat Python codebases as reference specifications to be cleanly transcribed.
- _**Pros:**_
  - ✅ Unambiguous target standard for developers and AI agents (no confusion between competing paradigms).
  - ✅ Modern C# 14 features (file-scoped namespaces, primary constructors, enhanced pattern matching, Native AOT).
  - ✅ High-performance EF Core 10 with Npgsql, compiled queries, and `SaveChangesInterceptor` for GxP audit ledgers.
  - ✅ Central Package Management (CPM) via `Directory.Packages.props` ensures zero transitive dependency drift.
- _**Cons:**_
  - ❌ Requires a structured transcription phase across existing microservices.

### Option 2: Long-Term Strangler Fig Dual-Stack Coexistence

- **Overview:** Maintain both Python and .NET backends indefinitely, routing between them at an API gateway layer while supporting both stacks in CI and agent instructions.
- _**Pros:**_
  - ✅ Allows partial service-by-service deployment without full transcription upfront.
- _**Cons:**_
  - ❌ Doubles CI maintenance, dependency scans, security audits, and agent instructions.
  - ❌ Fractures developer focus across two vastly different language ecosystems.

### Option 3: Incremental Python Optimization (Cython / PyPy / Mypyc / Rust FFI)

- **Overview:** Keep Python as the primary language, optimizing hot paths with C/Rust extensions.
- _**Pros:**_
  - ✅ Preserves existing codebase without language migration.
- _**Cons:**_
  - ❌ Fails to resolve runtime type fragility, ecosystem packaging drift, or heavy memory consumption.

---

## 4. Decision Outcome

**Chosen Option:** **Option 1 (Clean-Break Architecture to .NET 10)**.

### Key Architectural Standards

1. **API Layer:**
   - **ASP.NET Core 10 Minimal APIs:** Direct route group mapping (`app.MapGroup("/api/v1/...")`) with typed results (`TypedResults.Ok(...)`, `TypedResults.NotFound()`).
   - **Endpoint Filters (`IEndpointFilter`):** Cross-cutting concerns such as Gateway HMAC signature validation, electronic signature re-authentication, and correlation ID propagation.

2. **Persistence & Data Access:**
   - **Entity Framework Core 10 + Npgsql:** PostgreSQL relational database persistence.
   - **Audit Interceptor (`ISaveChangesInterceptor`):** Automatically enforces 21 CFR Part 11 audit fields (`CreatedAt`, `CreatedBy`, `ReasonForChange`, `VersionIndex`) and Merkle tree block hashing prior to committing transactions.
   - **Read Optimization:** All query-only endpoints must explicitly use `.AsNoTracking()`.
   - **Neo4j Graph Database:** `Neo4j.Driver` for .NET 10 managing CDISC USDM metadata authoring in `Cadence.Apps.Designer`.

3. **Solution & Monorepo Structure:**
   - Root `Cadence.sln` binds all projects while preserving the existing domain directory hierarchy (`apps/<service>/src/Cadence.Apps.<Service>.csproj` and `packages/<library>/src/Cadence.<Library>.csproj`).
   - `Directory.Build.props` enforces `net10.0`, `LangVersion 14`, `Nullable enable`, and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
   - `Directory.Packages.props` enforces Central Package Management (CPM).

4. **Code Quality & Tooling:**
   - Replaces Ruff with `.editorconfig`, `dotnet format`, and Roslyn Analyzers (`Microsoft.CodeAnalysis.NetAnalyzers`, `SonarAnalyzer.CSharp`).
   - CLI Tooling: Transition developer commands from `uv run cadence` to native `dotnet cadence` (`Cadence.Cli`) built with `Spectre.Console`.

5. **GxP Traceability:**
   - Test methods must feature both XML doc comments (`/// <summary> @req:PRD-SYS-053 </summary>`) and strongly-typed compile-time attributes (`[GxPRequirement("PRD-SYS-053")]`).
   - Automated sync tools (`scripts/sync_gxp.py`) will ingest `.trx` and JUnit XML test reports from `dotnet test`.

---

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Drastic performance improvement (>10x throughput on eCRF ingestion, <1ms serialization).
  - Robust compile-time type validation preventing whole classes of runtime bugs.
  - Native enterprise telemetry (`System.Diagnostics.Activity`, OpenTelemetry, Prometheus metrics).
  - Clean separation of Domain, Application, and Infrastructure layers using standard C# class libraries.
- **Negative Impact & Liabilities:**
  - Transition effort required to transcribe existing Python domain logic into C# 14.
  - Development agents must be strictly guided with transcription tables and C# patterns.
- **Mitigation Strategy:**
  - Maintain comprehensive Python-to-.NET transcription matrix in `AGENTS.md`.
  - Equip agent guardrails (`.agents/hooks.json`) to validate `.cs` syntax and formatting on every tool step.

---

## 6. Implementation & Verification

- **Phase 1 (Foundations):** Establish `Cadence.sln`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, updated `AGENTS.md`, and agent hooks.
- **Phase 2 (Core Libraries):** Implement `Cadence.Core.Domain`, `Cadence.Security`, `Cadence.Testing`, and `Cadence.Cli`.
- **Phase 3 (Service Transcription):** Transcribe domain services (`Cadence.Apps.Execution`, `Cadence.Apps.Designer`, `Cadence.Apps.Gateway`, etc.) against existing test specifications.
- **Verification:** CI executes `dotnet test --logger trx` and `dotnet format --verify-no-changes`, passing through the GxP RTM sync gate (`scripts/sync_gxp.py`).
