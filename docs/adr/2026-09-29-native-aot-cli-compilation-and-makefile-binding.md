# ADR-2249: Native AOT CLI Compilation and Makefile Toolchain Binding

- **Status:** Superseded by [ADR-2824](2026-11-23-incremental-managed-local-cli-builds-and-ci-native-aot.md)
- **Date:** 2026-09-29
- **Authors:** @cadence-team
- **Requirements:** PRD-SYS-053

> [!NOTE]
> Local Native AOT `dotnet publish` builds in `scripts/run-cli.js` were superseded by [ADR-2824](2026-11-23-incremental-managed-local-cli-builds-and-ci-native-aot.md), which switches local `./bin/cadence` builds to incremental managed `dotnet build` with SHA-256 input hashing while keeping `IsAotCompatible` and `EnableTrimAnalyzer` active locally and preserving Native AOT publishing (`-p:PublishAot=true`) in CI per [ADR-2496](2026-09-30-parse-the-cli-with-system-commandline-and-publish-aot-without-suppressions.md).

---

## 1. Context & Problem Statement

The Cadence Clinical platform is unifying all developer tooling, execution runtimes, and quality sentinels around .NET 10 / C# 14. Historically, developer orchestration CLI commands relied on interpreted `dotnet run` invocations or legacy Python CLI wrappers (`packages/cli/main.py`), resulting in slower startup latency (>1.2s cold start) and dual-runtime dependencies.

## 2. Decision Drivers & Constraints

- **Performance:** Cold-start latency under 50 ms for developer workflows.
- **Runtime Unification:** Zero Python runtime dependencies for developer CLI commands and quality gates per `@req:PRD-SYS-053`.
- **GxP Compliance:** Deterministic quality sentinel execution.

## 3. Options Considered

### Option 1: Interpreted `dotnet run` CLI

- **Overview:** Use `dotnet run --project packages/cli/src/Cadence.Cli.csproj`.
- **Pros:**
  - Simple build configuration.
- **Cons:**
  - Cold-start latency >1.2s on every command invocation.

### Option 2: Native AOT Compiled C# Executable (Selected)

- **Overview:** Compile `Cadence.Cli` into a self-contained Native AOT binary (`./bin/cadence`).
- **Pros:**
  - Sub-50ms execution speed.
  - Self-contained binary without JIT overhead.
- **Cons:**
  - Requires Native AOT compatibility (no reflection-based JSON serialization).

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Achieves sub-50ms CLI execution time while satisfying runtime unification under PRD-SYS-053.

## 5. Consequences & Trade-offs

- **Positive Impact:** Cold-start execution time drops to < 50 ms and Python CLI is decommissioned.
- **Negative Impact / Technical Debt:** Refactored JSON serialization across CLI and tooling_core to use Native AOT DOM trees (`JsonObject`).
- **Mitigation Strategy:** Automated quality sentinels verify Native AOT compatibility on every build.

## 6. Implementation & Verification

Verified cold-start speed under 45ms and all 21 quality sentinels passing via `./bin/cadence check`.
