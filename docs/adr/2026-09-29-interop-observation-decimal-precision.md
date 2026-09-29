# ADR-2242: Interop Observation Domain Primitive Decimal Conversion & EF Core Explicit Precision Mapping

- **Status:** Accepted
- **Date:** 2026-09-29
- **Requirements:** PRD-SYS-053
- **Authors:** @jules
- **Deciders:** @fderuiter, @cadence-core-team

---

## 1. Context & Problem Statement

The interop microservice previously modeled clinical observation values and alert thresholds using floating-point `double` primitives (`SensorSafetyAlertRecord` and `SensorDailySummaryRecord`). Binary floating-point representation causes unexpected rounding errors at clinical decision boundaries (e.g. SpO2 threshold comparisons), risking missed safety alerts or inaccurate metric aggregation.

## 2. Decision Drivers & Constraints

- **Driver 1:** Eliminate floating-point representation errors across sensor safety alerts and daily summary vital metrics.
- **Driver 2:** Guarantee exact threshold comparisons and boundary evaluation under 21 CFR Part 11 and clinical safety protocols.
- **Driver 3:** Standardize on C# `decimal` / `decimal?` explicitly mapped to PostgreSQL `numeric(18,4)` columns across all EF Core entity configurations in `Cadence.Apps.Interop`.

## 3. Options Considered

### Option 1: Retain double floating-point types with runtime rounding
- **Overview:** Keep `double` domain properties and apply `Math.Round` or epsilon comparisons at runtime.
- **Pros:**
  - ✅ No database schema alterations required.
- **Cons:**
  - ❌ Does not eliminate IEEE 754 binary floating-point representation errors.
  - ❌ Risks false triggers or missed clinical safety alerts at boundary thresholds.

### Option 2: Convert properties to decimal / decimal? and configure explicit EF Core numeric(18,4) column mappings (Selected)
- **Overview:** Refactor `ObservedValue` and `ThresholdValue` in `SensorSafetyAlertRecord` to C# `decimal`, refactor vital measurement properties in `SensorDailySummaryRecord` to `decimal?`, configure `.HasPrecision(18, 4).HasColumnType("numeric(18,4)")` in `InteropConfigurations.cs`, and generate EF Core schema migration script.
- **Pros:**
  - ✅ Fixed-point exact decimal precision across all clinical observation safety threshold comparisons and daily metric aggregations.
  - ✅ 100% compliance with 21 CFR Part 11 and clinical data integrity standards.
- **Cons:**
  - ❌ Requires database migration altering column types from `double precision` to `numeric(18,4)`.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** C# `decimal` / `decimal?` mapped to PostgreSQL `numeric(18,4)` satisfies clinical safety requirements and guarantees zero floating-point representation errors across all threshold comparisons.

## 5. Consequences & Trade-offs

- **Positive Impact:** Exact evaluation of safety alert thresholds (e.g. SpO2 88.0% vs threshold 88.0%) without floating-point inaccuracies.
- **Negative Impact / Technical Debt:** Database column alteration required.
- **Mitigation Strategy:** EF Core migration script (`20260929052801_RefactorInteropDecimalPrecision`) alters column types while preserving numeric values.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `Cadence.Apps.Interop`, `Cadence.UnitTests`.
- **Verification Plan:** Verified via `InteropPrecisionTests` unit test suite, `dotnet build`, `dotnet format`, `dotnet test Cadence.sln`, and Cadence CLI quality checks.
