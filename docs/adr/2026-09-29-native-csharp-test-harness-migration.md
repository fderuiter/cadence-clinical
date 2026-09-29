# ADR-0089: Native C# Test Harness Migration & Bogus Factories Integration

- **Status:** Accepted
- **Date:** 2026-09-29
- **Authors:** @google-labs-stitch
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

As part of the clean-break architectural migration to .NET 10 (C# 14), legacy Python testing utilities under `packages/testing/` (such as `factories.py`, `fakes.py`, `security.py`, `dependency_graph.py`) were decommissioned in favor of native C# test doubles, Bogus entity factories, hexagonal fake adapters, and a Roslyn AST dependency graph parser in `Cadence.Testing`.

## 2. Decision Drivers & Constraints

- **Driver 1:** Complete .NET 10 runtime unification without legacy Python runtime test infrastructure dependencies.
- **Driver 2:** High-performance test data generation using Bogus fluent factories (`Cadence.Testing.Factories`).
- **Driver 3:** Roslyn AST-based test dependency analysis for instant affected test resolution (<200ms).

## 3. Options Considered

### Option 1: Maintain Dual Python & C# Testing Utilities

- **Overview:** Keep legacy Python test factories alongside C# test infrastructure.
- **Pros:**
  - ✅ Preserves existing Python helper scripts.
- **Cons:**
  - ❌ Creates dual maintenance overhead and runtime ambiguity.

### Option 2: Full Migration to Native C# Test Harness in Cadence.Testing (Selected)

- **Overview:** Decommission legacy Python test files and implement native C# factories, hexagonal fakes, security context helpers, and Roslyn AST dependency resolution in `Cadence.Testing`.
- **Pros:**
  - ✅ Native AOT ready, fast, strongly typed C# test infrastructure.
  - ✅ Centrally managed via Central Package Management (CPM).
- **Cons:**
  - ❌ Requires updating legacy test calls to native C# equivalents.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Aligns with PRD-SYS-053 GxP testing standards and completes runtime migration to .NET 10 / C# 14.

## 5. Consequences & Trade-offs

- **Positive Impact:** All unit, architecture, and E2E test suites run purely in native .NET 10 environment.
- **Negative Impact / Technical Debt:** None.
- **Mitigation Strategy:** Enforced via `dotnet cadence check` quality gates.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/testing/`, `packages/cli/`, `tests/Testing/`.
- **Verification Plan:** Executed via `dotnet test Cadence.sln` and `dotnet cadence check`.
